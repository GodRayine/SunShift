param([string]$PackagePath, [string]$CertificatePath, [string]$SdkToolsDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $PackagePath) { $PackagePath = Join-Path $projectRoot 'artifacts/SunShift-v0.5.0-development-x64.msix' }
if (-not $CertificatePath) { $CertificatePath = Join-Path $projectRoot 'artifacts/SunShift-development.cer' }
# Never sign a package intended for Partner Center with this development identity.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $zip.Dispose() }
if ($manifest.Package.Identity.Name -ne 'SunShift.Development' -or $manifest.Package.Identity.Publisher -ne 'CN=SunShift Development') { throw 'Only the development package may be signed by this script.' }
$tools = if ($SdkToolsDirectory) { $SdkToolsDirectory } else { & (Join-Path $PSScriptRoot 'Get-SdkTools.ps1') }
$cert = New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature -CertStoreLocation Cert:/CurrentUser/My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}') -Subject 'CN=SunShift Development' -FriendlyName 'SunShift temporary MSIX test' -NotAfter (Get-Date).AddDays(30)
try {
    & (Join-Path $tools 'signtool.exe') sign /fd SHA256 /sha1 $cert.Thumbprint $PackagePath
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }
    Export-Certificate -Cert $cert -FilePath $CertificatePath | Out-Null
    Write-Output "Public test certificate: $CertificatePath"
} finally {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('My',[Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try { $store.Remove($cert) } finally { $store.Close() }
}
Write-Output 'No trust store was changed. Installation requires explicit trust in LocalMachine/TrustedPeople.'
