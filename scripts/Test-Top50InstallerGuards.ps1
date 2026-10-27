param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$Installer=(Resolve-Path -LiteralPath $Installer).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory);New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$prefix=Join-Path $env:LOCALAPPDATA 'Temp\DokaTop50InstallerTests'
$root=Join-Path $prefix ([guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$repo=Split-Path $PSScriptRoot -Parent
function Run([string]$Target,[string]$Name){
    $log=Join-Path $evidence "$Name.log"
    $p=Start-Process -FilePath $Installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ISOLATEDROOT=`"$Target`" /DIR=`"$Target`" /LOG=`"$log`"" -WindowStyle Hidden -PassThru
    if(!$p.WaitForExit(30000)){throw "Timed out $Name, PID $($p.Id)"};$p.Refresh();return $p.ExitCode
}
$newer=Join-Path $root 'newer';New-Item -ItemType Directory -Path $newer | Out-Null
$current=[version][Diagnostics.FileVersionInfo]::GetVersionInfo($Installer).FileVersion.Trim()
$newerVersion=[version]::new($current.Major,$current.Minor,$current.Build+1,0)
"using System.Reflection; [assembly:AssemblyVersion(`"$newerVersion`")] [assembly:AssemblyFileVersion(`"$newerVersion`")] public sealed class NewerFixture {}" | Set-Content -LiteralPath (Join-Path $root 'Newer.cs')
& $compiler /nologo /target:library "/out:$newer\MyRevitAddin.dll" (Join-Path $root 'Newer.cs')
if($LASTEXITCODE -ne 0){throw 'Newer fixture compilation failed'}
Copy-Item -LiteralPath (Join-Path $repo 'MyRevitAddin.addin') -Destination $newer
$hash=(Get-FileHash -LiteralPath (Join-Path $newer 'MyRevitAddin.dll')).Hash
if((Run $newer 'downgrade') -eq 0){throw 'Downgrade was accepted'}
if((Get-FileHash -LiteralPath (Join-Path $newer 'MyRevitAddin.dll')).Hash -ne $hash){throw 'Newer fixture modified'}
if(!(Select-String -LiteralPath (Join-Path $evidence 'downgrade.log') -SimpleMatch 'newer Dashboard')){throw 'Unexpected downgrade rejection'}
'PASS newer-version rejection preserves existing DLL' | Set-Content -LiteralPath (Join-Path $evidence 'checks.txt')
$custom=Join-Path $root 'custom-registration';New-Item -ItemType Directory -Path $custom | Out-Null
$customManifest=(Get-Content -LiteralPath (Join-Path $repo 'MyRevitAddin.addin') -Raw).Replace('<Assembly>MyRevitAddin.dll</Assembly>','<Assembly>C:\CustomPreviousDashboard\MyRevitAddin.dll</Assembly>')
$customManifest | Set-Content -LiteralPath (Join-Path $custom 'MyRevitAddin.addin')
$customHash=(Get-FileHash -LiteralPath (Join-Path $custom 'MyRevitAddin.addin')).Hash
if((Run $custom 'custom-registration') -eq 0){throw 'Custom registration was overwritten'}
if((Get-FileHash -LiteralPath (Join-Path $custom 'MyRevitAddin.addin')).Hash -ne $customHash){throw 'Custom manifest changed'}
if(!(Select-String -LiteralPath (Join-Path $evidence 'custom-registration.log') -SimpleMatch 'custom assembly location')){throw 'Unexpected custom-location rejection'}
'PASS custom assembly registration is rejected and preserved' | Add-Content -LiteralPath (Join-Path $evidence 'checks.txt')
$live=Join-Path $root 'live'
if((Run $live 'before-live') -ne 0){throw 'Guard fixture install failed'}
'using System; using System.IO; using System.Threading; internal static class Program { static void Main(string[] args) { var end=DateTime.UtcNow.AddSeconds(30); while(!File.Exists(args[0]) && DateTime.UtcNow<end)Thread.Sleep(50); } }' | Set-Content -LiteralPath (Join-Path $root 'FakeHost.cs')
& $compiler /nologo /target:winexe "/out:$root\Revit.exe" (Join-Path $root 'FakeHost.cs')
if($LASTEXITCODE -ne 0){throw 'Fake host compilation failed'}
$release=Join-Path $root 'release-host'
$hostFixture=Start-Process -FilePath (Join-Path $root 'Revit.exe') -ArgumentList "`"$release`"" -WindowStyle Hidden -PassThru
try {
    $blocked=Join-Path $root 'blocked-live'
    if((Run $blocked 'live-revit') -eq 0){throw 'Live Revit install guard failed'}
    if(Test-Path -LiteralPath (Join-Path $blocked 'MyRevitAddin.dll')){throw 'Live guard wrote DLL'}
    if(!(Select-String -LiteralPath (Join-Path $evidence 'live-revit.log') -SimpleMatch 'close every Revit')){throw 'Unexpected live guard rejection'}
    'PASS live Revit process blocks installation before binary writes' | Add-Content -LiteralPath (Join-Path $evidence 'checks.txt')
    $uninstaller=Join-Path $live 'unins000.exe'
    $p=Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru
    if(!$p.WaitForExit(30000)){throw 'Live uninstall guard timed out'};$p.Refresh()
    if($p.ExitCode -eq 0 -or !(Test-Path -LiteralPath (Join-Path $live 'MyRevitAddin.dll'))){throw 'Live uninstall guard failed'}
    'PASS live Revit process blocks uninstall and retains the installed DLL' | Add-Content -LiteralPath (Join-Path $evidence 'checks.txt')
} finally {New-Item -ItemType File -Path $release -Force | Out-Null;$hostFixture.WaitForExit(5000) | Out-Null}
$p=Start-Process -FilePath (Join-Path $live 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru
if(!$p.WaitForExit(30000)){throw 'Guard fixture cleanup timed out'};$p.Refresh();if($p.ExitCode -ne 0){throw 'Guard fixture cleanup failed'}
'PASS uninstall succeeds after fake Revit exits' | Add-Content -LiteralPath (Join-Path $evidence 'checks.txt')
Get-Content -LiteralPath (Join-Path $evidence 'checks.txt')
