Add-Type -AssemblyName System.Drawing

$outputPath = Join-Path $PSScriptRoot "..\src\LinkMend.App\Assets\LinkMend.ico"
$outputDirectory = Split-Path -Parent $outputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.ColorTranslator]::FromHtml("#3B82F6"),
        [System.Drawing.ColorTranslator]::FromHtml("#1D4ED8"),
        45.0)

    $radius = [single]($size * 0.23)
    $diameter = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($size - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($size - $diameter, $size - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $size - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($brush, $path)

    $highlightRect = New-Object System.Drawing.RectangleF(0, 0, $size, [single]($size * 0.52))
    $highlight = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $highlightRect,
        [System.Drawing.Color]::FromArgb(42, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        90.0)
    $graphics.FillPath($highlight, $path)

    $font = New-Object System.Drawing.Font(
        "Segoe UI",
        [single]($size * 0.33),
        [System.Drawing.FontStyle]::Bold,
        [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF(0, [single](-$size * 0.04), $size, $size)
    $graphics.DrawString("LM", $font, [System.Drawing.Brushes]::White, $textRect, $format)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@($size, $stream.ToArray())

    $stream.Dispose()
    $font.Dispose()
    $format.Dispose()
    $highlight.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$fileStream = [System.IO.File]::Create($outputPath)
$writer = New-Object System.IO.BinaryWriter($fileStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    $size = $image[0]
    $data = $image[1]
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($image in $images) {
    $writer.Write($image[1])
}

$writer.Flush()
$writer.Dispose()
$fileStream.Dispose()
Write-Host "Created $outputPath"
