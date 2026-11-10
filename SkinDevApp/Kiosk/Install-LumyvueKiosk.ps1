# Install-LumyvueKiosk.ps1 - makes the Mini PC open LUMYVUE full screen every time the user signs in (also after a restart or a crash).
# Run once on the Mini PC: right-click > "Run with PowerShell" as administrator, signed in as the account the Mini PC uses.
# Add -NoHarden to skip the two small Windows tweaks (notifications and edge swipes off). Undo everything with Uninstall-LumyvueKiosk.ps1.
param([switch]$NoHarden)
$ErrorActionPreference = 'Stop'
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Please run this script as administrator (right-click > Run as administrator).' -ForegroundColor Yellow; exit 1
}
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'SkinDevApp.exe'
if (-not (Test-Path $exe)) { Write-Host "SkinDevApp.exe was not found at $exe. Keep this Kiosk folder inside the LUMYVUE folder." -ForegroundColor Red; exit 1 }
$launcher = Join-Path $PSScriptRoot 'Run-LumyvueKiosk.ps1'
$user = $id.Name

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $launcher) -WorkingDirectory (Split-Path $exe)
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew `
             -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName 'LUMYVUE Kiosk' -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
    -Description 'Opens LUMYVUE full screen when the user signs in and starts it again after an unexpected stop.' -Force | Out-Null
Write-Host "Start-up task 'LUMYVUE Kiosk' created for $user." -ForegroundColor Green

if (-not $NoHarden) {
    # no pop-up notifications over LUMYVUE (this user)
    New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications' -Force | Out-Null
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications' -Name ToastEnabled -Type DWord -Value 0
    # no edge swipes (widgets, action centre) on the touch screen
    New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\EdgeUI' -Force | Out-Null
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\EdgeUI' -Name AllowEdgeSwipe -Type DWord -Value 0
    Write-Host 'Notifications and edge swipes are turned off.' -ForegroundColor Green
}

Write-Host ''
Write-Host 'Two more things are needed so the Mini PC opens LUMYVUE by itself after being switched on:' -ForegroundColor Cyan
Write-Host '  1. Automatic sign-in: press Win+R, type  netplwiz , select the account, untick "Users must enter a user name and password", OK.'
Write-Host '     (Windows asks for the account password once. Do this yourself; this script never handles passwords.)'
Write-Host '  2. Switch on after power loss: in the BIOS/UEFI, set "Restore on AC power loss" (or "After power failure") to "Power on".'
Write-Host ''
Write-Host 'Restart the Mini PC to test. To stop LUMYVUE for maintenance, press Alt+F4 in LUMYVUE (the desktop is still behind it).'
