[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){throw 'Installer acceptance is restricted to a disposable Windows Actions runner.'}
if(Get-Process Revit,'Doka.EngineeringDashboard' -ErrorAction SilentlyContinue){throw 'A Revit or Dashboard process already exists. This test will not close user processes.'}
$installerPath=(Resolve-Path -LiteralPath $Installer).Path
$build=(Resolve-Path -LiteralPath $BuildDirectory).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
$repo=Split-Path $PSScriptRoot -Parent
$prefix=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Temp/DokaDashboardInstallerTests'))
# Keep the disposable fixture beneath legacy net48 MAX_PATH while retaining
# the real safety prefix and the separate path-with-spaces installation case.
$root=Join-Path $prefix ([guid]::NewGuid().ToString('N').Substring(0,12))
if(!$root.StartsWith($prefix+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe isolated installer test root.'}
New-Item -ItemType Directory -Path $root,$evidence -Force | Out-Null
$script:checks=[Collections.Generic.List[string]]::new()
$script:beforeProduction=$null
function Check([bool]$Condition,[string]$Label){if(!$Condition){throw "FAIL: $Label"};$script:checks.Add("PASS $Label");Write-Host "PASS $Label"}
function Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Quote([string]$Value){if($Value.Contains('"')){throw 'Unexpected quotation mark in an installer test path.'};return '"'+$Value+'"'}
function Write-Text([string]$Path,[string]$Text){New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Path)) -Force | Out-Null;[IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false))}
function Paths([string]$Case){return @{Case=$Case;App=(Join-Path $Case 'roaming/Autodesk/Revit/Addins/2024');Runtime=(Join-Path $Case 'local/DokaRevitAutomation/Dashboard');Backups=(Join-Path $Case 'local/DokaRevitAutomation/UpgradeBackups');Machine=(Join-Path $Case 'machine/Autodesk/Revit/Addins/2024')}}
function Version-Number([string]$Value){$v=[version]$Value;return [version]::new($v.Major,$v.Minor,$v.Build,[Math]::Max(0,$v.Revision))}
function Tree-Fingerprint([string]$Directory){
    if(!(Test-Path -LiteralPath $Directory)){return 'ABSENT'}
    $rows=[Collections.Generic.List[string]]::new()
    foreach($item in Get-ChildItem -LiteralPath $Directory -Recurse -Force | Sort-Object FullName){
        $relative=$item.FullName.Substring($Directory.TrimEnd('\','/').Length+1).Replace('\','/')
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){$rows.Add("LINK $relative");continue}
        if($item.PSIsContainer){$rows.Add("DIR $relative")}else{$rows.Add("FILE $relative $(Hash $item.FullName)")}
    }
    return $rows -join "`n"
}
function Case-Fingerprint([string]$Case){
    if(!(Test-Path -LiteralPath $Case)){return 'ABSENT'}
    $rows=[Collections.Generic.List[string]]::new()
    foreach($item in Get-ChildItem -LiteralPath $Case -Recurse -Force | Sort-Object FullName){
        $relative=$item.FullName.Substring($Case.TrimEnd('\','/').Length+1).Replace('\','/')
        if($relative -match '^local/DokaRevitAutomation/InstallerLogs(?:/|$)'){continue}
        if($item.PSIsContainer -and $relative -in @('local','local/DokaRevitAutomation')){continue}
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){$rows.Add("LINK $relative");continue}
        if($item.PSIsContainer){$rows.Add("DIR $relative")}else{$rows.Add("FILE $relative $(Hash $item.FullName)")}
    }
    return $rows -join "`n"
}
function Production-Fingerprint {
    $rows=[Collections.Generic.List[string]]::new()
    foreach($directory in @((Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024'),(Join-Path $env:PROGRAMDATA 'Autodesk/Revit/Addins/2024'),(Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/Dashboard'),(Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/UpgradeBackups'))){$rows.Add("$directory`n$(Tree-Fingerprint $directory)")}
    $session=Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/access-session.bin'
    $rows.Add("access-session.bin=$(if(Test-Path -LiteralPath $session){Hash $session}else{'ABSENT'})")
    foreach($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)){
        foreach($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)){
            $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
            try{
                $uninstall=$base.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall',$false)
                if(!$uninstall){continue}
                try{
                    foreach($name in $uninstall.GetSubKeyNames() | Sort-Object){
                        $key=$uninstall.OpenSubKey($name,$false)
                        try{
                            if(!$key){continue}
                            $display=[string]$key.GetValue('DisplayName')
                            if($name -notmatch 'Doka|Dashboard' -and $display -notmatch 'Doka'){continue}
                            $rows.Add("REG $hive $view $name")
                            foreach($value in $key.GetValueNames() | Sort-Object){$data=$key.GetValue($value);$rows.Add("$value $($key.GetValueKind($value)) $($data | ConvertTo-Json -Compress -Depth 8)")}
                        }finally{if($key){$key.Dispose()}}
                    }
                }finally{$uninstall.Dispose()}
            }finally{$base.Dispose()}
        }
    }
    return $rows -join "`n"
}
function Invoke-Inno([string]$Executable,[string]$Arguments,[string]$Label){
    Write-Host "Running actual Inno EXE: $Label"
    $process=Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try{
        $clock=[Diagnostics.Stopwatch]::StartNew()
        while(!$process.WaitForExit(1000)){if($clock.Elapsed.TotalSeconds -gt 180){throw "Owned installer did not complete within 180 seconds: $Label; PID $($process.Id)"}}
        $process.Refresh();return $process.ExitCode
    }finally{$process.Dispose()}
}
function Install([string]$Case,[string]$Label,[bool]$Success=$true,[string]$Extra=''){
    $log=Join-Path $evidence "$Label.log"
    $arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /ISOLATEDROOT='+(Quote $Case)+' /LOG='+(Quote $log)+' '+$Extra
    $code=Invoke-Inno $installerPath $arguments $Label
    $internal=Join-Path $Case 'local/DokaRevitAutomation/InstallerLogs'
    if(Test-Path -LiteralPath $internal){$copy=Join-Path $evidence ($Label+'-migration');New-Item -ItemType Directory -Path $copy -Force | Out-Null;Get-ChildItem -LiteralPath $internal -File | Copy-Item -Destination $copy -Force}
    Check $(if($Success){$code -eq 0}else{$code -ne 0}) "$Label actual installer exit code ($code)"
    return $log
}
function Uninstall([string]$Case,[string]$Label,[bool]$Success=$true){
    $app=(Paths $Case).App;$uninstaller=Join-Path $app 'unins000.exe'
    Check (Test-Path -LiteralPath $uninstaller -PathType Leaf) "$Label has actual installed uninstaller"
    $arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG='+(Quote (Join-Path $evidence "$Label.log"))
    # No ISOLATEDROOT argument: the uninstaller must use its persisted guarded marker.
    $code=Invoke-Inno $uninstaller $arguments $Label
    Check $(if($Success){$code -eq 0}else{$code -ne 0}) "$Label actual uninstall exit code ($code)"
}
function Reject-Install([string]$Case,[string]$Label,[string]$Reason,[string]$Extra=''){
    New-Item -ItemType Directory -Path $Case -Force | Out-Null
    $before=Case-Fingerprint $Case
    $log=Install $Case $Label $false $Extra
    Check ((Case-Fingerprint $Case) -ceq $before) "$Label leaves all application/runtime/settings/backup state unchanged"
    $text=[IO.File]::ReadAllText($log)
    $logs=Join-Path $Case 'local/DokaRevitAutomation/InstallerLogs'
    if(Test-Path -LiteralPath $logs){foreach($file in Get-ChildItem -LiteralPath $logs -File){$text+="`n"+[IO.File]::ReadAllText($file.FullName)}}
    Check ($text -match $Reason) "$Label records the expected rejection reason"
}
function Seed-Legacy([string]$Case){
    $paths=Paths $Case;New-Item -ItemType Directory -Path $paths.App -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $build 'payload/MyRevitAddin.dll') -Destination $paths.App
    Copy-Item -LiteralPath (Join-Path $repo 'MyRevitAddin.addin') -Destination $paths.App
    Copy-Item -LiteralPath (Join-Path $build 'bootstrap/Doka.RevitBridge.dll') -Destination $paths.App
    Copy-Item -LiteralPath (Join-Path $build 'bootstrap/Doka.RevitUpdater.exe') -Destination $paths.App
    Write-Text (Join-Path $paths.App 'access-settings.json') '{"schemaVersion":1,"isolatedAccessSetting":"preserve exactly"}'
    Write-Text (Join-Path $paths.App 'update-settings.json') '{"schemaVersion":1,"sourceDirectory":"C:\\ISOLATED-NOT-A-COMPANY-SHARE","preserve":"yes"}'
    Write-Text (Join-Path $paths.App 'saved-project.top50') 'independent isolated saved project sentinel'
    Write-Text (Join-Path $paths.App 'Unrelated.dll') 'independent unrelated add-in sentinel'
    return $paths
}
function Assert-Installed([string]$Case,[string]$Label){
    $paths=Paths $Case
    [xml]$registration=Get-Content -LiteralPath (Join-Path $paths.App 'MyRevitAddin.addin') -Raw
    $entries=@($registration.RevitAddIns.AddIn)
    Check ($entries.Count -eq 3) "$Label retains exactly three original registrations"
    $expected=@{'a1b2c3d4-e5f6-4a5b-6c7d-8e9f0a1b2c3d'='MyRevitAddin.App';'2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e'='EngineeringDashboard.Command';'c6dedce1-4571-4b7c-b781-1f338826be41'='MyRevitAddin.Updates.UpdateCommand'}
    foreach($entry in $entries){$id=([string]$entry.ClientId).ToLowerInvariant();Check ($expected.ContainsKey($id) -and [string]$entry.FullClassName -ceq $expected[$id] -and [string]$entry.Assembly -ceq 'Doka.RevitBridge.dll') "$Label registers original GUID $id against only the stable bridge"}
    foreach($name in @('Doka.RevitBridge.dll','Doka.RevitUpdater.exe')){Check ((Hash (Join-Path $paths.App $name)) -ceq (Hash (Join-Path $build ('bootstrap/'+$name)))) "$Label installed $name bytes match verified build"}
    Check ((Version-Number ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $paths.App 'Doka.RevitBridge.dll')).FileVersion)) -eq (Version-Number $feed.version)) "$Label bridge version matches the initial Dashboard"
    $active=Get-Content -LiteralPath (Join-Path $paths.Runtime 'active.json') -Raw | ConvertFrom-Json
    $runtimeFolder=[IO.Path]::GetFullPath((Join-Path $paths.Runtime $active.RelativeDirectory))
    Check ($runtimeFolder.StartsWith([IO.Path]::GetFullPath((Join-Path $paths.Runtime 'versions'))+'\',[StringComparison]::OrdinalIgnoreCase)) "$Label selects an immutable runtime under approved isolation"
    Check ((Version-Number $active.Version) -eq (Version-Number $feed.version) -and $active.ArchiveSha256 -ceq $feed.sha256) "$Label active version/archive match the supplied feed"
    $metadata=Get-Content -LiteralPath (Join-Path $runtimeFolder 'dashboard-package.json') -Raw | ConvertFrom-Json
    $allExact=$true
    foreach($file in $metadata.files){$installed=Join-Path $runtimeFolder $file.path;if(!(Test-Path -LiteralPath $installed) -or (Get-Item -LiteralPath $installed).Length -ne $file.sizeBytes -or (Hash $installed) -cne $file.sha256){$allExact=$false;break}}
    Check $allExact "$Label complete installed runtime matches every declared build hash/size"
    Check ((Hash (Join-Path $runtimeFolder 'Doka.EngineeringDashboard.exe')) -ceq (Hash (Join-Path $build 'payload/Doka.EngineeringDashboard.exe'))) "$Label actual external entry matches the build"
    foreach($name in @('BeamSolver/Doka.BeamSolver.exe','google-ortools-native.dll','ortools.dll','WebView2Loader.dll')){Check (Test-Path -LiteralPath (Join-Path $runtimeFolder $name) -PathType Leaf) "$Label has required worker/native dependency $name"}
    Check ((Hash (Join-Path $runtimeFolder 'BeamSolver.SourceHashes')) -ceq (Hash (Join-Path $runtimeFolder 'BeamSolver/BeamSolver.SourceHashes'))) "$Label desktop and self-contained worker source identities match"
    Check (!(Test-Path (Join-Path $runtimeFolder 'RevitAPI.dll')) -and !(Test-Path (Join-Path $runtimeFolder 'RevitAPIUI.dll'))) "$Label does not distribute Revit host assemblies"
    Check (([IO.File]::ReadAllText((Join-Path $paths.App '.doka-isolated-install'))).Trim() -ieq $Case) "$Label persists exact guarded isolated uninstall root"
    return @{Paths=$paths;Active=$active;Folder=$runtimeFolder;Entry=(Join-Path $runtimeFolder 'Doka.EngineeringDashboard.exe')}
}
function Preserve([string]$Case){
    $paths=Paths $Case
    Write-Text (Join-Path $paths.App 'saved-user.dokacalc') 'independent saved calculator project'
    Write-Text (Join-Path $paths.Runtime 'Sessions/0123456789abcdef0123456789abcdef.json') '{"independentRecovery":"preserve"}'
    Write-Text (Join-Path $paths.Backups 'independent-existing-backup/sentinel.txt') 'independent existing backup'
    return @{Runtime=(Tree-Fingerprint $paths.Runtime);Backups=(Tree-Fingerprint $paths.Backups);Files=@{'access-settings.json'=(Hash (Join-Path $paths.App 'access-settings.json'));'update-settings.json'=(Hash (Join-Path $paths.App 'update-settings.json'));'saved-user.dokacalc'=(Hash (Join-Path $paths.App 'saved-user.dokacalc'))}}
}
function Assert-Uninstalled([string]$Case,[hashtable]$Saved,[string]$Label){
    $paths=Paths $Case
    foreach($name in @('Doka.RevitBridge.dll','Doka.RevitUpdater.exe','MyRevitAddin.addin','.doka-isolated-install')){Check (!(Test-Path -LiteralPath (Join-Path $paths.App $name))) "$Label removes only known installed item $name"}
    foreach($name in $Saved.Files.Keys){Check ((Hash (Join-Path $paths.App $name)) -ceq $Saved.Files[$name]) "$Label preserves $name exactly"}
    Check ((Tree-Fingerprint $paths.Runtime) -ceq $Saved.Runtime) "$Label preserves all runtime, working projects and recovery checkpoints"
    Check ((Tree-Fingerprint $paths.Backups) -ceq $Saved.Backups) "$Label preserves all upgrade backups"
}

$beforeProduction=Production-Fingerprint;$script:beforeProduction=$beforeProduction
Write-Text (Join-Path $evidence 'production-before.txt') $beforeProduction
try{
    foreach($scriptName in @('Invoke-DashboardInstaller.ps1','Build-DashboardInstaller.ps1','Initialize-DashboardLiveUpdate.ps1')){$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $scriptName),[ref]$tokens,[ref]$errors) | Out-Null;Check ($errors.Count -eq 0) "$scriptName parses"}
    $feed=Get-Content -LiteralPath (Join-Path $build 'feed/dashboard-latest.json') -Raw | ConvertFrom-Json
    Check ((Version-Number ([Diagnostics.FileVersionInfo]::GetVersionInfo($installerPath).FileVersion)) -eq (Version-Number $feed.version)) 'actual installer version matches verified Dashboard feed'
    $fresh=Join-Path $root 'fresh with spaces';Install $fresh 'fresh' | Out-Null;$freshInstalled=Assert-Installed $fresh 'fresh'
    Check ((Production-Fingerprint) -ceq $beforeProduction) 'fresh isolated installation suppresses production uninstall registration and leaves real user directories unchanged'
    foreach($name in @('access-settings.json','update-settings.json')){Check ((Hash (Join-Path $freshInstalled.Paths.App $name)) -ceq (Hash (Join-Path $build ('bootstrap/'+$name)))) "fresh installs original default $name"}
    $freshSaved=Preserve $fresh
    $upgrade=Join-Path $root 'upgrade';$upgradePaths=Seed-Legacy $upgrade
    $retained=@{};foreach($name in @('MyRevitAddin.dll','access-settings.json','update-settings.json','saved-project.top50','Unrelated.dll')){$retained[$name]=Hash (Join-Path $upgradePaths.App $name)}
    $changed=@{};foreach($name in @('MyRevitAddin.addin','Doka.RevitBridge.dll','Doka.RevitUpdater.exe')){$changed[$name]=Hash (Join-Path $upgradePaths.App $name)}
    Install $upgrade 'upgrade' | Out-Null;$upgradeInstalled=Assert-Installed $upgrade 'upgrade'
    foreach($name in $retained.Keys){Check ((Hash (Join-Path $upgradePaths.App $name)) -ceq $retained[$name]) "upgrade preserves existing $name"}
    $backup=@(Get-ChildItem -LiteralPath $upgradePaths.Backups -Directory | Where-Object {Test-Path (Join-Path $_.FullName 'MyRevitAddin.addin')})
    Check ($backup.Count -eq 1) 'upgrade creates one backup containing the original registration'
    foreach($name in $changed.Keys){Check ((Hash (Join-Path $backup[0].FullName $name)) -ceq $changed[$name]) "upgrade backup preserves original changed file $name"}
    $generation=$upgradeInstalled.Active.Generation;$runtimeBefore=Tree-Fingerprint $upgradePaths.Runtime
    Install $upgrade 'repeat' | Out-Null
    $repeat=Get-Content -LiteralPath (Join-Path $upgradePaths.Runtime 'active.json') -Raw | ConvertFrom-Json
    Check ($repeat.Generation -ceq $generation -and (Tree-Fingerprint $upgradePaths.Runtime) -ceq $runtimeBefore) 'repeating the actual installer preserves active generation and complete runtime bytes'
    foreach($name in $retained.Keys){Check ((Hash (Join-Path $upgradePaths.App $name)) -ceq $retained[$name]) "repeated install preserves $name"}
    [xml]$again=Get-Content (Join-Path $upgradePaths.App 'MyRevitAddin.addin') -Raw;Check (@($again.RevitAddIns.AddIn).Count -eq 3) 'repeat creates no duplicate registrations'
    $upgradeSaved=Preserve $upgrade
    foreach($name in @('MyRevitAddin.dll','saved-project.top50','Unrelated.dll')){$upgradeSaved.Files[$name]=$retained[$name]}

    $custom=Join-Path $root 'custom';$customPaths=Seed-Legacy $custom
    [xml]$xml=Get-Content (Join-Path $customPaths.App 'MyRevitAddin.addin') -Raw
    $xml.RevitAddIns.AddIn[0].Assembly=Join-Path $root 'external-custom/MyRevitAddin.dll';$xml.Save((Join-Path $customPaths.App 'MyRevitAddin.addin'))
    Reject-Install $custom 'custom-registration' 'outside the supported|custom assembly'
    foreach($scope in @('user','machine')){
        $foreign=Join-Path $root "foreign-$scope";$foreignPaths=Seed-Legacy $foreign
        $folder=if($scope -eq 'machine'){$foreignPaths.Machine}else{$foreignPaths.App}
        New-Item -ItemType Directory -Force $folder | Out-Null
        Copy-Item (Join-Path $repo 'MyRevitAddin.addin') (Join-Path $folder 'foreign.addin')
        Reject-Install $foreign "foreign-$scope-registration" 'Another add-in registration|different command ID|owns a Dashboard ID'
    }
    $escape=Join-Path $root 'directory-override';$outside=Join-Path $root 'unapproved-directory';$outsideBefore=Tree-Fingerprint $outside
    Reject-Install $escape 'directory-override' 'Directory overrides|supported.*location' ('/DIR='+(Quote $outside))
    Check ((Tree-Fingerprint $outside) -ceq $outsideBefore) 'refused DIR override writes no unapproved target'
    $junction=Join-Path $root 'junction-case';$junctionTarget=Join-Path $root 'junction-target'
    Write-Text (Join-Path $junctionTarget 'external-sentinel.txt') 'disposable junction destination must remain unchanged'
    $junctionBefore=Tree-Fingerprint $junctionTarget
    New-Item -ItemType Junction -Path $junction -Target $junctionTarget | Out-Null
    try{
        $junctionLog=Install $junction 'junction-isolation' $false
        Check ((Tree-Fingerprint $junctionTarget) -ceq $junctionBefore) 'junction isolation rejection preserves the separate disposable target'
        Check (!(Test-Path (Join-Path $junctionTarget 'local/DokaRevitAutomation/InstallerLogs'))) 'junction root is rejected before migration log creation'
        Check (([IO.File]::ReadAllText($junctionLog)) -match 'Isolated|link|junction') 'junction isolation rejection is explained in the actual installer log'
    }finally{[IO.Directory]::Delete($junction)}
    foreach($component in @('local','roaming')){
        $derivedCase=Join-Path $root ($component+'-junction-case');$derivedTarget=Join-Path $root ($component+'-junction-target')
        New-Item -ItemType Directory -Path $derivedCase -Force | Out-Null
        Write-Text (Join-Path $derivedTarget 'external-sentinel.txt') 'derived folder junction must not receive installer writes'
        $targetBefore=Tree-Fingerprint $derivedTarget;$link=Join-Path $derivedCase $component
        New-Item -ItemType Junction -Path $link -Target $derivedTarget | Out-Null
        try{
            $derivedLog=Install $derivedCase ($component+'-junction-isolation') $false
            Check ((Tree-Fingerprint $derivedTarget) -ceq $targetBefore) "$component derived junction rejection preserves separate disposable target"
            Check (!(Test-Path (Join-Path $derivedCase 'local/DokaRevitAutomation/InstallerLogs'))) "$component derived junction is rejected before installer log creation"
            Check (([IO.File]::ReadAllText($derivedLog)) -match 'links|junction|Isolated') "$component derived junction rejection records its reason"
        }finally{[IO.Directory]::Delete($link)}
    }
    $newer=Join-Path $root 'newer-runtime';$newerPaths=Paths $newer
    Write-Text (Join-Path $newerPaths.Runtime 'active.json') (@{Version='99.0.0';ArchiveSha256=('a'*64);BridgeProtocol=1;Generation=[guid]::NewGuid().ToString('N');PackageId=('99.0.0-'+('a'*12));RelativeDirectory=('versions/99.0.0-'+('a'*12))} | ConvertTo-Json)
    Reject-Install $newer 'newer-runtime' 'newer Dashboard|not downgrade'
    $conflict=Join-Path $root 'same-version-conflict';$conflictPaths=Paths $conflict
    Write-Text (Join-Path $conflictPaths.Runtime 'active.json') (@{Version=$feed.version;ArchiveSha256=('b'*64);BridgeProtocol=1;Generation=[guid]::NewGuid().ToString('N');PackageId=($feed.version+'-'+('b'*12));RelativeDirectory=('versions/'+$feed.version+'-'+('b'*12))} | ConvertTo-Json)
    Reject-Install $conflict 'same-version-conflict' 'same Dashboard version has different contents'

    $freshManifest=Join-Path $freshInstalled.Paths.App 'MyRevitAddin.addin';$originalManifest=[IO.File]::ReadAllBytes($freshManifest)
    try{
        [IO.File]::AppendAllText($freshManifest,"`r`n<!-- independent changed-registration fixture -->")
        $before=Case-Fingerprint $fresh;Uninstall $fresh 'changed-registration-uninstall' $false
        Check ((Case-Fingerprint $fresh) -ceq $before) 'uninstaller refuses a changed registration without deleting user-modified files'
    }finally{[IO.File]::WriteAllBytes($freshManifest,$originalManifest)}
    $freshBridge=Join-Path $freshInstalled.Paths.App 'Doka.RevitBridge.dll';$originalBridge=[IO.File]::ReadAllBytes($freshBridge)
    try{
        [IO.File]::AppendAllText($freshBridge,'independent changed bridge fixture')
        $before=Case-Fingerprint $fresh;Uninstall $fresh 'changed-bridge-uninstall' $false
        Check ((Case-Fingerprint $fresh) -ceq $before) 'uninstaller refuses a changed bridge without deleting current integration files'
    }finally{[IO.File]::WriteAllBytes($freshBridge,$originalBridge)}

    $fakeDirectory=Join-Path $root 'cooperative-host';New-Item -ItemType Directory -Path $fakeDirectory | Out-Null
    $source=Join-Path $fakeDirectory 'FakeHost.cs'
    Write-Text $source 'using System; using System.IO; using System.Threading; internal static class Program { static int Main(string[] args) { File.WriteAllText(args[1],"ready"); var end=DateTime.UtcNow.AddMinutes(5); while(!File.Exists(args[0]) && DateTime.UtcNow<end) Thread.Sleep(25); return File.Exists(args[0]) ? 0 : 1; } }'
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    foreach($name in @('Revit','Doka.EngineeringDashboard')){
        $fakeExe=Join-Path $fakeDirectory ($name+'.exe');& $compiler /nologo /target:winexe "/out:$fakeExe" $source
        Check ($LASTEXITCODE -eq 0) "$name cooperative fake host compiles"
        $release=Join-Path $fakeDirectory ($name+'.release');$ready=Join-Path $fakeDirectory ($name+'.ready')
        $hostFixture=Start-Process -FilePath $fakeExe -ArgumentList ((Quote $release)+' '+(Quote $ready)) -PassThru
        try{
            $watch=[Diagnostics.Stopwatch]::StartNew();while(!(Test-Path $ready)){if($hostFixture.HasExited -or $watch.Elapsed.TotalSeconds -gt 15){throw 'Cooperative fake host failed to become ready.'};Start-Sleep -Milliseconds 25}
            $blocked=Join-Path $root ('blocked-'+$name);Reject-Install $blocked ('live-'+$name) 'close every Revit|Revit.*closed|Dashboard.*closed'
            $before=Case-Fingerprint $fresh;Uninstall $fresh ('live-uninstall-'+$name) $false
            Check ((Case-Fingerprint $fresh) -ceq $before) "$name live guard blocks uninstall without altering installed files"
        }
        finally{
            Write-Text $release 'cooperative release'
            if(!$hostFixture.WaitForExit(15000)){throw "Owned fake host did not exit cooperatively: $name"}
            Check ($hostFixture.ExitCode -eq 0) "$name fake host exits cooperatively; no user process is killed"
            $hostFixture.Dispose()
        }
    }
    Uninstall $fresh 'fresh-uninstall';Assert-Uninstalled $fresh $freshSaved 'fresh uninstall'
    Uninstall $upgrade 'upgrade-uninstall';Assert-Uninstalled $upgrade $upgradeSaved 'upgrade uninstall'
    Check (Test-Path -LiteralPath $freshInstalled.Entry -PathType Leaf) 'actual fresh runtime entry remains available for separate installed WPF smoke'
    $afterProduction=Production-Fingerprint;Write-Text (Join-Path $evidence 'production-after.txt') $afterProduction
    Check ($afterProduction -ceq $beforeProduction) 'production add-in/runtime/backup directories, auth session and uninstall registry unchanged'
    $result=@{checks=$script:checks.Count;root=$root;freshRoot=$fresh;upgradeRoot=$upgrade;freshEntry=$freshInstalled.Entry;upgradeEntry=$upgradeInstalled.Entry;installer=$installerPath;installerSha256=(Hash $installerPath);version=$feed.version;productionUnchanged=$true;companyPublished=$false;actualRevitHostTested=$false;limitations=@('Synthetic higher-version active metadata exercises installer downgrade preflight.','Windows Actions isolated test installation; no company-user or actual Revit host installation.')}
    Write-Text (Join-Path $evidence 'result.json') ($result | ConvertTo-Json -Depth 6)
    Write-Text (Join-Path $evidence 'checks.txt') ($script:checks -join "`r`n")
    Write-Host "$($script:checks.Count) actual isolated installer/uninstaller checks passed. Fixtures retained at $root"
}
catch{
    Write-Text (Join-Path $evidence 'failure.txt') ($_ | Out-String)
    $script:checks | Set-Content -LiteralPath (Join-Path $evidence 'checks.txt') -Encoding utf8
    throw
}
finally{
    if($script:beforeProduction){$final=Production-Fingerprint;Write-Text (Join-Path $evidence 'production-final.txt') $final;if($final -cne $script:beforeProduction){throw 'Installer test changed production directories or uninstall registration.'}}
}
