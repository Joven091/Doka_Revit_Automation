; Define your application details here
#define MyAppName "Doka Revit Automation"
#define MyAppPublisher "Joven091"
#define RevitAddinPath "{userappdata}\Autodesk\Revit\Addins\2024"

; Paths are relative to this script; BuildOutput may be overridden with /D.
#define RepoRoot AddBackslash(SourcePath)
#ifndef BuildOutput
  #define BuildOutput RepoRoot + "src\bin\Release\net48"
#endif
#define MyAppVersion GetVersionNumbersString(BuildOutput + "\MyRevitAddin.dll")

[Setup]
; Basic installer metadata
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductTextVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; Hide the directory selection page for a true "one-click" feel
DisableDirPage=yes
DefaultDirName={#RevitAddinPath}

; Output settings for the final .exe
OutputDir=artifacts\installer
OutputBaseFilename=Install_DokaRevitAutomation
Compression=lzma
SolidCompression=yes

; Do not require admin rights (writing to the user's AppData)
PrivilegesRequired=lowest
CloseApplications=no
RestartApplications=no

[Files]
; Public project settings only; never distribute server or SMTP secrets.
Source: "{#BuildOutput}\access-settings.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\update-settings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#BuildOutput}\Doka.RevitUpdater.exe"; DestDir: "{app}"; Flags: ignoreversion
; 1. Copy the manifest file (.addin)
Source: "{#RepoRoot}MyRevitAddin.addin"; DestDir: "{app}"; Flags: ignoreversion

; 2. Copy the main DLL and any other dependencies in the build folder
Source: "{#BuildOutput}\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
