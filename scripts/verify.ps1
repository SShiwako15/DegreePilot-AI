$ErrorActionPreference = 'Stop'
$apiPython = if ($env:OS -eq 'Windows_NT') { 'services/api/.venv/Scripts/python.exe' } else { 'services/api/.venv/bin/python' }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & $apiPython scripts/verify.py
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed. See the command output above.' }
} finally { Pop-Location }
