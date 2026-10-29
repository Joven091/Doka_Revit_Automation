# Repackage an already tested Dashboard release. Does not compile, install or publish.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $ReleaseDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a new output directory; existing releases are preserved.'}
if($output.StartsWith($source.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be outside the source release.'}
$metadata=Get-Content -LiteralPath (Join-Path $source 'release-info.json') -Raw | ConvertFrom-Json
if($metadata.target -ne 'Revit2024 Dashboard' -or $metadata.version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$' -or $metadata.installerSha256 -notmatch '^[0-9a-fA-F]{64}$'){
    throw 'Expected a tested Revit Dashboard release with version and installer checksum.'
}
$installer=Join-Path $source 'Install_DokaRevitAutomation.exe'
$hash=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if($hash -ne $metadata.installerSha256){throw 'Installer checksum does not match the tested release. No publishing kit created.'}
$version=[version]$metadata.version
$expected=[version]::new($version.Major,$version.Minor,$version.Build,[Math]::Max(0,$version.Revision))
$details=[Diagnostics.FileVersionInfo]::GetVersionInfo($installer)
$actual=[version]::new($details.FileMajorPart,$details.FileMinorPart,$details.FileBuildPart,$details.FilePrivatePart)
if($actual -ne $expected){throw 'Installer file version differs from release metadata.'}
$checksumRows=@(Get-Content -LiteralPath (Join-Path $source 'SHA256SUMS') | Where-Object {$_ -match '^[0-9a-fA-F]{64}  Install_DokaRevitAutomation\.exe$'})
if($checksumRows.Count -ne 1 -or $checksumRows[0].Substring(0,64).ToLowerInvariant() -ne $hash){throw 'Installer checksum manifest differs from the tested release.'}
New-Item -ItemType Directory -Path $output | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $output -Recurse
if((Get-FileHash -LiteralPath (Join-Path $output 'Install_DokaRevitAutomation.exe')).Hash.ToLowerInvariant() -ne $hash){throw 'Repackaged installer changed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Publish-RevitUpdate.ps1') -Destination $output
$number=$metadata.version
# Only the regex-validated numeric version enters the command file. Notes are fixed text.
$launcher=@"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-RevitUpdate.ps1" -Installer "%~dp0Install_DokaRevitAutomation.exe" -Version $number -ReleaseNotes "Engineering Dashboard $number. Reviewed Top50 planning fixes and retained dashboard tools."
if errorlevel 1 goto failed
echo Published version $number. The team can click Check for updates.
pause
exit /b 0
:failed
echo Publication failed. Read the error above. The update folder may require Modify permissions.
pause
exit /b 1
"@
[IO.File]::WriteAllText((Join-Path $output 'Publish-to-company.cmd'),$launcher.Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n",[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $source 'README.txt') -Destination (Join-Path $output 'Installer-README.txt')
@"
Engineering Dashboard $number - company update publishing kit

This contains the SAME tested installer, not a newly built installer.
Installer SHA-256: $hash

1. Extract this entire ZIP to a local folder.
2. Connect to the company network and run Publish-to-company.cmd.
3. Wait for Published version $number. Team members can then click Check for updates.

Publisher destination:
\\DOKAAREDUBA\ApplDat\MEA\ENG\Joven\BeamCalculator\Updates
(S:\MEA\ENG\Joven\BeamCalculator\Updates)

Publication creates DokaRevitAutomation-$number.exe and replaces latest.json last,
after verifying the copied installer. Simply pasting an EXE or ZIP does not
publish an update. Do not manually delete/edit latest.json or replace a versioned
installer with different bytes. The publisher needs Modify permission.

If you already downloaded the original release, the two files in the smaller
publishing-files download can instead be extracted beside its unchanged
Install_DokaRevitAutomation.exe. Run Publish-to-company.cmd from that folder.

The updater only offers versions NEWER than the version currently loaded in Revit.
If Revit already runs $number, no update prompt for this same version is expected.
For a newer offer, click Install after Revit closes, save and close Revit, wait
for completion, then reopen it. The updater does not close your models.

No company folder was accessed during creation of this kit. Original installer
notes/reports are retained; actual Revit-host acceptance remains unrun.
"@ | Set-Content -LiteralPath (Join-Path $output 'README.txt') -Encoding utf8
# Recompute the container manifest after adding the missing publication files.
Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -ne 'SHA256SUMS' | Sort-Object FullName | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.FullName.Substring($output.Length+1).Replace('\','/'))"
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS')
Write-Output "Prepared publication kit for $number; installer unchanged ($hash). No company publication performed."
