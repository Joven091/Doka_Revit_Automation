[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $evidence | Out-Null
$root=Join-Path $env:LOCALAPPDATA ('Temp\Top50 payload review\'+[guid]::NewGuid().ToString('N'))
$payload=Join-Path $root 'payload';New-Item -ItemType Directory -Force $payload | Out-Null
$build=Join-Path $repo 'src/bin/Release/net48'
Get-ChildItem -LiteralPath $build -Filter '*.dll' -File | Copy-Item -Destination $payload
if(Test-Path -LiteralPath (Join-Path $payload 'RevitAPI.dll')){throw 'The staged dashboard must not distribute the Revit API.'}
Copy-Item -LiteralPath (Join-Path $build 'BeamSolver') -Destination $payload -Recurse
foreach($file in Get-ChildItem -LiteralPath (Join-Path $build 'BeamSolver') -Recurse -File){
    $relative=$file.FullName.Substring($build.Length+1)
    if((Get-FileHash $file.FullName).Hash -ne (Get-FileHash (Join-Path $payload $relative)).Hash){throw "Staged worker mismatch: $relative"}
}
$smoke=Join-Path $repo 'tests/Top50.InstalledReleaseSmoke/bin/Release/net48/Top50.InstalledReleaseSmoke.exe'
$runner=Join-Path $root 'Top50.InstalledReleaseSmoke.exe';Copy-Item -LiteralPath $smoke -Destination $runner
'<configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><probing privatePath="payload"/></assemblyBinding></runtime></configuration>' | Set-Content ($runner+'.config')
& $runner $evidence $payload *> (Join-Path $evidence 'first-process.log')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $evidence 'first-process.log');throw 'The staged production payload failed its native integration checks.'}
& $runner $evidence $payload --restart *> (Join-Path $evidence 'restart-process.log')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $evidence 'restart-process.log');throw 'The staged production payload failed fresh-process restart.'}
Get-Content (Join-Path $evidence 'first-process.log')
Get-Content (Join-Path $evidence 'restart-process.log')
@{kind='Staged production payload; no installer or Revit session';sourceCommit=(& git -C $repo rev-parse HEAD).Trim();version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'MyRevitAddin.dll')).FileVersion;hostSha256=(Get-FileHash (Join-Path $payload 'MyRevitAddin.dll')).Hash;workerSha256=(Get-FileHash (Join-Path $payload 'BeamSolver/Doka.BeamSolver.exe')).Hash} | ConvertTo-Json | Set-Content (Join-Path $evidence 'provenance.json')
