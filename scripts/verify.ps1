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
} finally { Pop-Location }
