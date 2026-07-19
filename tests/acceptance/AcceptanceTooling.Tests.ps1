#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$toolsRoot = Join-Path $repoRoot 'tools/acceptance'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('octadock-acceptance-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$assertionCount = 0

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    $script:assertionCount++
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function Invoke-ChildTool {
    param(
        [Parameter(Mandatory)][string]$Script,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][int]$ExpectedExitCode
    )

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments
    $exitCode = $LASTEXITCODE
    Assert-True -Condition ($exitCode -eq $ExpectedExitCode) -Message "$Script exit code was $exitCode; expected $ExpectedExitCode."
}

try {
    $dictationOutput = Join-Path $testRoot 'dictation-plan.json'
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Invoke-DictationAcceptance.ps1') -Arguments @(
        '-PlanOnly',
        '-OutputPath', $dictationOutput
    ) -ExpectedExitCode 0
    $dictation = Get-Content -Raw -LiteralPath $dictationOutput | ConvertFrom-Json
    Assert-True -Condition ($dictation.overallStatus -eq 'planned') -Message 'Dictation plan status must be planned.'
    Assert-True -Condition (@($dictation.automatedSuites).Count -eq 3) -Message 'Dictation plan must retain three deterministic suites.'
    Assert-True -Condition (@($dictation.manualChecks | Where-Object { $_.status -eq 'pending_manual' }).Count -gt 0) -Message 'Hardware dictation checks must remain pending.'

    $performancePlanOutput = Join-Path $testRoot 'performance-plan.json'
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Measure-PerformanceBaseline.ps1') -Arguments @(
        '-PlanOnly',
        '-OutputPath', $performancePlanOutput
    ) -ExpectedExitCode 0
    $performancePlan = Get-Content -Raw -LiteralPath $performancePlanOutput | ConvertFrom-Json
    Assert-True -Condition ($performancePlan.overallStatus -eq 'planned_not_measured') -Message 'Performance plan must not contain invented measurements.'

    $performanceOutput = Join-Path $testRoot 'performance-tooling-test.json'
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Measure-PerformanceBaseline.ps1') -Arguments @(
        '-ProcessPath', (Join-Path $PSHOME 'powershell.exe'),
        '-ProcessArguments', '-NoExit',
        '-CreateNoWindow',
        '-StartupReadyMode', 'ProcessStarted',
        '-WarmupSeconds', '0',
        '-IdleSeconds', '1',
        '-SampleIntervalMilliseconds', '250',
        '-SkipGpu',
        '-AllowNonOctadockProcessForToolingTest',
        '-ForceTerminateStartedProcess',
        '-OutputPath', $performanceOutput
    ) -ExpectedExitCode 0
    $performance = Get-Content -Raw -LiteralPath $performanceOutput | ConvertFrom-Json
    Assert-True -Condition ($performance.overallStatus -eq 'tooling_test_only') -Message 'Non-product measurement must be labeled tooling_test_only.'
    Assert-True -Condition ($performance.metrics.idleCpuNormalizedPercent.status -eq 'measured') -Message 'Tooling test must produce an idle CPU observation.'

    $soakOutput = Join-Path $testRoot 'soak-plan.json'
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Invoke-SoakAcceptance.ps1') -Arguments @(
        '-PlanOnly',
        '-OutputPath', $soakOutput
    ) -ExpectedExitCode 0
    $soak = Get-Content -Raw -LiteralPath $soakOutput | ConvertFrom-Json
    Assert-True -Condition ($soak.overallStatus -eq 'planned_hardware_pending') -Message 'Soak plan must remain hardware pending.'
    Assert-True -Condition (($soak.plannedWorkflows | Where-Object { $_.id -eq 'capture' }).iterations -eq 50) -Message 'Default soak profile must plan 50 capture cycles.'
    Assert-True -Condition (($soak.plannedWorkflows | Where-Object { $_.id -eq 'scrolling' }).iterations -eq 10) -Message 'Default soak profile must plan 10 scrolling sessions.'

    $staticOutput = Join-Path $testRoot 'wpf-static.json'
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Test-WpfStaticAcceptance.ps1') -Arguments @(
        '-NoBaseline',
        '-OutputPath', $staticOutput
    ) -ExpectedExitCode 1
    $static = Get-Content -Raw -LiteralPath $staticOutput | ConvertFrom-Json
    Assert-True -Condition ($static.overallStatus -eq 'fail_new_findings') -Message 'Known unremediated WPF findings must keep the static gate red.'
    Assert-True -Condition ($static.summary.newFindings -gt 0) -Message 'Static evidence must enumerate the current debt.'

    Write-Host "Acceptance tooling self-test passed: $assertionCount assertions." -ForegroundColor Green
    exit 0
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
