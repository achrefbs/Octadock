[CmdletBinding()]
param(
    [string]$ArtifactPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'Octadock.sln'
$forbiddenTerms = @(
    'Octadock.WorkflowIntelligence.Internal',
    'FullDeveloperTraceCollector',
    'raw_documents',
    'DELETE_INTERNAL_TRACES'
)

function Fail([string]$message) {
    throw "Public artifact boundary failed: $message"
}

function Find-ForbiddenTermInFile([string]$path, [string[]]$terms) {
    # The sentinels are ASCII. Scan every artifact in bounded chunks so a large
    # single-file executable cannot bypass the gate and no entire binary is
    # loaded into CI memory.
    $maxTermLength = ($terms | Measure-Object -Property Length -Maximum).Maximum
    $buffer = [byte[]]::new(1MB)
    $carry = ''
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $text = $carry + [Text.Encoding]::ASCII.GetString($buffer, 0, $read)
            foreach ($term in $terms) {
                if ($text.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
                    return $term
                }
            }
            $carryLength = [Math]::Min($maxTermLength - 1, $text.Length)
            $carry = $text.Substring($text.Length - $carryLength, $carryLength)
        }
    }
    finally {
        $stream.Dispose()
    }
    return $null
}

$solution = Get-Content -Raw -LiteralPath $solutionPath
foreach ($term in $forbiddenTerms) {
    if ($solution.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
        Fail "Octadock.sln contains forbidden internal term '$term'."
    }
}

$publicProjects = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter *.csproj
foreach ($project in $publicProjects) {
    $content = Get-Content -Raw -LiteralPath $project.FullName
    foreach ($term in $forbiddenTerms) {
        if ($content.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
            Fail "Public project '$($project.FullName)' references '$term'."
        }
    }
    if ($content -match '(?i)tools[\\/]internal') {
        Fail "Public project '$($project.FullName)' references tools/internal."
    }
}

if ($ArtifactPath) {
    $resolvedArtifactPath = (Resolve-Path -LiteralPath $ArtifactPath).Path
    $files = Get-ChildItem -LiteralPath $resolvedArtifactPath -Recurse -File
    foreach ($file in $files) {
        foreach ($term in $forbiddenTerms) {
            if ($file.Name.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
                Fail "Published artifact name '$($file.FullName)' contains '$term'."
            }
        }

        $found = Find-ForbiddenTermInFile $file.FullName $forbiddenTerms
        if ($found) {
            Fail "Published artifact '$($file.FullName)' contains '$found'."
        }
    }
}

Write-Host 'Public artifact boundary passed: internal Workflow Intelligence collector is excluded.'
