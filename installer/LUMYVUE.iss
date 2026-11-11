; LUMYVUE installer (Inno Setup 6). Build:  ISCC.exe installer\LUMYVUE.iss   (after a Release build of SkinDevApp)
; Output: installer\Output\LUMYVUE-Setup.exe
; The installer only places the program files. Patient data lives in C:\ProgramData\LUMYVUE and is never touched by
; installing, updating or uninstalling.
#define AppName "LUMYVUE"
#define AppExe  "SkinDevApp.exe"
#define Src     "..\SkinDevApp\bin\Release"

[Setup]
AppId={{6E1B7F2A-4C3D-4B8E-9A55-1F0C2D7A9E31}
AppName={#AppName}
AppVersion=1.0.0
AppPublisher=LUMYVUE
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=LUMYVUE-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce

[Files]
; never ship a database or backups: the data is created on the PC in C:\ProgramData\LUMYVUE
Source: "{#Src}\*"; DestDir: "{app}"; Excludes: "lumyvue_db*.sqlite,Backups\*,*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

; No [UninstallDelete] entry for ProgramData on purpose: uninstalling must not erase patient data.
