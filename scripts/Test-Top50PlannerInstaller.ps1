[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$OldPayload,[Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$Installer=(Resolve-Path -LiteralPath $Installer).Path
$OldPayload=(Resolve-Path -LiteralPath $OldPayload).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $evidence | Out-Null
$root=Join-Path $env:LOCALAPPDATA ('Temp\DokaTop50InstallerTests\'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$expected=[Diagnostics.FileVersionInfo]::GetVersionInfo($Installer).FileVersion.Trim()
$checks=0
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw "FAIL $Message"};$script:checks++;Write-Output "PASS $Message"}
function Install([string]$Target,[string]$Name){
    $log=Join-Path $evidence "$Name.log"
    $p=Start-Process -FilePath $Installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ISOLATEDROOT=`"$Target`" /DIR=`"$Target`" /LOG=`"$log`"" -PassThru
    if(!$p.WaitForExit(60000)){throw "Installer timed out: $Name"};$p.Refresh();Check ($p.ExitCode -eq 0) "$Name installation"
}
$fresh=Join-Path $root 'fresh';Install $fresh 'fresh'
Check (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $fresh 'MyRevitAddin.dll')).FileVersion.Trim()) -eq $expected) 'installed version matches installer'
foreach($dll in Get-ChildItem (Join-Path $repo 'src/bin/Release/net48') -Filter '*.dll' -File){Check ((Get-FileHash $dll.FullName).Hash -eq (Get-FileHash (Join-Path $fresh $dll.Name)).Hash) "installed payload $($dll.Name)"}
Check (!(Test-Path (Join-Path $fresh 'RevitAPI.dll'))) 'host Revit API is not distributed'
[xml]$manifest=Get-Content -LiteralPath (Join-Path $fresh 'MyRevitAddin.addin') -Raw
Check (@($manifest.RevitAddIns.AddIn).Count -eq 3) 'three original add-in registrations retained'
Check (@($manifest.RevitAddIns.AddIn | Where-Object Assembly -ne (Join-Path $fresh 'MyRevitAddin.dll')).Count -eq 0) 'all registration paths resolve to installed assembly'
# Exercise the installed DLL and native libraries through probing only, without generated binding redirects.
$smoke=Join-Path $repo 'tests/Top50.PlannerSmoke/bin/Release/net48/Top50.PlannerSmoke.exe'
Copy-Item $smoke (Join-Path $root 'Top50.PlannerSmoke.exe')
'<configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><probing privatePath="fresh"/></assemblyBinding></runtime></configuration>' | Set-Content (Join-Path $root 'Top50.PlannerSmoke.exe.config')
& (Join-Path $root 'Top50.PlannerSmoke.exe') (Join-Path $evidence 'installed-planner') *> (Join-Path $evidence 'installed-planner.log')
Check ($LASTEXITCODE -eq 0) 'actual installed planner solves, diagnoses and renders with Excel export disabled and without binding redirects'

$steelSmoke=Join-Path $repo 'tests/SteelShape.HostSmoke/bin/Release/net48/SteelShape.HostSmoke.exe'
if(Test-Path $steelSmoke){
    Copy-Item $steelSmoke (Join-Path $fresh 'SteelShape.HostSmoke.exe')
    & (Join-Path $fresh 'SteelShape.HostSmoke.exe') (Join-Path $evidence 'installed-steel-shape') *> (Join-Path $evidence 'installed-steel-shape.log')
    Check ($LASTEXITCODE -eq 0) 'actual installed Steel Shape opens, calculates, selects weld edges and recovers browser failure without binding redirects'
}

$upgrade=Join-Path $root 'upgrade';New-Item -ItemType Directory -Path $upgrade | Out-Null
Get-ChildItem $OldPayload -File | Copy-Item -Destination $upgrade
Copy-Item (Join-Path $repo 'MyRevitAddin.addin') (Join-Path $upgrade 'MyRevitAddin.addin')
$oldHash=(Get-FileHash (Join-Path $upgrade 'MyRevitAddin.dll')).Hash
Check (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $upgrade 'MyRevitAddin.dll')).FileVersion.Trim()) -eq '1.1.15.0') 'upgrade fixture retains original beam version'
$preserved=@{}
foreach($name in @('access-settings.json','update-settings.json','saved-project.top50','Unrelated.dll')){
    'Disposable preservation sentinel' | Set-Content (Join-Path $upgrade $name)
    $preserved[$name]=(Get-FileHash (Join-Path $upgrade $name)).Hash
}
Install $upgrade 'upgrade'
foreach($name in $preserved.Keys){Check ((Get-FileHash (Join-Path $upgrade $name)).Hash -eq $preserved[$name]) "upgrade preserves $name"}
$backup=(Get-ChildItem ($upgrade+'-backups') -Directory | Sort-Object Name | Select-Object -Last 1).FullName
Check ((Get-FileHash (Join-Path $backup 'MyRevitAddin.dll')).Hash -eq $oldHash) 'backup preserves original beam assembly'
foreach($name in @('access-settings.json','update-settings.json')){Check ((Get-FileHash (Join-Path $backup $name)).Hash -eq $preserved[$name]) "backup preserves $name"}
Check (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $upgrade 'MyRevitAddin.dll')).FileVersion.Trim()) -eq $expected) 'upgrade installs current version'
foreach($target in @($fresh,$upgrade)){
    $p=Start-Process (Join-Path $target 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru
    if(!$p.WaitForExit(60000)){throw 'Uninstall timed out'};$p.Refresh();Check ($p.ExitCode -eq 0) "isolated uninstall $target"
}
foreach($name in $preserved.Keys){Check ((Get-FileHash (Join-Path $upgrade $name)).Hash -eq $preserved[$name]) "uninstall preserves $name"}
Check ((Get-FileHash (Join-Path $backup 'MyRevitAddin.dll')).Hash -eq $oldHash) 'upgrade backup survives uninstall'
"$checks isolated installed-payload/backup checks passed" | Set-Content (Join-Path $evidence 'installer-checks.txt')
Write-Output "$checks isolated installed-payload/backup checks passed"
