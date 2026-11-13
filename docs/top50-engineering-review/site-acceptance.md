# Top50 Planner — Final Practical Engineering Acceptance Record

Reviewer: Agent 1, Senior Formwork Site Engineer and Construction Project Manager. Scope: [approved AP-01/AP-01b through AP-05](approved-development-plan.md), followed by the supervisor's separate packaging gate for the existing Revit Engineering Dashboard. The original [site assessment](site-assessment.md) remains the baseline record.

**Practical engineering acceptance: PASS for the reviewed pre-installer build.** The approved changes solve the verified site-planning problems without expanding the product into scheduling or structural design. No practical engineering blocker remains in the reviewed scope. The supervisor may approve packaging after consolidating this record with independent QA; this is not installed-application or actual Revit-host acceptance.

## Build and evidence identity

| Item | Current record |
| --- | --- |
| Original baseline | `348f525ce019dd37a4aea38391ffc285baeec2fc`; tree `171d0664aeda44df602bd66caddcd215b9a0086c` |
| Current local source reviewed | `61f6877d1d6b39f8d488f6cc9466a182ca042623`; tree `45cfaa3fe49d51fd7dc8b41ea0f6322edc9f2061`; branch `codex/top50-engineering-review-20261028` |
| Corresponding tested remote source | `1e0c81e3a00dab3ae9ddc0211c58a0a3973bab22`, identified by the supervisor and staged-payload provenance. Application source is unchanged from this role's preceding `969b3a1` source review; the intervening diff changes the UI test and implementation notes |
| Current source version | `Directory.Build.props`: 1.1.27; project format V5; search policy V4 |
| Planner Windows artifact | Run `40759276182`, artifact `12847028214`; ZIP SHA-256 `36fc8725309e1d1bb41fe247fb1c27e6a1f18ef903b0af3de5d1072555ebb0f4` (supervisor verified); `/workspace/validation/top50-engineering-review/windows-planner-40759276182/top50-planner-checks`; **114 checks passed** |
| Planner assembly identity | MyRevitAddin 1.1.27.0; MVID `70817daa-35da-40c0-8001-2832bb5f77a4` in the reviewed planner diagnosis |
| Full Windows engineering artifact | Successful run `40759276277`, artifact `12847278138`; ZIP SHA-256 `e937c5a44dd954ff5c9f09918520f5744b10f93b265c1b66aa75774045595b92` (supervisor verified); `/workspace/validation/top50-engineering-review/windows-40759276277` |
| Staged production host | `prepackage/provenance.json`: MyRevitAddin 1.1.27.0; host SHA-256 `BFFE0A686DFDAB41881A975D5940332253AC281C359521E36498DE4B7263BD72`; source `1e0c81e3a00dab3ae9ddc0211c58a0a3973bab22`; staged diagnosis MVID `32ede02d-cc62-46de-bb69-3110363237b1` |
| Staged worker | Provenance SHA-256 `14077B1291D3ABBD791FCD33BEF6F5AF63B80C513A789C18CCD05E6311DB76BC`; process logs confirm the bundled worker completes without fallback |
| Earlier baseline renders | Artifact 12839322417, 1.1.26; reviewed in the initial assessment only. They do not accept the changed application |

This role reviews source, supplied Windows artifacts and practical equipment scenarios. It has not itself run a Revit 2024 session. Staged-payload tests must be identified as staged tests, even when their helper uses filenames containing “installed.” Actual installed-package checks follow supervisor approval and are a separate gate. No structural approval, global optimization proof or verified site programme is implied.

## Current source review

The current source provides the intended small corrections without adding input burden or restoring a long warning footer:

| Area | Current implementation reviewed | Practical assessment |
| --- | --- | --- |
| Results scope | `Top50Window.ResultsPage` shows “Best found · fewest panels first · global optimum not established” under the existing KPIs. Panel quantities remain both faces; offered LM remains per face. | Concise and readable in the current 100%, 125% and 150% images. |
| Connector stock | The existing “Connectors per level — both faces” heading and both equipment articles are retained. Connector-card tooltip explains reuse between groups and permits spare adjustable extensions at direct joints only where the approved assembly allows; separate geometry/clearance/structural verification is explicit. | Correct distinction between counting and assembly approval. Current WPF checks inspect the actual tooltip content; staged results verify nonzero quantities. The one-level convention is preserved; the user applies applicable levels manually. |
| Group interpretation | Visible “Sequential equipment reuse · not a pour sequence”; selector tooltip also excludes approval of concurrent site demand. Matching-two-face/unequal-face text remains. | The complete header fits at all three scales and leaves the diagram usable. No zone or scheduler input has been added. |
| Presets | Existing selector keeps all three choices. Tooltip states fewer panels first; describes Economical LM/types, Balanced secondary planning metrics, and Fastest allocation-group count; says construction duration is not calculated. | Suitable for preliminary proposals. It does not imply actual cost or programme calculation. No additional settings are needed. |
| Active offering method | Fixed-only kit preference is retained but effective only in fixed mode. Percentage uses its active target/range inputs. | Current fixed/percentage/restored-fixed images show the right controls and retained checkbox. Independent Windows scenarios pass the actual search transition and confirm percentage offers do not claim fixed kits. |
| Numeric editing | Integer fields validate before conversion, identify the field, restore the previous displayed value and retain valid history. Percentage fields allow decimals. | Source and independent Windows tests confirm field-specific rejection, preservation of valid settings/history and decimal percentage support. No extra geometry rounding is introduced. |
| Completion feedback | Existing status remains visible at idle. First cancellation has an explicit terminal message; post-await cancellation assigns that message; an idle/late `Cancel()` cannot replace a finished outcome. Revit status is cleared when a new planner status arrives. | Corrected current renders show readable first no-result and cancellation outcomes. Current Windows checks cover late Cancel and retained previous proposals. Earlier failed runs are not counted as acceptance. |
| Cache policy | Geometry/source validation remains separate from project-policy checking; incompatible cached offers are omitted while valid input/settings open with an Optimize explanation. | Independent Windows cases pass; incompatible catalog/band/kit caches cannot be presented as current offers. |

Source references: [native views](../../src/Views/Top50Window.cs), [view model](../../src/ViewModels/Top50ViewModel.cs), [policy](../../src/Top50/PlannerPolicy.cs), [connector arithmetic](../../src/Top50/ConnectorAllocation.cs), [implementation record](implementation-summary.md).

The populated current screens retain the compact layout without a large disclaimer footer, report tab, Excel action, new configuration requirement or approval prompt. Settings still uses its existing scroll region; at 150% the lower filler field requires scrolling. This is acceptable and does not hide a mandatory control without access. Tooltip text is verified by source and native visual-tree assertions; mouse-hover tooltip placement was not separately captured in these images and is not claimed as a manual interaction test.

## Practical acceptance scenarios and independent expectations

| Scenario | Engineering expectation | Evidence/status |
| --- | --- | --- |
| Fixed inventory at three scales | Four 5000 mm walls, 5000 mm-only catalog, two longest simultaneous: four physical panels, 10.00 LM per face, two groups. Single-panel walls have no internal connectors. Settings, Results and group views remain readable at 100%, 125%, 150%. | **Pass:** inspected all twelve current page images, including Import |
| Percentage offering | Active percentage inputs replace fixed-only controls; decimal percentages remain supported. Fixed identical → percentage → fixed retains the preference without applying it to percentage offers. | **Pass:** three method images plus independent Windows scenarios |
| First restrictive no-result | Four 1000 mm walls, 1000 mm-only panels, 45% target ±1 percentage point: no whole-wall stock offer in the requested band. Idle screen explains no result without claiming infeasibility. | **Pass:** `first-no-result.png` visibly retains the bounded-search explanation |
| First cancellation and late Cancel | No proposal is created after early cancellation; the terminal message remains visible. A later idle Cancel leaves completed cancellation/no-result/success text unchanged. Existing valid offers are retained when applicable. | **Pass:** `first-cancelled.png` says no offer was created; current 114-check run covers late calls and retention |
| Actual direct and filler joints from staged production DLL | Two walls 1500 and 1000 mm; catalog 500/750; two simultaneous; Economical preset. Expected 1500 arrangement `750 + 250 filler + 500`; 1000 arrangement `500 + 500` direct. Total stock per face: three 500s and one 750 = 2.25 LM; physical inventory: six 500s and two 750s = eight panels. One group, 100% nominal-panel utilization. | **Pass:** inspected staged results, actual diagram, diagnosis and both process logs; fresh-process results remain correct |
| Connector hand reference for that staged fixture | One direct and one filler joint per face. Across both faces and one level: `D=2`, `F=2`; adjustable stock 2, standard stock 2. Names/articles must be Formwork element connector FF20/50 Z 587533000 and Adjustable waling extension FF20/50 587532000. No full-height multiplication. | **Pass:** the visible nonzero table and diagram agree with independent hand arithmetic; article names/numbers and per-level/both-face heading are clear |
| Correct actual one-kit allocation, QA-003 | For walls 500/500/1500 and stock `{750:2,500:2}` per face, a required 1500 wall uses `750+500+250 filler`, not both 750s. Two groups are necessary for this fixed stock; each required wall fits one kit. | **Pass:** independent current Windows reference tests; hand witness remains valid |
| Completed identical-kit seeds, AP-01b | Four prior no-offer inputs recover valid offers. Minimum panel tiers per face are 8/8/6/8 in the independent tiny proof. Do not require two groups: two kits each `1000×3+500×1` can form all three walls in one group for 500/500/3000. | **Pass:** current independent Windows references and 286-input kit sweep, zero no-offer inputs |
| Saved incompatible policy and invalid editing | A cache conflicting with allowed lengths, active percentage band or required kits is not shown current; valid input/settings remain available for Optimize. Fractional discrete inputs identify the field and keep the previous value/history. | **Pass:** current Windows independent policy/state checks and reviewed editor source |

The 250 mm filler witness is an arithmetic planning fixture, not a Doka assembly approval. The selected filler maximum remains 350 mm subject to approved project assembly details; clearance, ties, walings, backing, pressure and height remain engineering checks outside this planner.

## Evidence review ledger

All **19 PNGs** in the planner artifact were inspected individually. The reviewed base directory is `/workspace/validation/top50-engineering-review/windows-planner-40759276182/top50-planner-checks`:

| Files inspected | Practical finding |
| --- | --- |
| `planner-page-0-1.png`, `planner-page-0-1.25.png`, `planner-page-0-1.5.png` | Four reviewed wall rows, lengths and statuses are readable; toolbar/units actions remain visible. |
| `planner-page-1-1.png`, `planner-page-1-1.25.png`, `planner-page-1-1.5.png` | Fixed settings remain legible without new fields. The existing scroll region handles the lower filler fields at smaller logical heights. |
| `planner-page-2-1.png`, `planner-page-2-1.25.png`, `planner-page-2-1.5.png` | Four physical panels, 10 LM per face and two groups are unambiguous. Best-found context, connector articles and one-level/both-face headings are readable. |
| `planner-page-3-1.png`, `planner-page-3-1.25.png`, `planner-page-3-1.5.png` | Group utilization, wall labels and panel lengths are clear. The full sequential-reuse/pour-sequence distinction remains visible at 150%. |
| `settings-fixed.png`, `settings-percentage.png`, `settings-fixed-restored.png` | Appropriate method controls are shown; fixed identical preference is visibly restored. |
| [first-no-result.png](/workspace/validation/top50-engineering-review/windows-planner-40759276182/top50-planner-checks/first-no-result.png), [first-cancelled.png](/workspace/validation/top50-engineering-review/windows-planner-40759276182/top50-planner-checks/first-cancelled.png) | The terminal status remains readable at idle. The existing empty-result notice fills the unused content region, but its text is short; no verbose engineering disclaimer has been restored. |
| `dashboard-planners.png`, `top50-diagnosis.png` | The dashboard identifies Top50 as a planner below the existing calculators; detailed source/settings evidence stays in Diagnose. No actual Revit session is inferred. |

The reviewed staged-payload evidence is in `/workspace/validation/top50-engineering-review/windows-40759276277/prepackage`:

- [installed-results.png](/workspace/validation/top50-engineering-review/windows-40759276277/prepackage/installed-results.png): six 500 mm and two 750 mm physical panels; 2.25 LM per face; standard 2 and adjustable 2 per level; both article identities and quantity basis visible.
- [installed-casting.png](/workspace/validation/top50-engineering-review/windows-40759276277/prepackage/installed-casting.png): 1500 mm wall visibly includes the labelled 250 mm internal filler between 750 and 500 panels; 1000 mm wall shows the direct 500/500 boundary. One group and 100% utilization are visible.
- [installed-restarted-results.png](/workspace/validation/top50-engineering-review/windows-40759276277/prepackage/installed-restarted-results.png): same correct offer after saved-project reopening and optimization in a fresh process.
- `first-process.log` and `checks.txt`: **8 checks passed**; `restart-process.log` and `restart-checks.txt`: **9 checks passed**. They verify loading the DLL from the isolated staged directory, actual direct/filler calculation, saved-project reopen, diagnosis and bundled beam-worker operation. The helper's word “installed” does not change the provenance classification: these are staged production payloads, before installer creation.
- `installed-diagnosis.txt`: exact selected stock `{500:3,750:1}` per face, selected wall patterns/gap, one connector level and two faces, V5 project/V4 search policy, Economical preset. `provenance.json` explicitly states “Staged production payload; no installer or Revit session” and identifies the source/host/worker hashes recorded above.

Independent current Windows output in the same full artifact records **10,888 assertions on each of net48 and net10**, a **286-input / 603-offer kit sweep with 12,729 assertions and zero no-offer inputs**, and **402 quantity/work/project checks**. The two fresh representative runs each pass 835 arrangement checks and share signature `f1fc66ee658feaa2568fe8acff695311b77f603dec58f5588bcd3c2b71d7ef6b`. Their leader is 23 panels per face / 46 physical panels, 99.25 LM per face and 10 groups. These counts support returned-result correctness and reproducibility in the stated model, not global stock optimality. The successful engineering workflow also ran the existing 84-check search suite; the supervisor/independent QA retain the full workflow record.

The [independent numerical report](final-independent-qa.md) is Agent 3's mathematical/software judgment. This role read the current Windows logs and separately hand-checked the practical fixture arithmetic and reviewed the actual diagrams; it did not derive independence merely from the implementing agent's summary.

## Remaining engineering limits

Unrestricted mathematical groups continue to assume sequential stock availability. Actual simultaneous fronts, stripping times, cranes, crews, access and dependencies are externally checked. Optional manual zones and advanced sequencing remain deferred by the supervisor; no new scheduling requirement is introduced here.

Quantity-first ranking remains deliberate for every preset. The application finds a bounded best-found equipment offer, not a globally proven optimum. A feasible stock may be omitted by retained patterns/candidate families; clear no-result feedback does not establish infeasibility.

Connector quantities remain one level across both faces. Approved assembly applicability, short-panel clashes, filler backing, corners, unequal faces, ties, walings, height and concrete pressure remain project-engineering responsibilities. No fabricated clearance threshold or automatic level multiplication is requested.

## Acceptance decision

**PASS for practical engineering use within the stated preliminary-planning scope, on the exact reviewed source/build evidence.** The useful corrections are implemented: actual required-wall kit fit, recovery of the bounded kit examples, method switching without a hidden-setting failure, project-policy cache checks, meaningful completed outcomes and honest compact model context. Inputs remain practical; stock, panel combinations, utilization and connector quantities can be understood without a new reporting or scheduling workflow. No unresolved practical release blocker was found in the approved scenarios.

**Recommendation: proceed to the supervisor's packaging decision after independent QA consolidation.** The installer must still be built and separately tested for isolated installation, launch, restart, upgrade/backup and uninstall. Actual Revit 2024 host execution remains unverified; no structural, assembly or site-sequence approval is granted. Those limits are disclosed rather than treated as new application features.
