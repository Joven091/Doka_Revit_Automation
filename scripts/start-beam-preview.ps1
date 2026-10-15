$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\Beam.UiSmoke\Beam.UiSmoke.csproj'
dotnet run --project $project -c Release -- --preview
if ($LASTEXITCODE -ne 0) { throw "Standalone preview exited with code $LASTEXITCODE" }
