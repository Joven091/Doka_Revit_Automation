[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[switch]$NoRestore)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
$arguments=@('publish',(Join-Path $repo 'tools/BeamSolver.Worker/BeamSolver.Worker.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-p:PublishTrimmed=false','-p:PublishSingleFile=false','--output',$output,'--warnaserror')
if($NoRestore){$arguments+='--no-restore'}
& dotnet @arguments
if($LASTEXITCODE -ne 0){throw 'The self-contained .NET 10 beam solver could not be published.'}
foreach($name in @('Doka.BeamSolver.exe','Doka.BeamSolver.dll','Doka.BeamSolver.runtimeconfig.json','Doka.BeamSolver.deps.json','coreclr.dll','BeamSolver.SourceHashes')){
    if(!(Test-Path -LiteralPath (Join-Path $output $name))){throw "Missing beam worker runtime file: $name"}
}
$hostIdentity=Join-Path $repo 'src/bin/Release/net48/BeamSolver.SourceHashes'
if(Test-Path -LiteralPath $hostIdentity){
    if((Get-FileHash -LiteralPath $hostIdentity -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $output 'BeamSolver.SourceHashes') -Algorithm SHA256).Hash){throw 'The Revit host and beam worker were built from different solver source. Rebuild both before packaging.'}
}
Write-Output "Published matching self-contained Windows x64 beam solver to $output"
