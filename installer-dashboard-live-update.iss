#define RepoRoot AddBackslash(SourcePath)
#ifndef BuildRoot
  #error BuildRoot must name the verified Dashboard build directory
#endif
#ifndef ArchiveName
  #error ArchiveName must name the verified Dashboard ZIP
#endif
#ifndef ManifestSha256
  #error ManifestSha256 is required
#endif
#ifndef BridgeSha256
  #error BridgeSha256 is required
#endif
#define MyVersion GetVersionNumbersString(BuildRoot + "\bootstrap\Doka.RevitBridge.dll")

[Setup]
AppId={code:InstallerAppId}
AppName=Doka Revit Automation
AppVersion={#MyVersion}
AppVerName=Doka Engineering Dashboard {#MyVersion}
AppPublisher=Joven091
VersionInfoVersion={#MyVersion}
VersionInfoDescription=Revit 2024 Engineering Dashboard with Dashboard-only updates
DefaultDirName={code:InstallDirectory}
UsePreviousAppDir=no
UsePreviousLanguage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
AllowCancelDuringInstall=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
CreateUninstallRegKey=not IsIsolatedTest
UninstallDisplayName=Doka Engineering Dashboard {#MyVersion}
UninstallFilesDir={app}
OutputBaseFilename=Install_DokaRevitAutomation_{#MyVersion}
Compression=lzma2
SolidCompression=yes
InfoBeforeFile={#RepoRoot}packaging\dashboard-live-update\installer-notes.txt
SetupLogging=yes

[Files]
Source: "{#BuildRoot}\bootstrap\*"; Flags: dontcopy
Source: "{#BuildRoot}\feed\{#ArchiveName}"; Flags: dontcopy
Source: "{#BuildRoot}\feed\dashboard-latest.json"; Flags: dontcopy
Source: "{#RepoRoot}scripts\Invoke-DashboardInstaller.ps1"; Flags: dontcopy
Source: "{#RepoRoot}packaging\dashboard-live-update\installer-notes.txt"; DestDir: "{app}"; DestName: "Doka-Dashboard-Installation.txt"; Flags: ignoreversion

[UninstallDelete]
Type: files; Name: "{app}\Doka.RevitBridge.dll"
Type: files; Name: "{app}\Doka.RevitUpdater.exe"
Type: files; Name: "{app}\MyRevitAddin.addin"
Type: files; Name: "{app}\.doka-isolated-install"

[Messages]
FinishedLabel=Engineering Dashboard is installed. Open Revit 2024 and launch Engineering Dashboard. Compatible future updates restart only the Dashboard.

[Code]
var MigrationComplete, SetupComplete: Boolean; MigrationLogPath: String;

function FileAttributes(const Path: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function HasNoLinks(const Path: String): Boolean;
var Part, Parent: String; Attributes: Cardinal;
begin
  Result := True; Part := ExpandFileName(Path);
  repeat
    Attributes := FileAttributes(Part);
    if (Attributes<>$FFFFFFFF) and ((Attributes and $400)<>0) then begin Result := False; Exit; end;
    Parent := ExtractFileDir(Part);
    if CompareText(Parent,Part)=0 then Break;
    Part := Parent;
  until Length(Part)<=3;
end;

function IsIsolatedTest: Boolean;
begin Result := ExpandConstant('{param:ISOLATEDROOT|}') <> ''; end;

function IsValidTestRoot(const Root: String): Boolean;
var Full, Prefix: String;
begin
  Full := ExpandFileName(Root);
  Prefix := AddBackslash(ExpandConstant('{localappdata}\Temp\DokaDashboardInstallerTests'));
  Result := (Length(Full)>Length(Prefix)) and (CompareText(Copy(Full,1,Length(Prefix)),Prefix)=0);
  if Result then Result := HasNoLinks(Full);
end;

function InstallerAppId(Param: String): String;
begin
  if IsIsolatedTest then Result := 'Doka Dashboard Live Update Isolated Test'
  else Result := 'Doka Revit Automation';
end;

function InstallDirectory(Param: String): String;
begin
  if IsIsolatedTest then Result := AddBackslash(ExpandFileName(ExpandConstant('{param:ISOLATEDROOT|}')))+'roaming\Autodesk\Revit\Addins\2024'
  else Result := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2024');
end;

function ApplicationIsOpen: Boolean;
var Locator, Service, Processes: Variant;
begin
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Service := Locator.ConnectServer('', 'root\cimv2');
  Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Revit.exe'' OR Name = ''Doka.EngineeringDashboard.exe''');
  Result := Processes.Count > 0;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if IsIsolatedTest and not IsValidTestRoot(ExpandConstant('{param:ISOLATEDROOT|}')) then begin
    SuppressibleMsgBox('Isolated tests are restricted to the dedicated temporary test directory.',mbError,MB_OK,IDOK); Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Arguments, Root, LogDirectory, LogPath: String; ExitCode: Integer;
begin
  Result := '';
  Root := ExpandFileName(InstallDirectory(''));
  if CompareText(ExpandFileName(ExpandConstant('{app}')),Root)<>0 then begin Result := 'Directory overrides are refused. Use the supported current-user Revit 2024 location.'; Exit; end;
  try
    if ApplicationIsOpen then begin Result := 'Save your models and close every Revit and Engineering Dashboard window, then run this installer again.'; Exit; end;
  except Result := 'Unable to verify that Revit and the Dashboard are closed. Installation was not started.'; Exit; end;
  if not IsIsolatedTest and not FileExists(ExpandConstant('{autopf64}\Autodesk\Revit 2024\Revit.exe')) and not RegKeyExists(HKLM64,'SOFTWARE\Autodesk\Revit\2024') then begin
    Result := 'Autodesk Revit 2024 is required. This installer does not install Revit.'; Exit;
  end;
  if IsIsolatedTest then LogDirectory := AddBackslash(ExpandFileName(ExpandConstant('{param:ISOLATEDROOT|}')))+'local\DokaRevitAutomation\InstallerLogs'
  else LogDirectory := ExpandConstant('{localappdata}\DokaRevitAutomation\InstallerLogs');
  if IsIsolatedTest and (not HasNoLinks(Root) or not HasNoLinks(LogDirectory)) then begin Result := 'Isolated installation paths cannot be links.'; Exit; end;
  if not ForceDirectories(LogDirectory) then begin Result := 'Unable to create the installation log directory.'; Exit; end;
  LogPath := AddBackslash(LogDirectory)+'setup-'+GetDateTimeString('yyyymmdd-hhnnss',#0,#0)+'.log';
  ExtractTemporaryFile('Doka.RevitBridge.dll');
  ExtractTemporaryFile('Doka.RevitUpdater.exe');
  ExtractTemporaryFile('Doka.DashboardPackage.exe');
  ExtractTemporaryFile('MyRevitAddin.addin');
  ExtractTemporaryFile('access-settings.json');
  ExtractTemporaryFile('update-settings.json');
  ExtractTemporaryFile('Initialize-DashboardLiveUpdate.ps1');
  ExtractTemporaryFile('Invoke-DashboardInstaller.ps1');
  ExtractTemporaryFile('dashboard-latest.json');
  ExtractTemporaryFile('{#ArchiveName}');
  Arguments := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+ExpandConstant('{tmp}\Invoke-DashboardInstaller.ps1')+'" -FeedDirectory "'+ExpandConstant('{tmp}')+'" -LogPath "'+LogPath+'"';
  if IsIsolatedTest then Arguments := Arguments+' -IsolatedRoot "'+ExpandFileName(ExpandConstant('{param:ISOLATEDROOT|}'))+'"';
  WizardForm.StatusLabel.Caption := 'Verifying the Dashboard and preserving your existing installation...';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),Arguments,ExpandConstant('{tmp}'),SW_HIDE,ewWaitUntilTerminated,ExitCode) then begin
    Result := 'Unable to start the Dashboard migration. See '+LogPath; Exit;
  end;
  if ExitCode<>0 then begin Result := 'The Dashboard migration did not complete. See '+LogPath; Exit; end;
  MigrationComplete := True; MigrationLogPath := LogPath;
  Log('DASHBOARD_MIGRATION_LOG='+LogPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep=ssDone then SetupComplete := True;
  if (CurStep=ssPostInstall) and IsIsolatedTest then
    if not SaveStringToFile(ExpandConstant('{app}\.doka-isolated-install'),ExpandFileName(ExpandConstant('{param:ISOLATEDROOT|}')),False) then RaiseException('Unable to record the isolated installation.');
end;

procedure DeinitializeSetup;
begin
  if MigrationComplete and not SetupComplete then begin
    Log('Dashboard migration completed, but installer registration did not finish. Runtime and upgrade backups were retained. Migration log: '+MigrationLogPath);
    SuppressibleMsgBox('The Dashboard runtime migration completed, but Setup did not finish its registration. Your work and upgrade backups are retained. Run this same installer again to repair Setup. Migration log: '+MigrationLogPath,mbInformation,MB_OK,IDOK);
  end;
end;

function InitializeUninstall: Boolean;
var Root, Expected: String; Marker: AnsiString;
begin
  Result := False; Root := ExpandFileName(ExpandConstant('{app}'));
  if LoadStringFromFile(AddBackslash(Root)+'.doka-isolated-install',Marker) then begin
    if not IsValidTestRoot(String(Marker)) then begin SuppressibleMsgBox('The isolated installation path is invalid.',mbError,MB_OK,IDOK); Exit; end;
    Expected := AddBackslash(ExpandFileName(String(Marker)))+'roaming\Autodesk\Revit\Addins\2024';
  end
  else Expected := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2024');
  if (CompareText(Root,ExpandFileName(Expected))<>0) or not HasNoLinks(Root) then begin SuppressibleMsgBox('This uninstaller does not own this location.',mbError,MB_OK,IDOK); Exit; end;
  try
    if ApplicationIsOpen then begin SuppressibleMsgBox('Close every Revit and Engineering Dashboard window before uninstalling.',mbError,MB_OK,IDOK); Exit; end;
    if FileExists(AddBackslash(Root)+'MyRevitAddin.addin') then
      if CompareText(GetSHA256OfFile(AddBackslash(Root)+'MyRevitAddin.addin'),'{#ManifestSha256}')<>0 then begin SuppressibleMsgBox('The Dashboard registration has changed. Preserve it for review before uninstalling.',mbError,MB_OK,IDOK); Exit; end;
    if FileExists(AddBackslash(Root)+'Doka.RevitBridge.dll') then
      if CompareText(GetSHA256OfFile(AddBackslash(Root)+'Doka.RevitBridge.dll'),'{#BridgeSha256}')<>0 then begin SuppressibleMsgBox('The Revit integration has changed. Use its current installer to uninstall it.',mbError,MB_OK,IDOK); Exit; end;
  except SuppressibleMsgBox('Unable to verify the installation. Uninstall was not started.',mbError,MB_OK,IDOK); Exit; end;
  Result := True;
end;
