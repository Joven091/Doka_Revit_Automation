# Cross-beam support validation

The draft implements Automatic, Existing supports only, Dedicated towers and explicit Legacy slab rows behavior. Cross supports are evaluated on complete network candidates before ranking. Main's primary UDL changes from PR #113 are retained.

Slab H20 feet now require equal setbacks from both concrete faces and equal stock-end overhangs, repeated at every row in each bay. This is mandatory during recalculation, including legacy crossings. Unequal construction-stage loads and individual receiving-member/leg reactions remain checked; reactions are not averaged.

## Validation results

Validation uses .NET SDK 10.0.401 on Linux (7 October 2026). The SDK was installed from the official archive through the approved network path after the earlier installation attempts failed.

- The full .NET 10 numeric suite passes locally and on Windows CI, including 121 cross-support checks, 457 symmetric slab-support checks, 590 primary-UDL checks and 242 search-reuse checks. The reported 9,900 mm primary-UDL scenario also passes with 48 legs and a 35.599 kN peak leg reaction.
- The Revit net48 add-in, net48 numeric regression project and WPF UI smoke project compile locally and on Windows CI with `--warnaserror`: zero warnings and errors. Linux UI smoke compilation uses `-p:EnableWindowsTargeting=true`.
- The D3 slab suite passes 528 checks after its linked-source project includes `CrossBeamSupport.cs`.
- The controlled network benchmark passes 24,268 layout, exact-demand, load-conservation, capacity and cancellation checks locally. Its load assertions distinguish physical covered quantities, full-spacing secondary design loads and the full-stock primary UDL model.
- `Microsoft.NETFramework.ReferenceAssemblies` remains in every net48 project. No Revit API operations, threading or transactions were changed.
- Project/XAML XML parsing, workflow YAML parsing and `git diff --check` pass.

The cross-support passing fixture now uses 100 mm receiving rows: three individually checked parallel H20s carry the 400 mm cross-beam width. Its original 200 mm spacing admits only one member within the footprint including the 80 mm flange, and that member fails the stock-overhang L/350 check. The original layout remains an explicit rejection regression; structural limits are retained. A separate narrow cross-beam fixture verifies a passing single-H20 arrangement.

The passing dedicated-tower fixture uses a -1,400 mm soffit to clear the existing main primaries. The original -1,000 mm fixture intersects those primaries with cross-beam concrete and remains a clash rejection test. Local primary reaction increments are checked against independent transverse static equilibrium at every distributed-load interval, both for one crossing and for combined crossings. Older primary-UDL crossing fixtures explicitly request Legacy slab rows. Cache regressions accept reuse within a beam solve or across receiving-beam solves, while still requiring fewer actual analyses and identical unrounded results.

The new UI smoke test returns its check count to the smoke runner, correcting its compile-time return-type mismatch. Numeric test failures now print the complete optimizer diagnosis, including geometry and cross-member failures, alongside the exception stack.

## Windows runtime validation

The [Windows primary/cross/symmetry job](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/37611930201) passes .NET 10 and .NET Framework 4.8 primary-UDL checks, both reported-case runs, net48 cross-support and symmetry checks, compilation with warnings treated as errors, and the complete WPF smoke executable. It reports 294 beam WPF checks and 143 drawing-workspace checks, including cross sections, filler heights, actual member diagrams and the bottom-elevation editor. The workspace cancellation regression waits for the queued dispatcher refresh before checking its controls.

The [Windows calculator regression job](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/37611930213) also passes the add-in build, full beam numeric suite, network economy/reuse regressions on .NET 10 and net48, D3 slab numeric/UI tests and the other existing calculator checks. These runs validate code commit `262dc8b7ce60919fe72ba62bd850d60f0759e6c5`, subsequently merged in PR #114 by the repository owner. This agent did not merge that PR. The remaining benchmark-assertion update is submitted separately for draft review.

Commands to repeat validation:

```text
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release -- --cross-supports
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release -- --primary-udl
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release -- --symmetric-slab-supports
dotnet build src/MyRevitAddin.csproj -c Release --warnaserror
dotnet build tests/Beam.Net48/Beam.Net48.csproj -c Release --warnaserror
dotnet build tests/Beam.UiSmoke/Beam.UiSmoke.csproj -c Release --warnaserror
```

On Windows, run `tests/Beam.Net48/bin/Release/net48/Beam.Net48.exe --cross-supports`, `tests/Beam.Net48/bin/Release/net48/Beam.Net48.exe --symmetric-slab-supports` and `tests/Beam.UiSmoke/bin/Release/net48/MyRevitAddin.exe`. These are also included in the repository's regression/smoke workflows.

## Regression coverage

`CrossSupportTests` covers zero/positive/negative filler gaps, unequal soffits, direct surface contact, actual stock-tail overlaps, shared reactions, local primary increments, receiving-member/leg checks, concrete/slab load conservation, multiple parallel H20s, combinations of crossings, automatic tower fallback after geometry or structural rejection, dedicated quantities, permitted custom stock, missing stock, overloads, slab exclusions, saved elevations/modes and migration/resaving of legacy inputs. An independent simply-supported UDL equilibrium check exercises the conservative nine-increment stage envelope and leg rejection.

`CrossSupportUiTests` covers section rendering and cross-member targets, filler heights, actual load/response plots, workspace H20/leg quantities and editable bottom elevation. Existing crossing-row regression fixtures explicitly use Legacy slab rows; new crossings still default to Automatic.

`SymmetricSlabSupportTests` covers asymmetric fixed-plan rejection, reversed symmetric plans, single-beam and network slab support symmetry, unequal main beams, repeated setbacks, equal uniform-load foot reactions, independent receiving-secondary equilibrium, one-sided slab stages, actual peak-leg capacity, primary UDL retention, load conservation and quantities. An independent partial-load statics example demonstrates that averaging unequal reactions would conceal an overload. Cross support and WPF regressions also assert the symmetric slab layout and site-setting-out summary.

## Remaining model limits

Filler height is a geometric gap; filler bearing strength, restraint and stability are not checked. Sheathing, bearing/connection details, tower braces/hardware, overall stability and foundations require separate design. Clash solids include H20 stock, concrete, filler blocks and tower leg columns. Dedicated tower stock covers the complete clear bay. The search uses a bounded set of main layouts, cross stocks and dedicated secondary spacings, so failure does not prove that no other engineered arrangement exists. Stage combinations are enumerated up to eight shared crossings per receiving beam; larger sets use conservative moment/shear/deflection and signed reaction bounds. Response diagrams for larger sets show actual selected stages, with conservative bounds separately identified in the schedules. Reported support movement is an estimate without a project acceptance tolerance.
