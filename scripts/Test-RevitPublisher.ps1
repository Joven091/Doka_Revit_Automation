[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$publisher = Join-Path $PSScriptRoot 'Publish-RevitUpdate.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('doka-publisher-test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$script:checks = 0
function Check([bool]$Condition,[string]$Label) {
    if (-not $Condition) { throw $Label }
    $script:checks++
}
function Reject([scriptblock]$Action,[string]$Message) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $_.Exception.Message -like "*$Message*" }
    Check $rejected "Expected failure containing: $Message"
}
try {
    # Use a real versioned PE without rebuilding the product or changing its version.
    $fixture = (Get-Process -Id $PID).Path
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($fixture)
    $version = "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart).$($info.FilePrivatePart)"
    $feed = Join-Path $root 'feed'
    & $publisher -Installer $fixture -Version $version -ReleaseNotes 'Initial release' -Destination $feed | Out-Null
    $latest = Join-Path $feed 'latest.json'
    $payload = Join-Path $feed "DokaRevitAutomation-$version.exe"
    Check ([IO.File]::Exists($latest) -and [IO.File]::Exists($payload)) 'First publication creates manifest and installer'
    $manifest = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
    Check ($manifest.sha256 -eq (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()) 'Published payload checksum matches'
    & $publisher -Installer $fixture -Version $version -ReleaseNotes 'Same reviewed installer' -Destination $feed | Out-Null
    Check ((Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).releaseNotes -eq 'Same reviewed installer') 'Repeat publication replaces existing manifest'
    $good = [IO.File]::ReadAllText($latest)
    $originalAttributes = [IO.File]::GetAttributes($latest)
    [IO.File]::SetAttributes($latest, ($originalAttributes -bor [IO.FileAttributes]::ReadOnly))
    try {
        Reject { & $publisher -Installer $fixture -Version $version -ReleaseNotes 'Blocked' -Destination $feed } 'marked Read-only'
        Check ([IO.File]::ReadAllText($latest) -eq $good) 'Read-only manifest is left intact'
    } finally { [IO.File]::SetAttributes($latest, $originalAttributes) }
    # Load the same shipping function definitions, then inject only OS failures.
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($publisher,[ref]$tokens,[ref]$errors)
    Check ($errors.Count -eq 0) 'Shipping script parses on this PowerShell runtime'
    foreach ($definition in $ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.FunctionDefinitionAst] }) {
        . ([scriptblock]::Create($definition.Extent.Text))
    }
    $originalReplace = ${function:Invoke-ManifestReplace}
    $originalRename = ${function:Invoke-ManifestRename}
    function Invoke-ManifestReplace([string]$Source,[string]$Target) { throw [UnauthorizedAccessException]::new('Simulated SMB ReplaceFile restriction') }
    $temporary = Join-Path $feed '.test-manifest'
    [IO.File]::WriteAllText($temporary, $good.Replace('Same reviewed installer','Atomic rename fallback'))
    $access = [Security.AccessControl.AccessControlSections]::Access
    $aclBefore = (Get-Acl -LiteralPath $latest).GetSecurityDescriptorSddlForm($access)
    Publish-Manifest $temporary $latest
    Check ((Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).releaseNotes -eq 'Atomic rename fallback') 'Denied ReplaceFile falls back to a complete atomic rename'
    Check (-not [IO.File]::Exists($temporary)) 'Successful rename consumes temporary source'
    Check ((Get-Acl -LiteralPath $latest).GetSecurityDescriptorSddlForm($access) -eq $aclBefore) 'Existing manifest access rules are preserved'
    $good = [IO.File]::ReadAllText($latest)
    function Invoke-ManifestRename([string]$Source,[string]$Target) { throw [ComponentModel.Win32Exception]::new(5,'Simulated denied rename') }
    [IO.File]::WriteAllText($temporary,'must never be advertised')
    Reject { Publish-Manifest $temporary $latest } 'Modify permission'
    Check ([IO.File]::ReadAllText($latest) -eq $good -and [IO.File]::Exists($temporary)) 'Denied replacement leaves old complete manifest and staged source untouched'
    Set-Item Function:Invoke-ManifestReplace -Value $originalReplace
    Set-Item Function:Invoke-ManifestRename -Value $originalRename
    Remove-Item -LiteralPath $temporary
    $held = [IO.File]::Open($latest,'Open','Read','Read')
    try {
        Reject { & $publisher -Installer $fixture -Version $version -ReleaseNotes 'Locked' -Destination $feed } 'Cannot replace'
        Check ([IO.File]::ReadAllText($latest) -eq $good) 'Locked destination is never deleted or truncated'
    } finally { $held.Dispose() }
    $lock = [IO.File]::Open((Join-Path $feed '.publish.lock'),'Open','ReadWrite','None');$lock.Dispose()
    Check (@(Get-ChildItem -LiteralPath $feed -Filter '.publish-*' -Force).Count -eq 0) 'Failed publication releases lock and cleans temporary files'
    & $publisher -Installer $fixture -Version $version -ReleaseNotes 'Retry after unlock' -Destination $feed | Out-Null
    Check ((Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).releaseNotes -eq 'Retry after unlock') 'Publication can be retried with the original installer'
    $good = [IO.File]::ReadAllText($latest)
    $changed = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
    $changed.version = "$($info.FileMajorPart+1).0.0"
    [IO.File]::WriteAllText($latest,($changed | ConvertTo-Json))
    Reject { & $publisher -Installer $fixture -Version $version -ReleaseNotes 'Older' -Destination $feed } 'older version'
    [IO.File]::WriteAllText($latest,$good)
    [IO.File]::WriteAllText($payload,'different installer contents')
    Reject { & $publisher -Installer $fixture -Version $version -ReleaseNotes 'Conflict' -Destination $feed } 'different contents'
    Check ([IO.File]::ReadAllText($latest) -eq $good) 'Conflicting version cannot alter current release'
    Write-Output "Passed $script:checks publisher checks on PowerShell $($PSVersionTable.PSVersion)."
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
