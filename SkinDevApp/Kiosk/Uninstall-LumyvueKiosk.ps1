# Uninstall-LumyvueKiosk.ps1 - removes the automatic start-up and the two Windows tweaks made by Install-LumyvueKiosk.ps1.
$ErrorActionPreference = 'Stop'
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Please run this script as administrator (right-click > Run as administrator).' -ForegroundColor Yellow; exit 1
}
if (Get-ScheduledTask -TaskName 'LUMYVUE Kiosk' -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName 'LUMYVUE Kiosk' -Confirm:$false
    Write-Host "Start-up task 'LUMYVUE Kiosk' removed." -ForegroundColor Green
} else { Write-Host 'The start-up task was not installed.' }
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications' -Name ToastEnabled -Type DWord -Value 1 -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\EdgeUI' -Name AllowEdgeSwipe -ErrorAction SilentlyContinue
Write-Host 'Notifications and edge swipes are back to the Windows defaults. (Automatic sign-in, if you turned it on, is changed with netplwiz.)'
