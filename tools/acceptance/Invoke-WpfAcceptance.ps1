#Requires -Version 5.1
<#
.SYNOPSIS
    Combines C-06 static WPF checks with operator-supplied manual evidence.

.DESCRIPTION
    Runs the deterministic XAML regression validator and merges the checked-in
    keyboard, screen-reader, high-contrast, motion/transparency, and DPI matrix.
    Missing manual rows remain pending. Use -RequireComplete only on a real
    Windows acceptance machine with a fully populated evidence document.
#>
[CmdletBinding()]
param(
    [string]$ManualEvidencePath,

    [string]$OutputPath,

    [switch]$RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Acceptance.Common.psm1') -Force
$repoRoot = Get-OctadockRepoRoot
$matrix = Read-AcceptanceJson -Path (Join-Path $PSScriptRoot 'config/wpf-manual-matrix.json')

if (-not $OutputPath) {
    $runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $repoRoot "artifacts/acceptance/wpf/$runId/wpf-acceptance.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$staticOutput = Join-Path (Split-Path -Parent $OutputPath) 'wpf-static.json'

& (Join-Path $PSScriptRoot 'Test-WpfStaticAcceptance.ps1') -RepoRoot $repoRoot -OutputPath $staticOutput -NoProcessExit
$staticEvidence = $null
if (Test-Path -LiteralPath $staticOutput -PathType Leaf) {
    $staticEvidence = Read-AcceptanceJson -Path $staticOutput
}
$staticExitCode = 1
if ($null -ne $staticEvidence -and [string]$staticEvidence.overallStatus -ne 'fail_new_findings') {
    $staticExitCode = 0
}

$manualEvidence = $null
if ($ManualEvidencePath) {
    $ManualEvidencePath = [System.IO.Path]::GetFullPath($ManualEvidencePath)
    $manualEvidence = Read-AcceptanceJson -Path $ManualEvidencePath
}
$manualResults = Resolve-ManualAcceptanceChecks -Checks @($matrix.manualChecks) -EvidenceDocument $manualEvidence -ExpectedGate 'C-06'

$hasManualFailure = @($manualResults | Where-Object { $_.status -in @('fail', 'invalid_evidence') }).Count -gt 0
$hasManualPending = @($manualResults | Where-Object { $_.status -in @('pending_manual', 'blocked_manual') }).Count -gt 0

$overallStatus = 'complete'
if ($staticExitCode -ne 0 -or $null -eq $staticEvidence -or $hasManualFailure) {
    $overallStatus = 'failed'
}
elseif ($hasManualPending) {
    if ($staticEvidence.overallStatus -eq 'regression_guard_pass_known_debt') {
        $overallStatus = 'static_regression_pass_known_debt_manual_pending'
    }
    else {
        $overallStatus = 'static_pass_manual_pending'
    }
}
elseif ($staticEvidence.overallStatus -eq 'regression_guard_pass_known_debt') {
    $overallStatus = 'manual_complete_static_known_debt'
}

$evidence = New-AcceptanceEnvelope -Gate 'C-06' -Tool 'Invoke-WpfAcceptance.ps1' -RepoRoot $repoRoot
$evidence['overallStatus'] = $overallStatus
$evidence['surfaces'] = @($matrix.surfaces)
$evidence['staticEvidence'] = $staticOutput
$evidence['staticStatus'] = $(if ($null -eq $staticEvidence) { 'missing' } else { [string]$staticEvidence.overallStatus })
$evidence['manualChecks'] = @($manualResults)
$evidence['manualEvidenceSource'] = $ManualEvidencePath
$evidence['standard'] = 'WCAG 2.1 AA plus Windows accessibility and mixed-DPI acceptance'

Write-AcceptanceJson -Value $evidence -Path $OutputPath
Write-Host "WPF acceptance evidence: $OutputPath" -ForegroundColor Green
Write-Host "C-06 status: $overallStatus"

if ($overallStatus -eq 'failed') {
    exit 1
}
if ($RequireComplete -and $overallStatus -ne 'complete') {
    exit 2
}
exit 0
