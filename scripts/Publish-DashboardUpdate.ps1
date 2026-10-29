[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$FeedDirectory,[string]$Destination='\\DOKAAREDUBA\ApplDat\MEA\ENG\Joven\BeamCalculator\Updates')
$ErrorActionPreference='Stop'
function NormalVersion([string]$text){$v=[version]$text;return [version]::new($v.Major,$v.Minor,$v.Build,[Math]::Max(0,$v.Revision))}
function Invoke-ManifestReplace([string]$Source, [string]$Target) {
    [IO.File]::Replace($Source, $Target, [NullString]::Value)
}
function Invoke-ManifestRename([string]$Source, [string]$Target) {
    # ReplaceFile can require metadata/ACL rights that SMB publishers do not have.
    # Preserve the existing access rules on our own temporary file, then request a
    # same-directory atomic rename. Never delete latest.json or truncate it in place.
    $access = [Security.AccessControl.AccessControlSections]::Access
    $existing = Get-Acl -LiteralPath $Target
    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetSecurityDescriptorSddlForm($existing.GetSecurityDescriptorSddlForm($access), $access)
    Set-Acl -LiteralPath $Source -AclObject $security
    if (-not ('DokaReleasePublisher.AtomicFiles' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace DokaReleasePublisher {
    public static class AtomicFiles {
        [DllImport("kernel32.dll", EntryPoint="MoveFileExW", CharSet=CharSet.Unicode, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveFileEx(string source, string target, int flags);
    }
}
'@
    }
    # REPLACE_EXISTING | WRITE_THROUGH; no cross-volume copy fallback.
    if (-not [DokaReleasePublisher.AtomicFiles]::MoveFileEx($Source, $Target, 9)) {
        $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw [ComponentModel.Win32Exception]::new($code, "Atomic rename of '$Target' failed (Windows error $code).")
    }
}
function Publish-Manifest([string]$Source, [string]$Target) {
    if (-not [IO.File]::Exists($Target)) { [IO.File]::Move($Source, $Target); return }
    for ($attempt=0; $attempt -lt 3; $attempt++) {
        try { Invoke-ManifestReplace $Source $Target; return }
        catch {
            $replaceFailure = $_.Exception
            # Argument/programming failures must not be treated as SMB restrictions.
            $cause = $replaceFailure
            while ($cause.InnerException) { $cause = $cause.InnerException }
            if ($cause -isnot [IO.IOException] -and $cause -isnot [UnauthorizedAccessException]) { throw }
            try { Invoke-ManifestRename $Source $Target; return }
            catch {
                $renameFailure = $_.Exception.Message
                if ($attempt -lt 2) { Start-Sleep -Milliseconds (200 * ($attempt+1)) }
            }
        }
    }
    throw "Cannot replace '$Target'. Check its Read-only attribute and ask the folder owner for Modify permission (including rename/delete) on this file and the Updates folder. A program holding the file open can also block replacement. Replace: $($replaceFailure.Message) Rename: $renameFailure"
}

$source=[IO.Path]::GetFullPath($FeedDirectory)
$manifest=Join-Path $source 'dashboard-latest.json'
$feed=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if($feed.schemaVersion -ne 1 -or $feed.productId -cne 'Doka.EngineeringDashboard.Desktop' -or $feed.revitVersion -ne 2024 -or $feed.bridgeProtocol -ne 1 -or $feed.version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$' -or $feed.archiveFile -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$' -or $feed.sha256 -notmatch '^[0-9a-f]{64}$'){throw 'Invalid Dashboard update feed. Do not use legacy latest.json or an installer EXE here.'}
$archive=Join-Path $source $feed.archiveFile
if((Get-Item -LiteralPath $archive).Length -ne $feed.sizeBytes -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $feed.sha256){throw 'The Dashboard ZIP does not match its manifest.'}
if(!$PSCmdlet.ShouldProcess($Destination,"Publish Dashboard $($feed.version) ZIP, then dashboard-latest.json")){return}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$publicationLock=[IO.File]::Open((Join-Path $Destination '.dashboard-publish.lock'),'OpenOrCreate','ReadWrite','None')
try{
$latest=Join-Path $Destination 'dashboard-latest.json'
if(Test-Path -LiteralPath $latest){
    $existing=Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
    if($existing.productId -cne 'Doka.EngineeringDashboard.Desktop' -or $existing.revitVersion -ne 2024){throw 'Existing Dashboard feed belongs to a different product.'}
    if((NormalVersion $existing.version) -gt (NormalVersion $feed.version)){throw 'A newer Dashboard is already published.'}
    if((NormalVersion $existing.version) -eq (NormalVersion $feed.version) -and ($existing.sha256 -cne $feed.sha256 -or $existing.archiveFile -cne $feed.archiveFile)){throw 'This version was already published with different contents.'}
}
$target=Join-Path $Destination $feed.archiveFile
if(Test-Path -LiteralPath $target){if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $feed.sha256){throw 'The immutable Dashboard ZIP name already has different contents.'}}
else{
    $temporary=Join-Path $Destination ('.dashboard-'+[guid]::NewGuid().ToString('N')+'.tmp')
    try{Copy-Item -LiteralPath $archive -Destination $temporary;if((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $feed.sha256){throw 'Network copy checksum mismatch.'};Move-Item -LiteralPath $temporary -Destination $target}
    finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
}
$tempManifest=Join-Path $Destination ('.dashboard-feed-'+[guid]::NewGuid().ToString('N')+'.tmp')
try{
    Copy-Item -LiteralPath $manifest -Destination $tempManifest
    Publish-Manifest $tempManifest $latest
}
finally{if(Test-Path -LiteralPath $tempManifest){Remove-Item -LiteralPath $tempManifest}}
Write-Output "Published Dashboard $($feed.version). Existing latest.json and Revit installer updates were not changed."

}finally{$publicationLock.Dispose()}
