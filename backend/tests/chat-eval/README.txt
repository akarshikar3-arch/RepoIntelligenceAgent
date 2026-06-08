CHATBOT EVAL TEMPLATE

1) Start backend API
   - expected base URL: http://localhost:5000

2) Run evaluation
   - from this folder (backend/tests/chat-eval):
     powershell -ExecutionPolicy Bypass -File .\run-chat-eval.ps1

3) Optional parameters
   - custom repo:
     powershell -ExecutionPolicy Bypass -File .\run-chat-eval.ps1 -RepoUrl "https://github.com/owner/repo"

   - custom API URL:
     powershell -ExecutionPolicy Bypass -File .\run-chat-eval.ps1 -ApiBaseUrl "http://localhost:5000"

   - custom review profile:
     powershell -ExecutionPolicy Bypass -File .\run-chat-eval.ps1 -ReviewProfile strict

4) Outputs
   - out-YYYYMMDD-HHMMSS/results.json
   - out-YYYYMMDD-HHMMSS/results.csv
   - out-YYYYMMDD-HHMMSS/summary.txt

5) How to add tests
   - edit cases.gitleaks.json
   - each case supports:
     id, category, question,
     expectedGrounded,
     expectCannotFind,
     minCitations, maxCitations,
     mustContainAll, mustContainAny, mustNotContain

6) CI behavior
   - script exits with code 1 if any case fails
   - script exits with code 0 if all cases pass
