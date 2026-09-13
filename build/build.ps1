#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the canonical local Octadock build and release-readiness gates.

.DESCRIPTION
    Validates version/copy/web contracts, restores, builds, and tests the desktop
    solution and internal harness. A Release run also publishes
    the self-contained app/CLI and enforces the public-artifact boundary. Sets
    ContinuousIntegrationBuild=true so the local build matches CI. Requires
    Windows for the net8.0-windows projects. On non-Windows hosts, use
    build/build.sh, which builds only the cross-platform projects.

.PARAMETER Configuration
    Build configuration: Debug (default) or Release.

.PARAMETER Pack
    Also run `dotnet pack` after a successful build/test.

.PARAMETER SkipTests
    Skip the test step.

.PARAMETER SkipWebValidation
    Skip the locked npm/Playwright website gate. Intended only for focused local
    desktop iteration; never use it as Phase-1 acceptance evidence.

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

    [switch]$SkipTests,

    [switch]$SkipWebValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Repo root is the parent of this script's folder.
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot 'Octadock.sln'
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'
$InternalTests = Join-Path $RepoRoot 'tests/internal/Octadock.WorkflowIntelligence.Internal.Tests/Octadock.WorkflowIntelligence.Internal.Tests.csproj'
$VersionScript = Join-Path $PSScriptRoot 'version.ps1'
$CopyHonestyScript = Join-Path $PSScriptRoot 'copy-honesty-gate.ps1'
$WebValidationScript = Join-Path $PSScriptRoot 'validate-web.ps1'
$PublicBoundaryScript = Join-Path $PSScriptRoot 'public-artifact-boundary.ps1'
$AppProject = Join-Path $RepoRoot 'src/Octadock.App/Octadock.App.csproj'
$CliProject = Join-Path $RepoRoot 'src/Octadock.Cli/Octadock.Cli.csproj'
$PublicGateDir = Join-Path $ArtifactsDir 'build-gate/publish'

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

Invoke-Step 'Validate version metadata' {
    & $VersionScript -Check
}

Invoke-Step 'Copy-honesty gate' {
    & $CopyHonestyScript
}

if (-not $SkipWebValidation) {
    Invoke-Step 'Website validation' {
        & $WebValidationScript
    }
}
else {
    Write-Host ''
    Write-Host '==> Website validation (skipped)' -ForegroundColor Yellow
}

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


    Invoke-Step "Internal harness tests ($Configuration)" {
        dotnet test $InternalTests -c $Configuration $ciArgs `
            --logger 'trx;LogFileName=workflow-intelligence-internal.trx' `
            --results-directory (Join-Path $ArtifactsDir 'test-results')
    }
}
else {
    Write-Host ''
    Write-Host "==> Test (skipped)" -ForegroundColor Yellow
}

if ($Configuration -eq 'Release') {
    $artifactsFull = [System.IO.Path]::GetFullPath($ArtifactsDir).TrimEnd('\', '/') +
        [System.IO.Path]::DirectorySeparatorChar
    $publishFull = [System.IO.Path]::GetFullPath($PublicGateDir)
    if (-not $publishFull.StartsWith($artifactsFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a publish directory outside '$artifactsFull': '$publishFull'."
    }

    if (Test-Path -LiteralPath $PublicGateDir) {
        Remove-Item -LiteralPath $PublicGateDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PublicGateDir -Force | Out-Null

    Invoke-Step 'Publish app (self-contained win-x64)' {
        dotnet publish $AppProject -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true $ciArgs `
            -o (Join-Path $PublicGateDir 'octadock')
    }

    Invoke-Step 'Publish CLI (self-contained win-x64)' {
        dotnet publish $CliProject -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true $ciArgs `
            -o (Join-Path $PublicGateDir 'octadock/cli')
    }

    Invoke-Step 'Public artifact boundary' {
        & $PublicBoundaryScript -ArtifactPath (Join-Path $PublicGateDir 'octadock')
    }
}
else {
    Invoke-Step 'Public source boundary' {
        & $PublicBoundaryScript
    }
}

if ($Pack) {
    Invoke-Step "Pack ($Configuration)" {
        dotnet pack $Solution -c $Configuration --no-build $ciArgs `
            --output (Join-Path $ArtifactsDir 'packages')
    }
}

Write-Host ''
Write-Host "Build succeeded." -ForegroundColor Green
