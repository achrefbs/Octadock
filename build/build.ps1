#Requires -Version 5.1
<#
.SYNOPSIS
    Restores, builds, and tests the Octadock solution.

.DESCRIPTION
    Runs `dotnet restore`, `dotnet build`, and `dotnet test` for Octadock.sln.
    Sets ContinuousIntegrationBuild=true so the local build matches CI
    (deterministic build, warnings treated as errors). Intended to build the FULL
    solution and therefore requires Windows for the net8.0-windows projects
    (Octadock.App, Octadock.Platform.Windows, Octadock.Cli). On non-Windows hosts,
    use build/build.sh, which builds only the cross-platform projects.

.PARAMETER Configuration
    Build configuration: Debug (default) or Release.

.PARAMETER Pack
    Also run `dotnet pack` after a successful build/test.

.PARAMETER SkipTests
    Skip the test step.

.EXAMPLE
    ./build/build.ps1

.EXAMPLE
    ./build/build.ps1 -Configuration Release -Pack
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Pack,

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Repo root is the parent of this script's folder.
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot 'Octadock.sln'
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'

# CI parity: deterministic build, warnings as errors (see Directory.Build.props).
$ciArgs = '-p:ContinuousIntegrationBuild=true'

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Octadock build" -ForegroundColor Green
Write-Host "  Configuration : $Configuration"
Write-Host "  Solution      : $Solution"
Write-Host "  dotnet        : $((dotnet --version))"

Invoke-Step 'Restore' {
    dotnet restore $Solution
}

Invoke-Step "Build ($Configuration)" {
    dotnet build $Solution -c $Configuration --no-restore $ciArgs
}

if (-not $SkipTests) {
    Invoke-Step "Test ($Configuration)" {
        dotnet test $Solution -c $Configuration --no-build $ciArgs `
            --logger 'trx' `
            --results-directory (Join-Path $ArtifactsDir 'test-results')
    }
}
else {
    Write-Host ''
    Write-Host "==> Test (skipped)" -ForegroundColor Yellow
}

if ($Pack) {
    Invoke-Step "Pack ($Configuration)" {
        dotnet pack $Solution -c $Configuration --no-build $ciArgs `
            --output (Join-Path $ArtifactsDir 'packages')
    }
}

Write-Host ''
Write-Host "Build succeeded." -ForegroundColor Green
