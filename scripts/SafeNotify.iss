#define AppVersion "1.0.0"

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
VersionInfoVersion=1.0.0.0
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

[UninstallDelete]
Type: dirifempty; Name: "{userprograms}\SafeNotify"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    if not WizardIsComponentSelected('client') or not WizardIsTaskSelected('startclient') then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SafeNotify Client');
    if not WizardIsComponentSelected('server') or not WizardIsTaskSelected('startserver') then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SafeNotify Server');
  end;
end;
