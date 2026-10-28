[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $output){throw 'Use a new output folder to preserve earlier testers.'}
New-Item -ItemType Directory -Path $output | Out-Null
& dotnet build (Join-Path $repo 'tools/SlabTester/SlabTester.csproj') -c Release --warnaserror
if($LASTEXITCODE -ne 0){throw 'Slab tester build failed.'}
& dotnet build (Join-Path $repo 'tests/Beam.UiSmoke/Beam.UiSmoke.csproj') -c Release --warnaserror -p:BuildOneOffTester=true
if($LASTEXITCODE -ne 0){throw 'Beam tester build failed.'}
$commit=(& git -C $repo rev-parse HEAD).Trim()
foreach($calculator in @('Slab','Beam')){
    $build=if($calculator -eq 'Slab'){Join-Path $repo 'tools/SlabTester/bin/Release/net48'}else{Join-Path $repo 'tests/Beam.UiSmoke/bin/Release/net48'}
    $folder=Join-Path $output $calculator
    New-Item -ItemType Directory -Path $folder | Out-Null
    Get-ChildItem $build -File | Where-Object {$_.Extension -eq '.dll' -or $_.Name -in @('MyRevitAddin.exe','MyRevitAddin.exe.config')} | Copy-Item -Destination $folder
    if(Get-ChildItem $folder -Filter 'Revit*.dll'){throw 'Revit assemblies must not be distributed.'}
    $exe=Join-Path $folder "${calculator}Calculator.exe"
    Move-Item (Join-Path $folder 'MyRevitAddin.exe') $exe
    if(Test-Path (Join-Path $folder 'MyRevitAddin.exe.config')){Move-Item (Join-Path $folder 'MyRevitAddin.exe.config') "$exe.config"}
    $p=Start-Process -FilePath $exe -ArgumentList '--startup-check' -WorkingDirectory $folder -PassThru
    if(!$p.WaitForExit(60000)){$p.Kill();throw "$calculator startup timed out."}
    $p.Refresh();if($p.ExitCode -ne 0 -or (Test-Path (Join-Path $folder 'startup-error.txt'))){throw "$calculator startup check failed. Inspect startup-error.txt."}
    $version=([version][Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion).ToString(3)
    @{
        version=$version; sourceCommit=$commit; calculator=$calculator
        mode='One-time Windows x64 standalone tester; no Revit or installation required'
        runtime='.NET Framework 4.8'; executable=[IO.Path]::GetFileName($exe)
    } | ConvertTo-Json | Set-Content (Join-Path $folder 'release-info.json')
    @"
$calculator CALCULATOR — ONE-TIME STANDALONE TESTER $version

Extract the entire ZIP to a local folder and open ${calculator}Calculator.exe.
Keep all supplied DLLs and the .config file beside it. Windows x64 with .NET
Framework 4.8 is required; no installer or Revit is needed.

This uses the current production calculator models, windows and diagrams.
Revit-only selection/import actions are hidden. The Slab tester is the current
Dokaflex slab calculator; the unreleased D3 slab system is not enabled.

This tester does not install, update or register the Revit dashboard. It is a
one-time test package, not a separately installed product or company update.

Source commit: $commit
"@ | Set-Content (Join-Path $folder 'README.txt')
    Get-ChildItem $folder -File | Where-Object Name -ne 'SHA256SUMS' | ForEach-Object {"$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"} | Set-Content (Join-Path $folder 'SHA256SUMS')
}
