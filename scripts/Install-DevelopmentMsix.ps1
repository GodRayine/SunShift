#Requires -RunAsAdministrator
param([string]$PackagePath, [string]$CertificatePath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $PackagePath) { $PackagePath = Join-Path $projectRoot 'artifacts/SunShift-v0.5.0-development-x64.msix' }
if (-not $CertificatePath) { $CertificatePath = Join-Path $projectRoot 'artifacts/SunShift-development.cer' }
$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.Path]::GetFullPath($CertificatePath))
if ($cert.Subject -ne 'CN=SunShift Development') { throw 'This script accepts only the SunShift development certificate.' }
$signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
if ($signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw 'The package signature and certificate do not match.' }
Import-Certificate -FilePath $CertificatePath -CertStoreLocation Cert:/LocalMachine/TrustedPeople | Out-Null
Add-AppxPackage -Path $PackagePath
Get-AppxPackage -Name SunShift.Development | Select-Object Name,PackageFullName,InstallLocation
Write-Output 'Development package installed for the current account. This is not a Microsoft Store submission.'
