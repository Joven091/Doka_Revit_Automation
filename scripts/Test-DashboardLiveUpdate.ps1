[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows'){throw 'Migration testing is restricted to a disposable Windows Actions runner.'}
$build=[IO.Path]::GetFullPath($BuildDirectory);$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $evidence | Out-Null
$script:checks=0
function Check([bool]$condition,[string]$message){if(!$condition){throw $message};$script:checks++}
function Reject([scriptblock]$action,[string]$message){$failed=$false;try{& $action | Out-Null}catch{$failed=$_.Exception.Message -like "*$message*"};Check $failed "Expected rejection: $message"}
foreach($name in @('Build-DashboardLiveUpdate.ps1','Initialize-DashboardLiveUpdate.ps1','Publish-DashboardUpdate.ps1')){
    $tokens=$null;$errors=$null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors) | Out-Null
    Check ($errors.Count -eq 0) "$name parses"
}
$publisher=Join-Path $PSScriptRoot 'Publish-DashboardUpdate.ps1'
$feed=Join-Path $build 'feed';$destination=Join-Path $evidence 'published-locally'
& $publisher -FeedDirectory $feed -Destination $destination
& $publisher -FeedDirectory $feed -Destination $destination
$latest=Join-Path $destination 'dashboard-latest.json';$good=[IO.File]::ReadAllText($latest)
$original=Get-Content (Join-Path $feed 'dashboard-latest.json') -Raw | ConvertFrom-Json
Check ((Get-FileHash (Join-Path $destination $original.archiveFile)).Hash -ieq $original.sha256) 'Published local ZIP matches exact source'
Check (!(Test-Path (Join-Path $destination 'latest.json'))) 'Legacy installer feed is not created or replaced'
$lock=[IO.File]::Open((Join-Path $destination '.dashboard-publish.lock'),'Open','ReadWrite','None')
try{Reject {& $publisher -FeedDirectory $feed -Destination $destination} 'being used';Check ([IO.File]::ReadAllText($latest) -ceq $good) 'A second publisher leaves the old feed unchanged'}finally{$lock.Dispose()}
$changed=$good | ConvertFrom-Json;$changed.version='99.0.0';[IO.File]::WriteAllText($latest,($changed|ConvertTo-Json))
Reject {& $publisher -FeedDirectory $feed -Destination $destination} 'newer Dashboard'
[IO.File]::WriteAllText($latest,$good)
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($publisher,[ref]$tokens,[ref]$errors)
foreach($definition in $ast.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.FunctionDefinitionAst]}){. ([scriptblock]::Create($definition.Extent.Text))}
$replace=${function:Invoke-ManifestReplace};$rename=${function:Invoke-ManifestRename}
function Invoke-ManifestReplace([string]$Source,[string]$Target){throw [UnauthorizedAccessException]::new('Simulated SMB metadata restriction')}
$temp=Join-Path $destination '.independent-feed';[IO.File]::WriteAllText($temp,$good)
$access=[Security.AccessControl.AccessControlSections]::Access;$acl=(Get-Acl $latest).GetSecurityDescriptorSddlForm($access)
Publish-Manifest $temp $latest
Check ([IO.File]::ReadAllText($latest) -ceq $good -and !(Test-Path $temp)) 'SMB Replace denial falls back to complete atomic rename'
Check ((Get-Acl $latest).GetSecurityDescriptorSddlForm($access) -ceq $acl) 'Fallback preserves feed access rules'
function Invoke-ManifestRename([string]$Source,[string]$Target){throw [ComponentModel.Win32Exception]::new(5,'Simulated denied rename')}
[IO.File]::WriteAllText($temp,'never advertise')
Reject {Publish-Manifest $temp $latest} 'Modify permission'
Check ([IO.File]::ReadAllText($latest) -ceq $good) 'Denied replacement never truncates/deletes the old complete feed'
Set-Item Function:Invoke-ManifestReplace -Value $replace;Set-Item Function:Invoke-ManifestRename -Value $rename
Remove-Item $temp
$saved=@{APPDATA=$env:APPDATA;LOCALAPPDATA=$env:LOCALAPPDATA;PROGRAMDATA=$env:PROGRAMDATA}
try{
    $env:APPDATA=Join-Path $evidence 'isolated-roaming';$env:LOCALAPPDATA=Join-Path $evidence 'isolated-local';$env:PROGRAMDATA=Join-Path $evidence 'isolated-machine'
    $install=Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024';New-Item -ItemType Directory -Force $install | Out-Null
    $bootstrap=Join-Path $build 'bootstrap';$migration=Join-Path $bootstrap 'Initialize-DashboardLiveUpdate.ps1'
    $manifest=Join-Path $install 'MyRevitAddin.addin'
    $originalManifest=Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'MyRevitAddin.addin') -Raw
    [IO.File]::WriteAllText($manifest,$originalManifest)
    [IO.File]::WriteAllText((Join-Path $install 'access-settings.json'),'existing-access-settings-keep')
    [IO.File]::WriteAllText((Join-Path $install 'update-settings.json'),'existing-update-settings-keep')
    Copy-Item (Join-Path $build 'payload/MyRevitAddin.dll') $install
    [IO.File]::WriteAllText($manifest,$originalManifest.Replace('MyRevitAddin.dll',(Join-Path $evidence 'external/MyRevitAddin.dll')))
    Reject {& $migration -FeedDirectory $feed} 'outside the supported installation'
    [IO.File]::WriteAllText($manifest,$originalManifest)
    [xml]$extra=$originalManifest;$extra.RevitAddIns.AddIn[0].ClientId=[guid]::NewGuid().ToString();$extra.Save((Join-Path $install 'foreign.addin'))
    Reject {& $migration -FeedDirectory $feed} 'different command ID'
    Remove-Item (Join-Path $install 'foreign.addin')
    & $migration -FeedDirectory $feed
    $root=Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/Dashboard'
    [xml]$registered=Get-Content $manifest -Raw
    Check (@($registered.RevitAddIns.AddIn | Where-Object {$_.Assembly -ceq 'Doka.RevitBridge.dll'}).Count -eq 3) 'Migration preserves all three command entries and loads only the stable bridge'
    Check ((Get-Content (Join-Path $install 'access-settings.json') -Raw) -ceq 'existing-access-settings-keep') 'Access settings retained exactly'
    Check ((Get-Content (Join-Path $install 'update-settings.json') -Raw) -ceq 'existing-update-settings-keep') 'Update source retained exactly'
    Check (Test-Path (Join-Path $install 'MyRevitAddin.dll')) 'Old calculator assembly retained for recovery'
    $active=Get-Content (Join-Path $root 'active.json') -Raw | ConvertFrom-Json
    Check ($active.Version -eq $original.version) 'Verified expected Dashboard runtime active'
    Check (Test-Path (Join-Path $root ($active.RelativeDirectory+'/Doka.EngineeringDashboard.exe'))) 'Actual external entry installed into immutable version folder'
    Check (@(Get-ChildItem (Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/UpgradeBackups') -Recurse -Filter MyRevitAddin.addin).Count -eq 1) 'Prior registration backed up'
    & $migration -FeedDirectory $feed
    Check ((Get-Content (Join-Path $root 'active.json') -Raw | ConvertFrom-Json).Generation -ceq $active.Generation) 'Repeating same verified migration is idempotent'
}
finally{foreach($name in $saved.Keys){Set-Item "Env:$name" $saved[$name]}}
"Passed $script:checks publisher/migration checks on isolated disposable Windows user paths. Actual Revit and company-share acceptance remain unrun." | Tee-Object -FilePath (Join-Path $evidence 'acceptance.txt')
