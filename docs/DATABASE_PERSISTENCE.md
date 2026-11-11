# LUMYVUE – permanent data, backup, restore and deployment

## Where the data lives (every PC has its own, independent copy)
`C:\ProgramData\LUMYVUE\`
- `Database\lumyvue_db.sqlite` – accounts, patients, scans (the only database)
- `Patient Records\`, `Captures\`, `Reports\` – scan images and reports
- `Backups\` – automatic daily backups, manual backups, safety copies made before a restore
- `Logs\` – `database.log` (creation, migration, restore), crash log

The folder is created automatically on first start. It is outside the exe folder, so copying, replacing
or deleting the Release folder never touches the data.

## Start-up rules
1. Database file exists -> it is opened as it is (never recreated, reset or overwritten). Login is shown, or the
   "create the initial administrator" screen only if the database truly has no account.
2. Database file absent -> older databases next to earlier builds (e.g. `bin\Debug`, `bin\Release`) are searched; the best
   non-empty one is COPIED (originals untouched), verified (integrity check, row counts, file sizes) and only then put in place.
3. Nothing found -> a new empty database is created (first install), and this is written to `Logs\database.log`.
4. Any failure (folder not writable, corrupt file, failed migration) -> a clear message is shown, sign-in is disabled and no empty database is created.

## Laptop Release build
Build `SkinDevApp.csproj` in Release. Output: `SkinDevApp\bin\Release\`. No database is needed in that folder.

## Transfer to the mini PC (USB)
1. Copy the whole `bin\Release` folder to the USB stick. Do NOT copy `lumyvue_db.sqlite` or `Backups` (they are not used anyway).
2. On the mini PC paste it anywhere (e.g. `C:\LUMYVUE\`) and run `SkinDevApp.exe`. First start creates `C:\ProgramData\LUMYVUE\`.
3. To update later, replace the files in that folder – the data stays.
The laptop and the mini PC never share or overwrite data unless you deliberately restore a backup on one of them.

## Backup
- Automatic: once a day when the app starts (SQLite online-backup API, consistent while the app runs).
- Manual: Records page -> "Back up now".
- Offline copy for a USB: copy a file from `C:\ProgramData\LUMYVUE\Backups\` (and the `Patient Records` folder for images).

## Restore (never automatic; LUMYVUE must be closed)
```
SkinDevApp.exe --restore-db "C:\ProgramData\LUMYVUE\Backups\<file>.sqlite"            (shows what would happen, changes nothing)
SkinDevApp.exe --restore-db "C:\ProgramData\LUMYVUE\Backups\<file>.sqlite" --confirm  (does it)
```
Add `--quiet` to suppress the message box; the result is always written to `Logs\restore_result.txt`.
The active database is verified-copied to `Backups\lumyvue_db_before_restore_*.sqlite` first, so a restore can be undone.
An invalid file is refused and nothing changes.

## Test / special options
- `LUMYVUE_ROOT_DIR` – relocates the data root (testing only).
- `LUMYVUE_DATA_DIR` or `data_folder.txt` – existing shared-folder mode, unchanged.
