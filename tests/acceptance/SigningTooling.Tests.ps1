#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SignToolPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$verify = Join-Path $repoRoot 'build/verify-signatures.ps1'
$testRoot = Join-Path $repoRoot ('artifacts/signing-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$signTool = (Resolve-Path -LiteralPath $SignToolPath).Path
$certificate = (Get-AuthenticodeSignature -LiteralPath $signTool).SignerCertificate
if ($null -eq $certificate) { throw 'This test requires the signed Windows SDK SignTool.' }
$script:assertions = 0

function Assert-Rejected {
    param([string[]]$Files, [string]$Thumbprint, [string]$ExpectedError)
    $rejected = $false
    $result = $null
    try { $result = & $verify -Files $Files -ExpectedThumbprint $Thumbprint -SignToolPath $signTool }
    catch {
        if ($_.Exception.Message -notlike $ExpectedError) { throw }
        $rejected = $true
    }
    if (-not $rejected -or $null -ne $result) { throw 'Invalid files must fail without returning successful evidence.' }
    $script:assertions++
}

# Use an existing vendor-signed binary; do not create certificates or change trust.
$valid = & $verify -Files @($signTool) -ExpectedThumbprint $certificate.Thumbprint -SignToolPath $signTool
if ($valid.files.Count -ne 1 -or $valid.files[0].signerThumbprint -ne $certificate.Thumbprint) {
    throw 'Trusted signed fixture did not produce the expected evidence.'
}
$script:assertions++

$unsigned = Join-Path $testRoot 'unsigned.ps1'
Set-Content -LiteralPath $unsigned -Value '# Unsigned fixture.' -Encoding utf8
Assert-Rejected -Files @($unsigned) -Thumbprint $certificate.Thumbprint -ExpectedError '*Expected a trusted embedded signature*'
Assert-Rejected -Files @($signTool, $unsigned) -Thumbprint $certificate.Thumbprint -ExpectedError '*Expected a trusted embedded signature*'
Assert-Rejected -Files @($signTool) -Thumbprint ('0' * 40) -ExpectedError '*Unexpected signing certificate*'
Assert-Rejected -Files @(Join-Path $testRoot 'missing.exe') -Thumbprint $certificate.Thumbprint -ExpectedError '*input does not exist*'

$tampered = Join-Path $testRoot 'tampered.exe'
$bytes = [System.IO.File]::ReadAllBytes($signTool)
$bytes[64] = $bytes[64] -bxor 1
[System.IO.File]::WriteAllBytes($tampered, $bytes)
Assert-Rejected -Files @($tampered) -Thumbprint $certificate.Thumbprint -ExpectedError '*Expected a trusted embedded signature*'

[pscustomobject]@{ status = 'passed'; assertions = $script:assertions; fixtures = $testRoot }
