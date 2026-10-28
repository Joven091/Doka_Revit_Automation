# Independent final QA and packaging-gate decision

**Decision: PASS for the approved application scope. Independent QA recommends proceeding to installer creation for the reviewed source, after the supervisor confirms site acceptance. Final distribution acceptance still requires the separate installed-package checks.**

Reviewer: Agent 3, independent QA and mathematical optimization reviewer. Date: 2026-10-28 UTC. Application: Revit Engineering Dashboard, version **1.1.27.0**, Top50 project format **V5**, search policy **V4**.

No unresolved release-blocking calculation, allocation, cache-policy, cancellation-outcome, or native presentation defect was found in the approved scope. This decision follows independently constructed references, reruns on Linux, successful native Windows execution, source review, and direct review of the Windows evidence. It does not rely solely on the implementing agent's results.

## Exact reviewed source and evidence

| Item | Identity |
|---|---|
| Original baseline | `348f525ce019dd37a4aea38391ffc285baeec2fc` |
| Reviewed local commit | `61f6877d1d6b39f8d488f6cc9466a182ca042623` |
| Validated Windows checkout | `1e0c81e3a00dab3ae9ddc0211c58a0a3973bab22` |
| Source tree | `45cfaa3fe49d51fd7dc8b41ea0f6322edc9f2061` — independently confirmed for the local reviewed commit |
| Core/view/independent-suite aggregate SHA-256 | `84d44e5dfafd291c090c90f4a0bfe5d4610603b2814e9c6f244836e88a4d416f` |
| Windows workflow run / job | `40759276277` / `123066499687` |
| Evidence artifact | `12847278138`, `top50-engineering-validation-evidence` |
| Published artifact ZIP SHA-256, confirmed in upload log | `e937c5a44dd954ff5c9f09918520f5744b10f93b265c1b66aa75774045595b92` |
| Staged production `MyRevitAddin.dll` SHA-256 | `bffe0a686dfdab41881a975d5940332253ac281c359521e36498de4b7263bd72` |
| Staged bundled `Doka.BeamSolver.exe` SHA-256 | `14077b1291d3abbd791fcd33bef6f5af63b80c513a789c18ccd05e6311db76bc` |

The reviewed job log directly records checkout of the validated Windows commit, clean builds, every suite outcome below, and the closed installer gate. Staged `provenance.json` independently records the same checkout, production file version, and binary hashes. The local source tree matches the stated reviewed tree. Full per-file review hashes and all 55 extracted Windows evidence-file hashes are in `/workspace/validation/top50-engineering-review/qa/final-reviewed-source-manifest.json`.

Evidence is extracted at `/workspace/validation/top50-engineering-review/windows-40759276277/`. The full decoded job log is `job-123066499687.log`. The [workflow run](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40759276277) and [artifact](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40759276277/artifacts/12847278138) identify the reviewed Windows execution.

## Passed verification

| Verification | Result and interpretation |
|---|---|
| Independent exact acceptance, Windows net48 | **10,888 assertions passed** |
| Independent exact acceptance, Windows .NET 10 | **10,888 assertions passed** |
| Independent exact acceptance, Linux .NET 10 | **10,888 assertions passed** on the final application code |
| Complete small pooled-stock models | **756** exact feasible/infeasible and minimum-group agreements |
| Complete small fixed-kit stock models | **120** exact cases plus indivisibility, group-cap and repeated-length reconstruction witnesses |
| Connector procurement reference | **256** independently enumerated demand vectors match the agreed one-level, both-face reuse convention |
| Ordinary identical-kit search sweep | **286 inputs, 603 valid offers, zero empty inputs; 12,729 independent assertions** |
| Representative 63-wall project | **835 independent assertions in each fresh Windows process**; reversed walls/catalog and German culture reproduce identical results |
| Existing quantity/history/persistence regressions | **402 checks passed** |
| Existing pattern/cache/native-cancellation/search regressions | **84 checks passed on each Windows target** |
| Native WPF planner/dashboard workflow | **114 checks passed**; current controls, outcomes and captures inspected independently |
| Dashboard release interaction | **22 checks passed** |
| Revit source/reader/refresh/highlight/persistence | **33 checks passed using test-only API stand-ins** |
| Staged production payload, first process | **8 checks passed** using the actual isolated production DLL and bundled beam worker |
| Staged production payload, fresh process | **9 checks passed**, including reopen of the saved project followed by native reoptimization and worker execution |
| Independent cancellation follow-up | **15 additional scratch checks passed**, including cancellation after a completed result returns but before its UI continuation accepts it |

Independent expected results come from complete bounded integer panel-count enumeration, labelled wall-to-group partition search, actual required-wall kit bounds, exhaustive connector procurement choices, exact decimal references, and hand-computed ranking tradeoffs. Production validators are not used to derive those expected answers. [The numerical report](final-independent-qa.md) contains fixtures, proofs, commands, timings, and the retained-search limitations.

The Windows representative leader is **23 panels per face / 46 physical panels**, **99.25 LM per face**, **10 groups**, **79.042821% mean utilization**, and **62.720403% minimum utilization**. Fresh-process default/reversed/culture Windows signatures both equal:

`f1fc66ee658feaa2568fe8acff695311b77f603dec58f5588bcd3c2b71d7ef6b`

The two Windows runs took 4.226 and 4.247 seconds. These are observed times, not deadlines or performance guarantees. Linux's signature differs because some equally valid retained allocations and their secondary ranking differ between native platform builds; each environment repeats its own signature. Cross-platform identity of arbitrary tied allocations is not established. No global stock optimum is claimed.

## Closed findings and approved acceptance

- **QA-001 / AP-02:** fixed + identical → percentage → fixed works, active controls change correctly, and the stored identical-set preference is restored. Percentage offers do not claim fixed kits.
- **QA-002 / AP-03:** cached catalog, percentage-band and required-kit contradictions are discarded while valid inputs/settings open with an Optimize explanation. Mixed expanded/in-band caches preserve in-band priority. Valid V1–V5 caches remain compatible; corrupt geometry is rejected atomically.
- **QA-003 / AP-01:** the actual required-wall allocations fit one kit, including repeated-length identity reconstruction. The original `{750:2,500:2}` witness correctly needs two groups under its specific kit constraints.
- **AP-01b:** all four prior no-offer cases are recovered. Complete tiny inventory enumeration proves panel-count minima of **8, 8, 6, 8 per face**, which the returned tiers reach. Independent constructive stocks allow one group for each case; no blanket two-group requirement is imposed. Original seeds are preserved and the completion remains bounded.
- **AP-04:** first no-result and first cancellation outcomes remain visible while idle. Results visibly state best-found/quantity-first scope. Connector help describes reuse and conditional adjustable substitution; casting groups visibly say they are not a pour sequence. Preset help describes its actual allocation metrics.
- **AP-05:** fractional/invalid whole-number settings fail with field-specific feedback and preserve prior settings/history. Decimal percentages remain valid. Numeric editor source was reviewed and normal native settings views were inspected.
- **Cancellation follow-up:** idle or late Cancel does not overwrite a completed outcome. Active cancellation retains prior valid or stale equipment appropriately. The held-continuation independent probe verifies a terminal cancellation result in the returned-result branch as well as the exception branch. The Windows WPF and Revit-source regressions pass on the corrected revision.

The earlier Windows failures were resolved before this decision: the cancellation terminal-state defect received an application correction; premature inspection of unrealized WPF template children received a test-layout correction. This report accepts only the complete successful final run, not those earlier failed runs.

## Native presentation and staged production arithmetic

Direct visual review covered first no-result and cancellation captures; fixed, percentage and restored-fixed settings; results at normal and 150% scale; casting groups at 150%; and the actual production payload's results, casting and restarted-results captures. The reviewed screens have readable terminal feedback, correct method-specific controls, preserved selection preference, both-face panel labels, per-face LM, one-level/both-face connector headings, and clear filler dimensions. No blocking clipping or misleading quantity basis was observed in these captures. Automated native checks additionally cover the supplied 100%, 125% and 150% layouts.

The staged production fixture independently closes as follows:

| Wall | Selected arrangement per face |
|---|---|
| 1500 mm | 750 mm panel + **250 mm internal filler** + 500 mm panel |
| 1000 mm | 500 mm panel + direct joint + 500 mm panel |

Per-face stock is three 500 mm panels and one 750 mm panel: **4 panels and 2.25 LM per face**, hence **8 physical panels** for both faces. There is one direct joint and one filler joint per face, so one level on both faces requires **2 standard FF20/50 Z connectors and 2 adjustable waling extensions**. The independent reviewer parsed the saved artifact project and recomputed closure, stock usage, quantities and joints; the native results/casting/restart images and diagnosis agree.

The staged harness checks that `Top50ViewModel` is loaded from the isolated payload's actual `MyRevitAddin.dll`. Its separate worker check requires `.NET 10 worker; complete result returned` and rejects silent .NET Framework fallback. The staged worker is byte-checked against the production build. The second invocation is a fresh process that reopens the saved project and executes the native planner and bundled worker again.

Despite some reused harness filenames containing “installed”, these tests ran against a **staged payload before installer creation**. They do not establish installation, upgrade, backup, uninstallation, Revit registration, or actual Revit execution.

## Remaining boundaries and next gate

There are no open critical/high QA defects within the approved preliminary equipment-planning model. The following limits remain explicit and acceptable for this scope:

- Four retained patterns and the bounded inventory family provide best-found results, not a general global optimum or a proof of full-space infeasibility. Complete optimality statements apply only to the stated tiny exhaustive models or the stated retained model.
- Allocation groups assume sequential stock availability. They are not an approved pour programme or a check of arbitrary concurrent zones, stripping times, crews or cranes.
- Connector quantities remain **one level, both faces**. Actual levels are applied manually. Approved assemblies, short-panel clearances, filler backing, ties, pressure, height, corners, unequal faces and structural adequacy require project engineering.
- Actual Revit 2024 execution and a completely clean-machine prerequisite test remain unperformed. API stand-ins and native WPF checks do not substitute for those tests.
- Three presets, two offering methods, quantity-first admission, Balanced default, both-face panel totals, per-face LM, and the no-Excel policy remain preserved.

**QA packaging recommendation: approved for this reviewed source and version.** The supervisor should combine this result with the separate site acceptance, then authorize installer creation. After building, verify the actual installed production DLL/worker, launch/restart, saved-project behavior, upgrade backup and uninstall under the separate package test workflow. Final distribution acceptance depends on those results. At this gate's original sign-off, no installer had been built or accepted as tested.

## Subsequent isolated-test path correction

After approval, run `40760797897` compiled the installer but its first isolated installation attempt stopped at `InitializeSetup returned False`, before payload installation. The test harness used `Temp\Doka Top50 Installer Tests`, which did not match the installer's fixed `Temp\DokaTop50InstallerTests\` allowlist. The independent reviewer checked the failed `fresh.log` against `installer-top50.iss:119–130` and approved the single-line harness correction to `Temp\DokaTop50InstallerTests\<GUID>\path with spaces`. This retains spaces coverage and the existing isolation restriction; it changes no application, installer or build logic. The reviewed application fingerprint remains `84d44e5dfafd291c090c90f4a0bfe5d4610603b2814e9c6f244836e88a4d416f`.

The harness correction is local commit `02448e5` / remote commit `c2cbba8454a1e3bfe901393376051be2d71c0849`, tree `a4f001c3b888e8089bd02ca18f32402e2b11c985`. Only `scripts/Test-Top50PlannerInstaller.ps1` changed in that commit. QA approves renewing the packaging gate against this bounded test correction while retaining the original application acceptance above.

This correction does not waive any installed-package acceptance test. Full Windows validation and the packaging gate must run again on the corrected harness revision. Installed-package acceptance remains pending its successful evidence and the separate `package-acceptance.md` review.
