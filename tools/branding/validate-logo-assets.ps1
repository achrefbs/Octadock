[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore

$script:Checks = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-Brand([bool]$Condition, [string]$Message) {
    $script:Checks++
    if (-not $Condition) {
        $script:Failures.Add($Message)
    }
}

function Repo([string]$RelativePath) {
    Join-Path $RepoRoot ($RelativePath -replace '/', '\')
}

function Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Normalized-Text([string]$Path) {
    ([System.IO.File]::ReadAllText($Path) -replace "`r`n?", "`n").Trim()
}

function Bitmap-Audit([string]$Path) {
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite
    )
    try {
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
        )
        $frames = @($decoder.Frames)
        $frame = $frames[0]
        $converted = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new()
        $converted.BeginInit()
        $converted.Source = $frame
        $converted.DestinationFormat = [System.Windows.Media.PixelFormats]::Bgra32
        $converted.EndInit()
        $converted.Freeze()

        $stride = $converted.PixelWidth * 4
        $pixels = [byte[]]::new($stride * $converted.PixelHeight)
        $converted.CopyPixels($pixels, $stride, 0)
        $alphaMin = 255
        $alphaMax = 0
        for ($index = 3; $index -lt $pixels.Length; $index += 4) {
            $alpha = [int]$pixels[$index]
            if ($alpha -lt $alphaMin) { $alphaMin = $alpha }
            if ($alpha -gt $alphaMax) { $alphaMax = $alpha }
        }
        $x = $converted.PixelWidth - 1
        $y = $converted.PixelHeight - 1
        [pscustomobject]@{
            FrameCount = $frames.Count
            Sizes = @($frames | ForEach-Object { "$($_.PixelWidth)x$($_.PixelHeight)" })
            Width = $converted.PixelWidth
            Height = $converted.PixelHeight
            AlphaMin = $alphaMin
            AlphaMax = $alphaMax
            Corners = @(
                [int]$pixels[3],
                [int]$pixels[$x * 4 + 3],
                [int]$pixels[$y * $stride + 3],
                [int]$pixels[$y * $stride + $x * 4 + 3]
            )
        }
    }
    finally {
        $stream.Dispose()
    }
}

$masterPath = Repo 'docs/brand/assets/logo/octadock-symbol-master.svg'
Assert-Brand (Test-Path -LiteralPath $masterPath) 'Missing canonical V2 SVG master.'
$master = [System.IO.File]::ReadAllText($masterPath)
Assert-Brand ($master -match 'viewBox="0 0 512 512"') 'Master SVG viewBox is wrong.'
Assert-Brand ($master -match 'id="mantle"') 'Master SVG is missing its mantle.'
Assert-Brand ($master -match 'id="dock-cutout"') 'Master SVG is missing its Dock cutout.'
$ports = [regex]::Matches($master, 'id="(port-\d{2})"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$expectedPorts = 1..8 | ForEach-Object { 'port-{0:d2}' -f $_ }
Assert-Brand (($ports -join ',') -eq ($expectedPorts -join ',')) 'Master SVG must contain ports 01 through 08 exactly once.'
$pairs = [regex]::Matches($master, 'id="(pair-[^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
Assert-Brand ($pairs.Count -eq 4) 'Master SVG must contain four mirrored port pairs.'
Assert-Brand ($master -notmatch '<(?:image|text|filter)\b') 'Master SVG contains a forbidden image, text, or filter.'
Assert-Brand ($master -notmatch '(?:href|src)="https?://') 'Master SVG contains a remote dependency.'

foreach ($size in 16, 20) {
    $path = Repo "docs/brand/assets/icons/octadock-symbol-optical-$size.svg"
    Assert-Brand (Test-Path -LiteralPath $path) "Missing $size px optical master."
    $svg = [System.IO.File]::ReadAllText($path)
    $viewBox = 'viewBox="0 0 {0} {0}"' -f $size
    Assert-Brand ($svg.Contains($viewBox)) "$size px optical master has the wrong viewBox."
    $ids = [regex]::Matches($svg, 'id="(port-\d{2})"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    Assert-Brand (($ids -join ',') -eq ($expectedPorts -join ',')) "$size px optical master must contain eight ports."
}

$docsIco = Repo 'docs/brand/assets/icons/octadock-master.ico'
$appIco = Repo 'src/Octadock.App/Resources/Icons/octadock.ico'
$webIco = Repo 'web/assets/brand/favicon.ico'
Assert-Brand ((Hash $docsIco) -eq (Hash $appIco)) 'Desktop ICO drifted from the approved export.'
Assert-Brand ((Hash $docsIco) -eq (Hash $webIco)) 'Web favicon ICO drifted from the approved export.'
$ico = Bitmap-Audit $appIco
$icoSizes = @($ico.Sizes | ForEach-Object { [int](($_ -split 'x')[0]) } | Sort-Object)
Assert-Brand (($icoSizes -join ',') -eq '16,24,32,48,64,128,256') 'Desktop ICO frame sizes are incomplete.'
Assert-Brand ($ico.FrameCount -eq 7) 'Desktop ICO must contain seven frames.'

$docs256 = Repo 'docs/brand/assets/icons/octadock-256.png'
$app256 = Repo 'src/Octadock.App/Resources/Icons/octadock-256.png'
Assert-Brand ((Hash $docs256) -eq (Hash $app256)) 'Desktop 256 px PNG drifted from the approved export.'
$docs1024 = Repo 'docs/brand/assets/logo/octadock-symbol-master-1024.png'
$app1024 = Repo 'src/Octadock.App/Resources/Icons/octadock-logo-transparent.png'
Assert-Brand ((Hash $docs1024) -eq (Hash $app1024)) 'Desktop transparent PNG drifted from the approved master.'
$png = Bitmap-Audit $docs1024
Assert-Brand ($png.Width -eq 1024 -and $png.Height -eq 1024) 'Master PNG is not 1024 x 1024.'
Assert-Brand ($png.AlphaMin -eq 0 -and $png.AlphaMax -eq 255) 'Master PNG has no real alpha range.'
Assert-Brand (($png.Corners -join ',') -eq '0,0,0,0') 'Master PNG corners are not transparent.'

$dockPill = [System.IO.File]::ReadAllText((Repo 'src/Octadock.App/CaptureUx/DockPill.cs'))
Assert-Brand ($dockPill -match 'CreateLogoGeometry') 'Dock Pill is missing native V2 geometry.'
Assert-Brand ($dockPill -notmatch '_logo\s*=\s*new PackIconLucide') 'Dock Pill still uses the placeholder icon control.'
$cli = [System.IO.File]::ReadAllText((Repo 'src/Octadock.Cli/Octadock.Cli.csproj'))
Assert-Brand ($cli.Contains('<ApplicationIcon>..\Octadock.App\Resources\Icons\octadock.ico</ApplicationIcon>')) 'CLI does not share the V2 ICO.'

$webBrand = Repo 'web/assets/brand'
Assert-Brand ((Hash (Repo 'docs/brand/assets/logo/octadock-symbol-signal-teal.svg')) -eq (Hash (Join-Path $webBrand 'octadock-symbol-signal-teal.svg'))) 'Web teal SVG drifted.'
Assert-Brand ((Hash (Repo 'docs/brand/assets/lockups/octadock-lockup-horizontal-frost.svg')) -eq (Hash (Join-Path $webBrand 'octadock-lockup-horizontal-frost.svg'))) 'Web lockup drifted.'
$paseoIcon = Repo 'icon.svg'
Assert-Brand (Test-Path -LiteralPath $paseoIcon) 'Missing root icon.svg used by Paseo.'
if (Test-Path -LiteralPath $paseoIcon) {
    Assert-Brand ((Normalized-Text (Repo 'docs/brand/assets/logo/octadock-symbol-signal-teal.svg')) -ceq (Normalized-Text $paseoIcon)) 'Paseo project icon drifted from the approved Signal Teal SVG.'
}

$htmlFiles = @(Get-ChildItem -LiteralPath (Repo 'web') -Filter '*.html' -File)
Assert-Brand ($htmlFiles.Count -eq 6) 'Expected six shipping HTML pages.'
$lockups = 0
$appleHrefs = [System.Collections.Generic.List[string]]::new()
foreach ($file in $htmlFiles) {
    $html = [System.IO.File]::ReadAllText($file.FullName)
    Assert-Brand ($html -match 'assets/brand/favicon\.ico') "$($file.Name) lacks the shared ICO."
    Assert-Brand ($html -match 'assets/brand/octadock-symbol-signal-teal\.svg') "$($file.Name) lacks the SVG favicon."
    Assert-Brand ($html -match 'rel="manifest"') "$($file.Name) lacks the manifest."
    Assert-Brand ($html -notmatch 'data:image/svg\+xml') "$($file.Name) still embeds a placeholder favicon."
    Assert-Brand ($html -notmatch 'brand-mark-dot|brand-dot') "$($file.Name) still uses a pulsing-dot placeholder."
    $apple = [regex]::Match($html, 'rel="apple-touch-icon"[^>]*href="([^"]+)"')
    Assert-Brand ($apple.Success) "$($file.Name) lacks an Apple touch icon."
    if ($apple.Success) { $appleHrefs.Add($apple.Groups[1].Value) }
    $lockups += [regex]::Matches($html, 'class="brand__lockup"').Count
}
Assert-Brand ($lockups -eq 12) "Expected 12 V2 header/footer lockups; found $lockups."
$uniqueApple = @($appleHrefs | Sort-Object -Unique)
Assert-Brand ($uniqueApple.Count -eq 1) 'All pages must share one Apple touch icon.'
if ($uniqueApple.Count -eq 1) {
    $apple = Bitmap-Audit (Repo ("web/" + $uniqueApple[0]))
    Assert-Brand ($apple.Width -eq 180 -and $apple.Height -eq 180) 'Apple touch icon must be 180 x 180.'
}

$manifest = Get-Content -LiteralPath (Repo 'web/site.webmanifest') -Raw | ConvertFrom-Json
Assert-Brand ($manifest.background_color -eq '#070B14') 'Manifest background must be Canvas.'
Assert-Brand ($manifest.theme_color -eq '#070B14') 'Manifest theme must be Canvas.'
Assert-Brand (@($manifest.icons).Count -ge 2) 'Manifest needs 192 px and 512 px icons.'
foreach ($icon in @($manifest.icons)) {
    $path = Repo ("web/" + $icon.src)
    Assert-Brand (Test-Path -LiteralPath $path) "Missing manifest icon $($icon.src)."
    if (Test-Path -LiteralPath $path) {
        $audit = Bitmap-Audit $path
        Assert-Brand ("$($audit.Width)x$($audit.Height)" -eq [string]$icon.sizes) "Manifest dimensions mismatch for $($icon.src)."
    }
}

foreach ($relative in @(
    'docs/brand/brand-spec.md',
    'docs/brand/brand-system.json',
    'docs/brand/provenance.md',
    'docs/brand/tokens.css',
    'docs/brand/motion/demo.html',
    'docs/brand/motion/motion-engine.js',
    'docs/brand/motion/exports/dock-resolve.mp4',
    'docs/brand/motion/exports/port-handshake.mp4',
    'docs/brand/motion/exports/signal-to-dock.mp4',
    'docs/brand/motion/exports/quiet-current.mp4'
)) {
    Assert-Brand (Test-Path -LiteralPath (Repo $relative)) "Missing brand artifact $relative."
}

if ($script:Failures.Count -gt 0) {
    Write-Host "Octadock V2 validation failed: $($script:Failures.Count) of $script:Checks checks." -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Octadock V2 validation passed: $script:Checks checks." -ForegroundColor Green
