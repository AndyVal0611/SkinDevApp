LUMYVUE on the Mini PC - automatic start and shutdown
=====================================================

Shut down
  "Shut down" is in the menu on the Dashboard and on the sign-in screen. It asks "Are you sure you want to shut down the device?".
  After Yes, a scan that is still being saved finishes first, then LUMYVUE closes and Windows shuts the Mini PC down.
  If Windows cannot start the shutdown, LUMYVUE stays open and says so.

Automatic start (kiosk)
  1. Copy the whole LUMYVUE folder to the Mini PC (the Kiosk folder must stay inside it).
  2. Right-click Install-LumyvueKiosk.ps1 > Run with PowerShell (as administrator), signed in as the Mini PC's account.
  3. Turn on automatic sign-in (netplwiz) and, in the BIOS, "power on after power loss". The script shows how.
  4. Restart. LUMYVUE opens full screen by itself after every start. If it stops unexpectedly it is started again
     after 5 seconds (details in %LOCALAPPDATA%\LUMYVUE\kiosk.log and crash.log).

Maintenance
  Alt+F4 in LUMYVUE closes it on purpose; it is not started again until the next sign-in.
  To run without kiosk mode, start SkinDevApp.exe without --kiosk.
  To remove the automatic start: Uninstall-LumyvueKiosk.ps1.
