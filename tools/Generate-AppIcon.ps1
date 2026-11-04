param(
	[string]$OutputDir = "Assets"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutputDir)) {
	New-Item -Path $OutputDir -ItemType Directory | Out-Null
}

$pngPath = Join-Path $OutputDir "AppIcon.png"
$icoPath = Join-Path $OutputDir "AppIcon.ico"

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(0,0,0,0))

# Background circle
$bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 31, 120, 180))
$g.FillEllipse($bgBrush, 8, 8, 240, 240)

# Folded map body
$mapBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 202, 233, 174))
$mapPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 45, 90, 39), 4)
$mapPoints = [System.Drawing.Point[]]@(
	(New-Object System.Drawing.Point(52, 160)),
	(New-Object System.Drawing.Point(98, 138)),
	(New-Object System.Drawing.Point(134, 162)),
	(New-Object System.Drawing.Point(176, 138)),
	(New-Object System.Drawing.Point(204, 152)),
	(New-Object System.Drawing.Point(204, 206)),
	(New-Object System.Drawing.Point(176, 192)),
	(New-Object System.Drawing.Point(134, 216)),
	(New-Object System.Drawing.Point(98, 192)),
	(New-Object System.Drawing.Point(52, 214))
)
$g.FillPolygon($mapBrush, $mapPoints)
$g.DrawPolygon($mapPen, $mapPoints)

# Map fold lines
$foldPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(170, 45, 90, 39), 3)
$g.DrawLine($foldPen, 98, 138, 98, 192)
$g.DrawLine($foldPen, 134, 162, 134, 216)
$g.DrawLine($foldPen, 176, 138, 176, 192)

# Route line
$routePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 255, 255), 5)
$routePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$routePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawCurve($routePen, [System.Drawing.Point[]]@(
	(New-Object System.Drawing.Point(74, 184)),
	(New-Object System.Drawing.Point(112, 171)),
	(New-Object System.Drawing.Point(146, 187)),
	(New-Object System.Drawing.Point(184, 171))
))

# Map pin
$pinBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 228, 26, 28))
$pinPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 130, 11, 12), 4)
$g.FillEllipse($pinBrush, 92, 44, 72, 72)
$g.DrawEllipse($pinPen, 92, 44, 72, 72)
$g.FillEllipse([System.Drawing.Brushes]::White, 118, 70, 20, 20)
$g.FillPolygon($pinBrush, [System.Drawing.Point[]]@(
	(New-Object System.Drawing.Point(128, 148)),
	(New-Object System.Drawing.Point(106, 108)),
	(New-Object System.Drawing.Point(150, 108))
))
$g.DrawPolygon($pinPen, [System.Drawing.Point[]]@(
	(New-Object System.Drawing.Point(128, 148)),
	(New-Object System.Drawing.Point(106, 108)),
	(New-Object System.Drawing.Point(150, 108))
))

$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Create ICO (64x64) for app/window icon
$iconBmp = New-Object System.Drawing.Bitmap($bmp, 64, 64)
$hIcon = $iconBmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)

$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()

$g.Dispose()
$bgBrush.Dispose()
$mapBrush.Dispose()
$mapPen.Dispose()
$foldPen.Dispose()
$routePen.Dispose()
$pinBrush.Dispose()
$pinPen.Dispose()
$iconBmp.Dispose()
$bmp.Dispose()

Write-Host "Generated: $pngPath"
Write-Host "Generated: $icoPath"
