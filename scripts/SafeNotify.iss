#ifndef AppVersion
#define AppVersion "1.0.4"
#endif

[Setup]
AppId={{6EFC4427-27A0-407F-9FF5-4FA0A901A57C}
AppName=SafeNotify
AppVersion={#AppVersion}
AppPublisher=SafeNotify contributors
DefaultDirName={localappdata}\Programs\SafeNotify
DefaultGroupName=SafeNotify
OutputDir=..\publish\installer
OutputBaseFilename=SafeNotify-Setup-{#AppVersion}-win-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=SafeNotify
VersionInfoDescription=SafeNotify Windows alert system

[Types]
Name: "full"; Description: "Client and server"; Flags: iscustom
Name: "client-only"; Description: "Alert client only"
Name: "server-only"; Description: "Organisation server only"

[Components]
Name: "client"; Description: "Windows alert client"; Types: full client-only
Name: "server"; Description: "Organisation server and dashboard"; Types: full server-only

[Tasks]
Name: "startclient"; Description: "Start the alert client when I sign in to Windows"; GroupDescription: "Automatic startup:"; Components: client; Flags: checkedonce
Name: "startserver"; Description: "Start the organisation server when I sign in to Windows"; GroupDescription: "Automatic startup:"; Components: server; Flags: checkedonce

[Files]
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\preproduction\client\*"; DestDir: "{app}\client"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: client; Excludes: "*.pdb"
Source: "..\publish\preproduction\server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: server; Excludes: "*.pdb,appsettings.Development.json,appsettings.json,web.config,data\*"
Source: "..\publish\preproduction\server\appsettings.json"; DestDir: "{app}\server"; Flags: onlyifdoesntexist; Components: server

[Icons]
Name: "{userprograms}\SafeNotify\SafeNotify Alert Client"; Filename: "{app}\client\SafeNotify.Client.exe"; WorkingDir: "{app}\client"; Components: client
Name: "{userprograms}\SafeNotify\SafeNotify Server Dashboard"; Filename: "http://127.0.0.1:5080"; Components: server
Name: "{userprograms}\SafeNotify\Uninstall SafeNotify"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SafeNotify Client"; ValueData: """{app}\client\SafeNotify.Client.exe"""; Flags: uninsdeletevalue; Tasks: startclient; Components: client
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SafeNotify Server"; ValueData: """{app}\server\SafeNotify.Server.exe"""; Flags: uninsdeletevalue; Tasks: startserver; Components: server

[Run]
Filename: "{app}\client\SafeNotify.Client.exe"; Description: "Launch SafeNotify Alert Client"; WorkingDir: "{app}\client"; Flags: postinstall nowait skipifsilent; Components: client
Filename: "{app}\server\SafeNotify.Server.exe"; Description: "Launch SafeNotify Server"; WorkingDir: "{app}\server"; Flags: postinstall nowait skipifsilent; Components: server

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM SafeNotify.Client.exe"; Flags: runhidden; RunOnceId: "StopSafeNotifyClient"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM SafeNotify.Server.exe"; Flags: runhidden; RunOnceId: "StopSafeNotifyServer"

[UninstallDelete]
Type: dirifempty; Name: "{userprograms}\SafeNotify"

[Code]
var
  ServerInfoPage: TWizardPage;
  ServerInfoMemo: TNewMemo;
  ExistingDataPage: TInputOptionWizardPage;
  ExistingVersion: string;
  ExistingInstallationFound: Boolean;

function ExistingServerDataDirectory: string;
begin
  Result := ExpandConstant('{localappdata}\SafeNotify\Server');
end;

function HasExistingInstallation: Boolean;
begin
  Result := RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6EFC4427-27A0-407F-9FF5-4FA0A901A57C}_is1', 'DisplayVersion', ExistingVersion);
  if not Result then
    Result := DirExists(ExistingServerDataDirectory);
end;

procedure InitializeWizard;
begin
  ExistingVersion := '';
  ExistingInstallationFound := HasExistingInstallation;
  if ExistingVersion <> '' then
    ExistingDataPage := CreateInputOptionPage(wpSelectComponents, 'Existing SafeNotify data found', 'SafeNotify ' + ExistingVersion + ' is already installed.', 'Replacing deletes the server database, security keys, and LAN settings under %LOCALAPPDATA%\SafeNotify\Server.', True, False)
  else
    ExistingDataPage := CreateInputOptionPage(wpSelectComponents, 'Existing SafeNotify data found', 'An earlier SafeNotify server data folder was found.', 'Replacing deletes the server database, security keys, and LAN settings under %LOCALAPPDATA%\SafeNotify\Server.', True, False);
  ExistingDataPage.Add('Keep the existing organisation, devices, server settings, and security keys');
  ExistingDataPage.Add('Replace them with a blank setup');
  ExistingDataPage.SelectedValueIndex := 0;

  ServerInfoPage := CreateCustomPage(wpSelectComponents, 'Server installation and first setup', 'Where SafeNotify installs the server and how to get started');
  ServerInfoMemo := TNewMemo.Create(WizardForm);
  ServerInfoMemo.Parent := ServerInfoPage.Surface;
  ServerInfoMemo.Left := 0;
  ServerInfoMemo.Top := 0;
  ServerInfoMemo.Width := ServerInfoPage.SurfaceWidth;
  ServerInfoMemo.Height := ServerInfoPage.SurfaceHeight;
  ServerInfoMemo.ReadOnly := True;
  ServerInfoMemo.ScrollBars := ssNone;
  ServerInfoMemo.WordWrap := True;
  ServerInfoMemo.BorderStyle := bsNone;
  ServerInfoMemo.Color := WizardForm.Color;
  ServerInfoMemo.TabStop := False;
  ServerInfoMemo.Lines.Add('Server program location:');
  ServerInfoMemo.Lines.Add('');
  ServerInfoMemo.Lines.Add('When installation finishes, the server starts automatically if you selected the server component. If you enabled automatic startup, it will also start when this Windows account signs in.');
  ServerInfoMemo.Lines.Add('');
  ServerInfoMemo.Lines.Add('Open Start menu > SafeNotify > SafeNotify Server Dashboard, or visit http://127.0.0.1:5080 on this computer. On first use, create your organisation and administrator account.');
  ServerInfoMemo.Lines.Add('');
  ServerInfoMemo.Lines.Add('Uninstall from Windows Settings > Apps > Installed apps > SafeNotify, or use the Uninstall SafeNotify shortcut in the SafeNotify Start menu folder. Uninstall stops the running client and server and removes the program. Your organisation data is kept under %LOCALAPPDATA%\SafeNotify so reinstalling does not erase it.');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = ServerInfoPage.ID then
    ServerInfoMemo.Lines[1] := WizardDirValue + '\server\SafeNotify.Server.exe';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = ExistingDataPage.ID) and ((not ExistingInstallationFound) or (not WizardIsComponentSelected('server')))) or
    ((PageID = ServerInfoPage.ID) and not WizardIsComponentSelected('server'));
end;

procedure ResetExistingServerData;
var
  ExitCode: Integer;
begin
  if ExistingDataPage.SelectedValueIndex <> 1 then
    Exit;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SafeNotify.Server.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if not DelTree(ExistingServerDataDirectory, '', True, True) and DirExists(ExistingServerDataDirectory) then
    RaiseException('SafeNotify could not replace the existing server data. Close the server and try again.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    if WizardIsComponentSelected('server') and ExistingInstallationFound then
      ResetExistingServerData;
    if not WizardIsComponentSelected('client') or not WizardIsTaskSelected('startclient') then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SafeNotify Client');
    if not WizardIsComponentSelected('server') or not WizardIsTaskSelected('startserver') then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SafeNotify Server');
  end;
end;
