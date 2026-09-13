#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$files = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Include *.cs,*.xaml | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
$patterns = @('new HttpClient', 'new HttpRequestMessage', 'WebRequest.Create', 'new TcpClient', 'new Socket', 'WhisperGgmlDownloader', 'api.openai.com', 'api.elevenlabs.io', '14-day trial', 'Account &amp; Billing', 'active trial or license', 'Cloud speech and AI are opt-in', 'Use with AI…', 'Create AI mockup from selected area')
$hits = $files | Select-String -SimpleMatch -Pattern $patterns
if ($hits) { $hits | ForEach-Object { Write-Host ("{0}:{1} violates the local-only boundary" -f $_.Path,$_.LineNumber) }; exit 1 }
if (Test-Path -LiteralPath (Join-Path $repoRoot 'services/license-service/Octadock.LicenseService.sln')) { throw 'Commercial service must not ship.' }
Write-Host 'Local-only source and copy gate passed.'
exit 0
