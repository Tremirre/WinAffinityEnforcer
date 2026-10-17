$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $key -Name 'WinAffinityEnforcer' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $key -Name 'WinAffinityEnforcer'
}
Write-Output 'Disabled start at sign-in. The running app has not been stopped.'
