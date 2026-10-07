# Cross-beam support validation

The draft implements Automatic, Existing supports only, Dedicated towers and explicit Legacy slab rows behavior. Cross supports are evaluated on complete network candidates before ranking. Main's primary UDL changes from PR #113 are retained.

## Checks completed in this environment

- Repository guidance, network optimizer, transverse and primary calculations, workspace drawings and XML persistence reviewed before changes.
- Project files and calculator XAML parsed as XML; workflow files parsed as YAML.
- `git diff --check` passed.
- `Microsoft.NETFramework.ReferenceAssemblies` preserved in add-in, net48 regressions and UI smoke projects. No Revit API operations, threading or transactions changed.

## Checks blocked

The numeric regression and compilation commands were attempted, but the execution environment has no `dotnet` executable. SDK installation was attempted through the configured network proxy, which refused the connection. The GitHub Windows workflow also failed before its first step started; no build/test log was produced. There is no successful numeric, add-in build or WPF smoke result for this draft yet.

Run these before approving engineering use:

```text
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release -- --cross-supports
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release -- --primary-udl
dotnet build src/MyRevitAddin.csproj -c Release --warnaserror
dotnet build tests/Beam.Net48/Beam.Net48.csproj -c Release --warnaserror
dotnet build tests/Beam.UiSmoke/Beam.UiSmoke.csproj -c Release --warnaserror
```

On Windows, run `tests/Beam.Net48/bin/Release/net48/Beam.Net48.exe --cross-supports` and `tests/Beam.UiSmoke/bin/Release/net48/Beam.UiSmoke.exe`. These are also included in the repository's regression/smoke workflows.

## Added regression coverage (not executed here)

`CrossSupportTests` covers zero/positive/negative filler gaps, unequal soffits, direct surface contact, actual stock-tail overlaps, shared reactions, local primary increments, receiving-member/leg checks, concrete/slab load conservation, multiple parallel H20s, combinations of crossings, automatic tower fallback after geometry or structural rejection, dedicated quantities, permitted custom stock, missing stock, overloads, slab exclusions, saved elevations/modes and migration/resaving of legacy inputs. An independent simply-supported UDL equilibrium check exercises the conservative nine-increment stage envelope and leg rejection.

`CrossSupportUiTests` covers section rendering and cross-member targets, filler heights, actual load/response plots, workspace H20/leg quantities and editable bottom elevation. Existing crossing-row regression fixtures explicitly use Legacy slab rows; new crossings still default to Automatic.

## Remaining model limits

Filler height is a geometric gap; filler bearing strength, restraint and stability are not checked. Sheathing, bearing/connection details, tower braces/hardware, overall stability and foundations require separate design. Clash solids include H20 stock, concrete, filler blocks and tower leg columns. Dedicated tower stock covers the complete clear bay. The search uses a bounded set of main layouts, cross stocks and dedicated secondary spacings, so failure does not prove that no other engineered arrangement exists. Stage combinations are enumerated up to eight shared crossings per receiving beam; larger sets use conservative moment/shear/deflection and signed reaction bounds. Response diagrams for larger sets show actual selected stages, with conservative bounds separately identified in the schedules. Reported support movement is an estimate without a project acceptance tolerance.
