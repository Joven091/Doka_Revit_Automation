[CmdletBinding()]
param([switch]$RequireConfiguredProject)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Run this validation on Windows: the client uses Windows DPAPI and WPF.'
}
foreach ($tool in @('dotnet', 'node')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Install $tool before running validation."
    }
}
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if ($RequireConfiguredProject) {
        $settings = Get-Content 'src/access-settings.json' -Raw | ConvertFrom-Json
        $projectUri = $null
        if (-not [Uri]::TryCreate($settings.project_url, [UriKind]::Absolute, [ref]$projectUri) -or
            $projectUri.Scheme -ne 'https' -or $projectUri.AbsolutePath -ne '/' -or
            $projectUri.UserInfo -or $projectUri.Query -or $projectUri.Fragment -or
            -not $settings.publishable_key.StartsWith('sb_publishable_')) {
            throw 'Populate src/access-settings.json with the deployed project URL and publishable key.'
        }
    }

    foreach ($testFile in Get-ChildItem 'supabase/functions/authorize-tool/*.test.js') {
        & node $testFile.FullName
        if ($LASTEXITCODE -ne 0) { throw "Backend access tests failed: $($testFile.Name)" }
    }
    & dotnet build tests/Access.Tests/Access.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Access client test build failed.' }
    & './tests/Access.Tests/bin/Release/net48/Access.Tests.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Access client tests failed.' }
    & dotnet build src/MyRevitAddin.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Revit add-in build failed.' }

    $copiedSettings = 'src/bin/Release/net48/access-settings.json'
    if (-not (Test-Path $copiedSettings) -or
        (Get-FileHash $copiedSettings).Hash -ne (Get-FileHash 'src/access-settings.json').Hash) {
        throw 'Public access settings were not correctly copied to the build output.'
    }
    Write-Host 'Backend tests, Windows client tests, and Revit add-in build passed.'
    Write-Host 'Verify sign-in, approvals, revocation, and cancellation in Revit before distribution.'
}
finally { Pop-Location }
