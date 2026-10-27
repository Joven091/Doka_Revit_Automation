[CmdletBinding()]
param([Parameter(Mandatory)][string]$Compiler,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$NoRestore)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$Compiler=(Resolve-Path -LiteralPath $Compiler).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a new output folder to preserve previous installers.'}
New-Item -ItemType Directory -Path $output | Out-Null
$arguments=@('build',(Join-Path $repo 'src/MyRevitAddin.csproj'),'-c','Release','--warnaserror')
if($NoRestore){$arguments+='--no-restore'}
& dotnet @arguments *> (Join-Path $output 'build.log')
if($LASTEXITCODE -ne 0){throw "Build failed; see $output\build.log"}
$build=Join-Path $repo 'src/bin/Release/net48'
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $build 'MyRevitAddin.dll'))
$releaseVersion=([version]$version.FileVersion).ToString(4)
foreach($name in @('abseil_dll.dll','google-ortools-native.dll','highs.dll','libprotobuf.dll','ortools.dll','re2.dll','utf8_validity.dll','zlib1.dll')){if(!(Test-Path -LiteralPath (Join-Path $build $name))){throw "Missing native dependency $name"}}
if(Test-Path -LiteralPath (Join-Path $build 'RevitAPI.dll')){throw 'Host Revit API must not be distributed.'}
& $Compiler "/O$output" (Join-Path $repo 'installer-top50.iss') *> (Join-Path $output 'installer-compile.log')
if($LASTEXITCODE -ne 0){throw "Installer compilation failed; see $output\installer-compile.log"}
$installer=Join-Path $output "DokaEngineeringDashboard_Top50_Setup_$releaseVersion.exe"
"$((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS')
Write-Output $installer
Write-Output 'Built locally only. No installation, company-feed publication or GitHub mutation performed.'
