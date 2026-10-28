# Final engineering and software validation — supervisor record

Target: Engineering Dashboard 1.1.27, Revit 2024 x64/net48. Top50 remains an
integrated planner. Project format V5; search policy V4.

**Supervisor decision: application and isolated package validation PASS,
28 October 2026.** Independent QA and practical engineering acceptance pass for
the source below, and the final installer has passed the separate Windows package
checks recorded in the final addendum. All approved requirements are met within
the stated verification scope. Actual Revit-host and clean-machine prerequisite
acceptance remain unperformed. The earlier packaging decisions below preserve
the approval sequence; no installer preceded the application acceptance gate.

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

## Package-test path correction and renewed packaging approval

Run `40760797897` repeated all application validation successfully and opened the
approval gate. Its installer job `123073219968` compiled the installer, then the
first isolated installation was correctly refused before payload writes. The
test harness had been moved to `Temp\Doka Top50 Installer Tests`, whereas the
installer deliberately permits isolated mode only beneath
`Temp\DokaTop50InstallerTests\`. The failed `fresh.log` ends with
`InitializeSetup returned False`; it is not an application/runtime failure or a
successful installation.

The supervisor corrected one test-script line to use
`Temp\DokaTop50InstallerTests\<GUID>\path with spaces`. This preserves the exact
allowlisted root, unique disposable directories and spaces coverage. No
application, installer definition or build configuration changed. Independent QA
reviewed the actual guard, failed log and correction and approved this bounded
harness fix. See the final QA addendum.

Corrected harness commit: `c2cbba8454a1e3bfe901393376051be2d71c0849`, tree
`a4f001c3b888e8089bd02ca18f32402e2b11c985`; corresponding local commit `02448e5`.
Application acceptance remains tied to the identical application sources from
`1e0c81e3a00dab3ae9ddc0211c58a0a3973bab22`. The supervisor renews packaging
approval against the corrected harness revision. The workflow must repeat all
native/application validation successfully before it rebuilds the installer and
runs the full installation/upgrade/restart/uninstall checks. No failed package is
approved for distribution. The source/test/build diff gate remains unchanged.

## Final installed-package acceptance and deliverables

Successful run [40762298379](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40762298379)
repeats the full application/native validation and gate, then builds and tests
the installer in separate Windows job `123078533497`. Built source is
`634b4d2ca3492325d658feba0f10b37904eed117`, tree
`cb2f6f86c548be738d455db28c33ed8415dfc6cc`. The application/installer sources remain
identical to the independently accepted application; the only subsequent
executable-file change is the independently reviewed disposable test path.

The supervisor reviewed the successful full job log, locally digest-verified
installed-evidence ZIP, installation/upgrade logs, installed checks/diagnoses and
actual installed results image. Independent package QA is recorded separately
in [package-acceptance.md](package-acceptance.md).

| Actual package check | Result |
| --- | --- |
| Fresh isolated installation, version, registration and payload hashes | PASS |
| All bundled self-contained worker files compared with the build | PASS |
| Installed planner solves, diagnoses, persists and renders; no Excel export | 114 checks PASS |
| Installed dashboard and locked entries | 22 checks PASS |
| Installed Steel Shape native/browser interactions and failure recovery | 24 checks PASS |
| Actual installed production DLL path, direct/filler quantities and bundled worker | 8 checks PASS |
| Fresh-process saved-project reopening, reoptimization and bundled worker | 9 checks PASS |
| Upgrade fixture from1.1.26; recursive worker/settings backup and preservation | PASS |
| Fresh/upgrade uninstallation; user data and backup preservation | PASS |
| Combined installed payload/backup assertions | 249 checks PASS |
| Newer-version/custom-registration rejection, simulated live-Revit install/uninstall block, cleanup after simulated host exit | 5 guards PASS |

The installed production diagnosis reports1.1.27.0, MVID
`5781dc60-c93f-4ce5-a46b-3d159c35c194`. The two-wall example remains eight
physical panels, 2.25 LM per face, one allocation group and two standard/two
adjustable connectors per level on both faces. The first and restarted processes
use the actual bundled .NET10 worker without silent Framework fallback. These
are isolated Windows tests, including a copied previous-version fixture and
simulated Revit process guards; no actual Revit session is implied.

Deliverables:

- [Installer, release notes, reports, release-info and SHA256SUMS](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40762298379/artifacts/12849965823),
  artifact12849965823, **41,153,182 bytes**. Archive SHA-256 reported consistently
  by GitHub artifact metadata and its upload log:
  `48db026e7f98f57327d9551058b4083edd2740b1dee2e414b21cd5455d9c09d2`.
  Installer filename: `Install_DokaRevitAutomation.exe`.
- [Installed evidence](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40762298379/artifacts/12849935290),
  artifact12849935290; locally recomputed ZIP SHA-256
  `f8eb5b4756a76f345f2b41fdd5e0d0ab8747f0f287136a7a3f1b8baa779c2b86`.
- Repeated application-validation artifact12849515411; GitHub ZIP SHA-256
  `59f2f31d298d6faffb71ea13c8237296cf60e920990644ff9174724b6cd2594d`.

The41MB release ZIP was fetched by the GitHub connector into a reusable user file
reference. It exceeds this executor's32MiB download-tool limit and was not
separately unpacked or rehashed here; no network-policy exception was requested.
The CI job tests the actual installer and installed binaries, then writes the
per-file checksum manifest and resolved dependency metadata before uploading the
release. The reported upload/archive digest is not misrepresented as a local
rehash. The final post-package reports in this branch supplement the immutable
prepackaging report snapshot included in the release ZIP.

The supervisor approves delivery within these verified limits. No known blocker
remains in the approved Top50 planning scope. This record does not claim every
unrelated long-running repository workflow completed. Source/build/test files
remain unchanged after successful package testing. Final documentation is added
without rebuilding the accepted artifact. PR174 and PR175 remain draft/unmerged;
main remains`e78f771122f92b58205e74ac5114cb22ebdf4e2d`. No company publication or
real-user installation occurred.
