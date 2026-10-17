$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'ColigationScheduler.exe'
if (-not (Test-Path $exe)) { throw "Build/publish the application first. Not found: $exe" }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name 'ColigationScheduler' -Value "`"$exe`"" -PropertyType String -Force | Out-Null
Write-Output 'Enabled start at sign-in for the current user. This does not start the app now.'
