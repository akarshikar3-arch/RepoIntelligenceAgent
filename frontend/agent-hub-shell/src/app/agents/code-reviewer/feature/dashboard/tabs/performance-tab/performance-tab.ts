import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap, takeWhile, timer } from 'rxjs';
import { DashboardApi } from '../../../../data-access/api/dashboard.api';
import { DashboardMetrics, Finding, Severity } from '../../../../data-access/models/dashboard.models';
import { FindingList } from '../../_ui/finding-list';
import { KpiCard } from '../../_ui/kpi-card';
import { PanelCard } from '../../_ui/panel-card';
import { SeverityBar } from '../../_ui/severity-bar';
import { summarizeFindings } from '../_shared/use-findings';

interface PerfState {
  status: 'loading' | 'ready' | 'error';
  metrics: DashboardMetrics | null;
  findings: Finding[];
  error: string | null;
}

const PERF_INITIAL: PerfState = {
  status: 'loading',
  metrics: null,
  findings: [],
  error: null,
};

const PERF_HINT_RE = /perf|latency|slow|cpu|memory|render|bundle|allocation|hotspot/i;

function severityPenalty(severity: Severity): number {
  switch (severity) {
    case 'critical': return 25;
    case 'high': return 15;
    case 'medium': return 8;
    case 'low': return 3;
    default: return 1;
  }
}

@Component({
  selector: 'cr-performance-tab',
  standalone: true,
  imports: [DecimalPipe, PanelCard, KpiCard, SeverityBar, FindingList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (state().status === 'loading') {
      <p class="muted">Loading performance insights…</p>
    } @else if (state().status === 'error') {
      <p class="muted error">{{ state().error }}</p>
    } @else if (!state().metrics) {
      <p class="muted">No metrics available yet.</p>
    } @else {
      <div class="grid">
        <div class="top-grid">
          <cr-panel-card class="fill exec" title="Executive Summary" icon="insights">
            <p class="score-line">{{ executiveSummary() }}</p>
            <p class="score-note"><strong>{{ performanceScore() }} / 100 ({{ scoreBand() }})</strong> · {{ scoreHeadline() }}</p>
          </cr-panel-card>

          <section class="kpis top-kpis">
            <cr-kpi-card
              label="Performance Score"
              icon="speed"
              [value]="performanceScore()"
              suffix="/100"
              [tone]="scoreTone()" />
            <cr-kpi-card
              label="Hotspot Files"
              icon="whatshot"
              [value]="hotspotCount()" />
            <cr-kpi-card
              label="Perf Findings"
              icon="warning"
              [value]="performanceFindings().length" />
          </section>

          <cr-panel-card class="fill risk-wide" title="Performance Risk Profile" icon="monitoring">
            <cr-severity-bar [counts]="riskCounts()" />
          </cr-panel-card>
        </div>

        <div class="hotspot-row">
          <cr-panel-card title="Performance Hotspots" icon="local_fire_department" subtitle="size and repo share">
            @if (hotspotRows().length === 0) {
              <p class="muted">No hotspots detected yet.</p>
            } @else {
              <ol class="hotspots">
                @for (row of hotspotRows(); track row.path) {
                  <li>
                    <div class="head">
                      <span class="path" [title]="row.path">{{ row.path }}</span>
                      <span class="meta">{{ row.kb | number:'1.0-1' }} KB · {{ row.lines | number }} lines · {{ row.repoPercent | number:'1.0-1' }}% of sampled repo</span>
                    </div>
                    <div class="bar"><span class="fill" [style.width.%]="row.repoPercent"></span></div>
                  </li>
                }
              </ol>
            }
          </cr-panel-card>

          <cr-panel-card title="Large Files Detected" icon="warning" subtitle="grouped analysis">
            @if (largeFilesDetected().length === 0) {
              <p class="muted">No large-file bottlenecks detected.</p>
            } @else {
              <p class="score-line">{{ largeFilesDetected().length }} oversized files detected.</p>
              <ul class="signals">
                @for (f of largeFilesDetected(); track f.path) {
                  <li>{{ f.path }} ({{ f.lines | number }} lines)</li>
                }
              </ul>
              <p class="impact">Impact: slower builds, harder reviews, and weaker module separation.</p>
              <p class="impact">Suggestion: split modules and lazy-load non-critical paths.</p>
            }
          </cr-panel-card>
        </div>

        <div class="details-grid">
          <cr-panel-card class="insight-card" title="Insight Summary" icon="psychology">
            <div class="insight-grid">
              <section>
                <p class="insight-title">This repository shows risk due to:</p>
                <ul class="signals">
                  @for (s of insightDrivers(); track s) {
                    <li>{{ s }}</li>
                  }
                </ul>
              </section>
              <section>
                <p class="insight-title">If unresolved:</p>
                <ul class="signals">
                  @for (s of insightImpacts(); track s) {
                    <li>{{ s }}</li>
                  }
                </ul>
              </section>
            </div>
          </cr-panel-card>

          <cr-panel-card class="benchmark-card" title="Benchmark Comparison" icon="compare_arrows">
            <ul class="signals">
              @for (b of benchmarkNotes(); track b) {
                <li>{{ b }}</li>
              }
            </ul>
          </cr-panel-card>

          <cr-panel-card class="trend-card" title="Trend Risk" icon="show_chart">
            <p class="score-note">If current file growth continues, performance score may degrade over time.</p>
          </cr-panel-card>

          <cr-panel-card class="runtime-card" title="Runtime Risk Signals" icon="timeline">
            <ul class="signals">
              @for (s of riskSignals(); track s) {
                <li>{{ s }}</li>
              }
              @if (riskSignals().length === 0) {
                <li class="muted">No obvious performance risk signals detected.</li>
              }
            </ul>
          </cr-panel-card>

          <cr-panel-card class="findings-card" title="Performance Findings" icon="list_alt">
            <cr-finding-list [findings]="performanceFindings()" defaultGroupMode="file" />
          </cr-panel-card>
        </div>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      container-type: inline-size;
    }
    .grid { display: grid; gap: 0.5rem; }
    .top-grid {
      display: grid;
      gap: 0.5rem;
      grid-template-columns: 1.45fr 1fr;
      grid-template-areas:
        "exec kpis"
        "risk risk";
      grid-template-rows: minmax(0, auto) minmax(0, auto);
      align-items: start;
    }
    .exec { grid-area: exec; }
    .top-kpis { grid-area: kpis; }
    .risk-wide { grid-area: risk; }
    .top-grid > cr-panel-card,
    .top-grid > section { min-height: 0; }
    .insight-grid { display: grid; gap: 0.85rem; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); }
    .kpis {
      display: grid;
      gap: 0.45rem;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      align-content: stretch;
    }
    .kpis :deep(.kpi) {
      min-height: 82px;
      padding: 0.55rem 0.65rem;
      gap: 0.15rem;
    }
    .kpis :deep(.label) { font-size: 0.7rem; }
    .kpis :deep(.num) { font-size: 1.4rem; }
    .hotspot-row {
      display: grid;
      grid-template-columns: 1.25fr 1fr;
      gap: 0.6rem;
      align-items: start;
    }
    .details-grid {
      margin-top: 0.15rem;
      display: grid;
      gap: 0.6rem;
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
    .findings-card { grid-column: 1 / -1; }
    .muted { color: var(--text-2, #b8bdd1); }
    .error { color: #ff5470; }
    .score-line { margin: 0; color: var(--text-1, #fff); font-size: 0.88rem; line-height: 1.35; }
    .score-note { margin: 0.28rem 0 0; color: var(--text-2, #b8bdd1); font-size: 0.8rem; line-height: 1.3; }
    .insight-title { margin: 0 0 0.35rem; color: var(--text-1, #fff); font-size: 0.83rem; }
    .impact { margin: 0.35rem 0 0; color: var(--text-2, #b8bdd1); font-size: 0.83rem; }
    .signals {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.5rem;
    }
    .signals li {
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      background: rgba(255,255,255,0.03);
      border-radius: 10px;
      padding: 0.5rem 0.65rem;
      color: var(--text-1, #fff);
      font-size: 0.84rem;
    }
    .hotspots {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.55rem;
    }
    .hotspots li {
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      background: rgba(255,255,255,0.03);
      border-radius: 10px;
      padding: 0.5rem 0.65rem;
      display: grid;
      gap: 0.35rem;
    }
    .head { display: flex; gap: 0.5rem; align-items: baseline; min-width: 0; }
    .path {
      color: var(--text-1, #fff);
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.8rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      flex: 1;
      min-width: 0;
    }
    .meta { color: var(--text-2, #b8bdd1); font-size: 0.74rem; }
    .bar { height: 6px; border-radius: 999px; background: rgba(255,255,255,0.06); overflow: hidden; }
    .fill { display: block; height: 100%; background: linear-gradient(90deg, #ffb547, #ff5470); border-radius: 999px; }
    @container (max-width: 1060px) {
      .top-grid {
        grid-template-columns: 1fr;
        grid-template-areas:
          "exec"
          "kpis"
          "risk";
      }
      .kpis { grid-template-columns: 1fr; }
      .hotspot-row { grid-template-columns: 1fr; }
      .details-grid { grid-template-columns: 1fr; }
      .findings-card { grid-column: auto; }
    }

    @media (max-width: 1020px) {
      .top-grid {
        grid-template-columns: 1fr;
        grid-template-areas:
          "exec"
          "kpis"
          "risk";
      }
      .kpis { grid-template-columns: 1fr; }
      .hotspot-row { grid-template-columns: 1fr; }
      .details-grid { grid-template-columns: 1fr; }
      .findings-card { grid-column: auto; }
    }
  `,
})
export class PerformanceTab {
  private readonly api = inject(DashboardApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly state = toSignal(
    this.route.parent!.paramMap.pipe(
      switchMap(p => {
        const id = p.get('sessionId');
        if (!id) {
          return of<PerfState>({ status: 'ready', metrics: null, findings: [], error: null });
        }

        return timer(0, 2000).pipe(
          switchMap(() => this.api.getOverview(id)),
          map(overview => {
            const status = overview.session.status;
            const isReady = status === 'ready';
            const isError = status === 'error';
            return {
              status: (isReady ? 'ready' : isError ? 'error' : 'loading') as PerfState['status'],
              metrics: isReady ? overview.metrics : null,
              findings: isReady ? overview.findings : [],
              error: isError ? 'Analysis failed. Please retry ingest.' : null,
              sessionStatus: status,
            };
          }),
          takeWhile(v => v.sessionStatus !== 'ready' && v.sessionStatus !== 'error', true),
          map(v => ({
            status: v.status,
            metrics: v.metrics,
            findings: v.findings,
            error: v.error,
          }) as PerfState),
          catchError(err =>
            of<PerfState>({
              status: 'error',
              metrics: null,
              findings: [],
              error: err?.message ?? 'Failed to load performance insights',
            })
          )
        );
      })
    ),
    { initialValue: PERF_INITIAL }
  );

  protected readonly largeFilesDetected = computed(() => {
    const metrics = this.state().metrics;
    if (!metrics) return [];
    return metrics.largestFiles.filter(f => f.lines >= 1000).slice(0, 8);
  });

  protected readonly performanceFindings = computed(() => {
    const metrics = this.state().metrics;
    const findings = this.state().findings;
    if (!metrics) return [] as Finding[];

    const explicit = findings.filter(f =>
      f.category === 'performance' || PERF_HINT_RE.test(`${f.ruleId} ${f.message} ${f.fixHint ?? ''}`)
    );

    const synthetic: Finding[] = [];
    const largeByLines = this.largeFilesDetected();
    if (largeByLines.length > 0) {
      const topList = largeByLines
        .slice(0, 4)
        .map(f => `${f.path} (${f.lines} lines)`)
        .join('; ');
      synthetic.push({
        id: 'P-LARGE-FILES-GROUPED',
        ruleId: 'P-LARGE-FILES',
        severity: largeByLines.some(f => f.lines >= 2000) ? 'high' : 'medium',
        category: 'performance',
        file: '(multiple files)',
        line: 1,
        column: 1,
        message: `Large files detected (${largeByLines.length}) impacting build and runtime maintainability.`,
        fixHint: `Examples: ${topList}. Split modules and lazy-load non-critical paths.`,
      });
    }

    const sourceFiles = metrics.categories.source;
    const testFiles = metrics.categories.test;
    if (sourceFiles > 0 && testFiles === 0) {
      synthetic.push({
        id: 'P-NO-TEST-REGRESSION-GUARD',
        ruleId: 'P-NO-TEST-REGRESSION-GUARD',
        severity: 'medium',
        category: 'performance',
        file: '(repo)',
        line: 1,
        column: 1,
        message: 'No test files detected; performance regressions may ship unnoticed.',
        fixHint: 'Add benchmark/integration tests around critical flows.',
      });
    }

    const dedup = new Map<string, Finding>();
    for (const f of [...explicit, ...synthetic]) {
      dedup.set(`${f.ruleId}|${f.file}|${f.message}`, f);
    }
    return [...dedup.values()].sort((a, b) => severityPenalty(b.severity) - severityPenalty(a.severity));
  });

  protected readonly riskCounts = computed(() => summarizeFindings(this.performanceFindings()));

  protected readonly hotspotCount = computed(() => {
    const metrics = this.state().metrics;
    if (!metrics) return 0;
    return metrics.largestFiles.filter(f => f.lines >= 700 || f.bytes >= 220_000).length;
  });

  protected readonly performanceScore = computed(() => {
    const findings = this.performanceFindings();
    const penalty = findings.reduce((acc, f) => acc + severityPenalty(f.severity), 0);
    return Math.max(0, 100 - penalty);
  });

  protected readonly scoreTone = computed<'default' | 'success' | 'warning' | 'danger'>(() => {
    const score = this.performanceScore();
    if (score >= 85) return 'success';
    if (score >= 65) return 'warning';
    return 'danger';
  });

  protected readonly scoreBand = computed(() => {
    const score = this.performanceScore();
    if (score >= 85) return 'Good';
    if (score >= 65) return 'Moderate';
    if (score >= 40) return 'Poor';
    return 'Critical';
  });

  protected readonly scoreHeadline = computed(() => {
    const score = this.performanceScore();
    const ratio = this.benchmarkRatio();
    if (score >= 85 && ratio >= 2) {
      return `Overall good performance health; however, structural risk exists due to oversized files (~${ratio.toFixed(1)}x above benchmark).`;
    }
    if (score >= 85) return 'Low risk - no major build/runtime bottlenecks detected.';
    if (score >= 65) return 'Medium risk - some hotspots may impact build/runtime performance.';
    if (score >= 40) return 'High risk - likely build/runtime bottlenecks.';
    return 'Critical risk - severe build/runtime bottleneck indicators present.';
  });

  protected readonly executiveSummary = computed(() => {
    const score = this.performanceScore();
    const ratio = this.benchmarkRatio();
    if (score >= 85 && ratio >= 2) {
      return `Summary: This repository is generally healthy, but large file sizes introduce moderate structural risk (${ratio.toFixed(1)}x above benchmark).`;
    }
    if (score >= 85) {
      return 'Summary: This repository is generally healthy with low near-term performance risk.';
    }
    if (score >= 65) {
      return 'Summary: This repository has moderate performance risk and should prioritize hotspot cleanup.';
    }
    return 'Summary: This repository has high performance risk and needs focused remediation in hotspot areas.';
  });

  protected readonly riskSignals = computed(() => {
    const metrics = this.state().metrics;
    if (!metrics) return [] as string[];

    const signals: string[] = [];
    const largest = metrics.largestFiles[0];
    if (largest && largest.lines >= 1500) {
      signals.push(`Largest file is ${largest.lines.toLocaleString()} lines (${largest.path}).`);
    }
    const bigFiles = metrics.largestFiles.filter(f => f.lines >= 900).length;
    if (bigFiles >= 3) {
      signals.push(`${bigFiles} large files exceed 900 lines; candidates for module split.`);
    }
    if (metrics.categories.source > 0 && metrics.categories.test === 0) {
      signals.push('No test files detected; performance regressions are harder to catch early.');
    }
    if (metrics.totalFiles >= 4000) {
      signals.push(`Large repository size (${metrics.totalFiles.toLocaleString()} files) may impact build and CI throughput.`);
    }

    return signals;
  });

  protected readonly insightDrivers = computed(() => {
    const metrics = this.state().metrics;
    if (!metrics) return [] as string[];

    const out: string[] = [];
    const largeFiles = this.largeFilesDetected().length;
    if (largeFiles > 0) out.push(`Multiple oversized files (>1000 lines): ${largeFiles}.`);
    if (metrics.totalFiles >= 2500) out.push(`Large codebase footprint (${metrics.totalFiles.toLocaleString()} files).`);
    if (metrics.categories.test === 0 && metrics.categories.source > 0) out.push('No tests detected for source paths.');
    if (out.length === 0) out.push('No dominant structural performance risk drivers detected.');
    return out;
  });

  protected readonly insightImpacts = computed(() => {
    const score = this.performanceScore();
    if (score >= 85) {
      return [
        'If maintained: low likelihood of CI slowdown.',
        'If maintained: stable runtime performance.',
        'Risk exists if file sizes continue to grow.',
      ];
    }
    return [
      'Slower CI/CD pipelines from heavy parse/build workloads.',
      'Harder debugging and code reviews due to oversized modules.',
      'Reduced scalability as complexity grows without module separation.',
    ];
  });

  protected readonly hotspotRows = computed(() => {
    const metrics = this.state().metrics;
    if (!metrics) return [] as Array<{ path: string; kb: number; lines: number; repoPercent: number }>;

    const rows = [...metrics.largestFiles].sort((a, b) => b.bytes - a.bytes).slice(0, 8);
    const sampleTotal = rows.reduce((acc, r) => acc + r.bytes, 0) || 1;

    return rows.map(r => ({
      path: r.path,
      kb: r.bytes / 1024,
      lines: r.lines,
      repoPercent: Math.max(2, (r.bytes / sampleTotal) * 100),
    }));
  });

  protected readonly benchmarkRatio = computed(() => {
    const rows = this.hotspotRows();
    if (rows.length === 0) return 1;
    const avgLines = rows.reduce((a, r) => a + r.lines, 0) / rows.length;
    return avgLines / 400;
  });

  protected readonly benchmarkNotes = computed(() => {
    const rows = this.hotspotRows();
    if (rows.length === 0) {
      return ['Not enough data yet to compare this repo against healthy baselines.'];
    }

    const avgLines = rows.reduce((a, r) => a + r.lines, 0) / rows.length;
    const healthyAvg = 400;
    const ratio = this.benchmarkRatio();

    return [
      `Compared to healthy repos: average module size baseline is ~${healthyAvg} lines.`,
      `This repo hotspot average is ${Math.round(avgLines).toLocaleString()} lines (~${ratio.toFixed(1)}x higher).`,
      ratio >= 2
        ? 'Result: elevated structural performance risk from oversized files.'
        : 'Result: module sizes are near healthy baseline range.',
    ];
  });
}
