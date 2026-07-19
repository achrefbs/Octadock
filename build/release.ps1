#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and stages a versioned Octadock release package.

.DESCRIPTION
    Validates version and optional tag/source preconditions, restores/builds/
    tests the full solution in Release, publishes the tray app and CLI, enforces
    the public-artifact boundary, then stages outputs under
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

.PARAMETER ExpectedTag
    Optional release tag to validate. It must be exactly v<version>, where
    <version> comes from version.json (for example v0.2.0-alpha.0).

.PARAMETER RequireTagAtHead
    Require ExpectedTag to exist as an exact Git tag and resolve to HEAD. The
    tagged GitHub Actions release workflow uses this guard.

.PARAMETER RequireCleanWorkingTree
    Fail when tracked or untracked, non-ignored files differ from HEAD.

.PARAMETER ValidateOnly
    Validate metadata and source/tag preconditions without restoring, building,
    testing, publishing, or writing release artifacts.

.EXAMPLE
    ./build/release.ps1

.EXAMPLE
    ./build/release.ps1 -SkipTests -NoArchive

.EXAMPLE
    ./build/release.ps1 -ValidateOnly -Strict -ExpectedTag v0.2.0-alpha.0 -RequireCleanWorkingTree
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$NoArchive,
    [switch]$Strict,
    [string]$ExpectedTag,
    [switch]$RequireTagAtHead,
    [switch]$RequireCleanWorkingTree,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot 'Octadock.sln'
$VersionJsonPath = Join-Path $RepoRoot 'version.json'
$VersionPropsPath = Join-Path $RepoRoot 'build/version.props'
$VersionScript = Join-Path $PSScriptRoot 'version.ps1'
$PublicBoundaryScript = Join-Path $PSScriptRoot 'public-artifact-boundary.ps1'
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

function Assert-RequiredFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing required release input '$Path'."
    }
}

function Assert-ReleaseSource {
    param([Parameter(Mandatory)]$Version)

    $canonicalTag = "v$($Version.Version)"

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTag) -and
        -not [string]::Equals($ExpectedTag, $canonicalTag, [System.StringComparison]::Ordinal)) {
        throw "Release tag '$ExpectedTag' must exactly match '$canonicalTag'."
    }

    $headCommit = Get-GitValue -Arguments @('rev-parse', 'HEAD')
    if (($RequireTagAtHead -or $RequireCleanWorkingTree) -and [string]::IsNullOrWhiteSpace($headCommit)) {
        throw 'The requested source checks require a Git worktree with a resolvable HEAD.'
    }

    if ($RequireTagAtHead) {
        if ([string]::IsNullOrWhiteSpace($ExpectedTag)) {
            throw '-RequireTagAtHead requires -ExpectedTag.'
        }

        $tagRef = "refs/tags/$ExpectedTag"
        $verifiedRef = Get-GitValue -Arguments @('show-ref', '--verify', $tagRef)
        if ([string]::IsNullOrWhiteSpace($verifiedRef)) {
            throw "Required release tag '$ExpectedTag' does not exist as an exact tag ref."
        }

        $tagCommit = Get-GitValue -Arguments @('rev-list', '-n', '1', $ExpectedTag)
        if (-not [string]::Equals($tagCommit, $headCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Release tag '$ExpectedTag' resolves to '$tagCommit', not HEAD '$headCommit'."
        }
    }

    if ($RequireCleanWorkingTree) {
        $status = & git -C $RepoRoot status --porcelain=v1 --untracked-files=all 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to inspect the Git working tree for release cleanliness.'
        }

        if ($status) {
            $sample = ($status | Select-Object -First 10) -join [Environment]::NewLine
            throw "Release source is not clean. Commit or remove these changes first:$([Environment]::NewLine)$sample"
        }
    }

    return [pscustomobject]@{
        CanonicalTag = $canonicalTag
        HeadCommit = $headCommit
        TagValidated = [bool]$RequireTagAtHead
        CleanWorkingTreeValidated = [bool]$RequireCleanWorkingTree
    }
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
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string[]]$Files
    )

    $checksumsPath = Join-Path $Directory 'SHA256SUMS.txt'
    $uniquePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $resolvedFiles = foreach ($filePath in $Files) {
        Assert-ChildPath -Path $filePath -Parent $Directory
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Cannot checksum missing release output '$filePath'."
        }

        $fullPath = [System.IO.Path]::GetFullPath($filePath)
        if ($uniquePaths.Add($fullPath)) {
            Get-Item -LiteralPath $fullPath
        }
    }

    $lines = foreach ($file in ($resolvedFiles | Sort-Object FullName)) {
        $relative = $file.FullName.Substring(([System.IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar).Length).Replace('\', '/')
        $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant())  $relative"
    }

    $lines | Set-Content -Path $checksumsPath -Encoding UTF8
    return $checksumsPath
}

function Test-Sha256Sums {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$ChecksumsPath
    )

    $lines = @(Get-Content -LiteralPath $ChecksumsPath)
    if ($lines.Count -eq 0) {
        throw "Checksum file '$ChecksumsPath' is empty."
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($line in $lines) {
        if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
            throw "Malformed SHA256SUMS entry '$line'."
        }

        $expected = $Matches[1].ToLowerInvariant()
        $relative = $Matches[2]
        if (-not $seen.Add($relative)) {
            throw "Duplicate SHA256SUMS entry '$relative'."
        }

        $target = Join-Path $Directory ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        Assert-ChildPath -Path $target -Parent $Directory
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            throw "SHA256SUMS entry '$relative' does not exist."
        }

        $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not [string]::Equals($expected, $actual, [System.StringComparison]::Ordinal)) {
            throw "SHA-256 mismatch for '$relative': expected '$expected', actual '$actual'."
        }
    }

    return $lines.Count
}

function Write-PublicBoundaryEvidence {
    param(
        [Parameter(Mandatory)][string]$ArtifactPath,
        [Parameter(Mandatory)][string]$EvidencePath,
        [AllowNull()][string]$GitCommit
    )

    & $PublicBoundaryScript -ArtifactPath $ArtifactPath

    $files = @(Get-ChildItem -LiteralPath $ArtifactPath -Recurse -File)
    $totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $totalBytes) {
        $totalBytes = 0
    }

    $evidence = [ordered]@{
        schema = 1
        gate = 'public-artifact-boundary'
        result = 'passed'
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        gitCommit = $GitCommit
        script = Get-RelativePath $PublicBoundaryScript
        scriptSha256 = (Get-FileHash -LiteralPath $PublicBoundaryScript -Algorithm SHA256).Hash.ToLowerInvariant()
        artifactPath = Get-RelativePath $ArtifactPath
        artifactFileCount = $files.Count
        artifactBytes = [long]$totalBytes
    }

    $evidence | ConvertTo-Json -Depth 4 | Set-Content -Path $EvidencePath -Encoding UTF8
}

foreach ($requiredFile in @(
        $Solution,
        $VersionJsonPath,
        $VersionPropsPath,
        $VersionScript,
        $PublicBoundaryScript,
        $AppProject,
        $CliProject,
        (Join-Path $RepoRoot 'CHANGELOG.md'),
        (Join-Path $RepoRoot 'LICENSE')
    )) {
    Assert-RequiredFile -Path $requiredFile
}

Invoke-Step 'Validate version metadata' {
    & $VersionScript -Check
}

$version = Read-ReleaseVersion
$source = Assert-ReleaseSource -Version $version

Write-Host ''
Write-Host 'Octadock release preflight' -ForegroundColor Green
Write-Host "  Version       : $($version.Version)"
Write-Host "  Canonical tag : $($source.CanonicalTag)"
Write-Host "  Expected tag  : $(if ([string]::IsNullOrWhiteSpace($ExpectedTag)) { '(not supplied)' } else { $ExpectedTag })"
Write-Host "  Tag at HEAD   : $($source.TagValidated)"
Write-Host "  Clean source  : $($source.CleanWorkingTreeValidated)"
Write-Host "  Strict CI gate: $Strict"

if ($ValidateOnly) {
    Write-Host ''
    Write-Host 'Release preflight passed; no artifacts were written.' -ForegroundColor Green
    return
}

$releaseDir = Join-Path $ReleaseRoot $version.Version
$testResultsDir = Join-Path $releaseDir 'test-results'
$publishDir = Join-Path $releaseDir 'publish'
$appPublishDir = Join-Path $publishDir 'octadock'
$cliPublishDir = Join-Path $appPublishDir 'cli'
$manifestPath = Join-Path $releaseDir 'release-manifest.json'
$publicBoundaryEvidencePath = Join-Path $releaseDir 'public-artifact-boundary.json'
$checksumsPath = Join-Path $releaseDir 'SHA256SUMS.txt'
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
Write-Host "  Source commit : $($source.HeadCommit)"
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

    Invoke-Step 'Verify test evidence' {
        $trxFiles = @(Get-ChildItem -LiteralPath $testResultsDir -Recurse -File -Filter *.trx)
        if ($trxFiles.Count -eq 0) {
            throw "Release tests passed without producing TRX evidence under '$testResultsDir'."
        }

        Write-Host "Recorded $($trxFiles.Count) TRX result files."
    }
}
else {
    Write-Host ''
    Write-Host '==> Test (skipped)' -ForegroundColor Yellow
}

# Distribution build (WS1, R1/R10): publish a self-contained, single-file
# win-x64 executable so a clean Windows machine with no .NET runtime installed
# can run Octadock without an install/runtime step. RID publish must compile for
# that runtime, so these steps intentionally omit --no-build (the earlier AnyCPU
# Release build/test still gate the code; publish rebuilds for win-x64).
$PublishRid = 'win-x64'
$PublishArguments = @(
    '-c',
    'Release',
    '-r',
    $PublishRid,
    '--self-contained',
    'true',
    '-p:PublishSingleFile=true'
)

Invoke-Step "Publish app (Release, self-contained single-file $PublishRid)" {
    Invoke-DotNet -Name 'Publish app' -Arguments (@(
            'publish',
            $AppProject
        ) + $PublishArguments + @(
            '-o',
            $appPublishDir
        ) + $buildProperties)
}

Invoke-Step "Publish CLI (Release, self-contained single-file $PublishRid)" {
    Invoke-DotNet -Name 'Publish CLI' -Arguments (@(
            'publish',
            $CliProject
        ) + $PublishArguments + @(
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

Invoke-Step 'Enforce public artifact boundary and record evidence' {
    Write-PublicBoundaryEvidence `
        -ArtifactPath $appPublishDir `
        -EvidencePath $publicBoundaryEvidencePath `
        -GitCommit $source.HeadCommit
}

$archiveRelative = $null
if (-not $NoArchive) {
    $archiveRelative = Get-RelativePath $archivePath
}

Invoke-Step 'Write release manifest' {
    $manifest = [ordered]@{
        schema = 2
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
            commit = $source.HeadCommit
            expectedTag = if ([string]::IsNullOrWhiteSpace($ExpectedTag)) { $null } else { $ExpectedTag }
            tagAtHeadValidated = $source.TagValidated
            cleanWorkingTreeValidated = $source.CleanWorkingTreeValidated
        }
        gates = [ordered]@{
            versionMetadata = [ordered]@{
                result = 'passed'
                script = Get-RelativePath $VersionScript
            }
            build = [ordered]@{
                result = 'passed'
                configuration = 'Release'
                continuousIntegrationBuild = [bool]$Strict
            }
            tests = [ordered]@{
                result = if ($SkipTests) { 'skipped' } else { 'passed' }
                evidence = if ($SkipTests) { $null } else { Get-RelativePath $testResultsDir }
                trxFileCount = if ($SkipTests) { 0 } else { @(Get-ChildItem -LiteralPath $testResultsDir -Recurse -File -Filter *.trx).Count }
            }
            publicArtifactBoundary = [ordered]@{
                result = 'passed'
                script = Get-RelativePath $PublicBoundaryScript
                evidence = Get-RelativePath $publicBoundaryEvidencePath
            }
        }
        outputs = [ordered]@{
            stagingDirectory = Get-RelativePath $releaseDir
            appPublish = Get-RelativePath $appPublishDir
            cliPublish = Get-RelativePath $cliPublishDir
            testResults = if ($SkipTests) { $null } else { Get-RelativePath $testResultsDir }
            archive = $archiveRelative
            checksums = Get-RelativePath $checksumsPath
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
            $manifestPath,
            $publicBoundaryEvidencePath
        )

        Compress-Archive -Path $archiveInputs -DestinationPath $archivePath -Force
    }
}

Invoke-Step 'Write SHA256 checksums' {
    $checksumInputs = @(
        $manifestPath,
        $publicBoundaryEvidencePath
    )

    if (-not $NoArchive) {
        $checksumInputs += $archivePath
    }
    else {
        $checksumInputs += @(Get-ChildItem -LiteralPath $publishDir -Recurse -File | ForEach-Object { $_.FullName })
    }

    if (-not $SkipTests) {
        $checksumInputs += @(Get-ChildItem -LiteralPath $testResultsDir -Recurse -File -Filter *.trx | ForEach-Object { $_.FullName })
    }

    $writtenChecksumsPath = Write-Sha256Sums -Directory $releaseDir -Files $checksumInputs
    if (-not [string]::Equals($writtenChecksumsPath, $checksumsPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected checksum path '$writtenChecksumsPath'."
    }
    Write-Host "Checksums: $checksumsPath"
}

Invoke-Step 'Verify SHA256 checksums' {
    $verifiedCount = Test-Sha256Sums -Directory $releaseDir -ChecksumsPath $checksumsPath
    Write-Host "Verified $verifiedCount SHA-256 entries."
}

Write-Host ''
Write-Host 'Release package staged.' -ForegroundColor Green
Write-Host "  Folder  : $releaseDir"
if (-not $NoArchive) {
    Write-Host "  Archive : $archivePath"
}
Write-Host "  Manifest: $manifestPath"
Write-Host "  Boundary: $publicBoundaryEvidencePath"
Write-Host "  SHA-256 : $checksumsPath"
