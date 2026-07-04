#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and stages a versioned Octadock release package.

.DESCRIPTION
    Validates version metadata, restores/builds/tests the full solution in
    Release, publishes the tray app and CLI, then stages outputs under
    artifacts/release/<version>.

.PARAMETER SkipTests
    Skip the test step. Use only for local smoke packaging after a separate
    Release test run.

.PARAMETER NoArchive
    Do not create the zip archive. The versioned staging folder is still
    produced.

.PARAMETER Strict
    Build and test with ContinuousIntegrationBuild=true. This enables the same
    warnings-as-errors gate used by CI and can fail on existing analyzer debt.

.EXAMPLE
    ./build/release.ps1

.EXAMPLE
    ./build/release.ps1 -SkipTests -NoArchive
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$NoArchive,
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot 'Octadock.sln'
$VersionJsonPath = Join-Path $RepoRoot 'version.json'
$VersionPropsPath = Join-Path $RepoRoot 'build/version.props'
$VersionScript = Join-Path $PSScriptRoot 'version.ps1'
$AppProject = Join-Path $RepoRoot 'src/Octadock.App/Octadock.App.csproj'
$CliProject = Join-Path $RepoRoot 'src/Octadock.Cli/Octadock.Cli.csproj'
$ArtifactsRoot = Join-Path $RepoRoot 'artifacts'
$ReleaseRoot = Join-Path $ArtifactsRoot 'release'
$buildProperties =
    if ($Strict) {
        @('-p:ContinuousIntegrationBuild=true')
    }
    else {
        @()
    }

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)

    if (-not $pathFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside '$parentFull': '$pathFull'."
    }
}

function Get-NullableString {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    return [string]$Value
}

function Read-ReleaseVersion {
    if (-not (Test-Path $VersionJsonPath)) {
        throw "Missing $VersionJsonPath."
    }

    $metadata = Get-Content -Path $VersionJsonPath -Raw | ConvertFrom-Json

    if ([int]$metadata.schema -ne 1) {
        throw "Unsupported version.json schema '$($metadata.schema)'."
    }

    $prefix = [string]$metadata.versionPrefix
    $suffix = Get-NullableString $metadata.versionSuffix
    $channel = [string]$metadata.channel
    $releaseDate = Get-NullableString $metadata.releaseDate

    if ($prefix -notmatch '^\d+\.\d+\.\d+$') {
        throw "version.json versionPrefix '$prefix' is not a SemVer core version."
    }

    if (-not [string]::IsNullOrWhiteSpace($suffix) -and $suffix -notmatch '^[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$') {
        throw "version.json versionSuffix '$suffix' is not a valid SemVer pre-release suffix."
    }

    $expectedChannel =
        if ([string]::IsNullOrWhiteSpace($suffix)) {
            'stable'
        }
        else {
            ($suffix -split '\.')[0]
        }

    if ($channel -ne $expectedChannel) {
        throw "version.json channel '$channel' should be '$expectedChannel'."
    }

    if (-not [string]::IsNullOrWhiteSpace($releaseDate)) {
        $parsedDate = [datetime]::MinValue
        if (-not [datetime]::TryParseExact(
                $releaseDate,
                'yyyy-MM-dd',
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::None,
                [ref]$parsedDate)) {
            throw "version.json releaseDate '$releaseDate' must use yyyy-MM-dd."
        }
    }

    $fullVersion =
        if ([string]::IsNullOrWhiteSpace($suffix)) {
            $prefix
        }
        else {
            "$prefix-$suffix"
        }

    [pscustomobject]@{
        Version = $fullVersion
        VersionPrefix = $prefix
        VersionSuffix = if ($null -eq $suffix) { '' } else { $suffix }
        Channel = $channel
        ReleaseDate = $releaseDate
    }
}

function Get-GitValue {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $value = & git -C $RepoRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return ($value | Select-Object -First 1)
}

function Get-RelativePath {
    param([Parameter(Mandatory)][string]$Path)

    $root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $full = [System.IO.Path]::GetFullPath($Path)

    if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($root.Length).Replace('\', '/')
    }

    return $full
}

function Write-Sha256Sums {
    param([Parameter(Mandatory)][string]$Directory)

    $checksumsPath = Join-Path $Directory 'SHA256SUMS.txt'
    $files = Get-ChildItem -Path $Directory -Recurse -File |
        Where-Object { $_.FullName -ne $checksumsPath } |
        Sort-Object FullName

    $lines = foreach ($file in $files) {
        $relative = $file.FullName.Substring(([System.IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar).Length).Replace('\', '/')
        $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant())  $relative"
    }

    $lines | Set-Content -Path $checksumsPath -Encoding UTF8
    return $checksumsPath
}

Invoke-Step 'Validate version metadata' {
    & $VersionScript -Check
}

$version = Read-ReleaseVersion
$releaseDir = Join-Path $ReleaseRoot $version.Version
$testResultsDir = Join-Path $releaseDir 'test-results'
$publishDir = Join-Path $releaseDir 'publish'
$appPublishDir = Join-Path $publishDir 'octadock'
$cliPublishDir = Join-Path $appPublishDir 'cli'
$manifestPath = Join-Path $releaseDir 'release-manifest.json'
$archivePath = Join-Path $releaseDir "Octadock-$($version.Version)-windows.zip"

Assert-ChildPath -Path $releaseDir -Parent $ReleaseRoot

Invoke-Step "Prepare staging folder ($($version.Version))" {
    if (Test-Path $releaseDir) {
        Remove-Item -Path $releaseDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $testResultsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $appPublishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $cliPublishDir -Force | Out-Null
}

Write-Host ''
Write-Host 'Octadock release package' -ForegroundColor Green
Write-Host "  Version       : $($version.Version)"
Write-Host "  Channel       : $($version.Channel)"
Write-Host "  Staging       : $releaseDir"
Write-Host "  Strict CI gate: $Strict"
Write-Host "  dotnet        : $((dotnet --version))"

Invoke-Step 'Restore' {
    Invoke-DotNet -Name 'Restore' -Arguments @('restore', $Solution)
}

Invoke-Step 'Build (Release)' {
    Invoke-DotNet -Name 'Build' -Arguments (@('build', $Solution, '-c', 'Release', '--no-restore') + $buildProperties)
}

if (-not $SkipTests) {
    Invoke-Step 'Test (Release)' {
        Invoke-DotNet -Name 'Test' -Arguments (@(
                'test',
                $Solution,
                '-c',
                'Release',
                '--no-build',
                '--logger',
                'trx',
                '--results-directory',
                $testResultsDir
            ) + $buildProperties)
    }
}
else {
    Write-Host ''
    Write-Host '==> Test (skipped)' -ForegroundColor Yellow
}

Invoke-Step 'Publish app (Release)' {
    Invoke-DotNet -Name 'Publish app' -Arguments (@(
            'publish',
            $AppProject,
            '-c',
            'Release',
            '--no-build',
            '-o',
            $appPublishDir
        ) + $buildProperties)
}

Invoke-Step 'Publish CLI (Release)' {
    Invoke-DotNet -Name 'Publish CLI' -Arguments (@(
            'publish',
            $CliProject,
            '-c',
            'Release',
            '--no-build',
            '-o',
            $cliPublishDir
        ) + $buildProperties)
}

Invoke-Step 'Copy release metadata' {
    Copy-Item -Path $VersionJsonPath -Destination (Join-Path $releaseDir 'version.json') -Force
    Copy-Item -Path $VersionPropsPath -Destination (Join-Path $releaseDir 'version.props') -Force
    Copy-Item -Path (Join-Path $RepoRoot 'CHANGELOG.md') -Destination (Join-Path $releaseDir 'CHANGELOG.md') -Force
    Copy-Item -Path (Join-Path $RepoRoot 'LICENSE') -Destination (Join-Path $releaseDir 'LICENSE') -Force
}

$archiveRelative = $null
if (-not $NoArchive) {
    $archiveRelative = Get-RelativePath $archivePath
}

Invoke-Step 'Write release manifest' {
    $manifest = [ordered]@{
        schema = 1
        product = 'Octadock'
        version = $version.Version
        versionPrefix = $version.VersionPrefix
        versionSuffix = $version.VersionSuffix
        channel = $version.Channel
        releaseDate = $version.ReleaseDate
        configuration = 'Release'
        strict = [bool]$Strict
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        dotnetSdk = (& dotnet --version)
        git = [ordered]@{
            branch = Get-GitValue -Arguments @('branch', '--show-current')
            commit = Get-GitValue -Arguments @('rev-parse', 'HEAD')
        }
        outputs = [ordered]@{
            stagingDirectory = Get-RelativePath $releaseDir
            appPublish = Get-RelativePath $appPublishDir
            cliPublish = Get-RelativePath $cliPublishDir
            testResults = if ($SkipTests) { $null } else { Get-RelativePath $testResultsDir }
            archive = $archiveRelative
        }
    }

    $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8
}

if (-not $NoArchive) {
    Invoke-Step 'Create zip archive' {
        $archiveInputs = @(
            $publishDir,
            (Join-Path $releaseDir 'version.json'),
            (Join-Path $releaseDir 'version.props'),
            (Join-Path $releaseDir 'CHANGELOG.md'),
            (Join-Path $releaseDir 'LICENSE'),
            $manifestPath
        )

        Compress-Archive -Path $archiveInputs -DestinationPath $archivePath -Force
    }
}

Invoke-Step 'Write SHA256 checksums' {
    $checksumsPath = Write-Sha256Sums -Directory $releaseDir
    Write-Host "Checksums: $checksumsPath"
}

Write-Host ''
Write-Host 'Release package staged.' -ForegroundColor Green
Write-Host "  Folder  : $releaseDir"
if (-not $NoArchive) {
    Write-Host "  Archive : $archivePath"
}
Write-Host "  Manifest: $manifestPath"
