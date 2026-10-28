# Beam worker integration and audit fixes — 28 October 2026

This continues the controlled beam-search measurements in
[combined-beam-20261028.md](combined-beam-20261028.md). Those earlier .NET 10
numbers measured a separate console experiment; they did not include a worker.

## Integration

The Revit 2024 add-in and WPF dashboard remain on .NET Framework 4.8. Normal
beam calculations send detached inputs to a separate .NET 10 process. Combined
drawings reuse one process across their sequential areas. Revit API calls stay
in the host; the worker has no Revit dependency.

The worker compiles the same numerical source files as the host. A versioned,
bounded transport checks a source manifest before accepting results. It copies
the complete model graph, shared member references and private beam-response
coefficients, so loading, shear, moment and deflection diagrams use the returned
solution without another search. No engineering limit, project stock permission,
search cutoff or ranking preference changes as part of this integration.

Cancellation stops the owned worker and does not start a replacement local
search. A missing, incompatible, crashed or malformed worker records the reason
in Diagnose and uses the existing .NET Framework solver. Specialized searches
with callbacks or prepared in-process state retain that existing path. Revision
checks continue to reject results after the user changes inputs.

The worker publish script produces a self-contained Windows x64 payload. Future
approved installer builds include it and back up the previous worker during an
upgrade. This work does not itself authorize an installer, installation or merge.

## Confirmed Top50 audit findings

Pattern enumeration was repeated for identical wall/catalog/filler/work inputs
across percentage seeds and inventory solves. One calculation now owns an exact
pattern cache. Entries do not cross projects or calculations; eviction only
recomputes entries. Candidate order, native allocation and complete ranked
proposals are unchanged. The regression fixture reduces 30 preparations to 6,
with 24 exact reuse hits. This is an enumeration count, not a claimed overall
runtime reduction.

Cancellation could call OR-Tools StopSearch before its native wrapper existed.
The callback could therefore be consumed while the subsequent solve continued.
Model preparation now checks cancellation, and cancellation repeats StopSearch
until Solve exits under the wrapper's synchronization lock. The repeated stop
thread exists only after cancellation. Cancelled results remain discarded.

## Verification

The review must cover the actual .NET Framework host to self-contained worker
path on Windows, including startup, transport, result validation and full diagram
responses. Measurements belong to that test harness; Revit model extraction,
canvas drawing and an actual Revit 2024 acceptance run are separate.

Local review has built the production net48 add-in, worker and test harness with
zero warnings/errors, and verified identical source manifests. Fourteen beam
regression suites pass. An independent full worker run preserves all five area
fingerprints and 494 member reaction/diagram responses. Focused checks cover real
normal/combined view-model routes, H20/WS10, transferred/independent beams,
custom/catalogue permissions, invalid transport, fallback, startup/active
cancellation and stale-result rejection.

The new Top50 suite passes 67 checks on each runtime on Windows, including forced
native-start cancellation races and exact cached/uncached ranked proposals.
The same job passes 103 production planner/dashboard checks, with installer
steps skipped. Evidence: commit `b035f4f3e5345b19d62755b66169fc80d77e8a97`,
[workflow 40739801765](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40739801765).
Existing 397 Top50 quantity/history/persistence checks also pass locally.

Windows worker evidence is recorded with the reviewed commit after the CI run
completes. Actual Revit 2024 host acceptance remains unrun.
