# Coordinating imported beam support layouts

An imported drawing can contain several independently designed slab areas and column-to-column beams. Previously every part could pass its member checks, then the final combined check rejected the first physical clash without searching another arrangement.

The combined search now retains passing parts and retries the two parts involved in a collision, starting with an isolated beam when available. Each retry receives the other parts' actual primary stocks, receiving secondaries, tower legs, frame reservations and concrete as fixed obstacles, transformed into its own coordinates. The original member/load/capacity checks still determine acceptance. Standalone engineer candidates are screened geometrically before numerical evaluation; network receiver and complete-candidate checks enforce the same constraints. A subsequent centring pass cannot reintroduce an external collision.

Only a fully checked replacement that clears every fixed neighbor is committed. Failed retries retain the previous child results for diagnosis; cancellation or changed inputs cannot accept a partial combined solution. Successful replacements cannot introduce new inter-part clashes, so already repaired parts need not be recalculated. A bounded-search failure remains a search limitation, not proof of physical impossibility.

Diagnostics list the detected pairs, each retry duration and result, and the number of original solutions retained. The new obstacle geometry uses the existing exact worker graph transport; runtime spatial-index caches are excluded. The host and self-contained worker must be built from matching search sources.

The October 29 captured selection is a regression fixture: 23 beams, 12 slabs and 12 columns produce eleven calculation parts. The original first failure is beam 523915's primary against Area 5's B1 tower leg. Three further clashes occur after that first one. Four isolated-beam retries clear all four pairs while retaining all five slab-area solutions and two other beams. The 30 kN permissible leg load and original standalone design UDLs are retained. Changed support positions/orientation are reflected in the diagrams and revised reactions.

Validation commands:

```
dotnet run --project tests/Beam.Tests -c Release -- --combined-constraints
dotnet run --project tests/Beam.Tests -c Release -- --combined-network-constraints
dotnet run --project tests/Beam.Tests -c Release -- --combined-coordination
dotnet run --project tests/Beam.Tests -c Release -- --revit-topology
```

The Windows coordination workflow compares the unchanged baseline with this version using the same captured geometry, .NET Framework 4.8 host and matching self-contained .NET 10 workers. It compares exact numerical fingerprints for all five retained slab areas. Actual Autodesk Revit 2024 host acceptance remains a separate check.
