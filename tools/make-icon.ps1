[CmdletBinding()]
param(
    [switch]$Preview
)

# 生成 assets\EveryGo.ico（B 方案：深色圆角底 + 白色 G + 蓝色 O）
# 用法：powershell -ExecutionPolicy Bypass -File .\tools\make-icon.ps1 [-Preview]

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outDir = Join-Path $root 'assets'
$fontName = 'Arial Black'

$families = [System.Drawing.FontFamily]::Families | ForEach-Object { $_.Name }
if ($families -notcontains $fontName) { throw "缺少字体：$fontName" }

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -lt 0.5) { $r = 0.5 }
    if ($r -gt ($w / 2)) { $r = $w / 2 }
    if ($r -gt ($h / 2)) { $r = $h / 2 }
    $d = $r * 2
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Get-InkBox([System.Drawing.Bitmap]$bmp) {
    $data = $bmp.LockBits(
        (New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $w = $bmp.Width; $h = $bmp.Height; $stride = $data.Stride
    $len = [math]::Abs($stride) * $h
    $raw = New-Object byte[] $len
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $len)
    $bmp.UnlockBits($data)
    $minX = $w; $minY = $h; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $h; $y++) {
        $row = $y * $stride
        if ($stride -lt 0) { $row = ($h - 1 - $y) * (-$stride) }
        for ($x = 0; $x -lt $w; $x++) {
            if ($raw[$row + $x * 4 + 3] -ge 24) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { return (New-Object System.Drawing.Rectangle 0, 0, 1, 1) }
    return (New-Object System.Drawing.Rectangle $minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1))
}

function Render-GoTile([int]$size, [double]$inkRatio, [double]$tracking, [int]$superSample, [bool]$gridFit) {
    $S = $size * $superSample
    $hint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    if ($gridFit) { $hint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit }
    $canvas = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = $hint
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 底板：纵向渐变
    $tile = New-RoundedPath 0 0 ($S - 1) ($S - 1) (0.22 * $S)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point 0, 0),
        (New-Object System.Drawing.Point 0, $S),
        [System.Drawing.Color]::FromArgb(255, 30, 34, 44),
        [System.Drawing.Color]::FromArgb(255, 9, 11, 15))
    $g.FillPath($brush, $tile)
    $brush.Dispose()

    # 上方蓝色微光
    $glowRect = New-Object System.Drawing.RectangleF (0.5 * $S - 0.55 * $S), (0.32 * $S - 0.55 * $S), (1.10 * $S), (1.10 * $S)
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse($glowRect)
    $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush $glowPath
    $glow.CenterPoint = New-Object System.Drawing.PointF (0.5 * $S), (0.32 * $S)
    $glow.CenterColor = [System.Drawing.Color]::FromArgb(32, 72, 110, 170)
    $glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 72, 110, 170))
    $g.FillPath($glow, $glowPath)
    $glow.Dispose(); $glowPath.Dispose()

    # GO：先量墨迹框，再按目标高度重绘，保证每档尺寸视觉大小一致
    $em = $inkRatio * $S / 0.74
    $font = New-Object System.Drawing.Font($fontName, [single]$em, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel))
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $fmt.FormatFlags = $fmt.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces
    $tmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $tg = [System.Drawing.Graphics]::FromImage($tmp)
    $tg.TextRenderingHint = $hint
    $tg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $tg.Clear([System.Drawing.Color]::Transparent)

    $wG = $tg.MeasureString('G', $font, (New-Object System.Drawing.PointF 0, 0), $fmt)
    $xO = [single]($wG.Width + $tracking * $em)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 248, 255))
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 143, 190, 255))
    $tg.DrawString('G', $font, $white, (New-Object System.Drawing.PointF 0, 0), $fmt)
    $tg.DrawString('O', $font, $blue, (New-Object System.Drawing.PointF $xO, 0), $fmt)
    $white.Dispose(); $blue.Dispose()
    $tg.Dispose()

    $ink = Get-InkBox $tmp
    $k = ($inkRatio * $S) / $ink.Height
    $font1 = New-Object System.Drawing.Font($fontName, [single]($em * $k), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel))
    $px = ($S - $ink.Width * $k) / 2 - $ink.X * $k
    $py = ($S - $ink.Height * $k) / 2 - $ink.Y * $k + 0.006 * $S
    $white1 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 248, 255))
    $blue1 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 143, 190, 255))
    $g.DrawString('G', $font1, $white1, (New-Object System.Drawing.PointF ([single]$px), ([single]$py)), $fmt)
    $g.DrawString('O', $font1, $blue1, (New-Object System.Drawing.PointF ([single]($px + $xO * $k)), ([single]$py)), $fmt)
    $white1.Dispose(); $blue1.Dispose(); $font1.Dispose()
    $tmp.Dispose(); $font.Dispose()

    # 边缘受光：向内描一圈半透明白
    $rimWidth = [math]::Max(1.0, 0.0075 * $S)
    $rimPath = New-RoundedPath ($rimWidth / 2) ($rimWidth / 2) ($S - 1 - $rimWidth) ($S - 1 - $rimWidth) (0.22 * $S - $rimWidth / 2)
    $rimPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(36, 255, 255, 255)), ([single]$rimWidth)
    $g.DrawPath($rimPen, $rimPath)
    $rimPen.Dispose(); $rimPath.Dispose(); $tile.Dispose()
    $g.Dispose()

    if ($superSample -gt 1) {
        $small = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $sg = [System.Drawing.Graphics]::FromImage($small)
        $sg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $sg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $ia = New-Object System.Drawing.Imaging.ImageAttributes
        $ia.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $sg.DrawImage($canvas, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), 0, 0, $S, $S, ([System.Drawing.GraphicsUnit]::Pixel), $ia)
        $ia.Dispose(); $sg.Dispose(); $canvas.Dispose()
        return $small
    }
    return $canvas
}

function Get-SuperSample([int]$size) {
    if ($size -le 24) { return 8 }
    if ($size -le 48) { return 6 }
    if ($size -le 64) { return 4 }
    if ($size -le 128) { return 3 }
    return 2
}

# 小尺寸加大字面并收紧字距，抵消降采样后的模糊
function Get-SizeParams([int]$s) {
    if ($s -le 16) { return @{ Ink = 0.425; Track = -0.05 } }
    if ($s -le 20) { return @{ Ink = 0.410; Track = -0.04 } }
    if ($s -le 24) { return @{ Ink = 0.395; Track = -0.03 } }
    if ($s -le 32) { return @{ Ink = 0.375; Track = -0.02 } }
    if ($s -le 48) { return @{ Ink = 0.355; Track = -0.012 } }
    if ($s -le 64) { return @{ Ink = 0.345; Track = -0.006 } }
    return @{ Ink = 0.338; Track = 0.0 }
}

# 32bpp BMP 帧（含 AND 掩码），兼容所有 Windows 图标读取路径
function Get-IcoBmpEntry([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $data = $bmp.LockBits(
        (New-Object System.Drawing.Rectangle 0, 0, $s, $s),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $raw = New-Object byte[] ([math]::Abs($stride) * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint32]40)
    $bw.Write([int32]$s)
    $bw.Write([int32]($s * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]0)
    $bw.Write([uint32]($s * $s * 4))
    $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($row = $s - 1; $row -ge 0; $row--) {
        $line = New-Object byte[] ($s * 4)
        [Array]::Copy($raw, ($row * $stride), $line, 0, ($s * 4))
        $bw.Write($line)
    }
    $maskStride = [int](([math]::Floor(($s + 31) / 32)) * 4)
    for ($i = 0; $i -lt ($maskStride * $s); $i++) { $bw.Write([byte]0) }
    $bw.Flush()
    $out = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return $out
}

function Write-IcoFile([string]$path, $frames) {
    $count = $frames.Count
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)
    $offset = 6 + 16 * $count
    foreach ($f in $frames) {
        $bw.Write([byte]$(if ($f.Size -ge 256) { 0 } else { $f.Size }))
        $bw.Write([byte]$(if ($f.Size -ge 256) { 0 } else { $f.Size }))
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$f.Data.Length)
        $bw.Write([uint32]$offset)
        $offset += $f.Data.Length
    }
    foreach ($f in $frames) { $bw.Write([byte[]]$f.Data) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
$rendered = @{}
foreach ($s in $sizes) {
    $p = Get-SizeParams $s
    $ss = Get-SuperSample $s
    $grid = $false
    if ($s -le 24) { $ss = 1; $grid = $true }
    $bmp = Render-GoTile $s $p.Ink $p.Track $ss $grid
    $rendered[$s] = $bmp
    $frames += @{ Size = $s; Data = (Get-IcoBmpEntry $bmp) }
}

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$icoPath = Join-Path $outDir 'EveryGo.ico'
Write-IcoFile $icoPath $frames
$ico = Get-Item $icoPath
Write-Host ('已生成：' + $ico.FullName + '  ' + $ico.Length + ' bytes / ' + $sizes.Count + ' 帧')

if ($Preview) {
    $previewDir = Join-Path $env:TEMP 'EveryGo-icon-preview'
    if (-not (Test-Path $previewDir)) { New-Item -ItemType Directory -Path $previewDir | Out-Null }
    $big = Render-GoTile 512 0.338 0.0 1 $false
    $big.Save((Join-Path $previewDir 'preview-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $big.Dispose()
    $strip = New-Object System.Drawing.Bitmap 790, 320, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sgg = [System.Drawing.Graphics]::FromImage($strip)
    $sgg.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
    $x = 20
    foreach ($s in $sizes) { $sgg.DrawImage($rendered[$s], (New-Object System.Drawing.Rectangle $x, 40, $s, $s)); $x += $s + 8 }
    $sgg.Dispose()
    $strip.Save((Join-Path $previewDir 'preview-strip.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $strip.Dispose()
    Write-Host ('预览：' + $previewDir)
}
foreach ($k in $rendered.Keys) { $rendered[$k].Dispose() }
