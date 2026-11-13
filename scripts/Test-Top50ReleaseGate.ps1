[CmdletBinding()]
param([string]$ApprovalFile='packaging/top50-engineering-release.json')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$path=Join-Path $repo $ApprovalFile
if(!(Test-Path -LiteralPath $path)){
    Write-Output 'Installer gate closed: implementation and independent acceptance are still in progress.'
    if($env:GITHUB_OUTPUT){'approved=false' | Add-Content -LiteralPath $env:GITHUB_OUTPUT}
    exit 0
}
$approval=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if($approval.status -ne 'approved' -or $approval.validatedCommit -notmatch '^[0-9a-f]{40}$' -or $approval.target -ne 'Revit2024 Dashboard'){
    throw 'The Top50 release approval record is incomplete.'
}
foreach($report in @('docs/top50-engineering-review/final-qa.md','docs/top50-engineering-review/site-acceptance.md','docs/top50-engineering-review/final-validation.md')){
    if(!(Test-Path -LiteralPath (Join-Path $repo $report))){throw "Missing final acceptance report: $report"}
}
& git -C $repo merge-base --is-ancestor $approval.validatedCommit HEAD
if($LASTEXITCODE -ne 0){throw 'The independently validated source is not an ancestor of this release.'}
# Approval can add evidence documents, but no executable source, tests, build or
# installer configuration may change after the validated revision.
& git -C $repo diff --exit-code $approval.validatedCommit HEAD -- . ':!docs/**' ":!$ApprovalFile"
if($LASTEXITCODE -ne 0){throw 'Source or packaging changed after validation. Repeat acceptance before building an installer.'}
[xml]$properties=Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw
if($approval.version -ne $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText){throw 'Release version differs from the approved version.'}
Write-Output "Installer gate approved: $($approval.version), validated source $($approval.validatedCommit)."
if($env:GITHUB_OUTPUT){'approved=true' | Add-Content -LiteralPath $env:GITHUB_OUTPUT}
