$ErrorActionPreference = 'Stop'
$npmCommand = if ($env:OS -eq 'Windows_NT') { 'npm.cmd' } else { 'npm' }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet build DegreePilot.slnx --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    dotnet test DegreePilot.slnx --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    dotnet format DegreePilot.slnx --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Backend format check failed.' }
    Push-Location apps/web
    try {
        foreach ($task in @('lint', 'test', 'build')) {
            & $npmCommand run $task
            if ($LASTEXITCODE -ne 0) { throw "Frontend $task failed." }
        }
    } finally { Pop-Location }
    $plannerPython = if ($env:OS -eq 'Windows_NT') { 'services/ai/.venv/Scripts/python.exe' } else { 'services/ai/.venv/bin/python' }
    $plannerReady = Test-Path -LiteralPath $plannerPython
    if ($plannerReady) {
        & $plannerPython --version 2>$null | Out-Null
        $plannerReady = $LASTEXITCODE -eq 0
    }
    if ($plannerReady) {
        & $plannerPython -m pytest -q -c services/ai/pyproject.toml services/ai/tests
        if ($LASTEXITCODE -ne 0) { throw 'Planner tests failed.' }
        & $plannerPython -m ruff check services/ai/app services/ai/tests
        if ($LASTEXITCODE -ne 0) { throw 'Planner lint failed.' }
        & $plannerPython -m ruff format --check services/ai/app services/ai/tests
        if ($LASTEXITCODE -ne 0) { throw 'Planner format check failed.' }
        & $plannerPython scripts/security_check.py
        if ($LASTEXITCODE -ne 0) { throw 'Privacy scan failed.' }
    } else {
        Write-Host 'Planner virtual environment is unavailable; checking the running Compose planner.'
        docker compose exec -T planner python -m pytest -q -p no:cacheprovider
        if ($LASTEXITCODE -ne 0) { throw 'Planner tests failed (Compose fallback).' }
        docker compose exec -T planner python -m ruff check app tests
        if ($LASTEXITCODE -ne 0) { throw 'Planner lint failed (Compose fallback).' }
        docker compose exec -T planner python -m ruff format --check app tests
        if ($LASTEXITCODE -ne 0) { throw 'Planner format check failed (Compose fallback).' }
        python scripts/security_check.py
        if ($LASTEXITCODE -ne 0) { throw 'Privacy scan failed; local Python is required for the source scan.' }
    }
} finally { Pop-Location }
