; Lemon installer (Inno Setup 6). Built by make-installer.ps1, which passes the version.
; Installs for the current user only: no admin prompt, into %LOCALAPPDATA%\Programs\Lemon.

#ifndef AppVersion
  #define AppVersion "0.3.0"
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
OutputDir=..\..\dist\Windows
OutputBaseFilename=Lemon-Setup
; Lemon holds this mutex while running (also from the tray), so Setup and Uninstall ask to quit it first.
AppMutex=Lemon.App
CloseApplications=yes

[Tasks]
Name: "contextmenu"; Description: "Add ""Convert with Lemon"" to the right-click menu in File Explorer"; GroupDescription: "Windows integration:"
Name: "startup"; Description: "Start Lemon in the notification area when I sign in, so the conversion wheel is always ready"; GroupDescription: "Windows integration:"
; Models are downloaded (and checked against a SHA-256) into %LOCALAPPDATA%\Lemon\models; see [Code] below.
Name: "modellite"; Description: "Download the AI model for Remove background: BiRefNet Lite (224 MB, recommended)"; GroupDescription: "Background remover (needs internet):"
Name: "modelfull"; Description: "Download the higher-quality AI model: BiRefNet (973 MB, needs more memory)"; GroupDescription: "Background remover (needs internet):"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\dist\Windows\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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

[Code]
// Keep in sync with Lemon.Core\Models\BackgroundModel.cs (same pinned URLs and hashes).
const
  LiteUrl  = 'https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/de15b22ba131738a16dff04aab8bdf8dc32e3ac1/onnx/model.onnx';
  LiteHash = '5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333';
  FullUrl  = 'https://huggingface.co/onnx-community/BiRefNet-ONNX/resolve/534d3c82d3bb8b2f0867db6dfbc3a525b8e42f67/onnx/model.onnx';
  FullHash = '58f621f00f5d756097615970a88a791584600dcf7c45b18a0a6267535a1ebd3c';

var
  Downloads: TDownloadWizardPage;
  Failed: String;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Downloaded %s', [FileName]));
  Result := True;
end;

procedure InitializeWizard;
begin
  Downloads := CreateDownloadPage('Downloading AI models', 'Lemon is downloading the background remover model. This can take a few minutes.', @OnDownloadProgress);
end;

// True when this model is already installed and intact, so it needn't be downloaded again.
function AlreadyInstalled(const Name, Hash: String): Boolean;
var
  Path: String;
begin
  Path := ExpandConstant('{localappdata}\Lemon\models\') + Name;
  Result := FileExists(Path) and (CompareText(GetSHA256OfFile(Path), Hash) = 0);
end;

// Downloads one model into {tmp}; a failure only skips that model, Lemon still installs.
procedure Fetch(const Url, Hash, Name: String);
begin
  if AlreadyInstalled(Name, Hash) then
  begin
    Log(Name + ' is already installed; skipping the download.');
    Exit;
  end;
  try
    if WizardSilent then
      DownloadTemporaryFile(Url, Name, Hash, nil)
    else
    begin
      Downloads.Clear;
      Downloads.Add(Url, Name, Hash);
      Downloads.Show;
      try
        Downloads.Download;
      finally
        Downloads.Hide;
      end;
    end;
  except
    Failed := Failed + #13#10 + Name + ': ' + GetExceptionMessage;
    DeleteFile(ExpandConstant('{tmp}\') + Name);
  end;
end;

procedure FetchSelected;
begin
  Failed := '';
  if WizardIsTaskSelected('modellite') then Fetch(LiteUrl, LiteHash, 'birefnet-lite.onnx');
  if WizardIsTaskSelected('modelfull') then Fetch(FullUrl, FullHash, 'birefnet.onnx');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpReady then
    FetchSelected;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder, Name: String;
  Names: TArrayOfString;
  I: Integer;
begin
  // Silent installs never show the Ready page, so download here instead.
  if (CurStep = ssInstall) and WizardSilent then
    FetchSelected;
  if CurStep <> ssPostInstall then Exit;
  Folder := ExpandConstant('{localappdata}\Lemon\models');
  ForceDirectories(Folder);
  SetArrayLength(Names, 2);
  Names[0] := 'birefnet-lite.onnx';
  Names[1] := 'birefnet.onnx';
  for I := 0 to 1 do
  begin
    Name := ExpandConstant('{tmp}\') + Names[I];
    if FileExists(Name) then
      if not RenameFile(Name, Folder + '\' + Names[I]) and not FileCopy(Name, Folder + '\' + Names[I], False) then
        Failed := Failed + #13#10 + Names[I] + ': could not be saved to ' + Folder;
  end;
  if Failed <> '' then
    SuppressibleMsgBox('Lemon is installed, but these models could not be saved:' + Failed + #13#10#13#10 +
      'You can download them later in Lemon: Settings > Background remover.', mbInformation, MB_OK, IDOK);
end;
