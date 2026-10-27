# Native Top50 foundation

Base: `d1a6862f809f5054f0b20becb7ff4a74a7800585` (main), isolated local branch `codex/top50-native-foundation`. No commits, pushes, deployment or production installation. PR168 at `bb37debee36f9d6a9dbce79bc41f3d47bdb35b6b` is untouched; its launcher Revit-selection callback must be retained during eventual reconciliation.

Reference is the user-labelled original V1 recording, not rebuilt 1.0.1/1.0.2 source or visuals. Recording SHA-256: `56f1b4842231d7d724317c1123f6eae067494f14339d7c799557eb890cad5f35`. DXF SHA-256: `4de6bfde8c4a86d6de2997d07f6074ebed9d5d3e9a3d70198bc1974e097d52b5` (4,737,819 bytes), identical to the existing local reference.

## Scope and architecture

- Native WPF Top50 calculator owned by the Revit 2024 dashboard; no standalone executable launcher or embedded Qt window. Existing access gate stays in place.
- Isolated `EngineeringDashboard.Top50` domain, DXF audit, retained-pattern search, independent validation, ranking, persistence and workbook interfaces. No Revit API objects in background numerical work.
- Original five views: Import / Settings / Results / Casting groups / Reports. Navy sidebar, neutral main background, amber actions; Top50-local resources prevent global restyling of other calculators. Match reference pixels once decoded locally.
- Immutable calculation input snapshots, cancellation/progress, revision checks before accepting results; session state held by its own view model. Project/result persistence and nine-sheet workbook derive from the same selected snapshot.
- Keep the original terminology and common diagram scale. Do not substitute the rebuilt UI, sequential wall IDs or full-row-stretched bars.

## Confirmed numerical policy

Raw geometry is audited; the original compatibility measurement is rounded once to nearest 0.001 mm, half up, before feasibility. IDs are `DXF-<handle>`. Display rounding never feeds feasibility. DIMLFAC is annotation scale, not a physical multiplier. Original visible settings: five simultaneous longest walls, Balanced Optimization, all 18 supplied nominal sizes, no identical-kit requirement. Selected state does not establish defaults.

Retain up to four fewest-component multisets per precise length/catalog strategy. CP-SAT optimality/bounds apply only to that retained model. Pareto front first. Balanced ranks the equal mean of min-max normalized stock length, types, casting count, mean idle fraction, filler locations, worst-group idle fraction, and absolute target deviation across found candidates. Rank all found candidates before applying the requested display limit. Unknown catalog strategies, advanced caps, seeds, tie-breaks and search budgets remain explicit.

Observed S3: 27 panels, 96.250m, 11 groups, 74.12% average/35.58% minimum use; stock 0.50x1,0.75x1,2.00x5,3.50x5,4.50x15. This is a test fixture, never a stock constant in candidate generation. Matching its aggregate metrics does not establish complete assignment or original algorithm equivalence.

## Import interpretation issue

IxMilia successfully loads all 65 dimensions but its convenience ActualMeasurement property is zero for the inspected source records; the raw DXF audit must retain stored group 42 and handles explicitly. More importantly, ezdxf's get_measurement maps both rotated and aligned dimensions to its angle-projection routine. The two original flagged aligned rows have no angle and therefore project at zero degrees (0.2933835724mm/0mm), although their endpoint Euclidean distances agree approximately with stored 19200mm/9200mm. Preserve projected and aligned-distance evidence; flag these rows for review rather than declare physical geometry invalid or silently include two additional walls. This keeps the original 63-wall compatibility scenario without repeating an unlabelled measurement assumption.

## Acceptance and limits

Dependency spike: native net48 x64 OR-Tools solve; load 65 DXF dimensions; write/reopen Open XML workbook without Office. New core tests must cover rounding boundaries, stable IDs, audit conflicts, exact closure, internal-only fillers, per-group stock, every included wall exactly once, longest-wall simultaneity, cancellation and recorded ranking. Observed G03 provides a detailed partial assignment fixture. Actual all-group assignments/project/export are not available from the recording.

Still needed for exact parity: hidden advanced settings and candidate strategies, full saved original project or exported workbook, and additional original runs for other presets/percentage/identical-kit/error flows. Do not claim an original algorithm recovered from one screen recording. Actual Revit2024 host validation is separate; the local standard installation found earlier is Revit2023. No new year clarification is required: target remains 2024.

## Verified milestone — 2026-10-27

Implemented native five-view WPF tool, additive dashboard entry, source-audited DXF import/review, bounded general candidate discovery and CP-SAT allocation, independent validation, seven-criterion ranking, cancellation/revision guards, project persistence and nine-sheet Open XML export. No standalone launcher is used. Dependencies were validated on net48 x64. System.Memory is compiler-aliased to avoid extension-method collisions with existing beam source; no unrelated calculator source was changed.

Full add-in and isolated UI harness compile with zero warnings/errors. Core verification passed 33 checks plus a fresh end-to-end discovery check. Native UI/persistence/export verification passed 112 checks, including all five views at 100/125/150 percent, visible header/footer controls, source-cache/project round trips, stale-result export blocking and workbook schema validation. Dependency smoke verified native OR-Tools, 65 DXF dimensions and workbook write/reopen without Office.

Important numerical distinction: the recorded-stock general solve independently validates 11 groups but gives 74.0968 percent mean / 21.0390 percent minimum utilization. The recorded fixture reproduces 74.12 / 35.58 percent and 93 changes using all transcribed group memberships; unshown per-wall patterns are reconstructed, not recovered source. Fresh five-second general discovery ranked 95m/10 groups first, followed by 98.25m/9, 98.2m/9 and 96.25m/11. These are valid reconstructed-search outputs, NOT original algorithm parity. No golden inventory constant is used in production search.

Remaining acceptance: recover original hidden candidate strategies/settings or agree numerical equivalence criteria; interactive user-flow and visual comparison in actual Revit2024; native dependency coexistence in that process; final PR168 launcher reconciliation; release packaging. Original project/export and unrecorded flows remain unavailable. Tables use a row-review dialog rather than original direct cell editing; some styling and report formatting remain approximate. Stale results are retained in the active UI but omitted from saved result cache. ASCII DXF is supported; binary DXF is explicitly rejected. No Revit2024 host is installed locally (available host is2023), so host acceptance is UNRUN, not failed. No approval is pending.

Evidence is under ../verification/top50-core-tests.log, top50-production-build.log, top50-ui-tests.log, top50-net48-dependencies.log and top50-native-ui/*.png. Preview results use the clearly labelled test-only recorded fixture, not fresh optimization output. No commits, pushes, PRs, production installation or settings changes were performed.

## Extended native acceptance

The actual source-linked native HWND was exercised through routed navigation clicks (three cycles across five views), three window sizes (1000x680,1500x960,1900x1100), cancellation, retry, save and clean close. With the render/persistence checks this now totals 136 passing checks. This is automated WPF acceptance, not a human usability study or in-Revit run. A missing synchronization context in the initial harness exposed unsafe UI-notification access; ModelChanged now marshals through its window dispatcher and the harness uses a WPF dispatcher context. Rounded local button templates refine the original reference styling. Dirty modal discard/save-dialog branches still require interactive acceptance; they are not counted as passed.

Solver audit confirms model and independent validator both enforce five precise longest walls simultaneously, per-size reusable stock, exact micrometre closure and one assignment per included wall. Fillers are internal gaps only, bounded individually and by count. Retention traverses component cardinality ascending and caps at four patterns per length/catalog strategy. Ranking normalizes seven recorded costs over all found candidates before truncation. Differences remain attributable to unrecovered candidate strategy and allocation tie-break choices; no evidence establishes original seed or advanced budgets.

To resolve parity, request the original saved project with: exact included/acknowledged rows and corrections; units override; full catalog/offering/connector settings; expanded advanced limits, seed, timing and candidate strategies if serialized; complete candidate pool and selected ID. Original Excel should provide all wall/group assignments, precise filler positions and panel sequences, inventory, utilization formulas/denominators, panel-change metric, comparison including omitted S2, solver status/objective/bound and all nine sheet values. A workbook alone may not contain hidden search settings.

Launcher reconciliation remains an additive Top50 field/callback/card. Retain PR168's beamWindow.ReadRevitSelection assignment unchanged when applying; no PR branch was modified. Dependency coexistence is verified only inside isolated net48 harnesses, not the actual Revit process. Revit2024 native DLL/load-context coexistence remains unrun.

Desktop computer-use inspection of the isolated acceptance HWND was attempted. Window2232684 was located, but get_window/state returned 'Computer Use app approval timed out'; no UI input occurred. This is an approval timeout, not an application defect. Dirty modal dialog checks remain UNRUN. The disposable test process was cleaned up after verifying its exact executable path. Interactive harness --interactive mode is retained for later authorized acceptance. No user window was closed.
