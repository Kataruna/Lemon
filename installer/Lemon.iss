; Lemon installer (Inno Setup 6). Built by ..\make-installer.ps1, which passes the version.
; Installs for the current user only: no admin prompt, into %LOCALAPPDATA%\Programs\Lemon.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{5B8E2D47-9C1A-4F63-A2E8-7D4C1B9E3F06}
AppName=Lemon
AppVersion={#AppVersion}
AppVerName=Lemon {#AppVersion}
AppPublisher=Lemon
DefaultDirName={autopf}\Lemon
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 or later, the oldest Lemon's Windows App SDK build supports.
MinVersion=10.0.19041
WizardStyle=modern
SetupIconFile=..\src\Lemon.App\Assets\Lemon.ico
UninstallDisplayIcon={app}\Lemon.exe
UninstallDisplayName=Lemon
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=Lemon-Setup
; Lemon holds this mutex while running (also from the tray), so Setup and Uninstall ask to quit it first.
AppMutex=Lemon.App
CloseApplications=yes

[Tasks]
Name: "contextmenu"; Description: "Add ""Convert with Lemon"" to the right-click menu in File Explorer"; GroupDescription: "Windows integration:"
Name: "startup"; Description: "Start Lemon in the notification area when I sign in, so the conversion wheel is always ready"; GroupDescription: "Windows integration:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Lemon"; Filename: "{app}\Lemon.exe"
Name: "{autodesktop}\Lemon"; Filename: "{app}\Lemon.exe"; Tasks: desktopicon

[Registry]
; The same value Lemon's own "Start with Windows" switch uses, so the two stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Lemon"; ValueData: """{app}\Lemon.exe"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Lemon writes the menu itself so it only offers formats this PC can produce.
Filename: "{app}\Lemon.exe"; Parameters: "--register-context-menu"; StatusMsg: "Adding Convert with Lemon to File Explorer…"; Tasks: contextmenu; Flags: runhidden waituntilterminated
Filename: "{app}\Lemon.exe"; Description: "{cm:LaunchProgram,Lemon}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes the right-click menu and the sign-in entry, including ones turned on later from Lemon's Settings.
; Settings and presets in %LOCALAPPDATA%\Lemon are kept.
Filename: "{app}\Lemon.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "LemonUnregister"
