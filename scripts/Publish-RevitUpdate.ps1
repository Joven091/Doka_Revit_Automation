# Publishes a reviewed Revit 2024 installer. Run from Windows with write access to the feed.
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [Parameter(Mandatory)][string]$ReleaseNotes,
    [string]$Destination = '\\DOKAAREDUBA\ApplDat\MEA\ENG\Joven\BeamCalculator\Updates'
)
$ErrorActionPreference = 'Stop'
function NormalVersion([string]$Text) {
    $v = [version]$Text
    return [version]::new($v.Major, $v.Minor, [Math]::Max(0,$v.Build), [Math]::Max(0,$v.Revision))
}
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
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$details = [Diagnostics.FileVersionInfo]::GetVersionInfo($Installer)
$fileVersion = [version]::new($details.FileMajorPart,$details.FileMinorPart,$details.FileBuildPart,$details.FilePrivatePart)
if ($fileVersion -ne (NormalVersion $Version)) { throw 'Installer file version does not match the requested release version.' }
if ($ReleaseNotes.Length -gt 4000) { throw 'Release notes must be 4000 characters or fewer.' }
$size = (Get-Item -LiteralPath $Installer).Length
if ($size -le 0 -or $size -gt 512MB) { throw 'Installer size is invalid.' }
$hash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
$name = "DokaRevitAutomation-$Version.exe"
if (-not $PSCmdlet.ShouldProcess($Destination, "Publish Revit 2024 version $Version")) { return }
[IO.Directory]::CreateDirectory($Destination) | Out-Null
# Serialize publishers and prevent a stale release replacing a newer one.
$lock = $null
$temporary = Join-Path $Destination ('.publish-' + [guid]::NewGuid().ToString('N'))
$phase = 'opening the publisher lock'
try {
    $lock = [IO.File]::Open((Join-Path $Destination '.publish.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $latest = Join-Path $Destination 'latest.json'
    $phase = "checking '$latest'"
    if (Test-Path -LiteralPath $latest) {
        if (([IO.File]::GetAttributes($latest) -band [IO.FileAttributes]::ReadOnly) -ne 0) {
            throw "'$latest' is marked Read-only. Clear that attribute if you manage the feed, or ask its owner to do so, then run publication again."
        }
        $previous = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
        if ($previous.productId -ne 'Doka.RevitAutomation' -or $previous.revitVersion -ne 2024) { throw 'Existing feed is for a different product.' }
        if ((NormalVersion $previous.version) -gt (NormalVersion $Version)) { throw 'Publishing an older version is not allowed.' }
        if ((NormalVersion $previous.version) -eq (NormalVersion $Version) -and $previous.sha256 -ne $hash) { throw 'This version is already published with a different installer. Increase the release version.' }
    }
    $target = Join-Path $Destination $name
    $phase = "copying and verifying '$target'"
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'This versioned installer already exists with different contents.' }
    } else {
        Copy-Item -LiteralPath $Installer -Destination $temporary
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'Network copy checksum failed.' }
        [IO.File]::Move($temporary, $target)
    }
    $manifest = [ordered]@{
        schemaVersion=1; productId='Doka.RevitAutomation'; revitVersion=2024; version=$Version
        installerFile=$name; sha256=$hash; sizeBytes=$size; releaseNotes=$ReleaseNotes
        publishedUtc=[DateTime]::UtcNow.ToString('O')
    }
    [IO.File]::WriteAllText($temporary, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    # Publish metadata last. Readers see either the complete old or complete new release.
    $phase = "publishing '$latest'"
    Publish-Manifest $temporary $latest
    Write-Output "Published Revit 2024 version $Version to $Destination"
} catch {
    throw "Publication failed while $phase. $($_.Exception.Message)"
} finally {
    try {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -ErrorAction Stop }
    } catch { Write-Warning "Could not remove temporary file '$temporary'. $($_.Exception.Message)" }
    finally { if ($lock) { $lock.Dispose() } }
}
