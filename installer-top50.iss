#define RepoRoot AddBackslash(SourcePath)
#ifndef BuildOutput
  #define BuildOutput RepoRoot + "src\bin\Release\net48"
#endif
#define MyVersion GetVersionNumbersString(BuildOutput + "\MyRevitAddin.dll")

[Setup]
AppId={code:InstallerAppId}
AppName=Doka Revit Automation
AppVersion={#MyVersion}
AppVerName=Doka Engineering Dashboard {#MyVersion} - Top50 review
AppPublisher=Joven091
VersionInfoVersion={#MyVersion}
VersionInfoProductVersion={#MyVersion}
VersionInfoDescription=Engineering Dashboard with integrated Top50 - review release
DefaultDirName={code:InstallDirectory}
UsePreviousAppDir=no
UsePreviousLanguage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
CreateUninstallRegKey=not IsIsolatedTest
UninstallDisplayName=Doka Engineering Dashboard {#MyVersion} - Top50 review
UninstallFilesDir={app}
OutputDir=artifacts\top50-installer
OutputBaseFilename=DokaEngineeringDashboard_Top50_Setup_1.1.16
Compression=lzma2
SolidCompression=yes
InfoBeforeFile={#RepoRoot}packaging\top50-review.txt
SetupLogging=yes

[Files]
Source: "{#BuildOutput}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Doka.RevitUpdater.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\access-settings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#BuildOutput}\update-settings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#RepoRoot}MyRevitAddin.addin"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}packaging\top50-review.txt"; DestDir: "{app}"; DestName: "Doka-Top50-Review.txt"; Flags: ignoreversion

[Code]
var BackupDirectory: String;

function IsIsolatedTest: Boolean;
begin Result := ExpandConstant('{param:ISOLATEDROOT|}') <> ''; end;

function InstallerAppId(Param: String): String;
begin
  if IsIsolatedTest then Result := 'Doka Revit Automation Top50 Isolated Test'
  else Result := 'Doka Revit Automation';
end;

function InstallDirectory(Param: String): String;
begin
  if IsIsolatedTest then Result := ExpandConstant('{param:ISOLATEDROOT|}')
  else Result := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2024');
end;

function RevitIsOpen: Boolean;
var Locator, Service, Processes: Variant;
begin
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Service := Locator.ConnectServer('', 'root\cimv2');
  Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Revit.exe''');
  Result := Processes.Count > 0;
end;

function HasDashboardIds(const Path: String): Boolean;
var Text: AnsiString;
begin
  Result := False;
  if LoadStringFromFile(Path, Text) then
    Result := (Pos('a1b2c3d4-e5f6-4a5b-6c7d-8e9f0a1b2c3d', Lowercase(String(Text))) > 0) or
      (Pos('2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e', Lowercase(String(Text))) > 0) or
      (Pos('c6dedce1-4571-4b7c-b781-1f338826be41', Lowercase(String(Text))) > 0);
end;

function ConflictingManifest(const Directory, Allowed: String): String;
var Find: TFindRec; Path: String;
begin
  Result := '';
  if FindFirst(AddBackslash(Directory) + '*.addin', Find) then
  try
    repeat
      Path := AddBackslash(Directory) + Find.Name;
      if (CompareText(Path, Allowed) <> 0) and HasDashboardIds(Path) then begin Result := Path; Exit; end;
    until not FindNext(Find);
  finally FindClose(Find); end;
end;

function InitializeSetup: Boolean;
var Root, Prefix: String;
begin
  Result := False;
  if IsIsolatedTest then begin
    Root := ExpandFileName(ExpandConstant('{param:ISOLATEDROOT|}'));
    Prefix := AddBackslash(ExpandConstant('{localappdata}\Temp\DokaTop50InstallerTests'));
    if (CompareText(Copy(Root,1,Length(Prefix)),Prefix) <> 0) or (Length(Root) <= Length(Prefix)) then begin
      SuppressibleMsgBox('Isolated tests are restricted to the dedicated temporary test directory.',mbError,MB_OK,IDOK); Exit;
    end;
  end;
  Result := True;
end;

function ExistingManifestIsLocal(const Path, Directory: String): Boolean;
var Bytes: AnsiString; Content, Value: String; P, Q, Count: Integer;
begin
  Result := False; Count := 0;
  if not LoadStringFromFile(Path,Bytes) then Exit;
  Content := Lowercase(Utf8Decode(Bytes));
  repeat
    P := Pos('<assembly>',Content);
    if P=0 then Break;
    Delete(Content,1,P+9); Q := Pos('</assembly>',Content);
    if Q=0 then Exit;
    Value := Trim(Copy(Content,1,Q-1));
    StringChangeEx(Value,'&amp;','&',True);
    if (CompareText(Value,'MyRevitAddin.dll')<>0) and
      (CompareText(Value,'.\MyRevitAddin.dll')<>0) and
      (CompareText(Value,AddBackslash(Directory)+'MyRevitAddin.dll')<>0) then Exit;
    Count := Count+1; Delete(Content,1,Q+10);
  until False;
  Result := Count>0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Conflict, Root: String; Major, Minor: Cardinal;
begin
  Result := '';
  Root := ExpandFileName(InstallDirectory(''));
  if CompareText(ExpandFileName(ExpandConstant('{app}')),Root) <> 0 then begin
    Result := 'This installer supports only its verified per-user location. Directory overrides are refused.'; Exit;
  end;
  try
    if RevitIsOpen then begin Result := 'Save your models and close every Revit window, then run this installer again.'; Exit; end;
  except Result := 'Unable to verify whether Revit is closed. Installation was not started.'; Exit; end;
  if not IsIsolatedTest then begin
    if not FileExists(ExpandConstant('{autopf64}\Autodesk\Revit 2024\Revit.exe')) and
      not RegKeyExists(HKLM64,'SOFTWARE\Autodesk\Revit\2024') then begin
      Result := 'Autodesk Revit2024 is required. This package does not support Revit2023 or install Revit.'; Exit;
    end;
  end;
  Conflict := ConflictingManifest(Root, AddBackslash(Root)+'MyRevitAddin.addin');
  if (Conflict='') and not IsIsolatedTest then
    Conflict := ConflictingManifest(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024'),'');
  if Conflict<>'' then begin Result := 'Another Dashboard registration exists: '+Conflict+'. Back it up and resolve that registration before installing; it was not changed.'; Exit; end;
  if FileExists(AddBackslash(Root)+'MyRevitAddin.addin') and not HasDashboardIds(AddBackslash(Root)+'MyRevitAddin.addin') then begin
    Result := 'The existing MyRevitAddin.addin is not recognized as this Dashboard. No replacement was made.'; Exit;
  end;
  if FileExists(AddBackslash(Root)+'MyRevitAddin.addin') and not ExistingManifestIsLocal(AddBackslash(Root)+'MyRevitAddin.addin',Root) then begin
    Result := 'The existing Dashboard points to a custom assembly location. A reviewed migration is required; its registration was not changed.'; Exit;
  end;
  if GetVersionNumbers(AddBackslash(Root)+'MyRevitAddin.dll',Major,Minor) then
    if (Major > (1 shl 16)+1) or ((Major=(1 shl 16)+1) and (Minor>(16 shl 16))) then
      Result := 'A newer Dashboard is already installed. Downgrade refused.';
end;

procedure BackupOne(const Name: String);
var Source: String;
begin
  Source := AddBackslash(ExpandConstant('{app}'))+Name;
  if FileExists(Source) then begin
    if not CopyFile(Source,AddBackslash(BackupDirectory)+Name,False) then RaiseException('Backup failed for '+Name+'; replacement stopped.');
    if not SaveStringToFile(AddBackslash(BackupDirectory)+'SHA256SUMS',GetSHA256OfFile(Source)+'  '+Name+#13#10,True) then RaiseException('Unable to record backup checksums.');
  end;
end;

function XmlPath(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result,'&','&amp;',True);
  StringChangeEx(Result,'<','&lt;',True);
  StringChangeEx(Result,'>','&gt;',True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Find: TFindRec; Text: AnsiString; Content, BaseBackup: String; Index: Integer;
begin
  if CurStep=ssInstall then begin
    if FileExists(ExpandConstant('{app}\MyRevitAddin.dll')) or FileExists(ExpandConstant('{app}\MyRevitAddin.addin')) then begin
      if IsIsolatedTest then BackupDirectory := ExpandConstant('{app}')+'-backups\'
      else BackupDirectory := ExpandConstant('{localappdata}\DokaRevitAutomation\Backups\');
      BackupDirectory := BackupDirectory+'1.1.16-'+GetDateTimeString('yyyymmdd-hhnnss',#0,#0);
      BaseBackup := BackupDirectory; Index := 0;
      while DirExists(BackupDirectory) do begin Index := Index+1; BackupDirectory := BaseBackup+'-'+IntToStr(Index); end;
      if not ForceDirectories(BackupDirectory) then RaiseException('Unable to create upgrade backup.');
      if FindFirst(ExpandConstant('{app}\*.dll'),Find) then try
        repeat BackupOne(Find.Name); until not FindNext(Find);
      finally FindClose(Find); end;
      BackupOne('MyRevitAddin.addin'); BackupOne('Doka.RevitUpdater.exe');
      BackupOne('access-settings.json'); BackupOne('update-settings.json');
      SaveStringToFile(AddBackslash(BackupDirectory)+'RESTORE.txt','Previous Dashboard files from '+ExpandConstant('{app}')+#13#10+'Close Revit, uninstall the review release, then reinstall your previously trusted installer. Keep these files/settings for a reviewed restoration if that installer is unavailable. Do not mix dependency versions.'+#13#10,False);
      Log('PREVIOUS_DASHBOARD_BACKUP='+BackupDirectory);
    end;
  end;
  if CurStep=ssPostInstall then begin
    if not LoadStringFromFile(ExpandConstant('{app}\MyRevitAddin.addin'),Text) then RaiseException('Unable to read add-in registration.');
    Content := String(Text);
    StringChangeEx(Content,'<Assembly>MyRevitAddin.dll</Assembly>','<Assembly>'+XmlPath(ExpandConstant('{app}\MyRevitAddin.dll'))+'</Assembly>',True);
    if not SaveStringToFile(ExpandConstant('{app}\MyRevitAddin.addin'),Utf8Encode(Content),False) then RaiseException('Unable to write absolute add-in registration.');
    Log('REGISTERED_ASSEMBLY='+ExpandConstant('{app}\MyRevitAddin.dll'));
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := False;
  try
    if RevitIsOpen then begin SuppressibleMsgBox('Close every Revit window before uninstalling the Dashboard.',mbError,MB_OK,IDOK); Exit; end;
  except SuppressibleMsgBox('Unable to verify that Revit is closed; uninstall was not started.',mbError,MB_OK,IDOK); Exit; end;
  Result := True;
end;
