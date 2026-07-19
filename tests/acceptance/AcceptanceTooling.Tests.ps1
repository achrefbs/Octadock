#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$toolsRoot = Join-Path $repoRoot 'tools/acceptance'
Import-Module (Join-Path $toolsRoot 'Acceptance.Common.psm1') -Force
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
    if ($exitCode -ne $ExpectedExitCode) {
        $outputIndex = [Array]::IndexOf($Arguments, '-OutputPath')
        if ($outputIndex -ge 0 -and $outputIndex + 1 -lt $Arguments.Count -and
            (Test-Path -LiteralPath $Arguments[$outputIndex + 1] -PathType Leaf)) {
            Write-Host (Get-Content -Raw -LiteralPath $Arguments[$outputIndex + 1])
        }
    }
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
    $testProcessPath = (Get-Command powershell.exe -CommandType Application -ErrorAction Stop).Source
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Measure-PerformanceBaseline.ps1') -Arguments @(
        '-ProcessPath', $testProcessPath,
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

    $fingerprintRoot = Join-Path $testRoot 'source-state-fingerprint'
    New-Item -ItemType Directory -Path $fingerprintRoot -Force | Out-Null
    & git -C $fingerprintRoot init --quiet
    & git -C $fingerprintRoot config user.email 'acceptance@octadock.local'
    & git -C $fingerprintRoot config user.name 'Octadock Acceptance'
    $trackedFixture = Join-Path $fingerprintRoot 'tracked.txt'
    [System.IO.File]::WriteAllText($trackedFixture, 'tracked')
    & git -C $fingerprintRoot add -- tracked.txt
    & git -C $fingerprintRoot commit --quiet -m 'fixture'
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the source-state fingerprint fixture repository.'
    }

    $quotedName = 'quoted caf' + [char]0x00E9 + ' file.txt'
    $quotedFixture = Join-Path $fingerprintRoot $quotedName
    [System.IO.File]::WriteAllText($quotedFixture, 'first')
    $firstFingerprint = Get-AcceptanceSourceState -RepoRoot $fingerprintRoot
    [System.IO.File]::WriteAllText($quotedFixture, 'second')
    $secondFingerprint = Get-AcceptanceSourceState -RepoRoot $fingerprintRoot
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string]$firstFingerprint.diffIdentitySha256)) -Message 'Source-state fingerprinting must support quoted untracked paths.'
    Assert-True -Condition ($firstFingerprint.diffIdentitySha256 -ne $secondFingerprint.diffIdentitySha256) -Message 'Changing a quoted untracked file must change the source-state identity.'

    $soakOutput = Join-Path $testRoot 'soak-plan.json'
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Invoke-SoakAcceptance.ps1') -Arguments @(
        '-PlanOnly',
        '-OutputPath', $soakOutput
    ) -ExpectedExitCode 0
    $soak = Get-Content -Raw -LiteralPath $soakOutput | ConvertFrom-Json
    Assert-True -Condition ($soak.overallStatus -eq 'planned_hardware_pending') -Message 'Soak plan must remain hardware pending.'
    Assert-True -Condition (($soak.plannedWorkflows | Where-Object { $_.id -eq 'capture' }).iterations -eq 50) -Message 'Default soak profile must plan 50 capture cycles.'
    Assert-True -Condition (($soak.plannedWorkflows | Where-Object { $_.id -eq 'scrolling' }).iterations -eq 10) -Message 'Default soak profile must plan 10 scrolling sessions.'
    Assert-True -Condition ($null -ne $soak.sourceState.dirty) -Message 'Acceptance evidence must record whether the source tree is dirty.'
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string]$soak.sourceState.diffIdentitySha256)) -Message 'Acceptance evidence must fingerprint the exact source state.'
    Assert-True -Condition ($soak.isolation.required -eq 'separate_windows_profile') -Message 'Soak evidence must retain the safe separate-profile isolation contract.'
    Assert-True -Condition (@($soak.completionBlockers).Count -eq 0) -Message 'Implemented capture-ID correlation must not remain a declared product seam.'

    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($null -ne $sqlite) {
        $correlationRoot = Join-Path $testRoot 'soak-correlation'
        $managedRoot = Join-Path $correlationRoot 'data'
        $capturesRoot = Join-Path $managedRoot 'Captures'
        New-Item -ItemType Directory -Path $capturesRoot -Force | Out-Null
        Add-Type -AssemblyName System.Drawing
        $captureSource = Join-Path $capturesRoot 'capture.png'
        $scrollSource = Join-Path $capturesRoot 'scroll.png'
        foreach ($definition in @(
                @($captureSource, [System.Drawing.Color]::Teal),
                @($scrollSource, [System.Drawing.Color]::DarkOrange))) {
            $bitmap = New-Object System.Drawing.Bitmap 2, 2
            try {
                $bitmap.SetPixel(0, 0, $definition[1])
                $bitmap.Save($definition[0], [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $bitmap.Dispose()
            }
        }

        $captureId = [Guid]::NewGuid().ToString('D')
        $scrollId = [Guid]::NewGuid().ToString('D')
        $databasePath = Join-Path $managedRoot 'octadock.db'
        $schema = @"
CREATE TABLE captures (id TEXT NOT NULL PRIMARY KEY, original_path TEXT NOT NULL, deleted_at TEXT NULL);
INSERT INTO captures (id, original_path, deleted_at) VALUES ('$captureId', 'Captures/capture.png', NULL);
INSERT INTO captures (id, original_path, deleted_at) VALUES ('$scrollId', 'Captures/scroll.png', NULL);
"@
        & $sqlite.Source $databasePath $schema
        Assert-True -Condition ($LASTEXITCODE -eq 0) -Message 'Synthetic correlation database must be created.'

        $captureProbeTemplate = @'
param([int]$Iteration, [string]$ResultPath, [string]$EvidenceDirectory, [int]$ProcessId)
$artifactName = '__ARTIFACT__'
$artifactPath = Join-Path $EvidenceDirectory $artifactName
Copy-Item -LiteralPath '__SOURCE__' -Destination $artifactPath
(Get-Item -LiteralPath $artifactPath).LastWriteTimeUtc = [DateTime]::UtcNow
$result = [ordered]@{
    schemaVersion = 1
    workflow = '__WORKFLOW__'
    iteration = $Iteration
    status = 'pass'
    hookState = 'released'
    releaseVerification = [ordered]@{ succeeded = $true; method = 'Synthetic tooling fixture released its hook.' }
    databaseCorrelation = [ordered]@{
        commandCaptureId = '__CAPTURE_ID__'
        method = 'Synthetic tooling fixture read the additive CLI response field.'
    }
    artifacts = @([ordered]@{ path = $artifactName; kind = 'image' })
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
'@
        $probeDefinitions = @(
            @('capture', $captureSource, 'capture.png', $captureId),
            @('scrolling', $scrollSource, 'scroll.png', $scrollId)
        )
        $probePaths = @{}
        foreach ($definition in $probeDefinitions) {
            $probePath = Join-Path $correlationRoot ($definition[0] + '-probe.ps1')
            $content = $captureProbeTemplate.Replace('__WORKFLOW__', $definition[0])
            $content = $content.Replace('__SOURCE__', $definition[1].Replace("'", "''"))
            $content = $content.Replace('__ARTIFACT__', $definition[2])
            $content = $content.Replace('__CAPTURE_ID__', $definition[3])
            Set-Content -LiteralPath $probePath -Value $content -Encoding UTF8
            $probePaths[$definition[0]] = $probePath
        }

        $dictationProbe = Join-Path $correlationRoot 'dictation-probe.ps1'
        @'
param([int]$Iteration, [string]$ResultPath, [string]$EvidenceDirectory, [int]$ProcessId)
$artifactName = 'transcript.txt'
'octadock acceptance phrase' | Set-Content -LiteralPath (Join-Path $EvidenceDirectory $artifactName) -Encoding UTF8
$result = [ordered]@{
    schemaVersion = 1
    workflow = 'dictation'
    iteration = $Iteration
    status = 'pass'
    deviceState = 'released'
    expectedTranscript = 'octadock acceptance phrase'
    releaseVerification = [ordered]@{ succeeded = $true; method = 'Synthetic tooling fixture released its device.' }
    artifacts = @([ordered]@{ path = $artifactName; kind = 'text' })
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
'@ | Set-Content -LiteralPath $dictationProbe -Encoding UTF8

        $fixtureProcess = Start-Process `
            -FilePath $testProcessPath `
            -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 120') `
            -WindowStyle Hidden `
            -PassThru
        try {
            $correlationOutput = Join-Path $correlationRoot 'soak-result.json'
            Invoke-ChildTool -Script (Join-Path $toolsRoot 'Invoke-SoakAcceptance.ps1') -Arguments @(
                '-ProcessId', $fixtureProcess.Id,
                '-CaptureProbePath', $probePaths['capture'],
                '-ScrollingProbePath', $probePaths['scrolling'],
                '-DictationProbePath', $dictationProbe,
                '-CaptureCycles', '1',
                '-ScrollingSessions', '1',
                '-DictationSessions', '1',
                '-DatabasePath', $databasePath,
                '-AcknowledgeDesktopAndMicrophoneInteraction',
                '-AllowNonOctadockProcessForToolingTest',
                '-OutputPath', $correlationOutput
            ) -ExpectedExitCode 0
            $correlation = Get-Content -Raw -LiteralPath $correlationOutput | ConvertFrom-Json
            Assert-True -Condition ($correlation.overallStatus -eq 'tooling_test_only') -Message 'Synthetic soak must be labeled tooling_test_only.'
            $captureCorrelation = ($correlation.workflowResults | Where-Object { $_.id -eq 'capture' }).iterations[0].databaseCorrelation
            $scrollCorrelation = ($correlation.workflowResults | Where-Object { $_.id -eq 'scrolling' }).iterations[0].databaseCorrelation
            Assert-True -Condition ($captureCorrelation.commandCaptureId -eq $captureId) -Message 'Capture command ID must correlate to the exact SQLite row.'
            Assert-True -Condition ($captureCorrelation.managedFileSha256 -eq (Get-FileHash $captureSource -Algorithm SHA256).Hash.ToLowerInvariant()) -Message 'Capture evidence must match the managed file hash.'
            Assert-True -Condition ($scrollCorrelation.commandCaptureId -eq $scrollId) -Message 'Scrolling command ID must correlate to the exact SQLite row.'
            Assert-True -Condition ($scrollCorrelation.managedFileSha256 -eq (Get-FileHash $scrollSource -Algorithm SHA256).Hash.ToLowerInvariant()) -Message 'Scrolling evidence must match the managed file hash.'
        }
        finally {
            if ($null -ne $fixtureProcess -and -not $fixtureProcess.HasExited) {
                Stop-Process -Id $fixtureProcess.Id -Force
                [void]$fixtureProcess.WaitForExit(5000)
            }
        }
    }
    else {
        Write-Host 'Skipping synthetic soak correlation test because sqlite3 is unavailable.' -ForegroundColor Yellow
    }

    $staticOutput = Join-Path $testRoot 'wpf-static.json'
    Invoke-ChildTool -Script (Join-Path $toolsRoot 'Test-WpfStaticAcceptance.ps1') -Arguments @(
        '-NoBaseline',
        '-OutputPath', $staticOutput
    ) -ExpectedExitCode 0
    $static = Get-Content -Raw -LiteralPath $staticOutput | ConvertFrom-Json
    Assert-True -Condition ($static.overallStatus -eq 'clean') -Message 'Remediated WPF sources must keep the static gate clean.'
    Assert-True -Condition ($static.summary.newFindings -eq 0) -Message 'Static evidence must report zero new findings.'
    Assert-True -Condition ($static.summary.knownFindings -eq 0) -Message 'Static evidence must not hide findings in the baseline.'

    Write-Host "Acceptance tooling self-test passed: $assertionCount assertions." -ForegroundColor Green
    exit 0
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
