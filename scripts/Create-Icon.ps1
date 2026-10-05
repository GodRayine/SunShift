$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\src\SunShift.App\Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$bitmap = [System.Drawing.Bitmap]::new(64,64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(49,63,84))
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(245,241,224))
$graphics.FillEllipse($background,2,2,60,60)
$graphics.FillEllipse($white,15,15,34,34)
$graphics.FillEllipse($background,30,12,30,36)
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(245,241,224),2)
$graphics.DrawLine($pen,8,32,12,32)
$graphics.DrawLine($pen,13,14,17,18)
$graphics.DrawLine($pen,13,50,17,46)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $assetDirectory 'SunShift.ico'))
try { $icon.Save($stream) } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $white.Dispose(); $background.Dispose(); $pen.Dispose() }
