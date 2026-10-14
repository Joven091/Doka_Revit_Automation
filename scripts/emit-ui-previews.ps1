param(
    [Parameter(Mandatory = $true)][string]$Directory,
    [Parameter(Mandatory = $true)][string[]]$Names
)

$ErrorActionPreference = 'Stop'
# Preserve the original PNG bytes for inspection through authenticated CI logs
# when an artifact download cannot be materialized in the review environment.
foreach ($name in $Names) {
    $path = Join-Path $Directory $name
    if (-not (Test-Path $path -PathType Leaf)) { continue }
    $encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
    Write-Output "UI_PREVIEW_BEGIN $name"
    for ($offset = 0; $offset -lt $encoded.Length; $offset += 2048) {
        Write-Output ('UI_PREVIEW_DATA ' + $encoded.Substring($offset, [Math]::Min(2048, $encoded.Length - $offset)))
    }
    Write-Output "UI_PREVIEW_END $name"
}
