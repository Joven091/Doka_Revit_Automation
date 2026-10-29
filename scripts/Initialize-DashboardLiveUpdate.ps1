[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$FeedDirectory,[string]$InstallDirectory=(Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024'),[string]$DashboardRoot=(Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/Dashboard'))
$ErrorActionPreference='Stop'
# One-time migration only. Never execute from Check for updates while Revit is open.
if(Get-Process Revit,'Doka.EngineeringDashboard' -ErrorAction SilentlyContinue){throw 'Close every Revit and Engineering Dashboard window before this one-time migration.'}
$expected=[IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2024'))
if([IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\','/') -ine $expected.TrimEnd('\','/')){throw 'This migration supports only the current user Revit2024 add-in folder. Ask the administrator to migrate managed installations.'}
$expectedRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/Dashboard'))
if([IO.Path]::GetFullPath($DashboardRoot).TrimEnd('\','/') -ine $expectedRoot.TrimEnd('\','/')){throw 'Use the standard current-user Dashboard runtime folder.'}
$source=$PSScriptRoot
foreach($name in @('Doka.RevitBridge.dll','Doka.RevitUpdater.exe','Doka.DashboardPackage.exe','MyRevitAddin.addin','access-settings.json','update-settings.json')){
    if(!(Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)){throw "Incomplete bootstrap kit: $name"}
}
$feed=[IO.Path]::GetFullPath($FeedDirectory)
if(!(Test-Path -LiteralPath (Join-Path $feed 'dashboard-latest.json'))){throw 'Supply the local verified Dashboard feed folder from the same release.'}
$expectedIds=@('a1b2c3d4-e5f6-4a5b-6c7d-8e9f0a1b2c3d','2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e','c6dedce1-4571-4b7c-b781-1f338826be41')
$expectedClasses=@('MyRevitAddin.App','EngineeringDashboard.Command','MyRevitAddin.Updates.UpdateCommand')
$manifest=Join-Path $InstallDirectory 'MyRevitAddin.addin'
$seen=@{}
foreach($folder in @($InstallDirectory,(Join-Path $env:PROGRAMDATA 'Autodesk/Revit/Addins/2024'))){
    if(!(Test-Path -LiteralPath $folder)){continue}
    foreach($file in Get-ChildItem -LiteralPath $folder -Filter '*.addin' -File -Recurse){
        [xml]$xml=Get-Content -LiteralPath $file.FullName -Raw
        foreach($entry in $xml.RevitAddIns.AddIn){
            $id=if($entry.ClientId){[string]$entry.ClientId}else{[string]$entry.AddInId}
            if($expectedClasses -ccontains [string]$entry.FullClassName -and $expectedIds -inotcontains $id){throw "A second Dashboard registration uses a different command ID: $($file.FullName). Preserve and resolve it before migration."}
            if($expectedIds -icontains $id){
                if($file.FullName -ine $manifest){throw "Another add-in registration owns a Dashboard ID: $($file.FullName). Resolve it before migrating."}
                $index=[array]::IndexOf($expectedIds,$id.ToLowerInvariant())
                if($seen.ContainsKey($id) -or [string]$entry.FullClassName -cne $expectedClasses[$index]){throw 'The existing Dashboard registration is duplicated or customized; preserve it for review.'}
                $assembly=[IO.Path]::GetFileName([string]$entry.Assembly)
                if($assembly -notin @('MyRevitAddin.dll','Doka.RevitBridge.dll')){throw 'The existing Dashboard registration uses a custom assembly.'}
                $assemblyPath=[string]$entry.Assembly
                if(![IO.Path]::IsPathRooted($assemblyPath)){$assemblyPath=Join-Path $file.DirectoryName $assemblyPath}
                if([IO.Path]::GetFullPath($assemblyPath) -ine [IO.Path]::GetFullPath((Join-Path $InstallDirectory $assembly))){throw 'The existing Dashboard assembly is outside the supported installation folder.'}
                $seen[$id]=$true
            }
        }
        if($file.FullName -ieq $manifest -and @($xml.RevitAddIns.AddIn).Count -ne 3){throw 'The existing Dashboard registration contains unrelated entries. Preserve it for review.'}
    }
}
if((Test-Path -LiteralPath $manifest) -and $seen.Count -ne 3){throw 'The existing Dashboard registration is not recognized.'}
$bridgeVersion=[version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'Doka.RevitBridge.dll')).FileVersion
foreach($name in @('Doka.RevitBridge.dll','MyRevitAddin.dll')){
    $file=Join-Path $InstallDirectory $name
    if(Test-Path -LiteralPath $file){if([version][Diagnostics.FileVersionInfo]::GetVersionInfo($file).FileVersion -gt $bridgeVersion){throw 'A newer Dashboard/integration is installed. This bootstrap will not downgrade it.'}}
}
if(!$PSCmdlet.ShouldProcess($InstallDirectory,'Back up and migrate to the external Engineering Dashboard; Revit must be closed')){return}
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
$backup=Join-Path $env:LOCALAPPDATA ('DokaRevitAutomation/UpgradeBackups/live-update-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup | Out-Null
$changed=@('Doka.RevitBridge.dll','Doka.RevitUpdater.exe','MyRevitAddin.addin')
$created=@()
foreach($name in $changed){$path=Join-Path $InstallDirectory $name;if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $backup $name)}else{$created+=$name}}
# Credentials, access/update configuration and projects are never replaced.
$settingsCreated=@()
$receipt=Join-Path $backup 'runtime-activation.json'
try{
    foreach($name in @('access-settings.json','update-settings.json')){
        $path=Join-Path $InstallDirectory $name
        if(!(Test-Path -LiteralPath $path)){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $path;$settingsCreated+=$name}
    }
    & (Join-Path $source 'Doka.DashboardPackage.exe') bootstrap $feed $DashboardRoot $receipt
    if($LASTEXITCODE -ne 0){throw 'The initial Dashboard runtime could not be verified and activated.'}
    if(Get-Process Revit,'Doka.EngineeringDashboard' -ErrorAction SilentlyContinue){throw 'Revit or the Dashboard opened during migration. Close it and retry.'}
    foreach($name in $changed){
        $temporary=Join-Path $InstallDirectory ('.doka-'+[guid]::NewGuid().ToString('N'))
        try{Copy-Item -LiteralPath (Join-Path $source $name) -Destination $temporary;Move-Item -LiteralPath $temporary -Destination (Join-Path $InstallDirectory $name) -Force}
        finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
    }
    Write-Output "Migration complete. Start Revit2024 and open Engineering Dashboard. Backup: $backup"
    Write-Output 'Compatible future Dashboard updates restart only the Dashboard. Revit integration module updates still require a Revit restart.'
}
catch{
    foreach($name in $changed){$saved=Join-Path $backup $name;if(Test-Path -LiteralPath $saved){Copy-Item -LiteralPath $saved -Destination (Join-Path $InstallDirectory $name) -Force}elseif($created -contains $name){Remove-Item -LiteralPath (Join-Path $InstallDirectory $name) -ErrorAction SilentlyContinue}}
    foreach($name in $settingsCreated){Remove-Item -LiteralPath (Join-Path $InstallDirectory $name) -ErrorAction SilentlyContinue}
    if(Test-Path -LiteralPath $receipt){& (Join-Path $source 'Doka.DashboardPackage.exe') rollback $DashboardRoot $receipt;if($LASTEXITCODE -ne 0){Write-Warning 'Runtime pointer changed; the previous integration files are restored but the runtime pointer needs review.'}}
    throw
}
