$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$previousConnection = $env:DEGREEPILOT_TEST_DATABASE
try {
    # Resolve only the Compose database settings; never print configuration or credentials.
    # The integration test creates and drops its own GUID-named database.
    if (-not $env:DEGREEPILOT_TEST_DATABASE) {
        $composeJson = docker compose config --format json
        if ($LASTEXITCODE -ne 0) { throw 'Could not resolve local Compose settings.' }
        $databaseEnvironment = ($composeJson | ConvertFrom-Json).services.postgres.environment
        function Quote-ConnectionValue([string] $value) { '"' + $value.Replace('"', '""') + '"' }
        $env:DEGREEPILOT_TEST_DATABASE = 'Host=127.0.0.1;Port=5432;Database=' +
            (Quote-ConnectionValue $databaseEnvironment.POSTGRES_DB) + ';Username=' +
            (Quote-ConnectionValue $databaseEnvironment.POSTGRES_USER) + ';Password=' +
            (Quote-ConnectionValue $databaseEnvironment.POSTGRES_PASSWORD)
    }
    dotnet test DegreePilot.slnx --no-restore --filter FullyQualifiedName~PostgresTests
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL integration test failed.' }
} finally {
    $env:DEGREEPILOT_TEST_DATABASE = $previousConnection
    Pop-Location
}
