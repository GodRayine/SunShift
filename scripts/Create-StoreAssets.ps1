param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# Draw the existing SunShift crescent directly at each target size.
$assets = @{ 'StoreLogo.png'=50; 'Square44x44Logo.png'=44; 'Square150x150Logo.png'=150; 'AppTile300.png'=300 }
foreach ($asset in $assets.GetEnumerator()) {
    $size = [int]$asset.Value
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size/64.0,$size/64.0)
    $bg = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(49,63,84))
    $fg = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(245,241,224))
    $pen = [System.Drawing.Pen]::new($fg,2)
    try {
        $g.FillEllipse($bg,2,2,60,60); $g.FillEllipse($fg,15,15,34,34); $g.FillEllipse($bg,30,12,30,36)
        $g.DrawLine($pen,8,32,12,32); $g.DrawLine($pen,13,14,17,18); $g.DrawLine($pen,13,50,17,46)
        $bitmap.Save((Join-Path $OutputDirectory $asset.Key),[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $pen.Dispose(); $fg.Dispose(); $bg.Dispose(); $g.Dispose(); $bitmap.Dispose() }
}
