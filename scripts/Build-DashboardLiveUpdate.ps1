[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[string]$Version='1.2.0')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new output directory; do not rebuild over a reviewed release.'}
New-Item -ItemType Directory -Path $output | Out-Null
$payload=Join-Path $output 'payload'
$bootstrap=Join-Path $output 'bootstrap'
New-Item -ItemType Directory -Path $payload,$bootstrap | Out-Null
foreach($project in @('DashboardDesktop/Doka.DashboardDesktop.csproj','DashboardUpdater/Doka.DashboardUpdater.csproj','RevitBridge/Doka.RevitBridge.csproj','DashboardPackage/Doka.DashboardPackage.csproj')){
    $buildArgs=@('build',(Join-Path $repo "tools/$project"),'-c','Release','--warnaserror',"-p:Version=$Version",'-p:UseSharedCompilation=false')
    if($project -like 'DashboardPackage/*'){$buildArgs+=@('-f','net48')}
    & dotnet @buildArgs
    if($LASTEXITCODE -ne 0){throw "Build failed: $project"}
}
Copy-Item (Join-Path $repo 'tools/DashboardDesktop/bin/Release/net48/*') $payload -Recurse
Copy-Item (Join-Path $repo 'tools/DashboardUpdater/bin/Release/net48/Doka.DashboardUpdater.exe') $payload
if(Test-Path (Join-Path $repo 'tools/DashboardUpdater/bin/Release/net48/Doka.DashboardUpdater.exe.config')){Copy-Item (Join-Path $repo 'tools/DashboardUpdater/bin/Release/net48/Doka.DashboardUpdater.exe.config') $payload}
# The worker is the same existing self-contained .NET10 solver; no engine changes.
& dotnet publish (Join-Path $repo 'tools/BeamSolver.Worker/BeamSolver.Worker.csproj') -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false "-p:Version=$Version" --output (Join-Path $payload 'BeamSolver') --warnaserror
if($LASTEXITCODE -ne 0){throw 'Worker publication failed'}
$source=Join-Path $repo 'src/bin/Release/dashboard/net48'
if((Get-FileHash (Join-Path $source 'BeamSolver.SourceHashes')).Hash -ne (Get-FileHash (Join-Path $payload 'BeamSolver/BeamSolver.SourceHashes')).Hash){throw 'Desktop and worker solver source identities differ'}
Copy-Item (Join-Path $source 'BeamSolver.SourceHashes') $payload
# Only versioned executable payload belongs in the ZIP. Host registration lives outside it.
foreach($name in @('RevitAPI.dll','RevitAPIUI.dll','Doka.RevitBridge.dll')){if(Test-Path (Join-Path $payload $name)){throw "Host-only file in desktop payload: $name"}}
$requested=[version]$Version
$expected=[version]::new($requested.Major,$requested.Minor,$requested.Build,[Math]::Max(0,$requested.Revision))
foreach($name in @('Doka.EngineeringDashboard.exe','MyRevitAddin.dll')){
    if([version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload $name)).FileVersion -ne $expected){throw "Mismatched Dashboard version: $name"}
}
$package=Join-Path $repo 'tools/DashboardPackage/bin/Release/net48/Doka.DashboardPackage.exe'
& $package pack $payload (Join-Path $output 'feed') $Version
if($LASTEXITCODE -ne 0){throw 'Dashboard ZIP verification failed'}
Copy-Item (Join-Path $repo 'tools/RevitBridge/bin/Release/net48/Doka.RevitBridge.dll') $bootstrap
Copy-Item (Join-Path $repo 'tools/RevitUpdater/bin/Release/net48/Doka.RevitUpdater.exe') $bootstrap
Copy-Item $package $bootstrap
Copy-Item (Join-Path $repo 'packaging/dashboard-live-update/MyRevitAddin.addin') $bootstrap
Copy-Item (Join-Path $repo 'scripts/Initialize-DashboardLiveUpdate.ps1') $bootstrap
Copy-Item (Join-Path $repo 'src/access-settings.json') $bootstrap
Copy-Item (Join-Path $repo 'src/update-settings.json') $bootstrap
Copy-Item (Join-Path $repo 'docs/dashboard-live-update/README.md') $output
Copy-Item (Join-Path $repo 'scripts/Publish-DashboardUpdate.ps1') $output
@'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-DashboardUpdate.ps1" -FeedDirectory "%~dp0feed"
if errorlevel 1 goto failed
echo Dashboard update published. The legacy Revit installer feed was not changed.
pause
exit /b 0
:failed
echo Publication failed. Read the error above; no incomplete feed is advertised.
pause
exit /b 1
'@ | Set-Content (Join-Path $output 'Publish-to-company.cmd') -Encoding ascii
@{schemaVersion=1;dashboardVersion=$Version;bridgeProtocol=1;bridgeFileVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $bootstrap 'Doka.RevitBridge.dll')).FileVersion;sourceCommit=(& git -C $repo rev-parse HEAD).Trim();bootstrapRequiresRevitRestart=$true;installerCreated=$false;companyPublished=$false} | ConvertTo-Json | Set-Content (Join-Path $output 'build-info.json') -Encoding utf8
Write-Output "Built verified Dashboard payload and review-only migration files in $output. No installer, installation or company publication was performed."
