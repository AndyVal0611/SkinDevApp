# Run-LumyvueKiosk.ps1 - starts LUMYVUE in kiosk mode and starts it again if it stops unexpectedly.
# LUMYVUE exits with code 0 when it is closed on purpose (Shut down, or Alt+F4 for maintenance); any other exit code is a crash.
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'SkinDevApp.exe'
$log = Join-Path $env:LOCALAPPDATA 'LUMYVUE\kiosk.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$failures = 0
while ($true) {
    $started = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList '--kiosk' -WorkingDirectory (Split-Path $exe) -PassThru -Wait
    if ($p.ExitCode -eq 0) { break }
    Add-Content -Path $log -Value ("{0:yyyy-MM-dd HH:mm:ss}  LUMYVUE stopped with exit code {1}; starting it again" -f (Get-Date), $p.ExitCode)
    if (((Get-Date) - $started).TotalSeconds -gt 120) { $failures = 0 } else { $failures++ }
    if ($failures -ge 10) { Add-Content -Path $log -Value 'LUMYVUE stopped 10 times in a row at start-up: giving up so Windows stays usable.'; break }
    Start-Sleep -Seconds 5
}
