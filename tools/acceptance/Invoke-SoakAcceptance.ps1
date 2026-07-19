#Requires -Version 5.1
<#
.SYNOPSIS
    Orchestrates the C-07 capture, scrolling, and dictation soak profile.

.DESCRIPTION
    Attaches to an explicitly named Octadock process and invokes operator-owned
    probe scripts. Each probe must emit a portable JSON result and copy its
    evidence artifacts into the supplied directory. The harness validates image
    or text artifacts, hashes them, samples resources, and requires released
    hook/device state. It never captures the desktop or microphone by itself.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 2147483647)]
    [int]$ProcessId,

    [string]$CaptureProbePath,

    [string]$ScrollingProbePath,

    [string]$DictationProbePath,

    [ValidateRange(0, 1000)]
    [int]$CaptureCycles = 0,

    [ValidateRange(0, 1000)]
    [int]$ScrollingSessions = 0,

    [ValidateRange(0, 100)]
    [int]$DictationSessions = 0,

    [switch]$AcknowledgeDesktopAndMicrophoneInteraction,

    [switch]$PlanOnly,

    [ValidateRange(5, 1800)]
    [int]$ProbeTimeoutSeconds = 300,

    [string]$DatabasePath,

    [string]$ReviewEvidencePath,

    [switch]$AllowNonOctadockProcessForToolingTest,

    [switch]$RequireComplete,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Acceptance.Common.psm1') -Force
$repoRoot = Get-OctadockRepoRoot
$profile = Read-AcceptanceJson -Path (Join-Path $PSScriptRoot 'config/soak-profile.json')

if (-not $OutputPath) {
    $runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $repoRoot "artifacts/acceptance/soak/$runId/soak-result.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$runDirectory = Split-Path -Parent $OutputPath
if (Test-Path -LiteralPath $OutputPath) {
    throw "Refusing to reuse existing soak evidence: $OutputPath"
}
foreach ($workflowDirectoryName in @('capture', 'scrolling', 'dictation')) {
    if (Test-Path -LiteralPath (Join-Path $runDirectory $workflowDirectoryName)) {
        throw "Refusing to reuse a soak run directory containing workflow artifacts: $runDirectory"
    }
}
New-AcceptanceDirectory -Path $runDirectory

$defaultIterations = @{}
foreach ($workflow in @($profile.workflows)) {
    $defaultIterations[[string]$workflow.id] = [int]$workflow.iterations
}
if ($CaptureCycles -eq 0) {
    $CaptureCycles = $defaultIterations['capture']
}
if ($ScrollingSessions -eq 0) {
    $ScrollingSessions = $defaultIterations['scrolling']
}
if ($DictationSessions -eq 0) {
    $DictationSessions = $defaultIterations['dictation']
}

if (-not $PlanOnly) {
    if (-not $ProcessId) {
        throw 'A live soak requires -ProcessId for the running Octadock instance.'
    }
    if (-not $AcknowledgeDesktopAndMicrophoneInteraction) {
        throw 'A live soak requires -AcknowledgeDesktopAndMicrophoneInteraction. Use only safe test content.'
    }
}

Add-Type -AssemblyName System.Drawing
if (-not ([System.Management.Automation.PSTypeName]'OctadockAcceptance.SoakNativeMethods').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace OctadockAcceptance
{
    public static class SoakNativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int GetGuiResources(IntPtr process, int flags);
    }
}
'@
}

function Get-SoakProcessSnapshot {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    $Process.Refresh()
    if ($Process.HasExited) {
        throw "Octadock process $($Process.Id) exited during the soak."
    }

    $gdi = $null
    $user = $null
    try {
        $gdi = [OctadockAcceptance.SoakNativeMethods]::GetGuiResources($Process.Handle, 0)
        $user = [OctadockAcceptance.SoakNativeMethods]::GetGuiResources($Process.Handle, 1)
    }
    catch {
        # Null remains explicit in the evidence.
    }

    return [pscustomobject][ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
        privateBytes = [long]$Process.PrivateMemorySize64
        workingSetBytes = [long]$Process.WorkingSet64
        handleCount = [int]$Process.HandleCount
        gdiObjectCount = $gdi
        userObjectCount = $user
        responding = [bool]$Process.Responding
        totalProcessorMilliseconds = $Process.TotalProcessorTime.TotalMilliseconds
    }
}

function Resolve-PortableArtifact {
    param(
        [Parameter(Mandatory)][string]$EvidenceDirectory,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "Probe artifact paths must be relative: $RelativePath"
    }
    $root = [System.IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $EvidenceDirectory $RelativePath))
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Probe artifact escapes its evidence directory: $RelativePath"
    }
    return $resolved
}

function Test-ProbeArtifact {
    param(
        [Parameter(Mandatory)]$Artifact,
        [Parameter(Mandatory)][string]$EvidenceDirectory,
        [Parameter(Mandatory)][string]$ExpectedKind,
        [Parameter(Mandatory)][DateTimeOffset]$NotBeforeUtc
    )

    $relativePath = [string]$Artifact.path
    $kind = ([string]$Artifact.kind).ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        throw 'Probe artifact has no path.'
    }
    if ($kind -ne $ExpectedKind) {
        throw "Probe artifact kind '$kind' does not match expected '$ExpectedKind'."
    }

    $resolved = Resolve-PortableArtifact -EvidenceDirectory $EvidenceDirectory -RelativePath $relativePath
    $resultMetadataPath = Join-Path $EvidenceDirectory 'probe-result.json'
    if ($resolved.Equals([System.IO.Path]::GetFullPath($resultMetadataPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Probe result metadata cannot also be the workflow artifact.'
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Probe artifact does not exist: $relativePath"
    }
    $file = Get-Item -LiteralPath $resolved
    if ($file.Length -le 0) {
        throw "Probe artifact is empty: $relativePath"
    }
    if ($file.LastWriteTimeUtc -lt $NotBeforeUtc.UtcDateTime.AddSeconds(-2)) {
        throw "Probe artifact predates this iteration: $relativePath"
    }

    $rootPath = [System.IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd('\', '/')
    $cursor = $file
    while ($null -ne $cursor -and
        $cursor.FullName.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
        if (($cursor.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Probe artifact path contains a reparse point: $relativePath"
        }
        if ($cursor -is [System.IO.FileInfo]) {
            $cursor = $cursor.Directory
        }
        else {
            $cursor = $cursor.Parent
        }
    }

    $details = [ordered]@{}
    if ($kind -eq 'image') {
        if ($file.Extension.ToLowerInvariant() -notin @('.png', '.jpg', '.jpeg', '.bmp')) {
            throw "Image artifact extension is not accepted: $($file.Extension)"
        }
        $image = $null
        try {
            $image = [System.Drawing.Image]::FromFile($resolved)
            if ($image.Width -le 0 -or $image.Height -le 0) {
                throw 'Image has invalid dimensions.'
            }
            $details['width'] = $image.Width
            $details['height'] = $image.Height
            $details['format'] = $image.RawFormat.ToString()
        }
        catch {
            throw "Image artifact cannot be decoded: $relativePath. $($_.Exception.Message)"
        }
        finally {
            if ($null -ne $image) {
                $image.Dispose()
            }
        }
    }
    elseif ($kind -eq 'text') {
        if ($file.Extension.ToLowerInvariant() -notin @('.txt', '.md')) {
            throw "Text artifact extension is not accepted: $($file.Extension)"
        }
        $text = Get-Content -Raw -LiteralPath $resolved
        if ([string]::IsNullOrWhiteSpace($text)) {
            throw "Text artifact contains no transcript: $relativePath"
        }
        $details['characterCount'] = $text.Length
    }

    $portableFromRun = $resolved.Substring(
        [System.IO.Path]::GetFullPath($runDirectory).TrimEnd('\', '/').Length
    ).TrimStart('\', '/').Replace('\', '/')
    return [pscustomobject][ordered]@{
        path = $portableFromRun
        kind = $kind
        bytes = [long]$file.Length
        sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        details = $details
    }
}

function Quote-ProcessArgument {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value.Contains('"')) {
        throw 'Probe paths containing quote characters are not supported.'
    }
    return '"' + $Value + '"'
}

function Invoke-ProbeProcess {
    param(
        [Parameter(Mandatory)][string]$ProbePath,
        [Parameter(Mandatory)][int]$Iteration,
        [Parameter(Mandatory)][string]$ResultPath,
        [Parameter(Mandatory)][string]$EvidenceDirectory,
        [Parameter(Mandatory)][int]$TargetProcessId
    )

    $hostExecutable = Join-Path $PSHOME 'powershell.exe'
    if ($PSVersionTable.PSEdition -eq 'Core') {
        $hostExecutable = (Get-Process -Id $PID).Path
    }
    $stdoutPath = Join-Path $EvidenceDirectory 'probe.stdout.txt'
    $stderrPath = Join-Path $EvidenceDirectory 'probe.stderr.txt'
    $argumentString = @(
        '-NoProfile',
        '-NonInteractive',
        '-ExecutionPolicy', 'Bypass',
        '-File', (Quote-ProcessArgument $ProbePath),
        '-Iteration', $Iteration,
        '-ResultPath', (Quote-ProcessArgument $ResultPath),
        '-EvidenceDirectory', (Quote-ProcessArgument $EvidenceDirectory),
        '-ProcessId', $TargetProcessId
    ) -join ' '

    $probeProcess = Start-Process -FilePath $hostExecutable -ArgumentList $argumentString -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if (-not $probeProcess.WaitForExit($ProbeTimeoutSeconds * 1000)) {
        try {
            $probeProcess.Kill()
            [void]$probeProcess.WaitForExit(5000)
        }
        catch {
            # The timeout remains the primary failure.
        }
        throw "Probe exceeded the $ProbeTimeoutSeconds second timeout."
    }

    return [pscustomobject][ordered]@{
        exitCode = $probeProcess.ExitCode
        stdout = $stdoutPath
        stderr = $stderrPath
    }
}

function Invoke-WorkflowSoak {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][int]$Iterations,
        [Parameter(Mandatory)][string]$ExpectedKind,
        [string]$ProbePath
    )

    if (-not $ProbePath) {
        return [pscustomobject][ordered]@{
            id = $Id
            status = 'pending_hardware_probe'
            plannedIterations = $Iterations
            completedIterations = 0
            reason = 'No probe script was supplied.'
            before = Get-SoakProcessSnapshot -Process $Process
            after = $null
            iterations = @()
        }
    }
    $ProbePath = [System.IO.Path]::GetFullPath($ProbePath)
    if (-not (Test-Path -LiteralPath $ProbePath -PathType Leaf)) {
        throw "Probe script does not exist: $ProbePath"
    }

    $workflowDirectory = Join-Path $runDirectory $Id
    New-AcceptanceDirectory -Path $workflowDirectory
    $before = Get-SoakProcessSnapshot -Process $Process
    $iterationResults = New-Object System.Collections.Generic.List[object]

    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $iterationDirectory = Join-Path $workflowDirectory ('{0:D4}' -f $iteration)
        New-AcceptanceDirectory -Path $iterationDirectory
        $probeResultPath = Join-Path $iterationDirectory 'probe-result.json'
        $started = [DateTimeOffset]::UtcNow
        $probeError = $null
        try {
            $probeExecution = Invoke-ProbeProcess -ProbePath $ProbePath -Iteration $iteration -ResultPath $probeResultPath -EvidenceDirectory $iterationDirectory -TargetProcessId $Process.Id
            if ($probeExecution.exitCode -ne 0) {
                throw "Probe exited with code $($probeExecution.exitCode)."
            }
            if (-not (Test-Path -LiteralPath $probeResultPath -PathType Leaf)) {
                throw 'Probe did not write its required result JSON.'
            }

            $probe = Read-AcceptanceJson -Path $probeResultPath
            if ([int]$probe.schemaVersion -ne 1) {
                throw 'Probe result schemaVersion must be 1.'
            }
            if ([string]$probe.workflow -ne $Id -or [int]$probe.iteration -ne $iteration) {
                throw 'Probe result workflow or iteration does not match the invocation.'
            }
            if ([string]$probe.status -ne 'pass') {
                throw "Probe reported status '$($probe.status)'."
            }

            if ($Id -in @('capture', 'scrolling')) {
                if ([string]$probe.hookState -notin @('released', 'not_used')) {
                    throw "Probe must report hookState released or not_used; received '$($probe.hookState)'."
                }
                if ($null -eq $probe.databaseCorrelation -or
                    [bool]$probe.databaseCorrelation.succeeded -ne $true -or
                    [string]::IsNullOrWhiteSpace([string]$probe.databaseCorrelation.method)) {
                    throw 'Capture/scrolling probes must correlate the produced artifact to app state and describe the method.'
                }
            }
            if ($Id -eq 'dictation' -and [string]$probe.deviceState -ne 'released') {
                throw "Dictation probe must report deviceState released; received '$($probe.deviceState)'."
            }
            if ($null -eq $probe.releaseVerification -or
                [bool]$probe.releaseVerification.succeeded -ne $true -or
                [string]::IsNullOrWhiteSpace([string]$probe.releaseVerification.method)) {
                throw 'Probe must include a successful releaseVerification with its verification method.'
            }

            $artifacts = New-Object System.Collections.Generic.List[object]
            foreach ($artifact in @($probe.artifacts)) {
                $artifacts.Add((Test-ProbeArtifact -Artifact $artifact -EvidenceDirectory $iterationDirectory -ExpectedKind $ExpectedKind -NotBeforeUtc $started))
            }
            if ($artifacts.Count -eq 0) {
                throw 'Passing probe result contains no evidence artifact.'
            }
            if ($Id -eq 'dictation') {
                $expectedTranscript = [string]$probe.expectedTranscript
                if ([string]::IsNullOrWhiteSpace($expectedTranscript)) {
                    throw 'Dictation probe must include expectedTranscript for artifact correlation.'
                }
                $transcriptArtifact = @($probe.artifacts | Where-Object { [string]$_.kind -eq 'text' } | Select-Object -First 1)
                $transcriptPath = Resolve-PortableArtifact -EvidenceDirectory $iterationDirectory -RelativePath ([string]$transcriptArtifact[0].path)
                $transcript = Get-Content -Raw -LiteralPath $transcriptPath
                if ($transcript.IndexOf($expectedTranscript, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                    throw 'Dictation transcript does not contain the probe expectedTranscript.'
                }
            }

            $processSnapshot = Get-SoakProcessSnapshot -Process $Process
            if (-not $processSnapshot.responding) {
                throw 'Octadock stopped responding after the probe.'
            }

            $iterationResults.Add([pscustomobject][ordered]@{
                    iteration = $iteration
                    status = 'pass'
                    startedUtc = $started.ToString('O')
                    completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                    artifacts = $artifacts.ToArray()
                    hookState = [string](Get-AcceptancePropertyValue -Object $probe -Name 'hookState')
                    deviceState = [string](Get-AcceptancePropertyValue -Object $probe -Name 'deviceState')
                    process = $processSnapshot
                    releaseVerification = Get-AcceptancePropertyValue -Object $probe -Name 'releaseVerification'
                    databaseCorrelation = Get-AcceptancePropertyValue -Object $probe -Name 'databaseCorrelation'
                    reason = $null
                })
        }
        catch {
            $probeError = $_.Exception.Message
            $iterationResults.Add([pscustomobject][ordered]@{
                    iteration = $iteration
                    status = 'fail'
                    startedUtc = $started.ToString('O')
                    completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                    artifacts = @()
                    hookState = $null
                    deviceState = $null
                    process = $(try { Get-SoakProcessSnapshot -Process $Process } catch { $null })
                    reason = $probeError
                })
            break
        }
    }

    $after = $null
    try {
        $after = Get-SoakProcessSnapshot -Process $Process
    }
    catch {
        $iterationResults.Add([pscustomobject][ordered]@{
                iteration = $iterationResults.Count + 1
                status = 'fail'
                startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                artifacts = @()
                hookState = $null
                deviceState = $null
                process = $null
                reason = "Process snapshot after '$Id' failed: $($_.Exception.Message)"
            })
    }
    $failed = @($iterationResults | Where-Object { $_.status -ne 'pass' }).Count -gt 0
    $status = $(if ($failed) { 'fail' } else { 'pass' })
    return [pscustomobject][ordered]@{
        id = $Id
        status = $status
        plannedIterations = $Iterations
        completedIterations = @($iterationResults | Where-Object { $_.status -eq 'pass' }).Count
        reason = $(if ($failed) { 'At least one probe or artifact validation failed.' } else { $null })
        before = $before
        after = $after
        iterations = $iterationResults.ToArray()
    }
}

function Compare-ResourceGuardrails {
    param(
        [Parameter(Mandatory)]$Before,
        [Parameter(Mandatory)]$After
    )

    $checks = New-Object System.Collections.Generic.List[object]
    $definitions = @(
        @('privateBytes', 'privateBytesGrowth'),
        @('workingSetBytes', 'workingSetBytesGrowth'),
        @('handleCount', 'handleGrowth'),
        @('gdiObjectCount', 'gdiObjectGrowth'),
        @('userObjectCount', 'userObjectGrowth')
    )
    foreach ($definition in $definitions) {
        $metric = $definition[0]
        $guardrailName = $definition[1]
        $beforeValue = $Before.$metric
        $afterValue = $After.$metric
        $limit = [long]$profile.provisionalResourceGuardrails.$guardrailName
        if ($null -eq $beforeValue -or $null -eq $afterValue) {
            $checks.Add([pscustomobject][ordered]@{
                    metric = $metric
                    status = 'unavailable'
                    start = $beforeValue
                    end = $afterValue
                    growth = $null
                    provisionalLimit = $limit
                })
            continue
        }
        $growth = [long]$afterValue - [long]$beforeValue
        $checks.Add([pscustomobject][ordered]@{
                metric = $metric
                status = $(if ($growth -le $limit) { 'within_provisional_guardrail' } else { 'review_required' })
                start = [long]$beforeValue
                end = [long]$afterValue
                growth = $growth
                provisionalLimit = $limit
            })
    }
    return $checks.ToArray()
}

function Get-ResourceTrends {
    param([Parameter(Mandatory)][object[]]$Samples)

    $trends = New-Object System.Collections.Generic.List[object]
    foreach ($metric in @('privateBytes', 'workingSetBytes', 'handleCount', 'gdiObjectCount', 'userObjectCount')) {
        $values = New-Object System.Collections.Generic.List[double]
        foreach ($sample in $Samples) {
            if ($null -ne $sample -and $null -ne $sample.$metric) {
                $values.Add([double]$sample.$metric)
            }
        }
        if ($values.Count -lt 2) {
            $trends.Add([pscustomobject][ordered]@{
                    metric = $metric
                    status = 'unavailable'
                    sampleCount = $values.Count
                    slopePerSample = $null
                    peak = $(if ($values.Count -eq 1) { $values[0] } else { $null })
                })
            continue
        }

        $xMean = ($values.Count - 1) / 2.0
        $yMean = ($values | Measure-Object -Average).Average
        $numerator = 0.0
        $denominator = 0.0
        for ($index = 0; $index -lt $values.Count; $index++) {
            $xDelta = $index - $xMean
            $numerator += $xDelta * ($values[$index] - $yMean)
            $denominator += $xDelta * $xDelta
        }
        $slope = $(if ($denominator -eq 0) { 0.0 } else { $numerator / $denominator })
        $trends.Add([pscustomobject][ordered]@{
                metric = $metric
                status = 'measured'
                sampleCount = $values.Count
                slopePerSample = [Math]::Round($slope, 4)
                peak = [long](($values | Measure-Object -Maximum).Maximum)
            })
    }
    return $trends.ToArray()
}

function Test-DatabaseIntegrity {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return [pscustomobject][ordered]@{
            status = 'pending'
            reason = 'DatabasePath was not supplied.'
            path = $null
            sha256 = $null
            output = @()
        }
    }
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return [pscustomobject][ordered]@{
            status = 'fail'
            reason = 'DatabasePath does not exist.'
            path = $resolved
            sha256 = $null
            output = @()
        }
    }
    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($null -eq $sqlite) {
        return [pscustomobject][ordered]@{
            status = 'unavailable'
            reason = 'sqlite3 is not installed; quick_check and foreign_key_check were not executed.'
            path = $resolved
            sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
            output = @()
        }
    }

    $output = @(& $sqlite.Source $resolved 'PRAGMA quick_check; PRAGMA foreign_key_check;' 2>&1)
    $exitCode = $LASTEXITCODE
    $clean = $exitCode -eq 0 -and $output.Count -eq 1 -and ([string]$output[0]).Trim() -eq 'ok'
    return [pscustomobject][ordered]@{
        status = $(if ($clean) { 'pass' } else { 'fail' })
        reason = $(if ($clean) { $null } else { 'SQLite quick_check or foreign_key_check did not return a clean result.' })
        path = $resolved
        sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        output = @($output | ForEach-Object { [string]$_ })
        exitCode = $exitCode
    }
}

function Get-SoakFileIdentity {
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

$evidence = New-AcceptanceEnvelope -Gate 'C-07' -Tool 'Invoke-SoakAcceptance.ps1' -RepoRoot $repoRoot
$evidence['profile'] = [string]$profile.profile
$evidence['planOnly'] = [bool]$PlanOnly
$evidence['probeTimeoutSeconds'] = $ProbeTimeoutSeconds
$evidence['completionBlockers'] = @($profile.completionBlockers)
$evidence['plannedWorkflows'] = @(
    [ordered]@{ id = 'capture'; iterations = $CaptureCycles; artifactKind = 'image' },
    [ordered]@{ id = 'scrolling'; iterations = $ScrollingSessions; artifactKind = 'image' },
    [ordered]@{ id = 'dictation'; iterations = $DictationSessions; artifactKind = 'text' }
)

if ($PlanOnly) {
    $evidence['overallStatus'] = 'planned_hardware_pending'
    $evidence['workflowResults'] = @(
        [ordered]@{ id = 'capture'; status = 'pending_hardware_probe'; plannedIterations = $CaptureCycles },
        [ordered]@{ id = 'scrolling'; status = 'pending_hardware_probe'; plannedIterations = $ScrollingSessions },
        [ordered]@{ id = 'dictation'; status = 'pending_hardware_probe'; plannedIterations = $DictationSessions }
    )
    $evidence['resourceGuardrails'] = @()
    $evidence['databaseIntegrity'] = [ordered]@{ status = 'pending'; reason = 'Plan-only run.' }
    $evidence['manualReview'] = @([ordered]@{ id = 'artifact-integrity-review'; status = 'pending_manual' })
    Write-AcceptanceJson -Value $evidence -Path $OutputPath
    Write-Host "Soak plan: $OutputPath" -ForegroundColor Green
    exit 0
}

$workflowResultsList = New-Object System.Collections.Generic.List[object]
$hasFailure = $false
$overallStatus = 'failed'
$process = $null
$soakWatch = [Diagnostics.Stopwatch]::StartNew()
try {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    $processPath = $process.MainModule.FileName
    if (-not $AllowNonOctadockProcessForToolingTest -and
        [System.IO.Path]::GetFileName($processPath) -ne 'Octadock.exe') {
        throw "C-07 requires the Octadock.exe desktop process; PID $ProcessId is '$processPath'."
    }

    $evidence['process'] = [ordered]@{
        id = $process.Id
        name = $process.ProcessName
        startTimeUtc = $process.StartTime.ToUniversalTime().ToString('O')
        executable = Get-SoakFileIdentity -Path $processPath
        nonProductToolingTest = [bool]$AllowNonOctadockProcessForToolingTest
    }
    $evidence['probes'] = [ordered]@{
        capture = Get-SoakFileIdentity -Path $CaptureProbePath
        scrolling = Get-SoakFileIdentity -Path $ScrollingProbePath
        dictation = Get-SoakFileIdentity -Path $DictationProbePath
    }

    $overallBefore = Get-SoakProcessSnapshot -Process $process
    $evidence['before'] = $overallBefore
    $workflowResultsList.Add((Invoke-WorkflowSoak -Process $process -Id 'capture' -Iterations $CaptureCycles -ExpectedKind 'image' -ProbePath $CaptureProbePath))
    $workflowResultsList.Add((Invoke-WorkflowSoak -Process $process -Id 'scrolling' -Iterations $ScrollingSessions -ExpectedKind 'image' -ProbePath $ScrollingProbePath))
    $workflowResultsList.Add((Invoke-WorkflowSoak -Process $process -Id 'dictation' -Iterations $DictationSessions -ExpectedKind 'text' -ProbePath $DictationProbePath))

    $overallAfter = Get-SoakProcessSnapshot -Process $process
    $guardrails = Compare-ResourceGuardrails -Before $overallBefore -After $overallAfter
    $trendSamples = New-Object System.Collections.Generic.List[object]
    $trendSamples.Add($overallBefore)
    foreach ($workflowResult in $workflowResultsList) {
        foreach ($iterationResult in @($workflowResult.iterations)) {
            if ($null -ne $iterationResult.process) {
                $trendSamples.Add($iterationResult.process)
            }
        }
    }
    $trendSamples.Add($overallAfter)
    $trends = Get-ResourceTrends -Samples $trendSamples.ToArray()
    $databaseIntegrity = Test-DatabaseIntegrity -Path $DatabasePath

    $reviewEvidence = $null
    if ($ReviewEvidencePath) {
        $ReviewEvidencePath = [System.IO.Path]::GetFullPath($ReviewEvidencePath)
        $reviewEvidence = Read-AcceptanceJson -Path $ReviewEvidencePath
    }
    $reviewResults = Resolve-ManualAcceptanceChecks -Checks @($profile.manualChecks) -EvidenceDocument $reviewEvidence -ExpectedGate 'C-07'

    $hasFailure = @($workflowResultsList | Where-Object { $_.status -eq 'fail' }).Count -gt 0
    $hasPending = @($workflowResultsList | Where-Object { $_.status -eq 'pending_hardware_probe' }).Count -gt 0
    $requiresResourceReview = @($guardrails | Where-Object { $_.status -in @('review_required', 'unavailable') }).Count -gt 0
    $reviewFailed = @($reviewResults | Where-Object { $_.status -in @('fail', 'invalid_evidence') }).Count -gt 0
    $reviewPending = @($reviewResults | Where-Object { $_.status -in @('pending_manual', 'blocked_manual') }).Count -gt 0

    if ($hasFailure -or $reviewFailed -or $databaseIntegrity.status -eq 'fail') {
        $overallStatus = 'failed'
    }
    elseif ($AllowNonOctadockProcessForToolingTest) {
        $overallStatus = 'tooling_test_only'
    }
    elseif ($hasPending) {
        $overallStatus = 'hardware_probes_pending'
    }
    elseif ($databaseIntegrity.status -ne 'pass') {
        $overallStatus = 'database_integrity_pending'
    }
    elseif ($requiresResourceReview) {
        $overallStatus = 'resource_review_required'
    }
    elseif ($reviewPending) {
        $overallStatus = 'operator_review_pending'
    }
    elseif (@($profile.completionBlockers).Count -gt 0) {
        $overallStatus = 'correlation_and_isolation_seams_pending'
    }
    else {
        $overallStatus = 'complete'
    }

    $evidence['after'] = $overallAfter
    $evidence['resourceGuardrails'] = $guardrails
    $evidence['resourceTrends'] = $trends
    $evidence['databaseIntegrity'] = $databaseIntegrity
    $evidence['manualReview'] = @($reviewResults)
    $evidence['manualReviewSource'] = $ReviewEvidencePath
}
catch {
    $hasFailure = $true
    $overallStatus = 'failed'
    $evidence['error'] = $_.Exception.Message
    if ($null -ne $process) {
        try {
            $evidence['after'] = Get-SoakProcessSnapshot -Process $process
        }
        catch {
            $evidence['afterError'] = $_.Exception.Message
        }
    }
}
finally {
    $soakWatch.Stop()
    $evidence['overallStatus'] = $overallStatus
    $evidence['durationSeconds'] = [Math]::Round($soakWatch.Elapsed.TotalSeconds, 3)
    $evidence['workflowResults'] = $workflowResultsList.ToArray()
    $evidence['guardrailDisclaimer'] = 'Guardrails are provisional regression tripwires, not product targets. Completion also requires decoded artifacts, database checks, process responsiveness, released device/hook evidence, and a structured operator review.'
    Write-AcceptanceJson -Value $evidence -Path $OutputPath
}

Write-Host "Soak evidence: $OutputPath" -ForegroundColor Green
Write-Host "C-07 status: $overallStatus"
if ($hasFailure) {
    exit 1
}
if ($RequireComplete -and $overallStatus -ne 'complete') {
    exit 2
}
exit 0
