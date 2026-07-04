#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the Octadock test suites with code coverage.

.DESCRIPTION
    Runs `dotnet test` for the solution (or, with -CrossPlatformOnly, just the
    net8.0 Core and Data test projects) using the coverlet collector. Writes TRX
    results and a Cobertura coverage report under artifacts/test-results/.

.PARAMETER Configuration
    Build configuration: Debug (default) or Release.

.PARAMETER CrossPlatformOnly
    Test only Octadock.Core.Tests and Octadock.Data.Tests (the projects that run
    on any OS). Use this on non-Windows hosts.

.EXAMPLE
    ./build/test.ps1

.EXAMPLE
    ./build/test.ps1 -Configuration Release -CrossPlatformOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$CrossPlatformOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ResultsDir = Join-Path $RepoRoot 'artifacts/test-results'
$ciArgs = '-p:ContinuousIntegrationBuild=true'

if (Test-Path $ResultsDir) {
    Remove-Item $ResultsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null

# The test targets: the whole solution on Windows, or just the cross-platform
# test projects when requested.
$targets =
    if ($CrossPlatformOnly) {
        @(
            (Join-Path $RepoRoot 'tests/Octadock.Core.Tests/Octadock.Core.Tests.csproj'),
            (Join-Path $RepoRoot 'tests/Octadock.Data.Tests/Octadock.Data.Tests.csproj')
        )
    }
    else {
        @((Join-Path $RepoRoot 'Octadock.sln'))
    }

Write-Host "Octadock tests" -ForegroundColor Green
Write-Host "  Configuration : $Configuration"
Write-Host "  Results       : $ResultsDir"
Write-Host "  dotnet        : $((dotnet --version))"

foreach ($target in $targets) {
    Write-Host ''
    Write-Host "==> Test: $target" -ForegroundColor Cyan
    dotnet test $target `
        -c $Configuration `
        $ciArgs `
        --logger 'trx' `
        --results-directory $ResultsDir `
        --collect:'XPlat Code Coverage' `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura

    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed for $target (exit code $LASTEXITCODE)."
    }
}

# Surface where coverage landed (coverlet writes coverage.cobertura.xml under a
# per-run GUID folder inside the results directory).
$coverage = Get-ChildItem -Path $ResultsDir -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue
if ($coverage) {
    Write-Host ''
    Write-Host "Coverage reports:" -ForegroundColor Green
    $coverage | ForEach-Object { Write-Host "  $($_.FullName)" }
}
else {
    Write-Warning "No coverage report was produced."
}

Write-Host ''
Write-Host "Tests succeeded." -ForegroundColor Green
