[CmdletBinding()]
param([Parameter(Mandatory)][string]$Compiler,[Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$build=(Resolve-Path -LiteralPath $BuildDirectory).Path
$Compiler=(Resolve-Path -LiteralPath $Compiler).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new output directory; do not overwrite a reviewed installer.'}
$info=Get-Content -LiteralPath (Join-Path $build 'build-info.json') -Raw | ConvertFrom-Json
$source=(& git -C $repo rev-parse HEAD).Trim()
if($info.sourceCommit -cne $source){throw 'The Dashboard payload was built from another source commit.'}
$feed=Get-Content -LiteralPath (Join-Path $build 'feed/dashboard-latest.json') -Raw | ConvertFrom-Json
$archive=Join-Path $build ('feed/'+$feed.archiveFile)
if((Get-FileHash -LiteralPath $archive).Hash -ine $feed.sha256 -or (Get-Item -LiteralPath $archive).Length -ne $feed.sizeBytes){throw 'The Dashboard ZIP does not match its feed.'}
$bridge=Join-Path $build 'bootstrap/Doka.RevitBridge.dll'
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($bridge).FileVersion
$target=[version]$feed.version
$target=[version]::new($target.Major,$target.Minor,$target.Build,[Math]::Max(0,$target.Revision))
if([version]$version -ne $target){throw 'Bridge and initial Dashboard versions must match for this migration installer.'}
New-Item -ItemType Directory -Path $output | Out-Null
$manifestHash=(Get-FileHash -LiteralPath (Join-Path $build 'bootstrap/MyRevitAddin.addin')).Hash.ToLowerInvariant()
$bridgeHash=(Get-FileHash -LiteralPath $bridge).Hash.ToLowerInvariant()
& $Compiler "/DBuildRoot=$build" "/DArchiveName=$($feed.archiveFile)" "/DManifestSha256=$manifestHash" "/DBridgeSha256=$bridgeHash" "/O$output" (Join-Path $repo 'installer-dashboard-live-update.iss') *> (Join-Path $output 'installer-compile.log')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $output 'installer-compile.log');throw 'Dashboard installer compilation failed.'}
$installer=Join-Path $output "Install_DokaRevitAutomation_$version.exe"
if(!(Test-Path -LiteralPath $installer)){throw 'The expected installer was not produced.'}
$hash=(Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Encoding ascii
@{version=$version;sourceCommit=$source;installerSha256=$hash;revitVersion=2024;bridgeProtocol=1;installerCreated=$true;firstInstallationRequiresRevitClosed=$true;futureCompatibleUpdatesRestartDashboardOnly=$true;companyPublished=$false;merged=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release-info.json') -Encoding utf8
@"
Doka Engineering Dashboard $version - Revit 2024

1. Extract this ZIP to a local folder.
2. Save your models and close every Revit and Engineering Dashboard window.
3. Run Install_DokaRevitAutomation_$version.exe under your own Windows account.
4. Wait for the installation-complete page, then open Revit 2024 and Engineering Dashboard.

This is the one-time migration installer. It includes the existing calculators, Top50 Planner, Steel Shape and the new Dashboard updater. Settings and project files are preserved. Compatible future Dashboard updates restart only the Dashboard; D3 and Top50 inputs may need Optimize again. Changes to the Revit integration can still require a Revit restart.

Run this migration installer directly. Do not advertise it to the old in-process updater by overwriting latest.json. After migration, Dashboard-only updates use dashboard-latest.json plus the matching Dashboard ZIP and publisher script from a newer verified release. Copying this EXE alone into the company folder does not publish a Dashboard update.

Installation logs: %LOCALAPPDATA%\DokaRevitAutomation\InstallerLogs
Upgrade backups: %LOCALAPPDATA%\DokaRevitAutomation\UpgradeBackups
The checked runtime migration completes before Setup records its uninstall entry. If that final Setup registration fails, the complete migrated runtime and backups remain; run this same installer again to repair Setup. The installer reports this case and retains the migration log.
Actual Revit 2024 host and company-share acceptance remain separate from the isolated Windows installer tests. No company publication or source merge was performed.
"@ | Set-Content -LiteralPath (Join-Path $output 'README.txt') -Encoding utf8
Write-Output "Created $installer. No installation, company publication or merge was performed."
