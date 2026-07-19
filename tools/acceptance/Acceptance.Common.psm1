Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-OctadockRepoRoot {
    $toolsDirectory = Split-Path -Parent $PSScriptRoot
    return (Split-Path -Parent $toolsDirectory)
}

function New-AcceptanceDirectory {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-AcceptanceJson {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Path,
        [int]$Depth = 12
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        New-AcceptanceDirectory -Path $parent
    }

    $json = $Value | ConvertTo-Json -Depth $Depth
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, $utf8WithoutBom)
}

function Read-AcceptanceJson {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required JSON file does not exist: $Path"
    }

    return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json)
}

function Get-AcceptanceGitCommit {
    param([Parameter(Mandatory)][string]$RepoRoot)

    try {
        $commit = (& git -C $RepoRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and $commit) {
            return ([string]$commit).Trim()
        }
    }
    catch {
        # Evidence remains usable outside a Git checkout.
    }

    return $null
}

function Get-AcceptanceSourceState {
    param([Parameter(Mandatory)][string]$RepoRoot)

    try {
        # Disable Git's safe-CRLF warning for these read-only identity commands;
        # PowerShell 5 promotes native stderr warnings to terminating errors when
        # the caller uses ErrorActionPreference=Stop.
        $statusLines = @(& git -c core.safecrlf=false -C $RepoRoot status --porcelain=v1 --untracked-files=all 2>$null)
        if ($LASTEXITCODE -ne 0) {
            throw 'git status failed.'
        }

        $identityParts = New-Object System.Collections.Generic.List[string]
        foreach ($line in @($statusLines | Sort-Object)) {
            $identityParts.Add([string]$line)
        }

        # Porcelain status quotes paths containing spaces/non-ASCII characters.
        # Enumerate untracked files separately with NUL delimiters so the exact
        # path can always be resolved and its content included in the identity.
        $untrackedRaw = [string](& git -c core.safecrlf=false -C $RepoRoot ls-files --others --exclude-standard -z 2>$null)
        if ($LASTEXITCODE -ne 0) {
            throw 'git ls-files failed.'
        }
        $untrackedPaths = @($untrackedRaw.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries) | Sort-Object)
        foreach ($relativePath in $untrackedPaths) {
            $untrackedPath = Join-Path $RepoRoot $relativePath
            if (Test-Path -LiteralPath $untrackedPath -PathType Leaf) {
                $hash = (Get-FileHash -LiteralPath $untrackedPath -Algorithm SHA256).Hash.ToLowerInvariant()
                $identityParts.Add(('untracked:{0}:{1}' -f $relativePath, $hash))
            }
        }

        $trackedDiff = (& git -c core.safecrlf=false -C $RepoRoot diff --binary HEAD -- 2>$null) -join [Environment]::NewLine
        $identityParts.Add($trackedDiff)
        $identityText = $identityParts -join [Environment]::NewLine
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($identityText)
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $identityHash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $sha.Dispose()
        }

        return [ordered]@{
            dirty = $statusLines.Count -gt 0
            status = @($statusLines)
            diffIdentitySha256 = $identityHash
        }
    }
    catch {
        return [ordered]@{
            dirty = $null
            status = @()
            diffIdentitySha256 = $null
            error = $_.Exception.Message
        }
    }
}

function Get-AcceptancePropertyValue {
    param(
        $Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function New-AcceptanceEnvelope {
    param(
        [Parameter(Mandatory)][string]$Gate,
        [Parameter(Mandatory)][string]$Tool,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    return [ordered]@{
        schemaVersion = 1
        gate = $Gate
        tool = $Tool
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        gitCommit = Get-AcceptanceGitCommit -RepoRoot $RepoRoot
        sourceState = Get-AcceptanceSourceState -RepoRoot $RepoRoot
        environment = [ordered]@{
            os = [Environment]::OSVersion.VersionString
            is64BitProcess = [Environment]::Is64BitProcess
            logicalProcessorCount = [Environment]::ProcessorCount
            powershellVersion = $PSVersionTable.PSVersion.ToString()
        }
    }
}

function Resolve-ManualAcceptanceChecks {
    param(
        [Parameter(Mandatory)][object[]]$Checks,
        $EvidenceDocument,
        [string]$ExpectedGate
    )

    $evidenceById = @{}
    $documentMetadataValid = $false
    $metadataReason = $null
    if ($null -ne $EvidenceDocument) {
        $schemaVersion = Get-AcceptancePropertyValue -Object $EvidenceDocument -Name 'schemaVersion'
        $gate = [string](Get-AcceptancePropertyValue -Object $EvidenceDocument -Name 'gate')
        $recordedUtcText = [string](Get-AcceptancePropertyValue -Object $EvidenceDocument -Name 'recordedUtc')
        $machineProfile = [string](Get-AcceptancePropertyValue -Object $EvidenceDocument -Name 'machineProfile')
        $recordedUtc = [DateTimeOffset]::MinValue
        $recordedUtcValid = [DateTimeOffset]::TryParse(
            $recordedUtcText,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$recordedUtc
        )
        $gateValid = [string]::IsNullOrWhiteSpace($ExpectedGate) -or $gate -eq $ExpectedGate
        $documentMetadataValid = [int]$schemaVersion -eq 1 -and
            $gateValid -and
            $recordedUtcValid -and
            -not [string]::IsNullOrWhiteSpace($machineProfile)

        if (-not $documentMetadataValid) {
            $metadataReason = "Manual evidence requires schemaVersion 1, gate '$ExpectedGate', a parseable recordedUtc, and machineProfile."
        }

        foreach ($entry in @(Get-AcceptancePropertyValue -Object $EvidenceDocument -Name 'checks')) {
            $id = [string](Get-AcceptancePropertyValue -Object $entry -Name 'id')
            if (-not [string]::IsNullOrWhiteSpace($id)) {
                $evidenceById[$id] = $entry
            }
        }
    }

    $resolved = New-Object System.Collections.Generic.List[object]
    foreach ($check in $Checks) {
        $id = [string]$check.id
        if (-not $evidenceById.ContainsKey($id)) {
            $resolved.Add([pscustomobject][ordered]@{
                    id = $id
                    status = 'pending_manual'
                    reason = 'No operator-supplied evidence was provided.'
                    evidence = @()
                })
            continue
        }

        $entry = $evidenceById[$id]
        $status = ([string](Get-AcceptancePropertyValue -Object $entry -Name 'status')).ToLowerInvariant()
        $evidence = @((Get-AcceptancePropertyValue -Object $entry -Name 'evidence') |
                Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        $notes = [string](Get-AcceptancePropertyValue -Object $entry -Name 'notes')

        if (-not $documentMetadataValid) {
            $resolved.Add([pscustomobject][ordered]@{
                    id = $id
                    status = 'invalid_evidence'
                    reason = $metadataReason
                    evidence = $evidence
                })
            continue
        }

        if ($status -notin @('pass', 'fail', 'blocked', 'pending')) {
            $resolved.Add([pscustomobject][ordered]@{
                    id = $id
                    status = 'invalid_evidence'
                    reason = "Unsupported manual status '$status'."
                    evidence = $evidence
                })
            continue
        }

        if ($status -eq 'pass' -and $evidence.Count -eq 0) {
            $resolved.Add([pscustomobject][ordered]@{
                    id = $id
                    status = 'invalid_evidence'
                    reason = 'A pass requires at least one evidence reference.'
                    evidence = $evidence
                })
            continue
        }

        $normalizedStatus = $status
        if ($status -eq 'pending') {
            $normalizedStatus = 'pending_manual'
        }
        elseif ($status -eq 'blocked') {
            $normalizedStatus = 'blocked_manual'
        }

        $resolved.Add([pscustomobject][ordered]@{
                id = $id
                status = $normalizedStatus
                reason = $notes
                evidence = $evidence
            })
    }

    return $resolved.ToArray()
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)][double[]]$Values,
        [Parameter(Mandatory)][ValidateRange(0, 100)][double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return $null
    }

    $sorted = @($Values | Sort-Object)
    $rank = ($Percentile / 100.0) * ($sorted.Count - 1)
    $lower = [Math]::Floor($rank)
    $upper = [Math]::Ceiling($rank)
    if ($lower -eq $upper) {
        return [double]$sorted[$lower]
    }

    $weight = $rank - $lower
    return ([double]$sorted[$lower] * (1.0 - $weight)) + ([double]$sorted[$upper] * $weight)
}

Export-ModuleMember -Function @(
    'Get-OctadockRepoRoot',
    'New-AcceptanceDirectory',
    'Write-AcceptanceJson',
    'Read-AcceptanceJson',
    'Get-AcceptanceGitCommit',
    'Get-AcceptanceSourceState',
    'Get-AcceptancePropertyValue',
    'New-AcceptanceEnvelope',
    'Resolve-ManualAcceptanceChecks',
    'Get-Percentile'
)
