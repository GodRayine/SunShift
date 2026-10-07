#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$PackageFullName, [string]$ReportPath)
$ErrorActionPreference = 'Stop'
$appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/App Certification Kit/appcert.exe'
if (-not (Test-Path -LiteralPath $appcert)) { throw 'Install the Windows SDK component Windows App Certification Kit from https://learn.microsoft.com/windows/apps/windows-sdk/downloads first.' }
if (-not $ReportPath) { $ReportPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/store-checks/wack.xml')) }
New-Item -ItemType Directory -Path (Split-Path $ReportPath) -Force | Out-Null
& $appcert reset
if ($LASTEXITCODE -ne 0) { throw 'WACK reset failed.' }
& $appcert test -packagefullname $PackageFullName -reportoutputpath $ReportPath
if ($LASTEXITCODE -ne 0) { throw "WACK did not pass; inspect $ReportPath" }
Write-Output $ReportPath
