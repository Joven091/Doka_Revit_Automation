# Independent installed-package acceptance — Dashboard 1.1.27

**Decision: PASS for the tested Windows installer and isolated installed payload.** Independent QA found no blocking package defect in the reviewed evidence. This closes the installed-package gate left open in [final-qa.md](final-qa.md), within the environment limits below.

Reviewer: Agent 3. Date: 2026-10-28 UTC. The reviewer inspected the actual installer job log, fresh/upgrade installation logs, installed-production execution logs, guard results, saved native project and current installed-results/casting/restart/Steel Shape captures. This decision is based on the successful final run, not the earlier rejected test path or the staged-only checks.

## Package and evidence identity

| Item | Identity |
|---|---|
| Release | Revit Engineering Dashboard **1.1.27**, assembly/file version **1.1.27.0** |
| Packaging source | `634b4d2ca3492325d658feba0f10b37904eed117` |
| Validated harness revision | `c2cbba8454a1e3bfe901393376051be2d71c0849` |
| Application review fingerprint | `84d44e5dfafd291c090c90f4a0bfe5d4610603b2814e9c6f244836e88a4d416f` — unchanged |
| Workflow / installer job | `40762298379` / `123078533497` |
| Installed evidence artifact | `12849935290`; ZIP SHA-256 `f8eb5b4756a76f345f2b41fdd5e0d0ab8747f0f287136a7a3f1b8baa779c2b86` |
| Release artifact | `12849965823`, `top50-dashboard-1.1.27-release`, **41,153,182 bytes** |
| Release archive SHA-256 | `48db026e7f98f57327d9551058b4083edd2740b1dee2e414b21cd5455d9c09d2` |

The [successful workflow](https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40762298379) records the approved gate before installer creation and all package checks below. Installed evidence was extracted at `/workspace/validation/top50-engineering-review/installed-40762298379/`; the complete installer job log is `/workspace/validation/top50-engineering-review/installer-job-123078533497.log`.

The small installed-evidence ZIP was locally verified. For the larger release archive, GitHub API metadata and the directly reviewed upload log agree on the archive identity and digest above. The connector fetched a reusable user file, but the execution download tool has a 32 MiB limit; QA did **not** locally unpack or rehash that larger ZIP. The workflow writes `release-info.json` and `SHA256SUMS` after successful tests, including the installer file checksum. This transfer limit does not negate execution of the actual installer and its installed files on Windows.

## Passed installed checks

| Verification | Result |
|---|---|
| Actual fresh installation under the allowed temporary root, including a path containing spaces | Pass; installer log ends with successful installation and the expected registered assembly path |
| Installed version, every packaged DLL, and self-contained worker files | Pass; installed hashes match the production build, version matches the installer, and the host Revit API is not distributed |
| Add-in registrations | Pass; all three original registrations are retained and resolve to the installed production assembly |
| Actual installed planner and native presentation | **114 checks passed**, using the production project reference and installed probing path |
| Actual installed production DLL and worker | **8 first-process checks + 9 fresh-process checks passed**, including explicit DLL-location verification, saved-project reopening, native solve and actual bundled worker without fallback |
| Installed dashboard and Steel Shape | **22 dashboard checks** and **24 WPF/WebView2 checks** passed |
| Upgrade from the real **1.1.26.0** build fixture | Pass; current version installed, previous assembly and worker tree backed up recursively |
| Upgrade/user-data preservation | Pass; access/update settings, saved project and unrelated-file sentinels retain their hashes |
| Fresh and upgraded uninstall | Pass; registered worker executable removed, user-data sentinels and upgrade backup preserved |
| Aggregate installation/payload/backup checks | **249 passed**; this count includes individual payload-file hash checks |
| Installer guard scenarios | **5 passed**: newer version preserved; custom assembly registration preserved; a simulated live Revit process blocks install and uninstall; uninstall succeeds after it exits |

The actual-production harness asserts that `Top50ViewModel` is loaded from the fresh installation's `MyRevitAddin.dll`. It does not substitute a source-linked view model. PlannerSmoke and Dashboard.ReleaseSmoke also reference the production project. Revit API stand-in tests remain a separate category and are not treated as actual Revit execution.

## Independent installed arithmetic and restart review

The reviewer parsed the installed saved project and independently recomputed the selected simultaneous arrangements:

- 1500 mm wall: 750 mm panel + **250 mm internal filler** + 500 mm panel.
- 1000 mm wall: 500 mm panel + direct joint + 500 mm panel.

Per-face stock is three 500 mm panels plus one 750 mm panel: **4 panels / 2.25 LM per face**, hence **8 physical panels** for both faces. One direct and one filler joint per face require **2 standard connectors and 2 adjustable extensions for one level on both faces**. Both walls close exactly, simultaneous stock usage matches inventory, and the installed results/casting/restarted-results captures agree. The separate fresh process reopens the saved project, reoptimizes successfully and executes the bundled .NET 10 beam worker without silent Framework fallback.

The earlier packaging attempt stopped before installation because its test path was outside the existing isolation allowlist. The approved one-line harness correction retained spaces coverage beneath the permitted prefix. The final logs demonstrate successful fresh/upgrade installs and uninstalls with that corrected path; no application or installer logic change was required.

## Remaining limits and disposition

This is acceptance of **actual isolated installation, installed execution, upgrade/backup and uninstall on the supplied Windows runner**. It does not establish an actual Revit 2024 session, a normal user-profile/Revit registration session, or a completely clean machine lacking preinstalled prerequisites. Live-process guard checks use a simulated process, not a running Revit model. Those boundaries remain disclosed in the release material.

The previously accepted bounded optimization, preliminary-planning assumptions and project engineering limits remain unchanged. Connector quantities stay per level; three presets, two offering methods, quantity-first admission and the no-Excel policy are preserved. No GitHub merge, company publication or installation on a real user's machine was performed by this acceptance.

**Independent package QA recommends delivery of the tested 1.1.27 release artifact with its reports and included checksums.** No package blocker remains in the tested scope. Per-file evidence hashes and the independently parsed installed fixture are recorded in `/workspace/validation/top50-engineering-review/qa/package-independent-review.json`.
