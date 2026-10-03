param(
    [string]$InputPath = (Join-Path $PSScriptRoot 'Assets/harbor-icon.png'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'harbor.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Bitmap]::new($InputPath)
try {
    $pixels = [System.Drawing.Bitmap]::new($image.Width, $image.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($pixels)
    try { $graphics.DrawImageUnscaled($image, 0, 0) } finally { $graphics.Dispose() }
    $data = $pixels.LockBits([System.Drawing.Rectangle]::new(0, 0, $pixels.Width, $pixels.Height), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $left = $image.Width; $top = $image.Height; $right = -1; $bottom = -1
    $row = [byte[]]::new([Math]::Abs($data.Stride))
    try {
        for ($y = 0; $y -lt $pixels.Height; $y++) {
            [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $row, 0, $row.Length)
            for ($x = 0; $x -lt $pixels.Width; $x++) {
                if ($row[$x * 4 + 3] -gt 8) { $left = [Math]::Min($left, $x); $right = [Math]::Max($right, $x); $top = [Math]::Min($top, $y); $bottom = [Math]::Max($bottom, $y) }
            }
        }
    } finally { $pixels.UnlockBits($data); $pixels.Dispose() }
    if ($right -lt $left) { throw 'Icon artwork is empty.' }
    $source = [System.Drawing.RectangleF]::new($left, $top, $right - $left + 1, $bottom - $top + 1)
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = ($size * 0.94) / [Math]::Max($source.Width, $source.Height)
            $width = $source.Width * $scale; $height = $source.Height * $scale
            $target = [System.Drawing.RectangleF]::new(($size - $width) / 2, ($size - $height) / 2, $width, $height)
            $g.DrawImage($image, $target, $source, [System.Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        } finally { $g.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
    }
    $file = [System.IO.File]::Create($OutputPath)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset); $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    } finally { $writer.Dispose(); $file.Dispose() }
    [pscustomobject]@{ Icon = $OutputPath; Sizes = $sizes; TransparentCorner = ($image.GetPixel(0, 0).A -eq 0) }
} finally { $image.Dispose() }
