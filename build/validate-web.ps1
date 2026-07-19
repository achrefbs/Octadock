#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the locked website validation toolchain and runs every web gate.

.DESCRIPTION
    Reproduces the website checks from a fresh checkout: npm ci, the pinned
    Chromium runtime, JavaScript syntax, link/asset and fallback contracts,
    automated accessibility, and browser smoke tests.

.PARAMETER SkipBrowserInstall
    Skip Playwright's cached Chromium installation. Useful only when the exact
    browser revision from package-lock.json is already installed.
#>
[CmdletBinding()]
param(
    [switch]$SkipBrowserInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WebRoot = Join-Path $RepoRoot 'web'

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

Push-Location $WebRoot
try {
    Write-Host 'Octadock website validation' -ForegroundColor Green
    Write-Host "  Node : $(& node --version)"
    Write-Host "  npm  : $(& npm --version)"

    Invoke-Step 'Install locked dependencies' {
        npm ci --no-audit --no-fund
    }

    if (-not $SkipBrowserInstall) {
        Invoke-Step 'Install pinned Chromium' {
            npx --no-install playwright install chromium
        }
    }

    Invoke-Step 'Validate website' {
        npm run validate
    }
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host 'Website validation passed.' -ForegroundColor Green
