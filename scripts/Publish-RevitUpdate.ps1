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
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$details = [Diagnostics.FileVersionInfo]::GetVersionInfo($Installer)
if ((NormalVersion $details.FileVersion) -ne (NormalVersion $Version)) { throw 'Installer file version does not match the requested release version.' }
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
try {
    $lock = [IO.File]::Open((Join-Path $Destination '.publish.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $latest = Join-Path $Destination 'latest.json'
    if (Test-Path -LiteralPath $latest) {
        $previous = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
        if ($previous.productId -ne 'Doka.RevitAutomation' -or $previous.revitVersion -ne 2024) { throw 'Existing feed is for a different product.' }
        if ((NormalVersion $previous.version) -gt (NormalVersion $Version)) { throw 'Publishing an older version is not allowed.' }
        if ((NormalVersion $previous.version) -eq (NormalVersion $Version) -and $previous.sha256 -ne $hash) { throw 'This version is already published with a different installer. Increase the release version.' }
    }
    $target = Join-Path $Destination $name
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
    if ([IO.File]::Exists($latest)) { [IO.File]::Replace($temporary, $latest, $null) }
    else { [IO.File]::Move($temporary, $latest) }
    Write-Output "Published Revit 2024 version $Version to $Destination"
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    if ($lock) { $lock.Dispose() }
}
