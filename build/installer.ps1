#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [Parameter(Mandatory=$true)][string]$CompilerPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/installer')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$manifest = Get-Content -LiteralPath (Join-Path $payload 'release-manifest.json') -Raw | ConvertFrom-Json
$app = Join-Path $payload 'publish/octadock/Octadock.exe'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw 'The self-contained Windows release payload is missing.' }
if ($manifest.version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$') { throw 'Invalid release version.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
& $compiler "/DPayloadDir=$payload" "/DReleaseVersion=$($manifest.version)" "/DInstallerOutput=$output" (Join-Path $PSScriptRoot 'windows-installer.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compiler failed ($LASTEXITCODE)." }
$installer = Join-Path $output "Octadock-$($manifest.version)-Setup.exe"
[ordered]@{
    version = $manifest.version
    desktopSourceCommit = $manifest.git.commit
    installerSourceCommit = (git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD)
    file = (Split-Path -Leaf $installer)
    bytes = (Get-Item -LiteralPath $installer).Length
    sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    signed = $false
    installScope = 'current user'
    dataPreservedOnUninstall = '%LOCALAPPDATA%\Octadock'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'installer-manifest.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $output 'installer-manifest.json')
