param([string]$ExecutablePath, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ExecutablePath) { $ExecutablePath = Join-Path $projectRoot 'src/SunShift.App/bin/Release/net10.0-windows10.0.19041.0/SunShift.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts/store-materials/screenshots' }
$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath); $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$scenes = @(
    @{ Name='01-day-light'; Flags=@('--light') },
    @{ Name='02-night-dark'; Flags=@('--dark','--night') },
    @{ Name='03-folders-light'; Flags=@('--light','--pool') },
    @{ Name='04-pairs-dark'; Flags=@('--dark','--pairs') },
    @{ Name='05-collection-dark'; Flags=@('--dark','--pairs','--collection') },
    @{ Name='06-location-light'; Flags=@('--light','--location') }
)
foreach ($scene in $scenes) {
    $destination = Join-Path $OutputDirectory "$($scene.Name).png"
    $arguments = @('--smoke-test','--store-screenshot','--output',('"'+$destination+'"')) + $scene.Flags
    $process = Start-Process -FilePath $ExecutablePath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(30000)) { throw "Screenshot timed out: $($scene.Name)" }
    if ($process.ExitCode -ne 0) { throw "Screenshot failed: $($scene.Name)" }
    Add-Type -AssemblyName System.Drawing
    $image = [Drawing.Image]::FromFile($destination)
    try { if ($image.Width -ne 1920 -or $image.Height -ne 1080) { throw 'Screenshot must be 1920x1080.' } } finally { $image.Dispose() }
    Write-Output $destination
}
