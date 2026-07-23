#Requires -Version 5.1
<#
.SYNOPSIS
    Generates the versioned STT benchmark corpus locally.

.DESCRIPTION
    Reads tools/acceptance/stt/manifest.json and synthesizes one 16 kHz 16-bit
    mono WAV per case with Windows SAPI (System.Speech), then applies the
    case's deterministic post-processing (white-noise mixing at a defined SNR,
    gain for quiet-microphone cases). Only the manifest and this script are
    committed; WAV output lands in the gitignored artifacts tree.

    Cases whose required voice language is not installed are recorded as
    pending (never fabricated, never substituted with a wrong-language voice).

    System.Speech requires .NET Framework, so under PowerShell 7 the script
    re-invokes itself with Windows PowerShell 5.1 (in-box on Windows).
#>
[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'manifest.json'),

    [string]$OutputDirectory,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    $ps5 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $ps5)) {
        throw 'Windows PowerShell 5.1 is required for System.Speech corpus generation.'
    }

    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
        '-ManifestPath', $ManifestPath)
    if ($OutputDirectory) {
        $arguments += @('-OutputDirectory', $OutputDirectory)
    }
    if ($Force) {
        $arguments += '-Force'
    }

    & $ps5 @arguments
    exit $LASTEXITCODE
}

Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'Acceptance.Common.psm1') -Force
$repoRoot = Get-OctadockRepoRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/acceptance/stt/corpus'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-AcceptanceDirectory -Path $OutputDirectory

Add-Type -AssemblyName System.Speech

# PCM16 WAV I/O and deterministic mixing, compiled once for speed (the
# long-form case is ~1M samples; a pure-PowerShell sample loop would crawl).
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public static class SttCorpusDsp
{
    public static float[] ReadWavPcm16(string path, out int sampleRate)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            throw new InvalidDataException("Not a RIFF/WAVE file: " + path);
        }

        sampleRate = 0;
        short channels = 0, bitsPerSample = 0;
        int dataOffset = -1, dataLength = 0;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            string chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
            int chunkSize = BitConverter.ToInt32(bytes, offset + 4);
            if (chunkId == "fmt ")
            {
                short audioFormat = BitConverter.ToInt16(bytes, offset + 8);
                channels = BitConverter.ToInt16(bytes, offset + 10);
                sampleRate = BitConverter.ToInt32(bytes, offset + 12);
                bitsPerSample = BitConverter.ToInt16(bytes, offset + 22);
                if (audioFormat != 1 || channels != 1 || bitsPerSample != 16)
                {
                    throw new InvalidDataException(
                        "Expected 16-bit mono PCM, got format=" + audioFormat +
                        " channels=" + channels + " bits=" + bitsPerSample + ": " + path);
                }
            }
            else if (chunkId == "data")
            {
                dataOffset = offset + 8;
                dataLength = Math.Min(chunkSize, bytes.Length - dataOffset);
            }

            offset += 8 + chunkSize + (chunkSize & 1);
        }

        if (dataOffset < 0 || sampleRate <= 0)
        {
            throw new InvalidDataException("WAVE file is missing fmt/data chunks: " + path);
        }

        int count = dataLength / 2;
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, dataOffset + 2 * i) / 32768f;
        }

        return samples;
    }

    public static void WriteWavPcm16(string path, float[] samples, int sampleRate)
    {
        int dataLength = samples.Length * 2;
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);      // PCM
            writer.Write((short)1);      // mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2); // byte rate
            writer.Write((short)2);      // block align
            writer.Write((short)16);     // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            foreach (float sample in samples)
            {
                float clamped = Math.Max(-1f, Math.Min(1f, sample));
                writer.Write((short)Math.Round(clamped * 32767f, MidpointRounding.AwayFromZero));
            }
        }
    }

    public static double Rms(float[] samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        double sumSquares = 0;
        foreach (float sample in samples)
        {
            sumSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumSquares / samples.Length);
    }

    public static float[] ApplyGainDb(float[] samples, double gainDb)
    {
        double gain = Math.Pow(10.0, gainDb / 20.0);
        var result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            result[i] = (float)(samples[i] * gain);
        }

        return result;
    }

    // White noise mixed so noise RMS sits snrDb below the speech RMS.
    public static float[] MixWhiteNoise(float[] samples, double snrDb, int seed)
    {
        double speechRms = Rms(samples);
        double noiseRms = speechRms / Math.Pow(10.0, snrDb / 20.0);
        // Uniform [-a, a] has RMS a/sqrt(3); scale the amplitude accordingly.
        double amplitude = noiseRms * Math.Sqrt(3.0);
        var random = new Random(seed);
        var result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            double noise = (2.0 * random.NextDouble() - 1.0) * amplitude;
            result[i] = (float)(samples[i] + noise);
        }

        return result;
    }
}
'@

function Get-CaseSeed {
    param([Parameter(Mandatory)][string]$Id)

    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Id))
        return [BitConverter]::ToInt32($bytes, 0)
    }
    finally {
        $md5.Dispose()
    }
}

function Select-SapiVoice {
    param(
        [Parameter(Mandatory)][object[]]$InstalledVoices,
        [Parameter(Mandatory)][string]$Requested,
        [switch]$ExactCultureOnly
    )

    # Exact culture first ("en-GB"), then same base language ("en" → "en-US",
    # preferring the US default voice when several exist).
    $exact = @($InstalledVoices | Where-Object {
            $_.VoiceInfo.Culture -ieq $Requested
        })
    if ($exact.Count -gt 0) {
        return $exact[0]
    }

    if ($ExactCultureOnly) {
        return $null
    }

    $base = ($Requested -split '-')[0]
    $candidates = @($InstalledVoices | Where-Object {
            ([string]$_.VoiceInfo.Culture).StartsWith($base + '-', [StringComparison]::OrdinalIgnoreCase)
        } | Sort-Object { $_.VoiceInfo.Culture })
    if ($candidates.Count -eq 0) {
        return $null
    }

    $preferred = @($candidates | Where-Object { $_.VoiceInfo.Culture -ieq ($base + '-US') })
    if ($preferred.Count -gt 0) {
        return $preferred[0]
    }

    return $candidates[0]
}

$manifest = Read-AcceptanceJson -Path $ManifestPath
if ([int]$manifest.schemaVersion -ne 1) {
    throw "Unsupported corpus manifest schemaVersion: $($manifest.schemaVersion)"
}

$sampleRate = [int]$manifest.sampleRate
$synthesizer = New-Object System.Speech.Synthesis.SpeechSynthesizer
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    $sampleRate,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono)

$voiceInventory = New-Object System.Collections.Generic.List[object]
foreach ($installed in @($synthesizer.GetInstalledVoices())) {
    $voiceInventory.Add([ordered]@{
            name = [string]$installed.VoiceInfo.Name
            culture = [string]$installed.VoiceInfo.Culture
        })
}

$installedVoices = @($synthesizer.GetInstalledVoices())
$results = New-Object System.Collections.Generic.List[object]
$audioFormatString = "$sampleRate Hz 16-bit mono PCM"

foreach ($case in @($manifest.cases)) {
    $caseId = [string]$case.id
    $wavName = "$caseId.wav"
    $wavPath = Join-Path $OutputDirectory $wavName

    $exactOnly = [string](Get-AcceptancePropertyValue -Object $case -Name 'voiceMatch') -eq 'exact'
    $voice = Select-SapiVoice -InstalledVoices $installedVoices -Requested ([string]$case.voiceLanguage) `
        -ExactCultureOnly:$exactOnly
    if ($null -eq $voice) {
        Write-Host "pending: $caseId (no installed SAPI voice for '$($case.voiceLanguage)')"
        $results.Add([pscustomobject][ordered]@{
                id = $caseId
                category = [string]$case.category
                status = 'pending'
                reason = "No installed SAPI voice for language '$($case.voiceLanguage)'."
                wav = $null
            })
        continue
    }

    if ((Test-Path -LiteralPath $wavPath) -and -not $Force) {
        Write-Host "cached:  $caseId"
        continue
    }

    $tempWav = Join-Path $OutputDirectory "$caseId.raw.wav"
    try {
        $synthesizer.SelectVoice([string]$voice.VoiceInfo.Name)
        $synthesizer.Rate = 0
        $synthesizer.Volume = 100
        $synthesizer.SetOutputToWaveFile($tempWav, $format)
        $synthesizer.Speak([string]$case.text)
        $synthesizer.SetOutputToNull()

        $rate = 0
        $samples = [SttCorpusDsp]::ReadWavPcm16($tempWav, [ref]$rate)
        if ($rate -ne $sampleRate) {
            throw "SAPI returned $rate Hz instead of $sampleRate Hz for case '$caseId'."
        }

        $seed = Get-CaseSeed -Id $caseId
        $gainDb = Get-AcceptancePropertyValue -Object $case -Name 'gainDb'
        if ($null -ne $gainDb) {
            $samples = [SttCorpusDsp]::ApplyGainDb($samples, [double]$gainDb)
        }

        $noise = Get-AcceptancePropertyValue -Object $case -Name 'noise'
        if ($null -ne $noise) {
            if ([string]$noise.type -ne 'white') {
                throw "Unsupported noise type '$($noise.type)' for case '$caseId'."
            }

            $samples = [SttCorpusDsp]::MixWhiteNoise($samples, [double]$noise.snrDb, $seed)
        }

        [SttCorpusDsp]::WriteWavPcm16($wavPath, $samples, $sampleRate)

        $results.Add([pscustomobject][ordered]@{
                id = $caseId
                category = [string]$case.category
                status = 'ready'
                reason = $null
                wav = $wavName
                sha256 = (Get-FileHash -LiteralPath $wavPath -Algorithm SHA256).Hash.ToLowerInvariant()
                durationSeconds = [Math]::Round($samples.Length / [double]$sampleRate, 3)
                rms = [Math]::Round([SttCorpusDsp]::Rms($samples), 6)
                voiceName = [string]$voice.VoiceInfo.Name
                voiceCulture = [string]$voice.VoiceInfo.Culture
                snrDb = if ($null -ne $noise) { [double]$noise.snrDb } else { $null }
                gainDb = if ($null -ne $gainDb) { [double]$gainDb } else { $null }
            })
        Write-Host "ready:   $caseId ($([Math]::Round($samples.Length / $sampleRate, 1))s, $($voice.VoiceInfo.Name))"
    }
    finally {
        if (Test-Path -LiteralPath $tempWav) {
            Remove-Item -LiteralPath $tempWav -Force
        }
    }
}

# Cached cases from an earlier run are merged back in from the previous
# generation record so the corpus record always covers the full manifest.
$recordPath = Join-Path $OutputDirectory 'corpus.generated.json'
if (Test-Path -LiteralPath $recordPath) {
    $previous = Read-AcceptanceJson -Path $recordPath
    $freshIds = @($results | ForEach-Object { $_.id })
    foreach ($entry in @($previous.cases)) {
        if ($freshIds -notcontains [string]$entry.id) {
            $wavStillThere = $null -ne $entry.wav -and
                (Test-Path -LiteralPath (Join-Path $OutputDirectory ([string]$entry.wav)))
            if ($entry.status -eq 'pending' -or $wavStillThere) {
                $results.Add($entry)
            }
        }
    }
}

$synthesizer.Dispose()

$record = [ordered]@{
    schemaVersion = 1
    corpusId = [string]$manifest.corpusId
    manifestSchemaVersion = [int]$manifest.schemaVersion
    manifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    audioFormat = $audioFormatString
    voices = $voiceInventory.ToArray()
    cases = @($results | Sort-Object { $_.id })
}

Write-AcceptanceJson -Value ([pscustomobject]$record) -Path $recordPath
$ready = @($results | Where-Object { $_.status -eq 'ready' }).Count
$pending = @($results | Where-Object { $_.status -eq 'pending' }).Count
Write-Host "Corpus generated: $ready ready, $pending pending -> $OutputDirectory"
