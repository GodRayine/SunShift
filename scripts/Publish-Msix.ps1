param(
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName = 'GodRayine',
    [switch]$Development,
    [string]$SdkToolsDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Development) {
    if ($IdentityName -or $Publisher) { throw 'Do not mix development and Store identities.' }
    $IdentityName = 'SunShift.Development'; $Publisher = 'CN=SunShift Development'
} elseif (-not $IdentityName -or -not $Publisher) {
    throw 'Supply IdentityName and Publisher exactly as issued by Partner Center, or use -Development.'
}
if ($IdentityName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$') { throw 'Invalid package identity name.' }
$null = [Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
$tools = if ($SdkToolsDirectory) { $SdkToolsDirectory } else { & (Join-Path $PSScriptRoot 'Get-SdkTools.ps1') }
$appProject = Join-Path $projectRoot 'src/SunShift.App/SunShift.App.csproj'
[xml]$definition = Get-Content -LiteralPath $appProject -Raw
$version = [string]$definition.Project.PropertyGroup.Version
$kind = if ($Development) { 'development' } else { 'store' }
$staging = Join-Path $projectRoot "artifacts/msix-$kind-v$version"
$app = Join-Path $staging 'App'
# Refuse reuse so stale binaries cannot enter a new package.
if (Test-Path -LiteralPath $staging) { throw "Staging already exists; archive it before rebuilding: $staging" }
New-Item -ItemType Directory -Path $app -Force | Out-Null
dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($file in @('LICENSE','COPYRIGHT','THIRD-PARTY-NOTICES.md','PRIVACY.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $app
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'Licenses') -Destination $app -Recurse
& (Join-Path $PSScriptRoot 'Create-StoreAssets.ps1') -OutputDirectory (Join-Path $staging 'Assets')
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'packaging/AppxManifest.xml.template') -Raw
$values = @{NAME=$IdentityName;PUBLISHER=$Publisher;VERSION="$version.0";DISPLAY=$PublisherDisplayName}
foreach ($entry in $values.GetEnumerator()) { $manifest = $manifest.Replace("@@$($entry.Key)@@",[Security.SecurityElement]::Escape($entry.Value)) }
$manifest | Set-Content -LiteralPath (Join-Path $staging 'AppxManifest.xml') -Encoding utf8
$package = Join-Path $projectRoot "artifacts/SunShift-v$version-$kind-x64.msix"
$log = Join-Path $projectRoot "artifacts/makeappx-$kind.log"
& (Join-Path $tools 'makeappx.exe') pack /d $staging /p $package /o *> $log
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log | Select-Object -Last 20; throw 'MakeAppx schema or package validation failed.' }
Write-Output $package
Write-Output 'Unsigned package. Store signs submissions; local installation requires a trusted test signature.'
