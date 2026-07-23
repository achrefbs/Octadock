#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the versioned STT benchmark against the live local Parakeet engine.

.DESCRIPTION
    Orchestrates the C-04 baseline benchmark:
      1. Generates the corpus from tools/acceptance/stt/manifest.json when the
         cached WAVs are missing (New-SttCorpus.ps1; Windows SAPI synthesis).
      2. Runs the gated xUnit harness (SttCorpusBenchmarkTests) with
         OCTADOCK_LIVE_STT=1. First-ever run downloads the ~640 MB Parakeet
         model into the real %LOCALAPPDATA%\Octadock model store.
      3. Records the machine profile, merges the harness measurements into a
         raw JSON envelope, and writes a short markdown summary.

    Cases without a generatable voice stay explicitly pending; real-microphone
    speaker rows are never produced here. The harness asserts pipeline
    integrity (no dropped/duplicated segments) but accuracy and latency are
    reported as measured, never gated to zero-error claims.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ArtifactsPath,

    [string]$ManifestPath = (Join-Path $PSScriptRoot 'manifest.json'),

    [string]$CorpusDirectory,

    [string]$OutputPath,

    [switch]$NoBuild,

    [switch]$ForceCorpus,

    [switch]$PlanOnly,

    [switch]$RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'Acceptance.Common.psm1') -Force
$repoRoot = Get-OctadockRepoRoot

if (-not $ArtifactsPath) {
    $ArtifactsPath = 'artifacts/build/stt-benchmark'
}
if (-not $CorpusDirectory) {
    $CorpusDirectory = Join-Path $repoRoot 'artifacts/acceptance/stt/corpus'
}
if (-not $OutputPath) {
    $runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $repoRoot "artifacts/acceptance/stt/$runId/stt-benchmark.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$runDirectory = Split-Path -Parent $OutputPath
New-AcceptanceDirectory -Path $runDirectory
New-AcceptanceDirectory -Path $CorpusDirectory
$resultsDirectory = Join-Path $runDirectory 'test-results'
New-AcceptanceDirectory -Path $resultsDirectory
$harnessResultsPath = Join-Path $runDirectory 'harness-results.json'

function Get-MachineProfile {
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem
    $computer = Get-CimInstance Win32_ComputerSystem
    return [ordered]@{
        cpu = [string]$cpu.Name.Trim()
        logicalProcessors = [int]$cpu.NumberOfLogicalProcessors
        totalMemoryBytes = [long]$computer.TotalPhysicalMemory
        osCaption = [string]$os.Caption
        osBuild = [string]$os.BuildNumber
        osVersion = [string]$os.Version
        dotnetSdk = (& dotnet --version)
    }
}

if ($PlanOnly) {
    $plan = New-AcceptanceEnvelope -Gate 'C-04' -Tool 'Invoke-SttBenchmark.ps1' -RepoRoot $repoRoot
    $plan['overallStatus'] = 'planned'
    $plan['machineProfile'] = Get-MachineProfile
    $plan['plan'] = @(
        'Generate corpus WAVs from manifest.json via New-SttCorpus.ps1 (SAPI + noise mixing).',
        'Run SttCorpusBenchmarkTests with OCTADOCK_LIVE_STT=1 against the live local Parakeet model.',
        'Merge per-case metrics with the machine profile; write raw JSON + markdown summary.'
    )
    Write-AcceptanceJson -Value ([pscustomobject]$plan) -Path $OutputPath
    Write-Host "STT benchmark plan: $OutputPath" -ForegroundColor Green
    exit 0
}

# 1. Corpus ---------------------------------------------------------------
$corpusRecordPath = Join-Path $CorpusDirectory 'corpus.generated.json'
if ($ForceCorpus -or -not (Test-Path -LiteralPath $corpusRecordPath)) {
    Write-Host '==> Generating STT corpus (SAPI synthesis)' -ForegroundColor Cyan
    $generator = Join-Path $PSScriptRoot 'New-SttCorpus.ps1'
    $generatorArgs = @{
        ManifestPath = $ManifestPath
        OutputDirectory = $CorpusDirectory
    }
    if ($ForceCorpus) {
        $generatorArgs['Force'] = $true
    }
    & $generator @generatorArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Corpus generation failed with exit code $LASTEXITCODE."
    }
}

# 2. Harness --------------------------------------------------------------
$projectPath = Join-Path $repoRoot 'tests/Octadock.Platform.Windows.Tests/Octadock.Platform.Windows.Tests.csproj'
$trxName = 'stt-corpus-benchmark.trx'
$trxPath = Join-Path $resultsDirectory $trxName
if (Test-Path -LiteralPath $trxPath) {
    Remove-Item -LiteralPath $trxPath -Force
}
if (Test-Path -LiteralPath $harnessResultsPath) {
    Remove-Item -LiteralPath $harnessResultsPath -Force
}

$arguments = @(
    'test',
    $projectPath,
    '-c', $Configuration,
    '--filter', 'FullyQualifiedName~SttCorpusBenchmarkTests',
    '--artifacts-path', $ArtifactsPath,
    '--logger', "trx;LogFileName=$trxName",
    '--results-directory', $resultsDirectory,
    '--nologo'
)
if ($NoBuild) {
    $arguments += '--no-build'
}

$priorLive = [Environment]::GetEnvironmentVariable('OCTADOCK_LIVE_STT')
$priorManifest = [Environment]::GetEnvironmentVariable('OCTADOCK_STT_MANIFEST')
$priorCorpus = [Environment]::GetEnvironmentVariable('OCTADOCK_STT_CORPUS')
$priorResults = [Environment]::GetEnvironmentVariable('OCTADOCK_STT_RESULTS')
$testExit = 0
try {
    [Environment]::SetEnvironmentVariable('OCTADOCK_LIVE_STT', '1')
    [Environment]::SetEnvironmentVariable('OCTADOCK_STT_MANIFEST', [System.IO.Path]::GetFullPath($ManifestPath))
    [Environment]::SetEnvironmentVariable('OCTADOCK_STT_CORPUS', [System.IO.Path]::GetFullPath($CorpusDirectory))
    [Environment]::SetEnvironmentVariable('OCTADOCK_STT_RESULTS', $harnessResultsPath)

    Write-Host '==> Running STT corpus benchmark harness (live Parakeet)' -ForegroundColor Cyan
    & dotnet @arguments | ForEach-Object { Write-Host $_ }
    $testExit = $LASTEXITCODE
}
finally {
    [Environment]::SetEnvironmentVariable('OCTADOCK_LIVE_STT', $priorLive)
    [Environment]::SetEnvironmentVariable('OCTADOCK_STT_MANIFEST', $priorManifest)
    [Environment]::SetEnvironmentVariable('OCTADOCK_STT_CORPUS', $priorCorpus)
    [Environment]::SetEnvironmentVariable('OCTADOCK_STT_RESULTS', $priorResults)
}

if (-not (Test-Path -LiteralPath $harnessResultsPath)) {
    throw "The benchmark harness did not produce results (dotnet test exit $testExit): $harnessResultsPath"
}

# 3. Merge + report -------------------------------------------------------
$harness = Read-AcceptanceJson -Path $harnessResultsPath
$corpusRecord = Read-AcceptanceJson -Path $corpusRecordPath

$evidence = New-AcceptanceEnvelope -Gate 'C-04' -Tool 'Invoke-SttBenchmark.ps1' -RepoRoot $repoRoot
$evidence['configuration'] = $Configuration
$evidence['machineProfile'] = Get-MachineProfile
$evidence['corpus'] = [ordered]@{
    manifest = [System.IO.Path]::GetFullPath($ManifestPath)
    manifestSha256 = [string]$corpusRecord.manifestSha256
    corpusDirectory = [System.IO.Path]::GetFullPath($CorpusDirectory)
    generatedUtc = [string]$corpusRecord.generatedUtc
    voices = @($corpusRecord.voices)
}
$evidence['dotnetTestExitCode'] = $testExit
$evidence['trx'] = $trxPath
$evidence['timing'] = $harness.timing
$evidence['cases'] = @($harness.cases)

$measured = @($harness.cases | Where-Object { $_.status -eq 'measured' })
$pending = @($harness.cases | Where-Object { $_.status -eq 'pending' })
$integrityFailures = @($measured | Where-Object {
        $null -ne $_.streaming -and
        ([int]$_.streaming.segmentsDropped -gt 0 -or [int]$_.streaming.segmentsDuplicated -gt 0)
    })

$overallStatus = 'complete'
if ($testExit -ne 0 -or $integrityFailures.Count -gt 0) {
    $overallStatus = 'failed'
}
elseif ($pending.Count -gt 0) {
    $overallStatus = 'measured_with_pending_cases'
}
$evidence['overallStatus'] = $overallStatus
$evidence['pendingCaseIds'] = @($pending | ForEach-Object { [string]$_.id })

Write-AcceptanceJson -Value ([pscustomobject]$evidence) -Path $OutputPath

# Markdown summary ---------------------------------------------------------
$summaryPath = Join-Path $runDirectory 'SUMMARY.md'
try {
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# STT benchmark summary')
$lines.Add('')
$lines.Add("- Run (UTC): $($evidence.generatedUtc)")
$lines.Add("- Status: $overallStatus")
$lines.Add("- Machine: $($evidence.machineProfile.cpu); $($evidence.machineProfile.logicalProcessors) logical processors; " +
    ('{0:N1}' -f ($evidence.machineProfile.totalMemoryBytes / 1GB)) + " GB RAM; Windows build $($evidence.machineProfile.osBuild)")
$lines.Add("- Engine: local Parakeet ($($harness.engineModel)); corpus schema v$($harness.corpusSchemaVersion)")
$lines.Add('')
$lines.Add('## Headline (batch decode)')
$lines.Add('')
$lines.Add('- Cold start (recognizer build + first decode): ' + ('{0:N0}' -f [double]$harness.timing.coldStartMs) + ' ms')
$lines.Add('- Recognizer build alone: ' + ('{0:N0}' -f [double]$harness.timing.recognizerBuildMs) + ' ms')
$warmRtfs = @($measured | Where-Object { $null -ne $_.batch } | ForEach-Object { [double]$_.batch.rtf })
if ($warmRtfs.Count -gt 0) {
    $medianRtf = Get-Percentile -Values $warmRtfs -Percentile 50
    $lines.Add('- Warm decode RTF (median): ' + ('{0:N3}' -f $medianRtf))
}
$lines.Add('')
$lines.Add('## WER by category (batch)')
$lines.Add('')
$lines.Add('| Category | Cases | Mean WER | Mean RTF |')
$lines.Add('| --- | --- | --- | --- |')
$byCategory = $measured | Where-Object { $null -ne $_.batch } | Group-Object { [string]$_.category }
foreach ($group in ($byCategory | Sort-Object Name)) {
    $meanWer = ($group.Group | ForEach-Object { [double]$_.batch.wer } | Measure-Object -Average).Average
    $meanRtf = ($group.Group | ForEach-Object { [double]$_.batch.rtf } | Measure-Object -Average).Average
    # NOTE: multi-operand -f cannot live inside a method call (argument mode
    # splits on commas); format first, then add.
    $rowText = '| {0} | {1} | {2:P1} | {3:N3} |' -f $group.Name, $group.Count, $meanWer, $meanRtf
    $lines.Add($rowText)
}
$lines.Add('')
$lines.Add('## Streaming integrity')
$lines.Add('')
$streamed = @($measured | Where-Object { $null -ne $_.streaming })
$totalDropped = ($streamed | ForEach-Object { [int]$_.streaming.segmentsDropped } | Measure-Object -Sum).Sum
$totalDuplicated = ($streamed | ForEach-Object { [int]$_.streaming.segmentsDuplicated } | Measure-Object -Sum).Sum
$finalize = @($streamed | ForEach-Object { [double]$_.streaming.endOfSpeechToFinalMs })
$lines.Add("- Streaming cases: $($streamed.Count); dropped segments: $totalDropped; duplicated segments: $totalDuplicated")
if ($finalize.Count -gt 0) {
    $lines.Add('- End-of-speech to final transcript (median): ' + ('{0:N0}' -f (Get-Percentile -Values $finalize -Percentile 50)) + ' ms')
}
$lines.Add('')
$lines.Add('## Pending rows (never fabricated)')
$lines.Add('')
foreach ($row in $pending) {
    $lines.Add("- $($row.id): $($row.reason)")
}
$lines.Add('- real-microphone-speaker: requires a human speaker and a physical microphone; covered only by the manual C-04 matrix rows.')
$lines.Add('')
$lines.Add('Numbers are measured on the machine above; WER uses SAPI-synthesized speech, not human speakers.')
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($summaryPath, ($lines -join [Environment]::NewLine) + [Environment]::NewLine, $utf8)
}
catch {
    Write-Host "SUMMARY DIAG: $($_.Exception.Message)"
    Write-Host "SUMMARY DIAG AT: $($_.InvocationInfo.PositionMessage)"
    throw
}

Write-Host "STT benchmark evidence: $OutputPath" -ForegroundColor Green
Write-Host "STT benchmark summary:  $summaryPath" -ForegroundColor Green
Write-Host "Status: $overallStatus"

if ($overallStatus -eq 'failed') {
    exit 1
}
if ($RequireComplete -and $pending.Count -gt 0) {
    exit 2
}
exit 0
