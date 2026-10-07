$ErrorActionPreference = 'Stop'
$sdkVersion = '10.0.28000.2705'
$toolFolder = Join-Path $env:TEMP "SunShift-sdk-$sdkVersion"
$makeappx = Join-Path $toolFolder 'bin/10.0.28000.0/x64/makeappx.exe'
if (-not (Test-Path -LiteralPath $makeappx)) {
    New-Item -ItemType Directory -Path $toolFolder -Force | Out-Null
    $zip = Join-Path $toolFolder 'sdk.zip'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$sdkVersion/microsoft.windows.sdk.buildtools.$sdkVersion.nupkg" -OutFile $zip
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '8BFDFB6CA2633F531CF80B5FA22512BA61A394D7988F0970DB83BAADC67929ED') {
        throw 'Windows SDK BuildTools archive checksum mismatch.'
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $toolFolder -Force
}
Join-Path $toolFolder 'bin/10.0.28000.0/x64'
