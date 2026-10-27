[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a new output directory; existing review files are preserved.'}
dotnet build (Join-Path $repo 'src/MyRevitAddin.csproj') -c Release --no-restore --warnaserror
if($LASTEXITCODE -ne 0){throw 'Review build failed.'}
$build=Join-Path $repo 'src/bin/Release/net48'
$native=@('abseil_dll.dll','google-ortools-native.dll','highs.dll','libprotobuf.dll','ortools.dll','re2.dll','utf8_validity.dll','zlib1.dll')
foreach($name in $native){if(!(Test-Path -LiteralPath (Join-Path $build $name))){throw "Missing native dependency: $name"}}
if(Test-Path -LiteralPath (Join-Path $build 'RevitAPI.dll')){throw 'Do not distribute the host Revit API.'}
New-Item -ItemType Directory -Path (Join-Path $output 'payload') | Out-Null
$payload=Join-Path $output 'payload'
Get-ChildItem -LiteralPath $build -File -Filter '*.dll' | Copy-Item -Destination $payload
foreach($name in @('access-settings.json','update-settings.json','Doka.RevitUpdater.exe')){Copy-Item -LiteralPath (Join-Path $build $name) -Destination $payload}
Copy-Item -LiteralPath (Join-Path $repo 'MyRevitAddin.addin') -Destination $payload
@'
REVIEW BUILD ONLY — Engineering Dashboard with integrated native Top50
Target: Revit2024 x64 / .NET Framework4.8. No installation or publication performed.
Base: PR168 bb37debee36f9d6a9dbce79bc41f3d47bdb35b6b plus the preserved uploaded Top50 overlay and planner diagnosis changes.
The newer beam ReadRevitSelection callback is retained. This does not merge or change PR168.
Original V1 visual/workflow reconstruction; exact original solver equivalence remains unresolved.
No golden reference fixture is compiled into this shipping assembly.
Actual Revit2024 load/access-gate/native-library coexistence and user acceptance remain unrun.
Do not copy into a live Revit add-in directory or run production replacement without approval.
The manifest is supplied for review; no user AppData/add-in directory was modified.
'@ | Set-Content -LiteralPath (Join-Path $output 'README.txt')
Get-ChildItem -LiteralPath $payload -File | Sort-Object Name | ForEach-Object {"$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  payload/$($_.Name)"} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS')
Compress-Archive -LiteralPath @($payload,(Join-Path $output 'README.txt'),(Join-Path $output 'SHA256SUMS')) -DestinationPath (Join-Path $output 'Doka-Revit2024-Top50-REVIEW.zip')
Write-Output $output
