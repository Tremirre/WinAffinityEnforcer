$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'WinAffinityEnforcer.exe'
$process = Start-Process -FilePath $exe -ArgumentList '--stop' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Stop failed. See %LOCALAPPDATA%\WinAffinityEnforcer\scheduler.log' }
