[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a new output directory to preserve earlier testers.'}
dotnet build (Join-Path $repo 'tools/Top50Tester/Top50Tester.csproj') -c Release --warnaserror
if($LASTEXITCODE -ne 0){throw 'Standalone Top50 build failed.'}
$build=Join-Path $repo 'tools/Top50Tester/bin/Release/net48'
foreach($name in @('Top50Planner.exe','Top50Planner.exe.config','abseil_dll.dll','google-ortools-native.dll','highs.dll','libprotobuf.dll','ortools.dll','re2.dll','utf8_validity.dll','zlib1.dll')){
    if(!(Test-Path -LiteralPath (Join-Path $build $name))){throw "Missing portable dependency: $name"}
}
if(Get-ChildItem -LiteralPath $build -Filter 'Revit*.dll'){throw 'The standalone tester must not include Revit assemblies.'}
New-Item -ItemType Directory -Path $output | Out-Null
Get-ChildItem -LiteralPath $build -File | Where-Object {$_.Extension -eq '.dll' -or $_.Name -in @('Top50Planner.exe','Top50Planner.exe.config')} | Copy-Item -Destination $output
$version=([version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'Top50Planner.exe')).FileVersion).ToString(3)
$commit=(& git -C $repo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0){throw 'Source commit could not be recorded.'}
$sources=@(Get-ChildItem (Join-Path $repo 'src/Top50/*.cs');Get-ChildItem (Join-Path $repo 'src/ViewModels/Top50ViewModel*.cs');Get-Item (Join-Path $repo 'src/Views/Top50Window.cs');Get-Item (Join-Path $repo 'src/Views/Top50DiagnosticWindow.cs'))
$hashes=@($sources | Sort-Object FullName | ForEach-Object {@{path=$_.FullName.Substring($repo.Length+1).Replace('\','/');sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
@{version=$version;sourceCommit=$commit;plannerBaseline='835e6de78f9170d4777ca14d982661ae7530c0dd';mode='Portable Windows x64 tester';plannerSources=$hashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'release-info.json') -Encoding UTF8
@"
TOP50 PLANNER — STANDALONE TESTER $version

1. Extract the complete ZIP into a local folder.
2. Open Top50Planner.exe. Keep all supplied DLLs and the .config file beside it.
3. Choose Open project for an existing .top50 file. Or use Import > Import DXF
   to read Wall_Length dimensions, or Add manual wall for reviewed dimensions.
4. Review Settings, then choose Optimize. Fewer panels take priority.
5. Use Reports to export Excel. Use Diagnose to copy/save a report for review.

No installer, Revit or AutoCAD is needed. This tester does not register an add-in
or update the installed dashboard. It uses the same planner source as PR #172.
Windows x64 and .NET Framework 4.8 are required. The native solver uses the
Microsoft Visual C++ 2015–2022 x64 runtime, normally already present with Revit.

Projects: older .top50 files can be opened. New saves use version 2 and require
the updated planner. Save a separate test copy to retain your earlier file.
The tester starts with empty input; no sample proposal is supplied as a result.
Unexpected startup error logs: %LOCALAPPDATA%\Doka\Top50StandaloneTester\Logs

Source commit: $commit
Review build. Best-found search does not prove global optimality. Existing
planner engineering-review requirements still apply.
"@ | Set-Content -LiteralPath (Join-Path $output 'README.txt') -Encoding UTF8
Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {"$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Encoding UTF8
Write-Output "Portable tester: $output"
