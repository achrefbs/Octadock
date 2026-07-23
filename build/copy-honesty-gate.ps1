#Requires -Version 5.1
<#
.SYNOPSIS
    Copy-honesty gate (WS7, R6/R7).

.DESCRIPTION
    Fails the build if user-facing copy makes a privacy / "offline" claim the code
    contradicts. Two claims are banned today:

      * "fully offline" in the app UI layer — dictation (Parakeet/Whisper) needs a
        one-time model download, so no UI copy may promise fully-offline. (Engine
        XML-doc comments in the platform/core layers are internal, not user-facing,
        and are intentionally out of scope.)
      * "Summarizing with local AI" — the explain/summarize flow shells out to
        cloud AI CLIs; calling it "local AI" hides the network hop.

    Extend $Rules as WS7 copy-freeze (plan §6 order 10) lands: recording/Context in
    pricing copy, "local AI" on the CLI path, etc.

.NOTES
    Exit 0 = clean; exit 1 = at least one violation (prints file:line).
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

$Rules = @(
    [pscustomobject]@{
        Name    = 'UI copy claims "fully offline" (dictation needs a one-time model download)'
        Path    = 'src/Octadock.App'
        Pattern = 'fully offline'
    },
    [pscustomobject]@{
        Name    = '"Summarizing with local AI" toast (explain shells out to cloud AI CLIs)'
        Path    = 'src'
        Pattern = 'Summarizing with local AI'
    },
    # Recording ships microphone + system-audio tracks as Beta opt-ins (Phase 1,
    # stream 1c), so "video only" claims are stale in the recording-owned
    # surfaces. KNOWN DEBT outside stream 1c ownership (wave 2a shell pass must
    # update, then widen these rules to src/Octadock.App):
    #   src/Octadock.App/CaptureUx/DockPill.cs  — "Record screen (Beta · MP4 video only)"
    #   src/Octadock.App/CaptureUx/HudWindow.xaml — "Record screen, beta, video only"
    [pscustomobject]@{
        Name    = 'Recording copy claims "video only" (microphone/system audio tracks exist as Beta opt-ins)'
        Path    = 'src/Octadock.App/Services'
        Pattern = 'video only'
    },
    [pscustomobject]@{
        Name    = 'Recording copy claims "video only" (microphone/system audio tracks exist as Beta opt-ins)'
        Path    = 'src/Octadock.App/Settings'
        Pattern = 'video only'
    },
    [pscustomobject]@{
        Name    = 'Recording copy claims audio is "planned"/unavailable (the tracks shipped as Beta opt-ins)'
        Path    = 'src/Octadock.App/Settings'
        Pattern = '— planned'
    }
)

$violations = New-Object System.Collections.Generic.List[object]

foreach ($rule in $Rules) {
    $root = Join-Path $RepoRoot $rule.Path
    if (-not (Test-Path $root)) { continue }

    $hits = Get-ChildItem -Path $root -Recurse -File -Include *.cs, *.xaml |
        Select-String -SimpleMatch -Pattern $rule.Pattern

    foreach ($h in $hits) {
        $violations.Add([pscustomobject]@{
                Rule = $rule.Name
                File = $h.Path
                Line = $h.LineNumber
                Text = $h.Line.Trim()
            })
    }
}

if ($violations.Count -gt 0) {
    Write-Host 'Copy-honesty gate FAILED — dishonest user-facing copy found:' -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Text) -ForegroundColor Red
        Write-Host ("      rule: {0}" -f $v.Rule) -ForegroundColor DarkYellow
    }
    exit 1
}

Write-Host 'Copy-honesty gate passed: no dishonest user-facing copy found.' -ForegroundColor Green
exit 0
