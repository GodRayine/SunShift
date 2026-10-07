#Requires -RunAsAdministrator
[CmdletBinding(DefaultParameterSetName = 'Installed')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Installed')][string]$PackageFullName,
    [Parameter(Mandatory, ParameterSetName = 'Package')][string]$PackagePath,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/App Certification Kit/appcert.exe'
if (-not (Test-Path -LiteralPath $appcert)) { throw 'Install the Windows SDK component Windows App Certification Kit from https://learn.microsoft.com/windows/apps/windows-sdk/downloads first.' }
if (-not $ReportPath) { $ReportPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/store-checks/wack.xml')) }
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Path (Split-Path $ReportPath) -Force | Out-Null
if (Test-Path -LiteralPath $ReportPath) { throw "Report already exists; choose a fresh path: $ReportPath" }
if ($PSCmdlet.ParameterSetName -eq 'Package') {
    $PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
}
& $appcert reset
if ($LASTEXITCODE -ne 0) { throw 'WACK reset failed.' }
if ($PSCmdlet.ParameterSetName -eq 'Package') {
    & $appcert test -appxpackagepath $PackagePath -reportoutputpath $ReportPath
} else {
    & $appcert test -packagefullname $PackageFullName -reportoutputpath $ReportPath
}
if ($LASTEXITCODE -ne 0) { throw "WACK did not pass; inspect $ReportPath" }
if (-not (Test-Path -LiteralPath $ReportPath)) { throw 'WACK returned without a report; no certification result is confirmed.' }
[xml]$report = Get-Content -LiteralPath $ReportPath -Raw
if ($report.REPORT.OVERALL_RESULT -ne 'PASS' -or $report.REPORT.PARTIAL_RUN -ne 'FALSE') {
    throw "WACK report is not a complete PASS; inspect $ReportPath"
}
Write-Output 'WACK overall result: PASS. Review optional test findings in the report.'
Write-Output $ReportPath
