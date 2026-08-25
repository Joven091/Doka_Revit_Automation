; Define your application details here
#define MyAppName "Doka Revit Automation"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Joven091"
#define RevitAddinPath "{userappdata}\Autodesk\Revit\Addins\2024"

; Your exact local build output directory
#define BuildOutput "C:\Users\jsazon\OneDrive - Umdasch Group\Documents\GitHub\Doka_Revit_Automation\src\bin\Debug\net48"

[Setup]
; Basic installer metadata
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; Hide the directory selection page for a true "one-click" feel
DisableDirPage=yes
DefaultDirName={#RevitAddinPath}

; Output settings for the final .exe
OutputDir={#BuildOutput}\InstallerOutput
OutputBaseFilename=Install_DokaRevitAutomation
Compression=lzma
SolidCompression=yes

; Do not require admin rights (writing to the user's AppData)
PrivilegesRequired=lowest

[Files]
; 1. Copy the manifest file (.addin)
Source: "{#BuildOutput}\*.addin"; DestDir: "{app}"; Flags: ignoreversion

; 2. Copy the main DLL and any other dependencies in the build folder
Source: "{#BuildOutput}\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs