[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerDirectory,[Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$build=(Resolve-Path $BuildDirectory).Path
$info=Get-Content (Join-Path $InstallerDirectory 'release-info.json') -Raw | ConvertFrom-Json
$feed=Get-Content (Join-Path $build 'feed/dashboard-latest.json') -Raw | ConvertFrom-Json
$v=[version]$feed.version
$normalized=[version]::new($v.Major,$v.Minor,$v.Build,[Math]::Max(0,$v.Revision))
if($info.revitVersion -ne $feed.revitVersion -or [version]$info.version -ne $normalized){throw 'Installer and Dashboard metadata differ.'}
if(Test-Path $OutputDirectory){throw 'Use a new company-files directory.'}
$year=[int]$info.revitVersion
$destination=if($year -eq 2024){$OutputDirectory}else{Join-Path $OutputDirectory 'Revit2027'}
New-Item -ItemType Directory $destination | Out-Null
$installer=Get-ChildItem $InstallerDirectory -Filter '*.exe'
if(@($installer).Count -ne 1){throw 'Expected exactly one verified installer.'}
if((Get-FileHash $installer.FullName).Hash -ine $info.installerSha256){throw 'Installer checksum mismatch.'}
$archive=Join-Path $build ('feed/'+$feed.archiveFile)
if((Get-FileHash $archive).Hash -ine $feed.sha256 -or (Get-Item $archive).Length -ne $feed.sizeBytes){throw 'Dashboard archive mismatch.'}
Copy-Item $installer.FullName $destination
Copy-Item $archive $destination
Copy-Item (Join-Path $build 'feed/dashboard-latest.json') $destination
@{schemaVersion=1;productId='Doka.RevitAutomation';revitVersion=$year;version=$info.version;installerFile=$installer.Name;sha256=$info.installerSha256;sizeBytes=$installer.Length;publishedUtc=[DateTime]::UtcNow.ToString('O');releaseNotes='Engineering Dashboard update, including checked Top50 panel-size consolidation. Save your work and close Revit for this integration installer.'} | ConvertTo-Json | Set-Content (Join-Path $destination 'latest.json') -Encoding utf8
$subfolder=if($year -eq 2024){''}else{'\Revit2027'}
@"
Revit $year - Engineering Dashboard $($info.version)

Extract this outer download. Copy its contents into:
S:\MEA\ENG\Joven\BeamCalculator\Updates

Preserve the Revit2027 subfolder in the 2027 download. Do not put its JSON files at the Updates root.
Final location for this version: S:\MEA\ENG\Joven\BeamCalculator\Updates$subfolder
Copy the EXE and inner Dashboard ZIP first. Leave the inner ZIP compressed.
Copy latest.json and dashboard-latest.json last, after those two files finish copying.

Existing external Dashboard users: Check for updates reads dashboard-latest.json and restarts only the Dashboard for compatible calculator changes.
Revit integration updates: open Check for updates, then click Revit integration updates. This reads latest.json, stages this installer and waits for Revit to close.
First installation, or a 2024 user still on 1.1.x: run the Revit-specific EXE directly with Revit closed. This one-time migration is not supported by the old in-process update helper.

Revit2027 uses the published 2027 API and .NET 10. The calculator desktop and numerical policies are shared with the 2024 release. Your project/settings files are retained. Runtime folders, registration and update feeds are separated by Revit version.
Pour-sequence planning remains a design preview and is not included. Autodesk Revit host acceptance must be performed on your own test workstation; isolated Windows installer and desktop checks do not run Revit.
No company publication, production installation or GitHub merge was performed.
"@ | Set-Content (Join-Path $destination 'READ-ME-FIRST.txt') -Encoding utf8
Copy-Item (Join-Path $InstallerDirectory 'release-info.json') (Join-Path $destination 'release-info.json')
Write-Output "Prepared ready-to-copy Revit $year files in $OutputDirectory. No network publication occurred."
