#Requires -Version 5.1
<#
.SYNOPSIS
    Performs deterministic static C-06 checks over Octadock WPF XAML.

.DESCRIPTION
    Parses every App XAML file, checks theme brush parity and resource references,
    detects raw shell colors outside theme dictionaries, and identifies interactive
    controls that have no statically discoverable accessible name or pointer-only
    interaction. A checked-in fingerprint baseline freezes existing debt; new
    findings fail. Static success does not replace keyboard, screen-reader, contrast,
    reduced-motion, or mixed-DPI testing.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,

    [string]$BaselinePath,

    [switch]$NoBaseline,

    [string]$WriteBaselinePath,

    [switch]$NoProcessExit,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Acceptance.Common.psm1') -Force
if (-not $RepoRoot) {
    $RepoRoot = Get-OctadockRepoRoot
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
if (-not $BaselinePath) {
    $BaselinePath = Join-Path $PSScriptRoot 'config/wpf-static-baseline.json'
}
if (-not $OutputPath) {
    $runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $RepoRoot "artifacts/acceptance/wpf/$runId/wpf-static.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

Add-Type -AssemblyName System.Xml.Linq

$xamlRoot = Join-Path $RepoRoot 'src/Octadock.App'
if (-not (Test-Path -LiteralPath $xamlRoot -PathType Container)) {
    throw "WPF source root does not exist: $xamlRoot"
}

$brushProperties = @(
    'Background',
    'Foreground',
    'BorderBrush',
    'Fill',
    'Stroke',
    'Color',
    'CaretBrush',
    'SelectionBrush',
    'SelectionTextBrush',
    'OpacityMask'
)
$interactiveTypes = @(
    'Button',
    'ToggleButton',
    'CheckBox',
    'RadioButton',
    'TextBox',
    'PasswordBox',
    'RichTextBox',
    'ComboBox',
    'Slider',
    'ListBox',
    'ListView',
    'TreeView',
    'MenuItem',
    'Hyperlink',
    'TabItem',
    'DatePicker',
    'Expander',
    'DataGrid',
    'Thumb',
    'RepeatButton',
    'ScrollBar'
)
$inputTypes = @('TextBox', 'PasswordBox', 'RichTextBox', 'ComboBox', 'Slider', 'ListBox', 'ListView', 'TreeView')
$pointerContainerTypes = @('Border', 'Grid', 'Image', 'TextBlock', 'Path', 'Canvas', 'StackPanel')
$pointerAttributes = @(
    'MouseDown',
    'MouseUp',
    'MouseLeftButtonDown',
    'MouseLeftButtonUp',
    'PreviewMouseDown',
    'PreviewMouseUp',
    'PreviewMouseLeftButtonDown',
    'PreviewMouseLeftButtonUp'
)
$keyboardAttributes = @('KeyDown', 'KeyUp', 'PreviewKeyDown', 'PreviewKeyUp')
$rawColorPattern = '^(#[0-9A-Fa-f]{3,8}|Black|White|Red|Blue|Green|Gray|Grey|Yellow|Orange|Purple|Pink|Magenta|Cyan|Brown|Lime|Navy|Teal|Silver|Gold)$'

function Get-PortableRelativePath {
    param([Parameter(Mandatory)][string]$Path)

    $rootWithSeparator = $RepoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Replace('\', '/')
    }
    return $fullPath.Substring($rootWithSeparator.Length).Replace('\', '/')
}

function Get-LineNumber {
    param([Parameter(Mandatory)]$Node)

    $lineInfo = [System.Xml.IXmlLineInfo]$Node
    if ($null -ne $lineInfo -and $lineInfo.HasLineInfo()) {
        return $lineInfo.LineNumber
    }
    return 0
}

function Get-ElementPath {
    param([Parameter(Mandatory)][System.Xml.Linq.XElement]$Element)

    $segments = New-Object System.Collections.Generic.List[string]
    $current = $Element
    while ($null -ne $current) {
        $name = $current.Name.LocalName
        $index = 1
        if ($null -ne $current.Parent) {
            foreach ($sibling in @($current.Parent.Elements())) {
                if ($sibling -eq $current) {
                    break
                }
                if ($sibling.Name.LocalName -eq $name) {
                    $index++
                }
            }
        }
        $segments.Insert(0, "$name[$index]")
        $current = $current.Parent
    }
    return '/' + ($segments -join '/')
}

function Get-AttributeValue {
    param(
        [Parameter(Mandatory)][System.Xml.Linq.XElement]$Element,
        [Parameter(Mandatory)][string]$Name
    )

    foreach ($attribute in @($Element.Attributes())) {
        if ($attribute.Name.LocalName -eq $Name) {
            return [string]$attribute.Value
        }
    }
    return $null
}

function Test-MeaningfulValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }
    $trimmed = $Value.Trim()
    if ($trimmed -in @('-', '...') -or $trimmed -eq ([char]0x2026).ToString()) {
        return $false
    }
    return $true
}

function Test-IsInsideTemplate {
    param([Parameter(Mandatory)][System.Xml.Linq.XElement]$Element)

    $current = $Element.Parent
    while ($null -ne $current) {
        if ($current.Name.LocalName -in @('ControlTemplate', 'ItemsPanelTemplate', 'Style')) {
            return $true
        }
        $current = $current.Parent
    }
    return $false
}

function Test-HasAccessibleName {
    param([Parameter(Mandatory)][System.Xml.Linq.XElement]$Element)

    foreach ($name in @('AutomationProperties.Name', 'AutomationProperties.LabeledBy')) {
        if (Test-MeaningfulValue (Get-AttributeValue -Element $Element -Name $name)) {
            return $true
        }
    }

    if ($Element.Name.LocalName -in $inputTypes) {
        return $false
    }

    foreach ($name in @('Content', 'Header', 'Text')) {
        if (Test-MeaningfulValue (Get-AttributeValue -Element $Element -Name $name)) {
            return $true
        }
    }

    foreach ($descendant in @($Element.Descendants())) {
        if ($descendant.Name.LocalName -in @('TextBlock', 'Run', 'AccessText')) {
            foreach ($name in @('Text', 'Content')) {
                if (Test-MeaningfulValue (Get-AttributeValue -Element $descendant -Name $name)) {
                    return $true
                }
            }
            if (Test-MeaningfulValue ([string]$descendant.Value)) {
                return $true
            }
        }
    }
    return $false
}

$findings = New-Object System.Collections.Generic.List[object]
function Add-Finding {
    param(
        [Parameter(Mandatory)][string]$Rule,
        [Parameter(Mandatory)][string]$Severity,
        [Parameter(Mandatory)][string]$File,
        [Parameter(Mandatory)][int]$Line,
        [Parameter(Mandatory)][string]$ElementPath,
        [Parameter(Mandatory)][string]$Message,
        [string]$FingerprintValue = ''
    )

    $normalizedFingerprintValue = $FingerprintValue.Trim().ToLowerInvariant()
    $fingerprint = "$Rule|$File|$ElementPath|$normalizedFingerprintValue"
    $findings.Add([pscustomobject][ordered]@{
            rule = $Rule
            severity = $Severity
            file = $File
            line = $Line
            elementPath = $ElementPath
            message = $Message
            fingerprint = $fingerprint
        })
}

$documents = @{}
$xamlFiles = @(Get-ChildItem -LiteralPath $xamlRoot -Recurse -File -Filter '*.xaml' | Sort-Object FullName)
foreach ($file in $xamlFiles) {
    $relative = Get-PortableRelativePath -Path $file.FullName
    try {
        $document = [System.Xml.Linq.XDocument]::Load(
            $file.FullName,
            [System.Xml.Linq.LoadOptions]::SetLineInfo
        )
        $documents[$relative] = $document
    }
    catch {
        Add-Finding -Rule 'XAML_PARSE' -Severity 'error' -File $relative -Line 0 -ElementPath '/' -Message $_.Exception.Message -FingerprintValue $_.Exception.GetType().FullName
    }
}

$definedBrushTokens = @{}
foreach ($entry in $documents.GetEnumerator()) {
    foreach ($element in @($entry.Value.Root) + @($entry.Value.Descendants())) {
        $key = Get-AttributeValue -Element $element -Name 'Key'
        if ($key -and $key.StartsWith('Octadock.Brush.', [StringComparison]::Ordinal)) {
            $definedBrushTokens[$key] = $true
        }
    }
}

foreach ($entry in $documents.GetEnumerator()) {
    $relative = [string]$entry.Key
    $document = $entry.Value
    $isThemeDictionary = $relative -like 'src/Octadock.App/Resources/Themes/*'

    foreach ($element in @($document.Root) + @($document.Descendants())) {
        $elementPath = Get-ElementPath -Element $element
        $line = Get-LineNumber -Node $element

        foreach ($attribute in @($element.Attributes())) {
            $attributeName = $attribute.Name.LocalName
            $value = [string]$attribute.Value
            if (-not $isThemeDictionary -and $attributeName -in $brushProperties -and $value -match $rawColorPattern) {
                Add-Finding -Rule 'RAW_SHELL_COLOR' -Severity 'error' -File $relative -Line (Get-LineNumber -Node $attribute) -ElementPath "$elementPath/@$attributeName" -Message "Use an Octadock.Brush.* token instead of '$value'." -FingerprintValue $value
            }

            foreach ($match in [regex]::Matches($value, 'Octadock\.Brush\.[A-Za-z0-9_.-]+')) {
                $token = $match.Value
                if (-not $definedBrushTokens.ContainsKey($token)) {
                    Add-Finding -Rule 'UNKNOWN_BRUSH_TOKEN' -Severity 'error' -File $relative -Line (Get-LineNumber -Node $attribute) -ElementPath "$elementPath/@$attributeName" -Message "Resource '$token' has no XAML definition." -FingerprintValue $token
                }
            }
        }

        if ($isThemeDictionary -or (Test-IsInsideTemplate -Element $element)) {
            continue
        }

        $type = $element.Name.LocalName
        if ($type -in $interactiveTypes -and -not (Test-HasAccessibleName -Element $element)) {
            Add-Finding -Rule 'ACCESSIBLE_NAME' -Severity 'warning' -File $relative -Line $line -ElementPath $elementPath -Message "$type has no statically discoverable accessible name or label." -FingerprintValue $type
        }

        if ($type -in $pointerContainerTypes) {
            $hasPointerHandler = $false
            foreach ($attributeName in $pointerAttributes) {
                if (Test-MeaningfulValue (Get-AttributeValue -Element $element -Name $attributeName)) {
                    $hasPointerHandler = $true
                    break
                }
            }
            if ($hasPointerHandler) {
                $focusable = (Get-AttributeValue -Element $element -Name 'Focusable') -eq 'True'
                $hasKeyboardHandler = $false
                foreach ($attributeName in $keyboardAttributes) {
                    if (Test-MeaningfulValue (Get-AttributeValue -Element $element -Name $attributeName)) {
                        $hasKeyboardHandler = $true
                        break
                    }
                }
                if (-not $focusable -or -not $hasKeyboardHandler -or -not (Test-HasAccessibleName -Element $element)) {
                    Add-Finding -Rule 'POINTER_ONLY_INTERACTION' -Severity 'warning' -File $relative -Line $line -ElementPath $elementPath -Message 'Pointer handler on a non-control requires keyboard focus, keyboard activation, and an accessible name.' -FingerprintValue $type
                }
            }
        }

        if ($element -eq $document.Root -and ($type -eq 'Window' -or $type -eq 'ToolWindowBase' -or $type.EndsWith('Window', [StringComparison]::Ordinal))) {
            $title = Get-AttributeValue -Element $element -Name 'Title'
            $automationName = Get-AttributeValue -Element $element -Name 'AutomationProperties.Name'
            if (-not (Test-MeaningfulValue $title) -and -not (Test-MeaningfulValue $automationName)) {
                Add-Finding -Rule 'WINDOW_NAME' -Severity 'warning' -File $relative -Line $line -ElementPath $elementPath -Message 'Window has no Title or AutomationProperties.Name.' -FingerprintValue $type
            }
        }
    }
}

$csharpFiles = @(Get-ChildItem -LiteralPath $xamlRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\/](bin|obj)[\/]' } |
        Sort-Object FullName)
foreach ($file in $csharpFiles) {
    $relative = Get-PortableRelativePath -Path $file.FullName
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $line = [string]$lines[$lineIndex]
        $lineNumber = $lineIndex + 1
        $elementPath = "/code/line[$lineNumber]"

        if ($relative -eq 'src/Octadock.App/Theming/OctadockDesignTokens.cs') {
            $fixedTokenMatch = [regex]::Match($line, 'Frozen\(\s*(?:0x[0-9A-Fa-f]+|\d+)')
            if ($fixedTokenMatch.Success) {
                Add-Finding -Rule 'PROGRAMMATIC_FIXED_THEME_TOKEN' -Severity 'error' -File $relative -Line $lineNumber -ElementPath $elementPath -Message 'Programmatic theme token is a fixed palette value and cannot follow light/high-contrast resources.' -FingerprintValue $fixedTokenMatch.Value
            }
            continue
        }

        $numericColorMatch = [regex]::Match($line, 'Color\.From(?:Argb|Rgb)\(\s*(?:0x[0-9A-Fa-f]+|\d+)')
        if ($numericColorMatch.Success) {
            Add-Finding -Rule 'PROGRAMMATIC_RAW_COLOR' -Severity 'error' -File $relative -Line $lineNumber -ElementPath $elementPath -Message 'Programmatic WPF shell color must resolve through the theme token system.' -FingerprintValue $numericColorMatch.Value
        }

        $embeddedHexMatch = [regex]::Match($line, '#[0-9A-Fa-f]{6,8}')
        if ($embeddedHexMatch.Success) {
            Add-Finding -Rule 'PROGRAMMATIC_RAW_COLOR' -Severity 'error' -File $relative -Line $lineNumber -ElementPath $elementPath -Message 'Embedded XAML/code color must resolve through the theme token system.' -FingerprintValue $embeddedHexMatch.Value
        }

        $namedBrushMatch = [regex]::Match($line, '(?<!OctadockDesignTokens\.)Brushes\.(?!Transparent\b)[A-Za-z]+')
        if ($namedBrushMatch.Success) {
            Add-Finding -Rule 'PROGRAMMATIC_RAW_BRUSH' -Severity 'error' -File $relative -Line $lineNumber -ElementPath $elementPath -Message 'Named WPF brush must resolve through an Octadock theme token.' -FingerprintValue $namedBrushMatch.Value
        }
    }
}

$themeFiles = @(
    'src/Octadock.App/Resources/Themes/Dark.xaml',
    'src/Octadock.App/Resources/Themes/Light.xaml',
    'src/Octadock.App/Resources/Themes/HighContrast.xaml'
)
$themeTokens = @{}
$themeUnion = @{}
foreach ($themeFile in $themeFiles) {
    $tokens = @{}
    if ($documents.ContainsKey($themeFile)) {
        foreach ($element in @($documents[$themeFile].Root) + @($documents[$themeFile].Descendants())) {
            $key = Get-AttributeValue -Element $element -Name 'Key'
            if ($key -and $key.StartsWith('Octadock.Brush.', [StringComparison]::Ordinal)) {
                $tokens[$key] = $true
                $themeUnion[$key] = $true
            }
        }
    }
    $themeTokens[$themeFile] = $tokens
}
foreach ($themeFile in $themeFiles) {
    foreach ($token in @($themeUnion.Keys | Sort-Object)) {
        if (-not $themeTokens[$themeFile].ContainsKey($token)) {
            Add-Finding -Rule 'THEME_TOKEN_PARITY' -Severity 'error' -File $themeFile -Line 0 -ElementPath "/ResourceDictionary/$token" -Message "Theme does not define '$token'." -FingerprintValue $token
        }
    }
}

$baselineEntries = @()
if (-not $NoBaseline -and (Test-Path -LiteralPath $BaselinePath -PathType Leaf)) {
    $baselineDocument = Read-AcceptanceJson -Path $BaselinePath
    $baselineEntries = @($baselineDocument.entries)
}
$baselineByFingerprint = @{}
foreach ($entry in $baselineEntries) {
    $baselineByFingerprint[[string]$entry.fingerprint] = $entry
}

$findingFingerprints = @{}
$newFindings = New-Object System.Collections.Generic.List[object]
$knownFindings = New-Object System.Collections.Generic.List[object]
foreach ($finding in $findings) {
    $findingFingerprints[$finding.fingerprint] = $true
    if ($baselineByFingerprint.ContainsKey($finding.fingerprint)) {
        $knownFindings.Add($finding)
    }
    else {
        $newFindings.Add($finding)
    }
}
$resolvedBaseline = @($baselineEntries | Where-Object { -not $findingFingerprints.ContainsKey([string]$_.fingerprint) })

if ($WriteBaselinePath) {
    $unbaselineable = @($findings | Where-Object { $_.rule -in @('XAML_PARSE', 'UNKNOWN_BRUSH_TOKEN') })
    if ($unbaselineable.Count -gt 0) {
        throw 'Refusing to write a baseline containing XAML parse or unknown-token failures.'
    }
    $entries = @($findings | Sort-Object fingerprint | ForEach-Object {
            [pscustomobject][ordered]@{
                fingerprint = $_.fingerprint
                reason = 'Existing C-06 static debt; retain until the corresponding WPF surface is remediated and manually verified.'
            }
        })
    $baselineOutput = [ordered]@{
        schemaVersion = 1
        generatedBy = 'Test-WpfStaticAcceptance.ps1'
        entries = $entries
    }
    Write-AcceptanceJson -Value $baselineOutput -Path ([System.IO.Path]::GetFullPath($WriteBaselinePath))
}

$overallStatus = 'clean'
if ($newFindings.Count -gt 0) {
    $overallStatus = 'fail_new_findings'
}
elseif ($knownFindings.Count -gt 0) {
    $overallStatus = 'regression_guard_pass_known_debt'
}

$evidence = New-AcceptanceEnvelope -Gate 'C-06-static' -Tool 'Test-WpfStaticAcceptance.ps1' -RepoRoot $RepoRoot
$evidence['overallStatus'] = $overallStatus
$evidence['xamlFileCount'] = $xamlFiles.Count
$evidence['csharpFileCount'] = $csharpFiles.Count
$evidence['definedBrushTokenCount'] = $definedBrushTokens.Count
$evidence['baselineEnabled'] = -not [bool]$NoBaseline
$evidence['baselinePath'] = $(if ($NoBaseline) { $null } else { [System.IO.Path]::GetFullPath($BaselinePath) })
$evidence['summary'] = [ordered]@{
    totalFindings = $findings.Count
    newFindings = $newFindings.Count
    knownFindings = $knownFindings.Count
    resolvedBaselineEntries = $resolvedBaseline.Count
}
$evidence['newFindings'] = $newFindings.ToArray()
$evidence['knownFindings'] = $knownFindings.ToArray()
$evidence['resolvedBaselineEntries'] = @($resolvedBaseline)
$evidence['limitations'] = @(
    'Static XAML cannot prove runtime UI Automation names, contrast after composition, keyboard order, focus restoration, or screen-reader announcements.',
    'Programmatic raw-color patterns are scanned, but static analysis cannot prove UI Automation names for controls created only in C#.',
    'Transparent is treated as semantic absence of paint and is not flagged as a raw color.'
)

Write-AcceptanceJson -Value $evidence -Path $OutputPath
Write-Host "WPF static evidence: $OutputPath" -ForegroundColor Green
Write-Host "C-06 static status: $overallStatus ($($newFindings.Count) new, $($knownFindings.Count) baselined)"
if ($newFindings.Count -gt 0) {
    foreach ($finding in $newFindings) {
        Write-Host "  $($finding.file):$($finding.line) [$($finding.rule)] $($finding.message)" -ForegroundColor Red
    }
    if ($NoProcessExit) {
        return
    }
    exit 1
}
if ($NoProcessExit) {
    return
}
exit 0
