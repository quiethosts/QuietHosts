#Requires -Version 5.1
<#
.SYNOPSIS
    Draws the QuietHosts app icon (a blue shield with a white check mark) and writes src\QuietHosts\QuietHosts.ico.

.DESCRIPTION
    Uses Windows PowerShell 5.1 and System.Drawing only. Writes one .ico file with 16, 20, 24, 32, 40, 48, 64 and
    256 px images: 32-bit bitmaps with an alpha channel for the small sizes and PNG for 256 px. Needs no admin
    rights and changes nothing else. The output is the same on every run.

    Run it from the repository root:
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
#>
[CmdletBinding()]
param(
    # Default: src\QuietHosts\QuietHosts.ico next to this tools folder.
    [string]$OutFile = ''
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ($OutFile -eq '') {
    $scriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutFile = Join-Path (Split-Path -Parent $scriptFolder) 'src\QuietHosts\QuietHosts.ico'
}

$Sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$ShieldColor = [System.Drawing.Color]::FromArgb(255, 0x1E, 0x63, 0xBE)
$EdgeColor = [System.Drawing.Color]::FromArgb(255, 0x14, 0x47, 0x8C)
$CheckColor = [System.Drawing.Color]::White

function Get-Point([single]$X, [single]$Y, [single]$S, [single]$FX, [single]$FY) {
    New-Object System.Drawing.PointF -ArgumentList ($X + $S * $FX), ($Y + $S * $FY)
}

# The same shield as the window's status icon, a little wider so small sizes stay readable.
function New-ShieldPath([single]$X, [single]$Y, [single]$S) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leftTop = Get-Point $X $Y $S 0.08 0.17
    $top = Get-Point $X $Y $S 0.50 0.03
    $rightTop = Get-Point $X $Y $S 0.92 0.17
    $rightMid = Get-Point $X $Y $S 0.92 0.47
    $bottom = Get-Point $X $Y $S 0.50 0.97
    $leftMid = Get-Point $X $Y $S 0.08 0.47
    $p.StartFigure()
    $p.AddBezier($leftTop, (Get-Point $X $Y $S 0.25 0.15), (Get-Point $X $Y $S 0.39 0.09), $top)
    $p.AddBezier($top, (Get-Point $X $Y $S 0.61 0.09), (Get-Point $X $Y $S 0.75 0.15), $rightTop)
    $p.AddLine($rightTop, $rightMid)
    $p.AddBezier($rightMid, (Get-Point $X $Y $S 0.92 0.73), (Get-Point $X $Y $S 0.72 0.88), $bottom)
    $p.AddBezier($bottom, (Get-Point $X $Y $S 0.28 0.88), (Get-Point $X $Y $S 0.08 0.73), $leftMid)
    $p.CloseFigure()
    return , $p
}

function New-IconBitmap([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        [single]$s = $Size
        $path = New-ShieldPath 0 0 $s
        $fill = New-Object System.Drawing.SolidBrush -ArgumentList $ShieldColor
        try { $g.FillPath($fill, $path) } finally { $fill.Dispose() }
        if ($Size -ge 32) {
            # A darker edge gives the shield a crisp outline on light backgrounds.
            $edge = New-Object System.Drawing.Pen -ArgumentList $EdgeColor, ([single]([Math]::Max(1.0, $s / 32.0)))
            try { $g.DrawPath($edge, $path) } finally { $edge.Dispose() }
        }
        $path.Dispose()

        [single]$width = [Math]::Max(2.0, $s * 0.115)
        $pen = New-Object System.Drawing.Pen -ArgumentList $CheckColor, $width
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            [System.Drawing.PointF[]]$check = @(
                (Get-Point 0 0 $s 0.30 0.50),
                (Get-Point 0 0 $s 0.445 0.645),
                (Get-Point 0 0 $s 0.71 0.365)
            )
            $g.DrawLines($pen, $check)
        }
        finally { $pen.Dispose() }
    }
    finally { $g.Dispose() }
    return , $bmp
}

# A 32-bit DIB for an icon entry: BITMAPINFOHEADER (double height), bottom-up BGRA rows, then the AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$Bitmap) {
    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $rect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $w, $h
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $raw = New-Object byte[] -ArgumentList ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    }
    finally { $Bitmap.UnlockBits($data) }

    $maskStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $mask = New-Object byte[] -ArgumentList ($maskStride * $h)
    for ($y = 0; $y -lt $h; $y++) {
        $maskRow = $h - 1 - $y
        for ($x = 0; $x -lt $w; $x++) {
            if ($raw[$y * $stride + $x * 4 + 3] -eq 0) {
                $i = $maskRow * $maskStride + [int][Math]::Floor($x / 8)
                $mask[$i] = $mask[$i] -bor (0x80 -shr ($x % 8))
            }
        }
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter -ArgumentList $ms
    $bw.Write([int32]40)
    $bw.Write([int32]$w)
    $bw.Write([int32]($h * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int32]0)
    $bw.Write([int32]($w * $h * 4 + $mask.Length))
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($raw, $y * $stride, $w * 4) }
    $bw.Write($mask)
    $bw.Flush()
    return , $ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$Bitmap) {
    $ms = New-Object System.IO.MemoryStream
    $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$images = @()
foreach ($size in $Sizes) {
    $bmp = New-IconBitmap $size
    try {
        if ($size -ge 256) { $bytes = Get-PngBytes $bmp } else { $bytes = Get-DibBytes $bmp }
    }
    finally { $bmp.Dispose() }
    $images += , @($size, $bytes)
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter -ArgumentList $out
$bw.Write([int16]0)              # reserved
$bw.Write([int16]1)              # type: icon
$bw.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $size = [int]$image[0]
    [byte[]]$bytes = $image[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([byte]$dim)        # width (0 means 256)
    $bw.Write([byte]$dim)        # height
    $bw.Write([byte]0)           # palette colors
    $bw.Write([byte]0)           # reserved
    $bw.Write([int16]1)          # planes
    $bw.Write([int16]32)         # bits per pixel
    $bw.Write([int32]$bytes.Length)
    $bw.Write([int32]$offset)
    $offset += $bytes.Length
}
foreach ($image in $images) {
    [byte[]]$bytes = $image[1]
    $bw.Write($bytes)
}
$bw.Flush()

$folder = Split-Path -Parent $OutFile
if (-not (Test-Path -LiteralPath $folder)) { throw "The folder $folder doesn't exist." }
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Output ("Wrote {0} ({1:N0} bytes, sizes {2})" -f (Resolve-Path -LiteralPath $OutFile), $out.Length, ($Sizes -join ', '))
