<#
.SYNOPSIS
    Genera VortexPad.App/Assets/vortexpad.ico (16, 24, 32, 48, 64 y 256 px).

.DESCRIPTION
    Dibuja el logo "VP" (cuadrado redondeado cian de la marca con texto oscuro) y lo empaqueta en un .ico.
    Los tamaños pequeños van como BMP de 32 bits (compatibles con System.Drawing.Icon y la bandeja);
    el de 256 px va como PNG, como hace Windows desde Vista.

    Uso:  powershell -ExecutionPolicy Bypass -File tools\generate-icon.ps1
#>
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\VortexPad.App\Assets\vortexpad.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # Cuadrado redondeado cian (#00E5FF) con radio del 22 %
    $r = [Math]::Max(2, [int]($size * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $max = $size - 1
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($max - $d, 0, $d, $d, 270, 90)
    $path.AddArc($max - $d, $max - $d, $d, $d, 0, 90)
    $path.AddArc(0, $max - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 229, 255))), $path)

    # Texto "VP" oscuro (#061018) centrado
    $fontSize = [single]($size * 0.46)
    $font = New-Object System.Drawing.Font "Segoe UI", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, ([single]($size * 0.03)), $size, $size
    $g.DrawString("VP", $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 6, 16, 24))), $rect, $format)

    $g.Dispose()
    return $bmp
}

# Entrada BMP de un .ico: BITMAPINFOHEADER (alto x2) + píxeles BGRA de abajo arriba + máscara AND
function Get-BmpEntryBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    # Máscara AND (toda 0: la transparencia la da el canal alfa), filas alineadas a 4 bytes
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return ,$ms.ToArray()
}

function Get-PngEntryBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$entries = foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s
    [byte[]]$bytes = if ($s -ge 256) { Get-PngEntryBytes $bmp } else { Get-BmpEntryBytes $bmp }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $bytes }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)   # ICONDIR
$offset = 6 + (16 * $entries.Count)
foreach ($e in $entries) {                                                # ICONDIRENTRY
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$e.Bytes.Length); $w.Write([int]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $w.Write([byte[]]$e.Bytes) }
$w.Flush()

$full = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full)) | Out-Null
[System.IO.File]::WriteAllBytes($full, $out.ToArray())
Write-Host "Icono generado: $full ($($out.Length) bytes, tamaños $($sizes -join ', '))"
