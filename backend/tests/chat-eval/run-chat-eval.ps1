param(
  [string]$ApiBaseUrl = "http://localhost:5000",
  [string]$RepoUrl = "https://github.com/gitleaks/gitleaks",
  [string]$ReviewProfile = "balanced",
  [string]$CasesFile = ".\\cases.gitleaks.json",
  [int]$TimeoutSeconds = 300,
  [switch]$SkipIngest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-ApiJson {
  param(
    [string]$Method,
    [string]$Uri,
    [object]$Body = $null
  )

  if ($null -eq $Body) {
    return Invoke-RestMethod -Method $Method -Uri $Uri
  }

  $json = $Body | ConvertTo-Json -Depth 20
  return Invoke-RestMethod -Method $Method -Uri $Uri -ContentType "application/json" -Body $json
}

function Contains-IgnoreCase {
  param(
    [string]$Text,
    [string]$Needle
  )

  if ([string]::IsNullOrEmpty($Needle)) { return $true }
  return $Text.IndexOf($Needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

if (-not (Test-Path $CasesFile)) {
  throw "Cases file not found: $CasesFile"
}

$cases = Get-Content $CasesFile -Raw | ConvertFrom-Json
if ($cases.Count -eq 0) {
  throw "No test cases in $CasesFile"
}

$sessionId = $null
if (-not $SkipIngest) {
  Write-Host "Creating session for $RepoUrl ..."
  $ingest = Invoke-ApiJson -Method "POST" -Uri "$ApiBaseUrl/api/ingest/repo" -Body @{ url = $RepoUrl; reviewProfile = $ReviewProfile }
  $sessionId = $ingest.sessionId
  Write-Host "Session: $sessionId"

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    Start-Sleep -Milliseconds 700
    $session = Invoke-ApiJson -Method "GET" -Uri "$ApiBaseUrl/api/sessions/$sessionId"
  } while ($session.status -ne "ready" -and $session.status -ne "failed" -and (Get-Date) -lt $deadline)

  if ($session.status -ne "ready") {
    throw "Session did not become ready. Final status: $($session.status)"
  }
}
else {
  throw "SkipIngest mode currently requires manual session wiring. Remove -SkipIngest to run full flow."
}

$runAt = Get-Date -Format "yyyyMMdd-HHmmss"
$outDir = Join-Path (Split-Path -Parent $CasesFile) "out-$runAt"
New-Item -ItemType Directory -Path $outDir | Out-Null

$results = @()
$passCount = 0
$failCount = 0

foreach ($case in $cases) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $chatReq = @{ question = [string]$case.question; history = @() }
  $chat = Invoke-ApiJson -Method "POST" -Uri "$ApiBaseUrl/api/sessions/$sessionId/chat" -Body $chatReq
  $sw.Stop()

  $content = [string]$chat.content
  $grounded = [bool]$chat.grounded
  $citations = @($chat.citations)
  $citationCount = $citations.Count

  $checks = @()

  if ($null -ne $case.expectedGrounded) {
    $checks += [pscustomobject]@{
      Name = "expectedGrounded"
      Pass = ($grounded -eq [bool]$case.expectedGrounded)
      Detail = "expected=$($case.expectedGrounded) actual=$grounded"
    }
  }

  if ($null -ne $case.expectCannotFind -and [bool]$case.expectCannotFind) {
    $expectedMsg = "I can't find that in the indexed repository."
    $checks += [pscustomobject]@{
      Name = "expectCannotFind"
      Pass = ($content.Trim() -eq $expectedMsg)
      Detail = "expectedExact='$expectedMsg' actual='$content'"
    }
  }

  if ($null -ne $case.minCitations) {
    $checks += [pscustomobject]@{
      Name = "minCitations"
      Pass = ($citationCount -ge [int]$case.minCitations)
      Detail = "min=$($case.minCitations) actual=$citationCount"
    }
  }

  if ($null -ne $case.maxCitations) {
    $checks += [pscustomobject]@{
      Name = "maxCitations"
      Pass = ($citationCount -le [int]$case.maxCitations)
      Detail = "max=$($case.maxCitations) actual=$citationCount"
    }
  }

  $mustContainAllPass = $true
  foreach ($token in @($case.mustContainAll)) {
    if (-not (Contains-IgnoreCase -Text $content -Needle ([string]$token))) {
      $mustContainAllPass = $false
      break
    }
  }
  $checks += [pscustomobject]@{
    Name = "mustContainAll"
    Pass = $mustContainAllPass
    Detail = "tokens=$(@($case.mustContainAll) -join '; ')"
  }

  $mustContainAny = @($case.mustContainAny)
  $mustContainAnyPass = $true
  if ($mustContainAny.Count -gt 0) {
    $mustContainAnyPass = $false
    foreach ($token in $mustContainAny) {
      if (Contains-IgnoreCase -Text $content -Needle ([string]$token)) {
        $mustContainAnyPass = $true
        break
      }
    }
  }
  $checks += [pscustomobject]@{
    Name = "mustContainAny"
    Pass = $mustContainAnyPass
    Detail = "tokens=$($mustContainAny -join '; ')"
  }

  $mustNotContainPass = $true
  foreach ($token in @($case.mustNotContain)) {
    if (Contains-IgnoreCase -Text $content -Needle ([string]$token)) {
      $mustNotContainPass = $false
      break
    }
  }
  $checks += [pscustomobject]@{
    Name = "mustNotContain"
    Pass = $mustNotContainPass
    Detail = "tokens=$(@($case.mustNotContain) -join '; ')"
  }

  $failedCheckItems = @($checks | Where-Object { -not $_.Pass })
  $allPass = $failedCheckItems.Count -eq 0
  if ($allPass) { $passCount++ } else { $failCount++ }

  $results += [pscustomobject]@{
    id = [string]$case.id
    category = [string]$case.category
    question = [string]$case.question
    pass = $allPass
    grounded = $grounded
    citationCount = $citationCount
    latencyMs = [int]$sw.ElapsedMilliseconds
    content = $content
    failedChecks = ($failedCheckItems | ForEach-Object { "$($_.Name): $($_.Detail)" }) -join " | "
  }
}

$resultsPathJson = Join-Path $outDir "results.json"
$resultsPathCsv = Join-Path $outDir "results.csv"
$summaryPath = Join-Path $outDir "summary.txt"

$results | ConvertTo-Json -Depth 8 | Out-File -Encoding utf8 $resultsPathJson
$results | Export-Csv -NoTypeInformation -Encoding utf8 $resultsPathCsv

$total = $results.Count
$passRate = if ($total -gt 0) { [math]::Round(($passCount * 100.0) / $total, 1) } else { 0 }
$latencies = $results | Select-Object -ExpandProperty latencyMs
$p95 = if ($latencies.Count -gt 0) {
  $sorted = $latencies | Sort-Object
  $idx = [int][math]::Ceiling(0.95 * $sorted.Count) - 1
  $sorted[[math]::Max(0, $idx)]
} else { 0 }

$summary = @(
  "SessionId: $sessionId",
  "TotalCases: $total",
  "Passed: $passCount",
  "Failed: $failCount",
  "PassRate: $passRate%",
  "P95LatencyMs: $p95",
  "ResultsJson: $resultsPathJson",
  "ResultsCsv: $resultsPathCsv"
) -join [Environment]::NewLine

$summary | Out-File -Encoding utf8 $summaryPath
Write-Host $summary

if ($failCount -gt 0) {
  Write-Error "Chat eval failed: $failCount case(s) failed."
  exit 1
}

Write-Host "Chat eval passed."
exit 0
