[CmdletBinding()]
param([Parameter(Mandatory)][string]$FeedDirectory,[Parameter(Mandatory)][string]$LogPath,[string]$IsolatedRoot)
$ErrorActionPreference='Stop'
$saved=@{APPDATA=$env:APPDATA;LOCALAPPDATA=$env:LOCALAPPDATA;PROGRAMDATA=$env:PROGRAMDATA}
try {
    if($IsolatedRoot){
        $root=[IO.Path]::GetFullPath($IsolatedRoot)
        $prefix=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Temp/DokaDashboardInstallerTests'))+'\'
        if(!$root.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $root.Length -le $prefix.Length){throw 'Invalid isolated installer root.'}
        for($part=$root;$part;$part=[IO.Path]::GetDirectoryName($part)){
            if((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Isolated installation paths cannot be links.'}
        }
        if(Test-Path -LiteralPath $root){
            $pending=[Collections.Generic.Queue[string]]::new();$pending.Enqueue($root)
            while($pending.Count){
                foreach($entry in Get-ChildItem -LiteralPath ($pending.Dequeue()) -Force){
                    if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Isolated installation paths cannot be links.'}
                    if($entry.PSIsContainer){$pending.Enqueue($entry.FullName)}
                }
            }
        }
        $env:APPDATA=Join-Path $root 'roaming';$env:LOCALAPPDATA=Join-Path $root 'local';$env:PROGRAMDATA=Join-Path $root 'machine'
    }
    else {
        $env:APPDATA=[Environment]::GetFolderPath('ApplicationData');$env:LOCALAPPDATA=[Environment]::GetFolderPath('LocalApplicationData');$env:PROGRAMDATA=[Environment]::GetFolderPath('CommonApplicationData')
    }
    $logs=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/InstallerLogs'))
    $log=[IO.Path]::GetFullPath($LogPath)
    if([IO.Path]::GetDirectoryName($log) -ine $logs){throw 'Use the current-user Dashboard installer log folder.'}
    New-Item -ItemType Directory -Force $logs | Out-Null
    "$(Get-Date -Format o) Starting Dashboard installation." | Out-File -LiteralPath $log -Encoding utf8 -Append
    function NormalVersion([string]$value){$v=[version]$value;return [version]::new($v.Major,$v.Minor,$v.Build,[Math]::Max(0,$v.Revision))}
    $feed=Get-Content -LiteralPath (Join-Path $FeedDirectory 'dashboard-latest.json') -Raw | ConvertFrom-Json
    $activePath=Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation/Dashboard/active.json'
    if(Test-Path -LiteralPath $activePath){
        $active=Get-Content -LiteralPath $activePath -Raw | ConvertFrom-Json
        if((NormalVersion $active.Version) -gt (NormalVersion $feed.version)){throw 'A newer Dashboard is already active. This installer will not downgrade it.'}
        if((NormalVersion $active.Version) -eq (NormalVersion $feed.version) -and $active.ArchiveSha256 -ine $feed.sha256){throw 'The same Dashboard version has different contents. Installation was not started.'}
    }
    & (Join-Path $PSScriptRoot 'Initialize-DashboardLiveUpdate.ps1') -FeedDirectory $FeedDirectory *>&1 | Out-File -LiteralPath $log -Encoding utf8 -Append
    "$(Get-Date -Format o) Installation completed. Open Revit 2024 and Engineering Dashboard." | Out-File -LiteralPath $log -Encoding utf8 -Append
    exit 0
}
catch {
    if($log){$_ | Out-String | Out-File -LiteralPath $log -Encoding utf8 -Append}
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 1
}
finally {foreach($name in $saved.Keys){Set-Item "Env:$name" $saved[$name]}}
