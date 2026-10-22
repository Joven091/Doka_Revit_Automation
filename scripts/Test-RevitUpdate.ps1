# End-to-end test on a disposable Windows CI user profile, never a designer's computer.
param([Parameter(Mandatory)][string]$Bootstrap, [Parameter(Mandatory)][string]$Feed, [Parameter(Mandatory)][string]$Helper, [Parameter(Mandatory)][string]$Tests)
$ErrorActionPreference='Stop'
$install=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
$cache=Join-Path $env:LOCALAPPDATA 'DokaRevitAutomation\Updates'
$fixture=Join-Path $env:RUNNER_TEMP 'revit-update-fixture'
New-Item -ItemType Directory -Force $fixture | Out-Null
$p=Start-Process -FilePath $Bootstrap -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS' -PassThru -Wait
if($p.ExitCode -ne 0){throw 'Bootstrap installation failed'}
$dll=Join-Path $install 'MyRevitAddin.dll'
if([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion) -ne [version]'1.1.0.0'){throw 'Wrong bootstrap version'}
# Preserve both a customized updater setting and existing calculation data.
$config=Join-Path $install 'update-settings.json'
$settings=Get-Content $config -Raw | ConvertFrom-Json
$settings.sourceDirectory=$Feed
$settings | ConvertTo-Json | Set-Content $config
$sentinel=Join-Path $install 'saved-calculation.xml'
Set-Content $sentinel 'existing user calculation'
$ready=Join-Path $fixture 'ready';$release=Join-Path $fixture 'release'
Copy-Item $Tests (Join-Path $fixture 'Revit.exe')
$hostProcess=Start-Process (Join-Path $fixture 'Revit.exe') -ArgumentList "--hold `"$dll`" `"$ready`" `"$release`"" -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    while(!(Test-Path $ready)) {if([DateTime]::UtcNow -gt $deadline){throw 'Fake Revit did not start'};Start-Sleep -Milliseconds 200}
    $stage=& $Tests --stage $Feed $Helper $hostProcess.Id
    if($LASTEXITCODE -ne 0){throw 'Staging failed'}
    $stage=($stage | Select-Object -Last 1).Trim()
    $updater=Start-Process (Join-Path $stage 'Doka.RevitUpdater.exe') -ArgumentList "--headless --plan `"$(Join-Path $stage 'update-plan.json')`"" -PassThru
    Start-Sleep -Seconds 3
    if($updater.HasExited){throw 'Updater exited before Revit closed'}
    if([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion) -ne [version]'1.1.0.0'){throw 'Updater changed a live Revit installation'}
    New-Item -ItemType File $release | Out-Null
    if(!$updater.WaitForExit(90000)){throw 'Update timed out'}
    if($updater.ExitCode -ne 0){throw "Updater failed: $(Get-Content (Join-Path $stage 'updater.log') -Raw)"}
    if([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion) -ne [version]'1.1.1.0'){throw 'Updated DLL version does not match'}
    if((Get-Content $sentinel -Raw).Trim() -ne 'existing user calculation'){throw 'Saved calculation changed'}
    if((Get-Content $config -Raw | ConvertFrom-Json).sourceDirectory -ne $Feed){throw 'Local update settings overwritten'}
    Write-Output 'PASS: real installer waits for Revit exit and preserves user files/settings'
} finally {
    if(!$hostProcess.HasExited){New-Item -ItemType File -Force $release | Out-Null;$hostProcess.WaitForExit(5000) | Out-Null}
}
