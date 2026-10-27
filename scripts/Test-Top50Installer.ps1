[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$OldPayload,[Parameter(Mandatory)][string]$SourceDxf,[Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$Installer=(Resolve-Path -LiteralPath $Installer).Path
$OldPayload=(Resolve-Path -LiteralPath $OldPayload).Path
$SourceDxf=(Resolve-Path -LiteralPath $SourceDxf).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$prefix=Join-Path $env:LOCALAPPDATA 'Temp\DokaTop50InstallerTests'
$root=Join-Path $prefix ([guid]::NewGuid().ToString('N'))
if(!([IO.Path]::GetFullPath($root).StartsWith([IO.Path]::GetFullPath($prefix)+'\',[StringComparison]::OrdinalIgnoreCase))){throw 'Unsafe test root'}
New-Item -ItemType Directory -Path $root | Out-Null
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Label){if(!$Condition){throw "FAIL: $Label"};$checks.Add("PASS $Label");Write-Output "PASS $Label"}
function ProductionFingerprint {
    $values=@()
    foreach($folder in @((Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'),(Join-Path $env:PROGRAMDATA 'Autodesk\Revit\Addins\2024'))){
        $values+="$folder exists=$(Test-Path -LiteralPath $folder)"
        if(Test-Path -LiteralPath $folder){$values+=@(Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName | ForEach-Object {"$($_.FullName) $((Get-FileHash -LiteralPath $_.FullName).Hash)"})}
    }
    foreach($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Doka Revit Automation_is1','HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Doka Revit Automation_is1')){
        $values+="$key exists=$(Test-Path -LiteralPath $key)";if(Test-Path -LiteralPath $key){$values+=(Get-ItemProperty -LiteralPath $key | ConvertTo-Json -Compress)}
    }
    return $values -join "`n"
}
function RunInstaller([string]$Target,[string]$Label,[bool]$Success=$true){
    $log=Join-Path $evidence "$Label.log"
    $arguments="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /ISOLATEDROOT=`"$Target`" /DIR=`"$Target`" /LOG=`"$log`""
    $process=Start-Process -FilePath $Installer -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(60000)){throw "Installer did not finish within 60 seconds: $Label, PID $($process.Id)"}
    $process.Refresh();if($Success){Check ($process.ExitCode -eq 0) "$Label installer exit zero"}else{Check ($process.ExitCode -ne 0) "$Label rejected before installation"}
}
$before=ProductionFingerprint
$expected=[Diagnostics.FileVersionInfo]::GetVersionInfo($Installer).FileVersion.Trim()
Check ([version]$expected -gt [version]'1.1.15.0') 'installer is newer than original beam fixture'
$fresh=Join-Path $root 'fresh'
RunInstaller $fresh 'fresh'
Check (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $fresh 'MyRevitAddin.dll')).FileVersion.Trim()) -eq $expected) 'fresh installed assembly version'
$build=Join-Path (Split-Path $PSScriptRoot -Parent) 'src\bin\Release\net48'
foreach($file in Get-ChildItem -LiteralPath $build -Filter '*.dll' -File){Check ((Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath (Join-Path $fresh $file.Name)).Hash) "installed hash $($file.Name)"}
[xml]$manifest=Get-Content -LiteralPath (Join-Path $fresh 'MyRevitAddin.addin') -Raw
Check (@($manifest.RevitAddIns.AddIn | Where-Object Assembly -ne (Join-Path $fresh 'MyRevitAddin.dll')).Count -eq 0) 'all three registration paths absolute and correct'
Check (@($manifest.RevitAddIns.AddIn).Count -eq 3) 'three original registrations retained without duplicates'
# Run from another application directory with probing only, not generated binding redirects.
$smoke=Join-Path (Split-Path $PSScriptRoot -Parent) 'tests\Top50.PackageSmoke\bin\Release\net48\Top50.PackageSmoke.exe'
Copy-Item -LiteralPath $smoke -Destination (Join-Path $root 'Top50.PackageSmoke.exe')
'<configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><probing privatePath="fresh"/></assemblyBinding></runtime></configuration>' | Set-Content -LiteralPath (Join-Path $root 'Top50.PackageSmoke.exe.config')
& (Join-Path $root 'Top50.PackageSmoke.exe') $SourceDxf (Join-Path $evidence 'installed-smoke') *> (Join-Path $evidence 'installed-smoke.log')
Check ($LASTEXITCODE -eq 0) 'actual installed payload solver/import/export/dashboard smoke without binding redirects'

$upgrade=Join-Path $root 'upgrade'
New-Item -ItemType Directory -Path $upgrade | Out-Null
Get-ChildItem -LiteralPath $OldPayload -File | Copy-Item -Destination $upgrade
$oldDllHash=(Get-FileHash -LiteralPath (Join-Path $upgrade 'MyRevitAddin.dll')).Hash
Check (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $upgrade 'MyRevitAddin.dll')).FileVersion.Trim()) -eq '1.1.15.0') 'prior 1.1.15 file-layout fixture'
'{"sourceDirectory":"ISOLATED_TEST_SHARE","custom":"preserve"}' | Set-Content -LiteralPath (Join-Path $upgrade 'update-settings.json')
'{"customAccess":"preserve"}' | Set-Content -LiteralPath (Join-Path $upgrade 'access-settings.json')
'disposable saved calculation' | Set-Content -LiteralPath (Join-Path $upgrade 'saved-project.xml')
'unrelated add-in sentinel' | Set-Content -LiteralPath (Join-Path $upgrade 'Unrelated.dll')
$settings=@{};foreach($name in @('update-settings.json','access-settings.json','saved-project.xml','Unrelated.dll')){$settings[$name]=(Get-FileHash -LiteralPath (Join-Path $upgrade $name)).Hash}
RunInstaller $upgrade 'upgrade'
Check (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $upgrade 'MyRevitAddin.dll')).FileVersion.Trim()) -eq $expected) 'upgrade replaces old assembly with current release'
foreach($name in $settings.Keys){Check ((Get-FileHash -LiteralPath (Join-Path $upgrade $name)).Hash -eq $settings[$name]) "upgrade preserves $name"}
$backup=@(Get-ChildItem -LiteralPath ($upgrade+'-backups') -Directory | Sort-Object Name)[-1].FullName
Check ((Get-FileHash -LiteralPath (Join-Path $backup 'MyRevitAddin.dll')).Hash -eq $oldDllHash) 'backup retains previous assembly bytes'
foreach($name in @('access-settings.json','update-settings.json')){Check ((Get-FileHash -LiteralPath (Join-Path $backup $name)).Hash -eq $settings[$name]) "backup retains previous $name"}
$duplicate=Join-Path $root 'duplicate';New-Item -ItemType Directory -Path $duplicate | Out-Null
Copy-Item -LiteralPath (Join-Path $OldPayload 'MyRevitAddin.addin') -Destination (Join-Path $duplicate 'OtherDashboard.addin')
RunInstaller $duplicate 'duplicate-registration' $false
Check (!(Test-Path -LiteralPath (Join-Path $duplicate 'MyRevitAddin.dll'))) 'duplicate IDs rejection leaves binaries untouched'
RunInstaller (Join-Path $root 'directory-escape\..\..\..\not-authorized') 'directory-escape' $false

foreach($target in @($fresh,$upgrade)){
    $uninstaller=Join-Path $target 'unins000.exe';$log=Join-Path $evidence ((Split-Path $target -Leaf)+'-uninstall.log')
    $process=Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$log`"" -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(60000)){throw "Uninstall timed out: $target"};$process.Refresh()
    Check ($process.ExitCode -eq 0) "uninstall exit zero $(Split-Path $target -Leaf)"
    Check (!(Test-Path -LiteralPath (Join-Path $target 'MyRevitAddin.addin'))) "uninstall removes registration $(Split-Path $target -Leaf)"
    Check (!(Test-Path -LiteralPath (Join-Path $target 'MyRevitAddin.dll'))) "uninstall removes assembly $(Split-Path $target -Leaf)"
}
foreach($name in $settings.Keys){Check ((Get-FileHash -LiteralPath (Join-Path $upgrade $name)).Hash -eq $settings[$name]) "uninstall preserves $name"}
Check ((Get-FileHash -LiteralPath (Join-Path $backup 'MyRevitAddin.dll')).Hash -eq $oldDllHash) 'backup survives uninstall'
# Exercise file restoration only inside the disposable fixture, not the user's add-in directories.
Get-ChildItem -LiteralPath $backup -File | Where-Object Name -notin 'RESTORE.txt','SHA256SUMS' | Copy-Item -Destination $upgrade
Check ((Get-FileHash -LiteralPath (Join-Path $upgrade 'MyRevitAddin.dll')).Hash -eq $oldDllHash) 'isolated prior-file restoration reproduces original assembly hash'
Check ((ProductionFingerprint) -eq $before) 'production add-in directories and uninstall registration unchanged'
@{root=$root;backup=$backup;checks=$checks.Count;installer=$Installer;installerSha256=(Get-FileHash -LiteralPath $Installer).Hash.ToLowerInvariant();limits=@('File-layout upgrade fixture, not an actual Revit-host upgrade','Normal Apps registration deliberately suppressed in isolated mode','Revit2024 host acceptance unrun')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'result.json')
$checks | Set-Content -LiteralPath (Join-Path $evidence 'checks.txt')
Write-Output "$($checks.Count) isolated installer checks passed; fixtures retained at $root"
