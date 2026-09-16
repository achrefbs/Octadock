#Requires -Version 5.1
<#
.SYNOPSIS
    Verifies release files against an independently approved signing certificate.
.DESCRIPTION
    Read-only: requires trusted embedded Authenticode signatures, timestamps,
    and the expected certificate thumbprint. Returns evidence only if every
    supplied file passes. This does not issue certificates or sign files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$Files,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedThumbprint,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$SignToolPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$signTool = (Resolve-Path -LiteralPath $SignToolPath).Path
if (-not (Test-Path -LiteralPath $signTool -PathType Leaf)) {
    throw 'SignToolPath must point to the Windows SDK signtool.exe.'
}

$evidence = foreach ($file in $Files) {
    if ([string]::IsNullOrWhiteSpace($file) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Signing verification input does not exist: '$file'."
    }
    $path = (Resolve-Path -LiteralPath $file).Path
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode') {
        throw "Expected a trusted embedded signature on '$path'; got $($signature.Status) / $($signature.SignatureType)."
    }
    if ($null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $ExpectedThumbprint) {
        throw "Unexpected signing certificate on '$path'."
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "Missing timestamp on '$path'."
    }

    # /all checks every embedded signature; /tw reports missing timestamps.
    # Warnings (exit code 2) fail along with errors (exit code 1).
    $verification = & $signTool verify /pa /all /tw /v $path 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool verification failed for '$path' (exit $LASTEXITCODE): $($verification -join [Environment]::NewLine)"
    }

    [pscustomobject]@{
        path = $path
        status = 'Valid'
        signatureType = [string]$signature.SignatureType
        signerSubject = $signature.SignerCertificate.Subject
        signerThumbprint = $signature.SignerCertificate.Thumbprint
        timestampSubject = $signature.TimeStamperCertificate.Subject
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        bytes = (Get-Item -LiteralPath $path).Length
    }
}

[pscustomobject]@{
    schema = 1
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    files = @($evidence)
}
