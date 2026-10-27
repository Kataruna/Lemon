; Lemon installer (Inno Setup 6). Built by make-installer.ps1, which passes the version.
; Installs for the current user only: no admin prompt, into %LOCALAPPDATA%\Programs\Lemon.

#ifndef AppVersion
  #define AppVersion "0.4.1"
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
; Lemon holds the mutex "Lemon.App" while running; Setup and Uninstall quit it first (see CloseLemon below), also when
; silent, as under winget, where AppMutex would just give up.
CloseApplications=yes

[Tasks]
Name: "contextmenu"; Description: "Add ""Convert with Lemon"" to the right-click menu in File Explorer"; GroupDescription: "Windows integration:"
Name: "startup"; Description: "Start Lemon in the notification area when I sign in, so the conversion wheel is always ready"; GroupDescription: "Windows integration:"
; Converters and AI models have their own page after this one (Optional downloads, see [Code]).
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
; A silent upgrade (winget) puts back the Lemon it quit, in the tray as at sign-in.
Filename: "{app}\Lemon.exe"; Parameters: "--background"; Flags: nowait; Check: RestartAfterSilentInstall

[UninstallRun]
; Removes the right-click menu and the sign-in entry, including ones turned on later from Lemon's Settings.
; Settings and presets in %LOCALAPPDATA%\Lemon are kept.
Filename: "{app}\Lemon.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "LemonUnregister"

[Code]
// Optional downloads page: converters installed with winget, and AI models downloaded (and checked against a
// SHA-256) into %LOCALAPPDATA%\Lemon\models. Not in silent installs (winget): a quiet install shouldn't pull a
// gigabyte or open console windows. Lemon's welcome guide and Settings offer all of these later.
const
  ToolCount = 4;
  ModelCount = 4;

var
  Downloads: TDownloadWizardPage;
  Extras: TWizardPage;
  ExtrasList: TNewCheckListBox;
  ToolNames, ToolIds: array of String;
  ToolItems: array of Integer;
  ModelNames, ModelFiles, ModelUrls, ModelHashes: array of String;
  ModelItems: array of Integer;
  Failed: String;
  WasRunning: Boolean;

function ModelsFolder: String;
begin
  Result := ExpandConstant('{localappdata}\Lemon\models');
end;

// Keep in sync with Lemon.Core\Models\BackgroundModel.cs and UpscaleModel.cs (same files, URLs and hashes), and
// the winget ids with Lemon.Core\ExternalTools\ToolLocator.cs. An empty id means the tool isn't on winget.
procedure SetUpLists;
begin
  SetArrayLength(ToolNames, ToolCount);
  SetArrayLength(ToolIds, ToolCount);
  SetArrayLength(ToolItems, ToolCount);
  ToolNames[0] := 'FFmpeg: videos and music (MP4, MP3, GIFs from videos)'; ToolIds[0] := 'Gyan.FFmpeg';
  ToolNames[1] := 'Pandoc: documents and ebooks (Word, EPUB, HTML)';      ToolIds[1] := 'JohnMacFarlane.Pandoc';
  ToolNames[2] := 'LibreOffice: PDFs from Word, Excel and PowerPoint';     ToolIds[2] := 'TheDocumentFoundation.LibreOffice';
  ToolNames[3] := 'Ghostscript: shrinking PDFs (opens its download page)';  ToolIds[3] := '';

  SetArrayLength(ModelNames, ModelCount);
  SetArrayLength(ModelFiles, ModelCount);
  SetArrayLength(ModelUrls, ModelCount);
  SetArrayLength(ModelHashes, ModelCount);
  SetArrayLength(ModelItems, ModelCount);
  ModelNames[0] := 'Remove background: BiRefNet Lite (224 MB, recommended)';
  ModelFiles[0] := 'birefnet-lite.onnx';
  ModelUrls[0] := 'https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/de15b22ba131738a16dff04aab8bdf8dc32e3ac1/onnx/model.onnx';
  ModelHashes[0] := '5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333';
  ModelNames[1] := 'Remove background, best quality: BiRefNet (973 MB, needs more memory)';
  ModelFiles[1] := 'birefnet.onnx';
  ModelUrls[1] := 'https://huggingface.co/onnx-community/BiRefNet-ONNX/resolve/534d3c82d3bb8b2f0867db6dfbc3a525b8e42f67/onnx/model.onnx';
  ModelHashes[1] := '58f621f00f5d756097615970a88a791584600dcf7c45b18a0a6267535a1ebd3c';
  ModelNames[2] := 'Upscale: Real-ESRGAN General (5 MB, recommended)';
  ModelFiles[2] := 'realesrgan-general-x4v3.onnx';
  ModelUrls[2] := 'https://github.com/Kataruna/Lemon/releases/download/models-v1/realesrgan-general-x4v3.onnx';
  ModelHashes[2] := '47710b200e8ea23e90cc4193bd9e6f3302f163db94865673c843dd12e2344b8a';
  ModelNames[3] := 'Upscale, best quality: Real-ESRGAN x4plus (67 MB, slower)';
  ModelFiles[3] := 'realesrgan-x4plus.onnx';
  ModelUrls[3] := 'https://github.com/Kataruna/Lemon/releases/download/models-v1/realesrgan-x4plus.onnx';
  ModelHashes[3] := '863cc55ef1f43a851418253eb475318634993fbe8c2c3aadec3737d727296e68';
end;

// Quits a running Lemon so its files can be replaced or removed: asks first unless silent. False if it's still running.
function CloseLemon(Silent: Boolean): Boolean;
var
  Code, I: Integer;
begin
  Result := True;
  if not CheckForMutexes('Lemon.App') then
    Exit;
  WasRunning := True;
  if not Silent and (MsgBox('Lemon is running. Setup will quit it to continue.', mbConfirmation, MB_OKCANCEL) <> IDOK) then
  begin
    Result := False;
    Exit;
  end;
  // /F: closing the window only sends Lemon to the tray.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Lemon.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  for I := 1 to 50 do
    if CheckForMutexes('Lemon.App') then
      Sleep(100);
  Result := not CheckForMutexes('Lemon.App');
  if not Result and not Silent then
    MsgBox('Lemon couldn''t be quit. Right-click the lemon by the clock, choose Quit Lemon, and try again.', mbError, MB_OK);
end;

function InitializeSetup: Boolean;
begin
  Result := CloseLemon(WizardSilent);
end;

function InitializeUninstall: Boolean;
begin
  Result := CloseLemon(UninstallSilent);
end;

function Interactive: Boolean;
begin
  Result := not WizardSilent;
end;

function RestartAfterSilentInstall: Boolean;
begin
  Result := WizardSilent and WasRunning;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Downloaded %s', [FileName]));
  Result := True;
end;

procedure InitializeWizard;
var
  I: Integer;
  Note: TNewStaticText;
begin
  SetUpLists;
  Downloads := CreateDownloadPage('Downloading AI models', 'Lemon is downloading the AI models you chose. This can take a few minutes.', @OnDownloadProgress);

  Extras := CreateCustomPage(wpSelectTasks, 'Optional downloads',
    'Pictures work right away. Pick what else Lemon should get now; you can add any of these later in Lemon.');

  Note := TNewStaticText.Create(Extras);
  Note.Parent := Extras.Surface;
  Note.WordWrap := True;
  Note.Width := Extras.SurfaceWidth;
  Note.Caption := 'Converters are installed with winget, each in a window of its own, after Lemon is installed. ' +
    'AI models run on this PC; no picture leaves it.';
  Note.AdjustHeight;

  ExtrasList := TNewCheckListBox.Create(Extras);
  ExtrasList.Parent := Extras.Surface;
  ExtrasList.Top := Note.Top + Note.Height + ScaleY(8);
  ExtrasList.Width := Extras.SurfaceWidth;
  ExtrasList.Height := Extras.SurfaceHeight - ExtrasList.Top;

  ExtrasList.AddGroup('Converters for other kinds of files (needs internet)', '', 0, nil);
  for I := 0 to ToolCount - 1 do
    ToolItems[I] := ExtrasList.AddCheckBox(ToolNames[I], '', 1, False, True, False, False, nil);

  ExtrasList.AddGroup('AI models (needs internet)', '', 0, nil);
  for I := 0 to ModelCount - 1 do
    if FileExists(ModelsFolder + '\' + ModelFiles[I]) then
      ModelItems[I] := ExtrasList.AddCheckBox(ModelNames[I], 'downloaded', 1, False, False, False, False, nil)
    else
      ModelItems[I] := ExtrasList.AddCheckBox(ModelNames[I], '', 1, (I = 0) or (I = 2), True, False, False, nil);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = Extras.ID) and WizardSilent;
end;

function ToolChosen(I: Integer): Boolean;
begin
  Result := not WizardSilent and ExtrasList.Checked[ToolItems[I]];
end;

function ModelChosen(I: Integer): Boolean;
begin
  Result := not WizardSilent and ExtrasList.ItemEnabled[ModelItems[I]] and ExtrasList.Checked[ModelItems[I]];
end;

// Lists the chosen downloads on the Ready page, under the usual sections.
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  I: Integer;
  Chosen: String;
begin
  Result := MemoDirInfo;
  if (Result <> '') and (MemoTasksInfo <> '') then
    Result := Result + NewLine + NewLine;
  Result := Result + MemoTasksInfo;
  Chosen := '';
  for I := 0 to ToolCount - 1 do
    if ToolChosen(I) then
      Chosen := Chosen + Space + ToolNames[I] + NewLine;
  for I := 0 to ModelCount - 1 do
    if ModelChosen(I) then
      Chosen := Chosen + Space + ModelNames[I] + NewLine;
  if (Result <> '') and (Chosen <> '') then
    Result := Result + NewLine + NewLine;
  if Chosen <> '' then
    Result := Result + 'Optional downloads:' + NewLine + Chosen;
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

function NextButtonClick(CurPageID: Integer): Boolean;
var
  I: Integer;
begin
  Result := True;
  if CurPageID <> wpReady then Exit;
  Failed := '';
  for I := 0 to ModelCount - 1 do
    if ModelChosen(I) then
      Fetch(ModelUrls[I], ModelHashes[I], ModelFiles[I]);
end;

// Installs one converter with winget in a console window of its own, which stays open on an error.
procedure InstallTool(I: Integer);
var
  Code: Integer;
begin
  if ToolIds[I] = '' then Exit;
  WizardForm.StatusLabel.Caption := 'Installing ' + Copy(ToolNames[I], 1, Pos(':', ToolNames[I]) - 1) + ' with winget…';
  if not FileExists(ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe')) then
    Failed := Failed + #13#10 + ToolIds[I] + ': winget isn''t on this PC'
  else if not Exec(ExpandConstant('{cmd}'), '/c winget install --id ' + ToolIds[I] +
      ' --exact --source winget --accept-source-agreements --accept-package-agreements || pause',
      '', SW_SHOW, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Failed := Failed + #13#10 + ToolIds[I] + ': winget didn''t finish';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Name: String;
  I, Code: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  ForceDirectories(ModelsFolder);
  for I := 0 to ModelCount - 1 do
  begin
    Name := ExpandConstant('{tmp}\') + ModelFiles[I];
    if FileExists(Name) then
      if not RenameFile(Name, ModelsFolder + '\' + ModelFiles[I]) and not FileCopy(Name, ModelsFolder + '\' + ModelFiles[I], False) then
        Failed := Failed + #13#10 + ModelFiles[I] + ': could not be saved to ' + ModelsFolder;
  end;
  for I := 0 to ToolCount - 1 do
    if ToolChosen(I) then
      InstallTool(I);
  // Ghostscript isn't on winget.
  if ToolChosen(3) then
    ShellExec('open', 'https://ghostscript.com/releases/gsdnld.html', '', '', SW_SHOWNORMAL, ewNoWait, Code);
  if Failed <> '' then
    SuppressibleMsgBox('Lemon is installed, but these downloads didn''t finish:' + Failed + #13#10#13#10 +
      'You can add them later in Lemon: converters in the welcome guide (Settings > About > Show the welcome guide again), models in Settings > AI models.',
      mbInformation, MB_OK, IDOK);
end;
