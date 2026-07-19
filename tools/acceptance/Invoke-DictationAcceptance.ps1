#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the automatable C-04 dictation matrix and records manual hardware gaps.

.DESCRIPTION
    Executes only the deterministic dictation test suites listed in the checked-in
    matrix. Native/local-model tests require -IncludeLiveModel and real microphone,
    privacy, accent, and device checks require separate operator evidence. Pending
    hardware rows never masquerade as passing automated tests.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,

    [switch]$PlanOnly,

    [switch]$IncludeLiveModel,

    [string]$ManualEvidencePath,

    [string]$OutputPath,

    [switch]$RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Acceptance.Common.psm1') -Force
$repoRoot = Get-OctadockRepoRoot
$matrixPath = Join-Path $PSScriptRoot 'config/dictation-matrix.json'
$matrix = Read-AcceptanceJson -Path $matrixPath

if (-not $OutputPath) {
    $runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $repoRoot "artifacts/acceptance/dictation/$runId/dictation-matrix.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$resultsDirectory = Join-Path (Split-Path -Parent $OutputPath) 'test-results'
New-AcceptanceDirectory -Path $resultsDirectory

function Read-TrxCounters {
    param([Parameter(Mandatory)][string]$Path)

    [xml]$document = Get-Content -Raw -LiteralPath $Path
    $counters = $document.TestRun.ResultSummary.Counters
    if ($null -eq $counters) {
        throw "TRX file has no ResultSummary counters: $Path"
    }

    return [ordered]@{
        total = [int]$counters.total
        executed = [int]$counters.executed
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        notExecuted = [int]$counters.notExecuted
    }
}

function Invoke-MatrixSuite {
    param(
        [Parameter(Mandatory)]$Suite,
        [Parameter(Mandatory)][string]$Kind
    )

    $projectPath = Join-Path $repoRoot ([string]$Suite.project)
    $trxName = "dictation-$($Suite.id).trx"
    $trxPath = Join-Path $resultsDirectory $trxName
    if (Test-Path -LiteralPath $trxPath) {
        Remove-Item -LiteralPath $trxPath -Force
    }

    $arguments = @(
        'test',
        $projectPath,
        '-c', $Configuration,
        '--filter', [string]$Suite.filter,
        '--logger', "trx;LogFileName=$trxName",
        '--results-directory', $resultsDirectory,
        '--nologo',
        '-p:ContinuousIntegrationBuild=true'
    )
    if ($NoBuild) {
        $arguments += '--no-build'
    }

    Write-Host "==> Dictation $Kind suite: $($Suite.id)" -ForegroundColor Cyan
    & dotnet @arguments | ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE

    $counters = $null
    $reason = $null
    if (Test-Path -LiteralPath $trxPath) {
        try {
            $counters = Read-TrxCounters -Path $trxPath
        }
        catch {
            $reason = $_.Exception.Message
        }
    }
    else {
        $reason = 'dotnet test did not produce the expected TRX evidence.'
    }

    $status = 'fail'
    if ($exitCode -eq 0 -and
        $null -ne $counters -and
        $counters.total -gt 0 -and
        $counters.executed -eq $counters.total -and
        $counters.failed -eq 0 -and
        $counters.notExecuted -eq 0) {
        $status = 'pass'
    }
    elseif ($exitCode -eq 0 -and $null -ne $counters -and $counters.total -eq 0) {
        $reason = 'The selector executed zero tests; coverage cannot be inferred.'
    }
    elseif ($exitCode -eq 0 -and $null -ne $counters -and $counters.notExecuted -gt 0) {
        $reason = 'At least one selected test was not executed; the suite is not accepted as passing.'
    }

    return [pscustomobject][ordered]@{
        id = [string]$Suite.id
        kind = $Kind
        status = $status
        project = [string]$Suite.project
        filter = [string]$Suite.filter
        counters = $counters
        trx = $trxPath
        exitCode = $exitCode
        reason = $reason
        covers = @($Suite.covers)
    }
}

$suiteResults = New-Object System.Collections.Generic.List[object]
if ($PlanOnly) {
    foreach ($suite in @($matrix.automatedSuites)) {
        $suiteResults.Add([pscustomobject][ordered]@{
                id = [string]$suite.id
                kind = 'automated'
                status = 'planned'
                project = [string]$suite.project
                filter = [string]$suite.filter
                counters = $null
                trx = $null
                exitCode = $null
                reason = 'Plan-only run; no test process was started.'
                covers = @($suite.covers)
            })
    }
}
else {
    foreach ($suite in @($matrix.automatedSuites)) {
        $suiteResults.Add((Invoke-MatrixSuite -Suite $suite -Kind 'automated'))
    }
}

$optInResults = New-Object System.Collections.Generic.List[object]
foreach ($suite in @($matrix.optInSuites)) {
    if ($PlanOnly -or -not $IncludeLiveModel) {
        $reason = 'Requires explicit -IncludeLiveModel opt-in.'
        if ($PlanOnly) {
            $reason = 'Plan-only run; local-model live tests were not started.'
        }
        $optInResults.Add([pscustomobject][ordered]@{
                id = [string]$suite.id
                kind = 'opt_in_local_model'
                status = 'pending_opt_in'
                project = [string]$suite.project
                filter = [string]$suite.filter
                counters = $null
                trx = $null
                exitCode = $null
                reason = $reason
                requires = @($suite.requires)
            })
        continue
    }

    $priorLiveValue = [Environment]::GetEnvironmentVariable('OCTADOCK_LIVE_STT')
    try {
        [Environment]::SetEnvironmentVariable('OCTADOCK_LIVE_STT', '1')
        $optInResults.Add((Invoke-MatrixSuite -Suite $suite -Kind 'opt_in_local_model'))
    }
    finally {
        [Environment]::SetEnvironmentVariable('OCTADOCK_LIVE_STT', $priorLiveValue)
    }
}

$manualEvidence = $null
if ($ManualEvidencePath) {
    $ManualEvidencePath = [System.IO.Path]::GetFullPath($ManualEvidencePath)
    $manualEvidence = Read-AcceptanceJson -Path $ManualEvidencePath
}
$manualResults = Resolve-ManualAcceptanceChecks -Checks @($matrix.manualChecks) -EvidenceDocument $manualEvidence -ExpectedGate 'C-04'

$hasFailure = @($suiteResults | Where-Object { $_.status -eq 'fail' }).Count -gt 0
$hasFailure = $hasFailure -or (@($optInResults | Where-Object { $_.status -eq 'fail' }).Count -gt 0)
$hasFailure = $hasFailure -or (@($manualResults | Where-Object { $_.status -in @('fail', 'invalid_evidence') }).Count -gt 0)
$hasPending = @($optInResults | Where-Object { $_.status -eq 'pending_opt_in' }).Count -gt 0
$hasPending = $hasPending -or (@($manualResults | Where-Object { $_.status -in @('pending_manual', 'blocked_manual') }).Count -gt 0)

$overallStatus = 'complete'
if ($PlanOnly) {
    $overallStatus = 'planned'
}
elseif ($hasFailure) {
    $overallStatus = 'failed'
}
elseif ($hasPending) {
    $overallStatus = 'automated_pass_manual_pending'
}

$evidence = New-AcceptanceEnvelope -Gate 'C-04' -Tool 'Invoke-DictationAcceptance.ps1' -RepoRoot $repoRoot
$evidence['configuration'] = $Configuration
$evidence['planOnly'] = [bool]$PlanOnly
$evidence['liveModelRequested'] = [bool]$IncludeLiveModel
$evidence['overallStatus'] = $overallStatus
$evidence['automatedSuites'] = $suiteResults.ToArray()
$evidence['optInSuites'] = $optInResults.ToArray()
$evidence['manualChecks'] = @($manualResults)
$evidence['manualEvidenceSource'] = $ManualEvidencePath

Write-AcceptanceJson -Value $evidence -Path $OutputPath
Write-Host "Dictation acceptance evidence: $OutputPath" -ForegroundColor Green
Write-Host "C-04 status: $overallStatus"

if ($hasFailure) {
    exit 1
}
if ($RequireComplete -and ($PlanOnly -or $hasPending)) {
    exit 2
}
exit 0
