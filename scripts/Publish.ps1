param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot "artifacts\portable-$Runtime" }
dotnet publish (Join-Path $projectRoot 'src\SunShift.App\SunShift.App.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $output 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $output 'THIRD-PARTY-NOTICES.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $output 'LICENSE')
Copy-Item -LiteralPath (Join-Path $projectRoot 'COPYRIGHT') -Destination (Join-Path $output 'COPYRIGHT')
Copy-Item -LiteralPath (Join-Path $projectRoot 'Licenses') -Destination $output -Recurse -Force
Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $projectRoot "artifacts\SunShift-$Runtime.zip") -Force
Write-Output (Join-Path $output 'SunShift.exe')
