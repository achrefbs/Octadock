#Requires -Version 5.1
<#
.SYNOPSIS
    Records a machine-readable C-05 process and capture-latency baseline.

.DESCRIPTION
    Measures startup readiness, idle CPU, memory, handles, GUI resources, and
    Windows GPU-engine counters for an explicitly supplied process. An optional
    capture probe measures time from an explicit command to a new stable artifact.
    The tool records observations only; it does not invent pass thresholds or call
    a partial result a completed C-05 baseline.
#>
[CmdletBinding()]
param(
    [string]$ProcessPath,

    [string]$ProcessArguments = '',

    [string]$WorkingDirectory,

    [switch]$CreateNoWindow,

    [ValidateRange(1, 2147483647)]
    [int]$AttachProcessId,

    [ValidateSet('ProcessStarted', 'InputIdle', 'MainWindow', 'FilePattern')]
    [string]$StartupReadyMode = 'FilePattern',

    [string]$StartupReadyFilePath,

    [string]$StartupReadyPattern = 'Octadock startup complete',

    [ValidateRange(1, 120)]
    [int]$StartupTimeoutSeconds = 30,

    [ValidateRange(0, 60)]
    [int]$WarmupSeconds = 3,

    [ValidateRange(1, 300)]
    [int]$IdleSeconds = 10,

    [ValidateRange(100, 5000)]
    [int]$SampleIntervalMilliseconds = 500,

    [switch]$SkipGpu,

    [ValidateRange(0, 100)]
    [int]$CaptureIterations = 0,

    [string]$CaptureCommandPath,

    [string[]]$CaptureCommandArguments = @(),

    [string]$CaptureArtifactDirectory,

    [ValidateRange(1, 120)]
    [int]$CaptureTimeoutSeconds = 30,

    [switch]$EnableDesktopCapture,

    [switch]$LeaveRunning,

    [string]$StopCommandPath,

    [string[]]$StopCommandArguments = @(),

    [switch]$ForceTerminateStartedProcess,

    [switch]$AllowNonOctadockProcessForToolingTest,

    [switch]$PlanOnly,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Acceptance.Common.psm1') -Force
$repoRoot = Get-OctadockRepoRoot

if (-not $OutputPath) {
    $runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $repoRoot "artifacts/acceptance/performance/$runId/performance-baseline.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if ($ProcessPath -and $AttachProcessId) {
    throw 'Specify ProcessPath or AttachProcessId, not both.'
}
if (-not $PlanOnly -and -not $ProcessPath -and -not $AttachProcessId) {
    throw 'A live measurement requires ProcessPath or AttachProcessId. Use -PlanOnly to emit an unexecuted plan.'
}
if (-not $PlanOnly -and $ProcessPath -and $StartupReadyMode -eq 'FilePattern' -and -not $StartupReadyFilePath) {
    throw 'StartupReadyMode FilePattern requires StartupReadyFilePath.'
}
if ($CaptureIterations -gt 0) {
    if (-not $EnableDesktopCapture) {
        throw 'Capture latency is opt-in. Re-run with -EnableDesktopCapture after placing safe test content on screen.'
    }
    if (-not $CaptureCommandPath -or -not $CaptureArtifactDirectory) {
        throw 'CaptureIterations requires CaptureCommandPath and CaptureArtifactDirectory.'
    }
    if ([System.IO.Path]::GetExtension($CaptureCommandPath) -ne '.exe') {
        throw 'CaptureCommandPath must be a native .exe so timeout and exit-code evidence are reliable.'
    }
}
if ($StopCommandPath -and [System.IO.Path]::GetExtension($StopCommandPath) -ne '.exe') {
    throw 'StopCommandPath must be a native .exe.'
}

Add-Type -AssemblyName System.Drawing
if (-not ([System.Management.Automation.PSTypeName]'OctadockAcceptance.NativeMethods').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace OctadockAcceptance
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int GetGuiResources(IntPtr process, int flags);
    }
}
'@
}

function New-Metric {
    param(
        [Parameter(Mandatory)][string]$Status,
        $Value,
        [string]$Unit,
        [string]$Reason
    )

    return [pscustomobject][ordered]@{
        status = $Status
        value = $Value
        unit = $Unit
        reason = $Reason
    }
}

function Get-ExecutableIdentity {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return [ordered]@{ path = $resolved; exists = $false }
    }
    $item = Get-Item -LiteralPath $resolved
    return [ordered]@{
        path = $resolved
        exists = $true
        bytes = [long]$item.Length
        sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        fileVersion = $item.VersionInfo.FileVersion
        productVersion = $item.VersionInfo.ProductVersion
    }
}

function Get-ProcessSnapshot {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    $Process.Refresh()
    if ($Process.HasExited) {
        throw "Measured process $($Process.Id) exited before sampling completed."
    }

    $gdiObjects = $null
    $userObjects = $null
    try {
        $gdiObjects = [OctadockAcceptance.NativeMethods]::GetGuiResources($Process.Handle, 0)
        $userObjects = [OctadockAcceptance.NativeMethods]::GetGuiResources($Process.Handle, 1)
    }
    catch {
        # GUI resource counters are Windows-only; null is reported explicitly.
    }

    return [pscustomobject][ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
        totalProcessorMilliseconds = $Process.TotalProcessorTime.TotalMilliseconds
        workingSetBytes = [long]$Process.WorkingSet64
        privateBytes = [long]$Process.PrivateMemorySize64
        handleCount = [int]$Process.HandleCount
        gdiObjectCount = $gdiObjects
        userObjectCount = $userObjects
        responding = [bool]$Process.Responding
    }
}

function Resolve-ReadyFile {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$PathSpec
    )

    $expanded = [Environment]::ExpandEnvironmentVariables($PathSpec).Replace('{pid}', [string]$Process.Id)
    if (Test-Path -LiteralPath $expanded -PathType Container) {
        return @(Get-ChildItem -LiteralPath $expanded -File -Filter "octadock-$($Process.Id)-*.log" -ErrorAction SilentlyContinue |
                Where-Object { $_.LastWriteTimeUtc -ge $Process.StartTime.ToUniversalTime().AddSeconds(-2) } |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1)
    }

    return @(Get-ChildItem -Path $expanded -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $Process.StartTime.ToUniversalTime().AddSeconds(-2) } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1)
}

function Wait-ForReady {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$Mode,
        [Parameter(Mandatory)][int]$TimeoutMilliseconds,
        [string]$ReadyFilePath,
        [string]$ReadyPattern,
        [long]$ReadyFileInitialLength = 0
    )

    if ($Mode -eq 'ProcessStarted') {
        return $true
    }
    if ($Mode -eq 'InputIdle') {
        try {
            return $Process.WaitForInputIdle($TimeoutMilliseconds)
        }
        catch {
            return $false
        }
    }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $Process.Refresh()
        if ($Process.HasExited) {
            return $false
        }
        if ($Mode -eq 'MainWindow' -and $Process.MainWindowHandle -ne [IntPtr]::Zero) {
            return $true
        }
        if ($Mode -eq 'FilePattern') {
            $readyFiles = @(Resolve-ReadyFile -Process $Process -PathSpec $ReadyFilePath)
            if ($readyFiles.Count -eq 0) {
                Start-Sleep -Milliseconds 100
                continue
            }
            $readyFile = $readyFiles[0]
            $stream = $null
            $reader = $null
            try {
                $stream = [System.IO.File]::Open(
                    $readyFile.FullName,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read,
                    [System.IO.FileShare]::ReadWrite
                )
                if ($ReadyFileInitialLength -gt 0 -and $ReadyFileInitialLength -le $stream.Length) {
                    [void]$stream.Seek($ReadyFileInitialLength, [System.IO.SeekOrigin]::Begin)
                }
                $reader = New-Object System.IO.StreamReader($stream)
                $appended = $reader.ReadToEnd()
                if ($appended -match $ReadyPattern) {
                    return $true
                }
            }
            catch {
                # The producer may be rotating or opening the file; poll again.
            }
            finally {
                if ($null -ne $reader) {
                    $reader.Dispose()
                }
                elseif ($null -ne $stream) {
                    $stream.Dispose()
                }
            }
        }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

function Get-ArtifactSnapshot {
    param([Parameter(Mandatory)][string]$Directory)

    $snapshot = @{}
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        return $snapshot
    }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -ErrorAction SilentlyContinue) {
        $snapshot[$file.FullName] = "$($file.Length)|$($file.LastWriteTimeUtc.Ticks)"
    }
    return $snapshot
}

function Measure-CaptureProbe {
    param([Parameter(Mandatory)][int]$Iteration)

    $before = Get-ArtifactSnapshot -Directory $CaptureArtifactDirectory
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $captureLogDirectory = Join-Path (Split-Path -Parent $OutputPath) 'capture-command'
    New-AcceptanceDirectory -Path $captureLogDirectory
    $stdoutPath = Join-Path $captureLogDirectory ("capture-{0:D3}.stdout.txt" -f $Iteration)
    $stderrPath = Join-Path $captureLogDirectory ("capture-{0:D3}.stderr.txt" -f $Iteration)
    $captureProcess = Start-Process -FilePath $CaptureCommandPath -ArgumentList $CaptureCommandArguments -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if (-not $captureProcess.WaitForExit($CaptureTimeoutSeconds * 1000)) {
        try {
            $captureProcess.Kill()
            [void]$captureProcess.WaitForExit(5000)
        }
        catch {
            # Timeout is the primary result.
        }
        return [pscustomobject][ordered]@{
            iteration = $Iteration
            status = 'fail'
            latencyMilliseconds = $null
            artifact = $null
            artifactBytes = $null
            commandExitCode = $null
            reason = "Capture trigger exceeded the $CaptureTimeoutSeconds second timeout."
        }
    }
    $commandExitCode = $captureProcess.ExitCode
    if ($commandExitCode -ne 0) {
        return [pscustomobject][ordered]@{
            iteration = $Iteration
            status = 'fail'
            latencyMilliseconds = $null
            artifact = $null
            artifactBytes = $null
            commandExitCode = $commandExitCode
            reason = 'Capture trigger command failed.'
        }
    }

    $candidate = $null
    $candidatePath = $null
    $lastLength = -1L
    $stablePolls = 0
    while ($watch.Elapsed.TotalSeconds -lt $CaptureTimeoutSeconds) {
        $files = @(Get-ChildItem -LiteralPath $CaptureArtifactDirectory -Recurse -File -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTimeUtc -Descending)
        foreach ($file in $files) {
            $signature = "$($file.Length)|$($file.LastWriteTimeUtc.Ticks)"
            if (-not $before.ContainsKey($file.FullName) -or $before[$file.FullName] -ne $signature) {
                $candidate = $file
                break
            }
        }

        if ($null -ne $candidate -and $candidate.Length -gt 0) {
            if ($candidate.FullName -ne $candidatePath) {
                $candidatePath = $candidate.FullName
                $lastLength = -1L
                $stablePolls = 0
            }
            if ($candidate.Length -eq $lastLength) {
                $stablePolls++
            }
            else {
                $stablePolls = 0
                $lastLength = $candidate.Length
            }
            if ($stablePolls -ge 2) {
                $image = $null
                try {
                    $image = [System.Drawing.Image]::FromFile($candidate.FullName)
                    if ($image.Width -le 0 -or $image.Height -le 0) {
                        throw 'Image has invalid dimensions.'
                    }
                    $watch.Stop()
                    $relative = $candidate.FullName.Substring(
                        [System.IO.Path]::GetFullPath($CaptureArtifactDirectory).TrimEnd('\').Length
                    ).TrimStart('\', '/').Replace('\', '/')
                    return [pscustomobject][ordered]@{
                        iteration = $Iteration
                        status = 'measured'
                        latencyMilliseconds = [Math]::Round($watch.Elapsed.TotalMilliseconds, 2)
                        artifact = $relative
                        artifactBytes = [long]$candidate.Length
                        artifactSha256 = (Get-FileHash -LiteralPath $candidate.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                        imageWidth = $image.Width
                        imageHeight = $image.Height
                        commandExitCode = $commandExitCode
                        reason = $null
                    }
                }
                catch {
                    $stablePolls = 0
                }
                finally {
                    if ($null -ne $image) {
                        $image.Dispose()
                    }
                }
            }
        }
        Start-Sleep -Milliseconds 100
    }

    return [pscustomobject][ordered]@{
        iteration = $Iteration
        status = 'fail'
        latencyMilliseconds = $null
        artifact = $null
        artifactBytes = $null
        commandExitCode = $commandExitCode
        reason = "No new stable artifact appeared within $CaptureTimeoutSeconds seconds."
    }
}

$evidence = New-AcceptanceEnvelope -Gate 'C-05' -Tool 'Measure-PerformanceBaseline.ps1' -RepoRoot $repoRoot
$evidence['planOnly'] = [bool]$PlanOnly
$evidence['measurementContract'] = [ordered]@{
    startupReadyMode = $StartupReadyMode
    startupReadyFilePath = $StartupReadyFilePath
    startupReadyPattern = $(if ($StartupReadyMode -eq 'FilePattern') { $StartupReadyPattern } else { $null })
    startupTimeoutSeconds = $StartupTimeoutSeconds
    warmupSeconds = $WarmupSeconds
    idleSeconds = $IdleSeconds
    sampleIntervalMilliseconds = $SampleIntervalMilliseconds
    gpuRequested = -not [bool]$SkipGpu
    captureIterations = $CaptureIterations
    processArguments = $ProcessArguments
    captureCommandArguments = @($CaptureCommandArguments)
}
$evidence['completionBoundary'] = 'This file is one observation. C-05 still requires comparable before/after Octadock runs, an isolated acceptance profile, capture evidence, and selection/verification of the top three fixes.'

if ($PlanOnly) {
    $evidence['overallStatus'] = 'planned_not_measured'
    $evidence['metrics'] = [ordered]@{
        startupReadyMilliseconds = New-Metric -Status 'not_run' -Value $null -Unit 'ms' -Reason 'Plan-only run.'
        idleCpuNormalizedPercent = New-Metric -Status 'not_run' -Value $null -Unit 'percent' -Reason 'Plan-only run.'
        workingSetBytes = New-Metric -Status 'not_run' -Value $null -Unit 'bytes' -Reason 'Plan-only run.'
        privateBytes = New-Metric -Status 'not_run' -Value $null -Unit 'bytes' -Reason 'Plan-only run.'
        handleCount = New-Metric -Status 'not_run' -Value $null -Unit 'count' -Reason 'Plan-only run.'
        gpuEngineAggregatePercent = New-Metric -Status 'not_run' -Value $null -Unit 'percent' -Reason 'Plan-only run.'
        captureLatencyMilliseconds = New-Metric -Status 'not_run' -Value $null -Unit 'ms' -Reason 'No desktop action was taken.'
    }
    Write-AcceptanceJson -Value $evidence -Path $OutputPath
    Write-Host "Performance measurement plan: $OutputPath" -ForegroundColor Green
    exit 0
}

$process = $null
$startedByHarness = $false
$measurementError = $null
try {
    $startupMetric = $null
    if ($AttachProcessId) {
        $process = Get-Process -Id $AttachProcessId -ErrorAction Stop
        $startupMetric = New-Metric -Status 'unavailable' -Value $null -Unit 'ms' -Reason 'Attached to an existing process; startup was not observed.'
    }
    else {
        $ProcessPath = [System.IO.Path]::GetFullPath($ProcessPath)
        if (-not (Test-Path -LiteralPath $ProcessPath -PathType Leaf)) {
            throw "ProcessPath does not exist: $ProcessPath"
        }
        if (-not $WorkingDirectory) {
            $WorkingDirectory = Split-Path -Parent $ProcessPath
        }

        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $ProcessPath
        $startInfo.Arguments = $ProcessArguments
        $startInfo.WorkingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = [bool]$CreateNoWindow

        $readyFileInitialLength = 0L
        if ($StartupReadyMode -eq 'FilePattern' -and
            $StartupReadyFilePath -notmatch '[*?{]' -and
            (Test-Path -LiteralPath $StartupReadyFilePath -PathType Leaf)) {
            $readyFileInitialLength = (Get-Item -LiteralPath $StartupReadyFilePath).Length
        }
        $startupWatch = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($startInfo)
        $startedByHarness = $true
        $ready = Wait-ForReady -Process $process -Mode $StartupReadyMode -TimeoutMilliseconds ($StartupTimeoutSeconds * 1000) -ReadyFilePath $StartupReadyFilePath -ReadyPattern $StartupReadyPattern -ReadyFileInitialLength $readyFileInitialLength
        $startupWatch.Stop()
        if ($ready) {
            $readyReason = 'Observed the configured startup-ready signal.'
            if ($StartupReadyMode -ne 'FilePattern') {
                $readyReason = "Observed '$StartupReadyMode'; this is process/UI readiness, not proof that Octadock InitializeAsync completed."
            }
            $startupMetric = New-Metric -Status 'measured' -Value ([Math]::Round($startupWatch.Elapsed.TotalMilliseconds, 2)) -Unit 'ms' -Reason $readyReason
            if ($StartupReadyMode -eq 'FilePattern') {
                $observedReadyFiles = @(Resolve-ReadyFile -Process $process -PathSpec $StartupReadyFilePath)
                if ($observedReadyFiles.Count -gt 0) {
                    $evidence['startupMarker'] = [ordered]@{
                        path = $observedReadyFiles[0].FullName
                        sha256 = (Get-FileHash -LiteralPath $observedReadyFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                        pattern = $StartupReadyPattern
                        caveat = 'The Serilog file sink flushes at up to two-second intervals, so this measurement includes logging visibility delay.'
                    }
                }
            }
        }
        else {
            $startupMetric = New-Metric -Status 'unavailable' -Value $null -Unit 'ms' -Reason "The '$StartupReadyMode' signal was not observed within the timeout."
        }
    }

    $measuredProcessPath = $process.MainModule.FileName
    if (-not $AllowNonOctadockProcessForToolingTest -and
        [System.IO.Path]::GetFileName($measuredProcessPath) -ne 'Octadock.exe') {
        throw "C-05 product evidence requires Octadock.exe; measured '$measuredProcessPath'."
    }

    if ($WarmupSeconds -gt 0) {
        Start-Sleep -Seconds $WarmupSeconds
    }

    $samples = New-Object System.Collections.Generic.List[object]
    $sampleCount = [Math]::Max(2, [Math]::Ceiling(($IdleSeconds * 1000.0) / $SampleIntervalMilliseconds) + 1)
    $idleWatch = [Diagnostics.Stopwatch]::StartNew()
    for ($index = 0; $index -lt $sampleCount; $index++) {
        $samples.Add((Get-ProcessSnapshot -Process $process))
        if ($index -lt ($sampleCount - 1)) {
            Start-Sleep -Milliseconds $SampleIntervalMilliseconds
        }
    }
    $idleWatch.Stop()

    $first = $samples[0]
    $last = $samples[$samples.Count - 1]
    $cpuDelta = [double]$last.totalProcessorMilliseconds - [double]$first.totalProcessorMilliseconds
    $normalizedCpu = ($cpuDelta / $idleWatch.Elapsed.TotalMilliseconds / [Environment]::ProcessorCount) * 100.0

    $workingSets = @($samples | ForEach-Object { [double]$_.workingSetBytes })
    $privateSets = @($samples | ForEach-Object { [double]$_.privateBytes })
    $handles = @($samples | ForEach-Object { [double]$_.handleCount })

    $gpuMetric = $null
    if ($SkipGpu) {
        $gpuMetric = New-Metric -Status 'not_requested' -Value $null -Unit 'percent' -Reason 'Skipped by caller.'
    }
    else {
        try {
            $counter = @(Get-Counter -Counter '\GPU Engine(*)\Utilization Percentage' -SampleInterval 1 -MaxSamples 2 -ErrorAction Stop)
            $pidMarker = "pid_$($process.Id)_"
            $sampleAggregates = New-Object System.Collections.Generic.List[double]
            foreach ($sampleSet in $counter) {
                $engineValues = @($sampleSet.CounterSamples |
                        Where-Object { $_.InstanceName -like "*$pidMarker*" } |
                        ForEach-Object { [double]$_.CookedValue })
                if ($engineValues.Count -gt 0) {
                    $sampleAggregates.Add([double](($engineValues | Measure-Object -Sum).Sum))
                }
            }
            if ($sampleAggregates.Count -gt 0) {
                $gpuMetric = New-Metric -Status 'measured' -Value ([Math]::Round((($sampleAggregates | Measure-Object -Average).Average), 3)) -Unit 'percent' -Reason 'Per-sample sum of matching Windows GPU Engine instances, averaged across samples.'
            }
            else {
                $gpuMetric = New-Metric -Status 'unavailable' -Value $null -Unit 'percent' -Reason 'No GPU Engine counter instance matched the measured process.'
            }
        }
        catch {
            $gpuMetric = New-Metric -Status 'unavailable' -Value $null -Unit 'percent' -Reason $_.Exception.Message
        }
    }

    $captureResults = New-Object System.Collections.Generic.List[object]
    for ($iteration = 1; $iteration -le $CaptureIterations; $iteration++) {
        $captureResults.Add((Measure-CaptureProbe -Iteration $iteration))
    }
    $captureMetric = $null
    if ($CaptureIterations -eq 0) {
        $captureMetric = New-Metric -Status 'not_run' -Value $null -Unit 'ms' -Reason 'Capture probe was not explicitly requested.'
    }
    elseif (@($captureResults | Where-Object { $_.status -ne 'measured' }).Count -gt 0) {
        $captureMetric = New-Metric -Status 'failed' -Value $null -Unit 'ms' -Reason 'At least one capture probe did not produce a stable artifact.'
    }
    else {
        $latencies = @($captureResults | ForEach-Object { [double]$_.latencyMilliseconds })
        $captureMetric = [pscustomobject][ordered]@{
            status = 'measured'
            value = [ordered]@{
                sampleCount = $latencies.Count
                median = [Math]::Round((Get-Percentile -Values $latencies -Percentile 50), 2)
                p95 = [Math]::Round((Get-Percentile -Values $latencies -Percentile 95), 2)
                maximum = [Math]::Round((($latencies | Measure-Object -Maximum).Maximum), 2)
            }
            unit = 'ms'
            reason = 'Time from explicit trigger command to a new non-empty artifact stable across three polls.'
        }
    }

    $metrics = [ordered]@{
        startupReadyMilliseconds = $startupMetric
        idleCpuNormalizedPercent = New-Metric -Status 'measured' -Value ([Math]::Round($normalizedCpu, 4)) -Unit 'percent' -Reason 'CPU time normalized across logical processors during the idle sample window.'
        workingSetBytes = [pscustomobject][ordered]@{
            status = 'measured'
            value = [ordered]@{
                average = [long](($workingSets | Measure-Object -Average).Average)
                peak = [long](($workingSets | Measure-Object -Maximum).Maximum)
                start = [long]$first.workingSetBytes
                end = [long]$last.workingSetBytes
            }
            unit = 'bytes'
            reason = $null
        }
        privateBytes = [pscustomobject][ordered]@{
            status = 'measured'
            value = [ordered]@{
                average = [long](($privateSets | Measure-Object -Average).Average)
                peak = [long](($privateSets | Measure-Object -Maximum).Maximum)
                start = [long]$first.privateBytes
                end = [long]$last.privateBytes
            }
            unit = 'bytes'
            reason = $null
        }
        handleCount = [pscustomobject][ordered]@{
            status = 'measured'
            value = [ordered]@{
                average = [Math]::Round((($handles | Measure-Object -Average).Average), 2)
                peak = [int](($handles | Measure-Object -Maximum).Maximum)
                start = [int]$first.handleCount
                end = [int]$last.handleCount
            }
            unit = 'count'
            reason = $null
        }
        gdiObjectCount = New-Metric -Status $(if ($null -ne $last.gdiObjectCount) { 'measured' } else { 'unavailable' }) -Value $last.gdiObjectCount -Unit 'count' -Reason $(if ($null -ne $last.gdiObjectCount) { $null } else { 'GetGuiResources was unavailable.' })
        userObjectCount = New-Metric -Status $(if ($null -ne $last.userObjectCount) { 'measured' } else { 'unavailable' }) -Value $last.userObjectCount -Unit 'count' -Reason $(if ($null -ne $last.userObjectCount) { $null } else { 'GetGuiResources was unavailable.' })
        gpuEngineAggregatePercent = $gpuMetric
        captureLatencyMilliseconds = $captureMetric
    }

    $partial = @($metrics.Values | Where-Object { $_.status -in @('unavailable', 'not_run', 'failed') }).Count -gt 0
    if ($AllowNonOctadockProcessForToolingTest) {
        $evidence['overallStatus'] = 'tooling_test_only'
    }
    else {
        $evidence['overallStatus'] = $(if ($partial) { 'partial_measurement' } else { 'measured' })
    }
    $evidence['process'] = [ordered]@{
        id = $process.Id
        name = $process.ProcessName
        startedByHarness = $startedByHarness
        executable = $(try { Get-ExecutableIdentity -Path $process.MainModule.FileName } catch { $null })
    }
    $evidence['captureCommand'] = Get-ExecutableIdentity -Path $CaptureCommandPath
    $evidence['metrics'] = $metrics
    $evidence['idleSamples'] = $samples.ToArray()
    $evidence['captureSamples'] = $captureResults.ToArray()
}
catch {
    $measurementError = $_.Exception.Message
    $evidence['overallStatus'] = 'measurement_failed'
    $evidence['error'] = $measurementError
}
finally {
    if ($startedByHarness -and $null -ne $process -and -not $LeaveRunning) {
        $cleanupStatus = 'not_attempted'
        try {
            if (-not $process.HasExited) {
                if ($StopCommandPath) {
                    $stopProcess = Start-Process -FilePath $StopCommandPath -ArgumentList $StopCommandArguments -PassThru
                    if (-not $stopProcess.WaitForExit(15000)) {
                        try {
                            $stopProcess.Kill()
                            [void]$stopProcess.WaitForExit(5000)
                        }
                        catch {
                            # The timeout remains the reported cleanup failure.
                        }
                        throw 'Stop command exceeded the 15 second timeout.'
                    }
                    if ($stopProcess.ExitCode -ne 0) {
                        throw "Stop command exited with code $($stopProcess.ExitCode)."
                    }
                    $cleanupStatus = 'graceful_stop_command_sent'
                    [void]$process.WaitForExit(10000)
                }
                elseif ($process.CloseMainWindow()) {
                    $cleanupStatus = 'close_main_window_sent'
                    [void]$process.WaitForExit(5000)
                }

                if (-not $process.HasExited -and $ForceTerminateStartedProcess) {
                    $process.Kill()
                    [void]$process.WaitForExit(5000)
                    $cleanupStatus = 'force_terminated_by_explicit_opt_in'
                }
                elseif (-not $process.HasExited) {
                    $cleanupStatus = 'left_running_no_safe_stop'
                    $evidence['cleanupWarning'] = 'The harness-started process remains running because no graceful stop completed. Supply StopCommandPath or stop it manually.'
                }
                else {
                    $cleanupStatus = 'stopped'
                }
            }
            else {
                $cleanupStatus = 'already_exited'
            }
        }
        catch {
            $evidence['cleanupWarning'] = $_.Exception.Message
            $cleanupStatus = 'cleanup_failed'
        }
        $evidence['cleanupStatus'] = $cleanupStatus
    }
    elseif ($startedByHarness -and $LeaveRunning) {
        $evidence['cleanupStatus'] = 'left_running_by_request'
    }
    Write-AcceptanceJson -Value $evidence -Path $OutputPath
}

Write-Host "Performance evidence: $OutputPath" -ForegroundColor Green
Write-Host "C-05 measurement status: $($evidence.overallStatus)"
if ($measurementError) {
    Write-Error $measurementError
    exit 1
}
if ($CaptureIterations -gt 0 -and $evidence.metrics.captureLatencyMilliseconds.status -eq 'failed') {
    exit 1
}
exit 0
