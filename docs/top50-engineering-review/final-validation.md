# Final engineering and software validation — supervisor record

Target: Engineering Dashboard 1.1.27, Revit 2024 x64/net48. Top50 remains an
integrated planner. Project format V5; search policy V4.

**Supervisor decision: APPROVED FOR PACKAGING, 28 October 2026.** Independent QA
and practical engineering acceptance both pass for the exact source below. All
approved application requirements are met, with no known release-blocking defect
in the stated planning scope. Installer creation may now begin. Final package
distribution acceptance still requires the separate installed-application checks.

## Scope and supervision

The user's multi-agent request authorizes routine supervisory decisions and a
final installer after acceptance. Three independently executable agents performed
the site assessment, implementation and independent QA roles. The supervisor
approved the smallest evidenced correction set in the development plan. The
implementer did not approve its own release or build an installer.

Baseline local commit348f525ce019dd37a4aea38391ffc285baeec2fc, tree
171d0664aeda44df602bd66caddcd215b9a0086c, remains preserved in its original
checkout. Draft PR175 is stacked on draft PR174 to retain the newest reviewed
beam-worker/dashboard work. Neither draft is merged by this task.

## Acceptance matrix

| Approved task | Required evidence | Current disposition |
| --- | --- | --- |
| AP-01 actual identical-kit allocations | Independent fixed-stock exact cases; original mixed-length witness; required-ID reconstruction | PASS |
| AP-01b bounded kit seed completion | Four original no-offer reproductions; independent tiny minimum-count proofs; mixed-length sweep; ordinary signature preservation | PASS |
| AP-02 effective method settings | Fixed/percentage/fixed transition; percentage ignores stored kit preference | PASS, including native controls and captures |
| AP-03 project-policy cache validation | Catalog/band/actual kit violations; valid V1–V5; source/geometry atomicity | PASS |
| AP-04 concise context and terminal outcomes | Native first-no-result/cancel/lateCancel; three-scale views; direct/filler fixture; site review | PASS, including independent cancellation follow-up |
| AP-05 numeric validation | Integer/range rejection and rollback; decimal percentages retained | PASS |
| AP-06 final provenance and package | Source/dependencies/checksums; gated installer; isolated install, actual DLL/worker, restart, backup/upgrade, uninstall | Provenance/staged payload PASS; installer creation now authorized, package checks follow |

## Corrections found during validation

The first Windows acceptance run exposed a fast-search cancellation status race.
Completion now owns terminal status; a returned-but-cancelled result explicitly
reports cancellation, and a late/idle Cancel cannot overwrite a completed
outcome. Independent QA separately exercised the returned-result/UI-continuation
branch in a held synchronization context, beyond the implementer's regressions.

The next Windows run passed cancellation but exposed a test-order defect: visual
controls under a ScrollViewer were inspected before template layout. The test now
measures/arranges/renders first; application behavior was not changed to satisfy
the test. Separate native settings captures verify both offering methods and
the stored identical-set preference without modifying the main result fixture.

## Exact accepted evidence and supervisor review

- Validated remote source: `1e0c81e3a00dab3ae9ddc0211c58a0a3973bab22`.
  Corresponding local commit: `61f6877d1d6b39f8d488f6cc9466a182ca042623`.
  Identical source tree: `45cfaa3fe49d51fd7dc8b41ea0f6322edc9f2061`.
- Successful Windows run [40759276277](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40759276277),
  job `123066499687`, artifact `12847278138`, ZIP SHA-256
  `e937c5a44dd954ff5c9f09918520f5744b10f93b265c1b66aa75774045595b92`.
  The supervisor downloaded, verified and safely extracted the artifact, inspected
  the actual current results/casting/settings/outcome images, full job log,
  diagnostics, process results and payload provenance.
- [Independent QA](final-qa.md): PASS and packaging recommended. Independent
  exact acceptance has 10,888 assertions on both Windows targets and Linux;
  includes 756 pooled-stock exact models, 120 fixed-kit cases and 256 connector
  procurement vectors. The ordinary kit sweep covers 286 inputs/603 offers,
  zero empty inputs and 12,729 independent assertions. Existing 402 and 84 checks
  pass. An independent held-continuation cancellation probe adds 15 checks.
- Native planner checks: 114; dashboard: 22; Revit source/reader/refresh/highlight:
  33 using test-only API stand-ins. Clean production/test builds pass. The separate
  planner workflow `40759276182` also passes, including release-entry locks.
- Actual staged production payload: 8 first-process and 9 fresh-process checks
  pass in a path containing spaces without generated binding redirects. The
  two-wall fixture has eight physical panels, 2.25 LM per face, one direct and one
  250 mm filler joint per face, and two standard/two adjustable connectors for one
  level on both faces. The actual bundled beam worker returns a complete result;
  silent Framework fallback is rejected. A fresh process reopens the saved project
  and executes both planner and worker again. No installer existed at this stage.
- Staged host: version1.1.27.0, MVID `32ede02d-cc62-46de-bb69-3110363237b1`,
  SHA-256 `bffe0a686dfdab41881a975d5940332253ac281c359521e36498de4b7263bd72`.
  Worker SHA-256 `14077b1291d3abbd791fcd33bef6f5af63b80c513a789c18ccd05e6311db76bc`.
- [Practical acceptance](site-acceptance.md): PASS after reviewing all 19 current
  planner captures and three staged captures, source context, process logs,
  exact provenance and independent hand arithmetic. No unresolved practical
  blocker was found in the approved preliminary-planning scope.

The representative 63-wall offer remains 46 physical panels, 99.25 LM per face,
10 groups, 79.042821% mean and 62.720403% minimum panel utilization. Two fresh
Windows processes take 4.226/4.247 seconds and produce the same signature after
input reordering and a culture change. Linux repeats its own valid tied solution;
cross-platform bitwise allocation/rank identity is not established. Observed times
are not deadlines or speed guarantees. No general global stock optimum is proven.

The supervisor accepts the evidence and the documented deferrals: manual zones,
full sequencing and speculative performance/history rewrites are not required
for this release. Quantity-first ranking, three presets, both offer methods,
two-face panels, per-face LM, one-level connectors and removed exports remain.
Project-specific assembly, concurrent-demand and structural checks stay outside
this preliminary planner. See [known limitations](known-limitations.md).

## Authorized final packaging and remaining platform boundary

The supervisor now authorizes packaging/top50-engineering-release.json for the
validated commit above. The release gate must reject executable/test/build/
packaging changes after that commit; only this evidence and the approval record
may be added. The existing Dashboard installer is the target, preserving the
bundled worker, updater, Steel Shape, D3 and Revit integration.

The next required stage is isolated installer creation and verification of the
installed actual DLL/worker, native planner/dashboard, restart, upgrade from
1.1.26 with recursive worker/settings backups, guarded registration and uninstall.
Final distribution acceptance depends on those checks, recorded in a subsequent
release addendum. Rebuilding after a documentation-only approval commit can change
generated commit/version metadata and binary hashes; the final package records
its own source and hashes and is retested separately.

Actual Revit 2024 host acceptance and a completely clean-machine prerequisite
test remain unperformed. WPF tests and API stand-ins do not substitute for them.
No company share publication, real user-machine installation or GitHub merge is
authorized by this record.
