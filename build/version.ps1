#Requires -Version 5.1
<#
.SYNOPSIS
    Shows, validates, or updates the Octadock product version.

.DESCRIPTION
    Octadock keeps human-readable release metadata in version.json and MSBuild
    stamping metadata in build/version.props. This script is the guardrail that
    keeps them synchronized before builds and releases.

.PARAMETER Version
    Optional SemVer core version to write, for example 0.2.1.

.PARAMETER Suffix
    Optional SemVer pre-release suffix to write, for example alpha.1 or rc.0.
    Pass an empty string for a stable release.

.PARAMETER Check
    Validate version.json and build/version.props without changing files.

.EXAMPLE
    ./build/version.ps1 -Check

.EXAMPLE
    ./build/version.ps1 -Version 0.2.1 -Suffix alpha.1
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidatePattern('^[0-9A-Za-z.-]*$')]
    [AllowEmptyString()]
    [string]$Suffix = $null,

    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PropsPath = Join-Path $RepoRoot 'build/version.props'
$JsonPath = Join-Path $RepoRoot 'version.json'

function Get-FullVersion {
    param(
        [Parameter(Mandatory)][string]$Prefix,
        [AllowEmptyString()][string]$PreRelease
    )

    if ([string]::IsNullOrWhiteSpace($PreRelease)) {
        return $Prefix
    }

    return "$Prefix-$PreRelease"
}

function Get-Channel {
    param([AllowEmptyString()][string]$PreRelease)

    if ([string]::IsNullOrWhiteSpace($PreRelease)) {
        return 'stable'
    }

    return ($PreRelease -split '\.')[0]
}

function Read-VersionProps {
    if (-not (Test-Path $PropsPath)) {
        throw "Missing $PropsPath."
    }

    [xml]$xml = Get-Content -Path $PropsPath -Raw
    $group = $xml.Project.PropertyGroup
    [pscustomobject]@{
        VersionPrefix = [string]$group.OctadockVersionPrefix
        VersionSuffix = [string]$group.OctadockVersionSuffix
        AssemblyVersion = [string]$group.OctadockAssemblyVersion
        FileVersion = [string]$group.OctadockFileVersion
        Channel = [string]$group.OctadockReleaseChannel
    }
}

function Read-VersionJson {
    if (-not (Test-Path $JsonPath)) {
        throw "Missing $JsonPath."
    }

    Get-Content -Path $JsonPath -Raw | ConvertFrom-Json
}

function Write-VersionProps {
    param(
        [Parameter(Mandatory)][string]$Prefix,
        [AllowEmptyString()][string]$PreRelease
    )

    $assembly = "$Prefix.0"
    $channel = Get-Channel $PreRelease
    $content = @"
<Project>
  <PropertyGroup>
    <OctadockVersionPrefix>$Prefix</OctadockVersionPrefix>
    <OctadockVersionSuffix>$PreRelease</OctadockVersionSuffix>
    <OctadockAssemblyVersion>$assembly</OctadockAssemblyVersion>
    <OctadockFileVersion>$assembly</OctadockFileVersion>
    <OctadockReleaseChannel>$channel</OctadockReleaseChannel>
  </PropertyGroup>
</Project>
"@

    Set-Content -Path $PropsPath -Value $content -Encoding UTF8
}

function Write-VersionJson {
    param(
        [Parameter(Mandatory)][string]$Prefix,
        [AllowEmptyString()][string]$PreRelease
    )

    $metadata = [ordered]@{
        schema = 1
        versionPrefix = $Prefix
        versionSuffix = $PreRelease
        channel = Get-Channel $PreRelease
        releaseDate = $null
    }

    $metadata | ConvertTo-Json | Set-Content -Path $JsonPath -Encoding UTF8
}

if ($PSBoundParameters.ContainsKey('Version')) {
    $preRelease = if ($PSBoundParameters.ContainsKey('Suffix')) { $Suffix } else { '' }
    Write-VersionProps -Prefix $Version -PreRelease $preRelease
    Write-VersionJson -Prefix $Version -PreRelease $preRelease
}

$props = Read-VersionProps
$json = Read-VersionJson
$full = Get-FullVersion $props.VersionPrefix $props.VersionSuffix

if ([string]$json.versionPrefix -ne $props.VersionPrefix) {
    throw "version.json versionPrefix '$($json.versionPrefix)' does not match build/version.props '$($props.VersionPrefix)'."
}

if ([string]$json.versionSuffix -ne $props.VersionSuffix) {
    throw "version.json versionSuffix '$($json.versionSuffix)' does not match build/version.props '$($props.VersionSuffix)'."
}

if ([string]$json.channel -ne $props.Channel) {
    throw "version.json channel '$($json.channel)' does not match build/version.props '$($props.Channel)'."
}

if ($props.AssemblyVersion -ne "$($props.VersionPrefix).0") {
    throw "AssemblyVersion '$($props.AssemblyVersion)' should be '$($props.VersionPrefix).0'."
}

if ($props.FileVersion -ne "$($props.VersionPrefix).0") {
    throw "FileVersion '$($props.FileVersion)' should be '$($props.VersionPrefix).0'."
}

Write-Host "Octadock version: $full"
Write-Host "Channel: $($props.Channel)"

if ($Check) {
    Write-Host "Version metadata is synchronized."
}
