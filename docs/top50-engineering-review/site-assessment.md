# Top50 Planner — Initial Site Engineering Assessment

Assessment date: 28 October 2026. Role: Senior Formwork Site Engineer and Construction Project Manager. Baseline: commit `348f525ce019dd37a4aea38391ffc285baeec2fc`, tree `171d0664aeda44df602bd66caddcd215b9a0086c`, branch `codex/top50-engineering-review-20261028`. Updated with current baseline Windows renders and independent QA findings after the supervisor approved [AP-01–06](approved-development-plan.md). This document records baseline findings; it does not claim those approved changes have already passed acceptance.

This is the initial assessment for the supervisor's development decision. It is not final engineering acceptance, structural approval, or a release authorization. No application source, tests, installers, or publication were changed by this assessment.

## Assessment outcome and evidence limits

The existing application has a sound scope for preliminary wall-formwork equipment proposals: reviewed wall lengths, selected nominal panel sizes, internal fillers, reusable inventory, mathematical allocation groups, and single-level connector stock. Preserve that scope, the two offering methods, all three presets, and the deliberate rule that fewer panels always take precedence. The most useful current changes are small workflow corrections and clear explanations of the existing model. A new scheduling engine is not justified by the evidence.

Verified source defects are the hidden identical-kit setting blocking a switch to percentage mode and the disappearance of completion/no-result messages. Independent QA additionally found that actual allocations can pool two identical kits even when a different one-kit pattern exists, causing a valid search to abort (QA-003, added below as SE-08). QA also demonstrated incomplete saved-policy validation; [AP-03](approved-development-plan.md) addresses that cross-role finding. There are material communication gaps: the normal results screen does not explain conditional connector substitution, sequential reuse, bounded optimization, or the limited meaning of “Fastest Construction.” These are communication defects against the stated planning purpose, not demands for structural calculations or additional scheduling.

Evidence reviewed:

- Current `src/Top50/*`, the Top50 window and view-model files, existing Top50 test programs, and historical Top50 documentation. Links below identify the implementation supporting each finding.
- Current baseline Windows artifact **12839322417**, tested source `799b61f`, assembly `MyRevitAddin 1.1.26.0`, reviewed at `/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks`. The supervisor established that these Top50 sources match the new baseline `348f525`. This role visually inspected Settings, Results and Casting groups at 100%, 125% and 150%; the files and findings are listed below. Its diagnostic names MVID `6219cacd-6f6a-490f-b8ae-b8813ec0a24c`; `planner-checks.txt` records 103 controlled-input checks. These are current-baseline WPF/standalone test artifacts, not an actual Revit 2024 session or installed-application acceptance.
- Historical screenshots also inspected: `/workspace/shared/Top50-1.1.25-results-typical.png`, `/workspace/shared/Top50-1.1.25-casting-typical.png`, and `/workspace/shared/Top50-1.1.23-import.png`. They provide historical mixed-panel/filler context only. The older import image still contains a removed Reports tab; the older casting image lacks current utilization. They are not used as current-release evidence.
- Existing test assertions were read, including connector reuse, quantity-first ranking, exact geometry, identical kits, project persistence, and automatic panel counts. Reading an assertion is not executing it. This role reviewed the supplied current Windows renders and reported check output, but did not itself launch WPF, Revit, or the solver. QA-003 runtime reproduction and its exhaustive oracle are attributed to [independent QA](baseline-qa.md), with a separate hand-checked engineering witness below.
- No project drawings, approved pour plan, actual panel availability, rental prices, crane data, wall-height design, or approved connector assembly details were supplied for this review. Construction examples below are explicitly hypothetical. Historical documentation links official Doka sources, but this assessment has not independently verified those documents or approved a particular assembly.

## Current baseline Windows views and compact-context assessment

All nine requested images were inspected individually:

| View | 100% | 125% | 150% | Observation |
| --- | --- | --- | --- | --- |
| Results | [page 2, 100%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-2-1.png) | [page 2, 125%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-2-1.25.png) | [page 2, 150%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-2-1.5.png) | Panel sizes, physical quantity, per-face LM, both connector articles and the per-level/both-face heading are readable. No long warning box is needed. Completion/best-found context is absent from the rendered idle screen, corroborating SE-03. |
| Settings | [page 1, 100%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-1-1.png) | [page 1, 125%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-1-1.25.png) | [page 1, 150%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-1-1.5.png) | Active fixed-mode controls, nominal catalog and filler fields are understandable. There is no preset explanation. The existing scroll region is visible at 100% and 150%; any new help must remain short or use tooltips rather than substantially increase the form. |
| Casting groups | [page 3, 100%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-3-1.png) | [page 3, 125%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-3-1.25.png) | [page 3, 150%](/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks/planner-page-3-1.5.png) | The selector clearly displays C-01, two walls and 100.00% utilization. Wall identities, 5000 mm panels and the matching-two-face basis are readable. A brief equipment-allocation/reuse label can fit beside or below the selector without replacing the diagram. |

This controlled fixture uses four 5000 mm walls, two longest walls required together, and a 5000 mm-only catalog. It displays four physical panels, 10.00 LM per face and two groups. Each wall is one panel, so there are no internal joints and zero connectors is correct; zero does not certify that a complete formwork assembly needs no ties, walings or other equipment. The nine images do not show a filler, direct-joint detail, active percentage controls, a tooltip, a no-result state or an installed Revit session. Those states need the final implementation's separate evidence; the older filler image cannot substitute for it.

The approved AP-04 approach is practical in this layout: one short Results best-found/quantity-first line; a concise group allocation/reuse label; a connector-local tooltip describing conditional adjustable reuse and manual applicable-level multiplication; and short preset help attached to the existing selector. Suggested group wording is “Equipment allocations; stock reused between groups. Verify concurrent site demand.” The exact wording may vary, but it must not imply a verified pour sequence. Preset help should say that Fastest compares allocation-group counts within the admitted quantity tier, rather than promise actual construction duration. Connector names/articles and the existing “Connectors per level — both faces” heading should stay prominent. There is no site-engineering justification to restore a large persistent disclaimer footer, verbose warning box, Reports tab, Excel action, new setting or approval prompt.

These observations support the small approved changes; they are not acceptance of unrendered proposed text. Final engineering acceptance will examine new Windows artifacts and approved numerical scenarios in a separate `site-acceptance.md`.

## Existing behavior to retain

| Area | Verified behavior and evidence | Practical assessment |
| --- | --- | --- |
| Wall input | Manual reviewed lengths and DXF import; explicit review reasons; reviewed unit override; source snapshots and checksums; undo/redo/delete. [Wall review](../../src/Top50/Domain.cs), [history](../../src/ViewModels/Top50ViewModel.History.cs), [import/review UI](../../src/Views/Top50Window.cs). | Suitable for a preliminary wall schedule. Manual entry uses two dialogs per wall, which is slow for large schedules, but existing DXF/Revit paths provide alternatives; no bulk-entry feature is essential to this release. |
| Revit geometry | One row per wall; longer straight forming face used on both sides; both original face lengths retained; unequal faces visibly flagged. [Revit source](../../src/Top50/RevitSource.cs), [review UI](../../src/Views/Top50Window.cs). | Conservative quantity allowance under the stated geometry convention. The shorter-face arrangement still needs review; it is not independently designed. |
| Nominal lengths | 500, 750, 1000, 1250, 1500, 1750, 2000, 2250, 2440, 2500, 2750, 3000, 3500, 4000, 4500, 5000, 5500, and 6000 mm. User can restrict the permitted lengths. [Catalog](../../src/Top50/Domain.cs), [settings](../../src/Views/Top50Window.cs). | The UI correctly calls this a user-supplied nominal catalog. These are planning lengths, not proof that every configuration is a standard stocked Doka assembly. |
| Panel patterns | Exact closure to rounded source geometry; fillers only between panels; each gap within the selected maximum, never above 350 mm; selected maximum number of internal filler locations; no end fillers or overrun. Panel count is geometry-derived, with at most four retained patterns per enumeration. [Patterns and validation](../../src/Top50/PatternsAndValidation.cs), [limits](../../src/Top50/Domain.cs). | Correctly bounded geometric model. A one-panel wall cannot absorb a residual as an end filler in this model. Report a bounded search failure accurately; do not infer structural or full-space infeasibility. |
| Inventory and faces | Internal stock is per face. Physical panel inventory doubles counts for two faces; LM remains explicitly per face. One physical wall remains one allocation row. [Fixed policy](../../src/Top50/PlannerPolicy.cs), [results](../../src/Views/Top50Window.cs). | Clear and appropriate. Do not double the displayed LM or count a physical wall twice. |
| Allocation | Every included wall assigned once; stock-by-length limits enforced within each group; specified longest walls fit simultaneously in one group; stock reused between groups. [Solver](../../src/Top50/RetainedInventorySolver.cs), [validator](../../src/Top50/PatternsAndValidation.cs). | Useful material-feasibility allocation. Later groups are ordered by panel load, not construction dependency or access. |
| Offering methods | Fixed longest-wall sets with optional identical kits; percentage reference with a target band and optional expansion. An eligible in-band offer is considered before expanded offers. [Candidate search](../../src/Top50/CandidateSearch.cs). | Keep both. Percentage is an approximate inventory target, not a programme, productivity rate, or guarantee of concurrent capacity. |
| Presets | Every preset first admits only the lowest validated panel-count tier and prefers the Pareto front. Within that ordering, Economical favors stock LM, types and groups; Balanced uses seven fixed-scale secondary metrics; Fastest favors groups then the balanced score. [Ranking](../../src/Top50/Ranking.cs). | Preserves the owner's quantity-first decision. “Economical” does not calculate prices; “Fastest” does not calculate site duration. Explain those limits. |
| Results | Native inventory and connector tables; up to three ranked offers; proportional panel/filler diagram; group selector includes utilization in current source. Excel export is unavailable. [Native results](../../src/Views/Top50Window.cs), [disabled export](../../src/ViewModels/Top50ViewModel.cs). | Retain the concise presentation and removed reporting scope. Diagnostics already expose detailed evidence; there is no justification to restore Excel or Reports. |

## Practical workflow and responsibilities

| Step | Appropriate application responsibility | Separate engineering/site responsibility |
| --- | --- | --- |
| Read drawings and establish wall schedule | Store reviewed lengths and source identity; flag questionable source geometry. | Confirm drawing revision, wall identity, openings, corners, wall thickness/height and construction joints. |
| Choose arrangements | Find exact nominal length/filler combinations under the selected planning limits. | Approve actual Top50 panel construction, filler backing, end details, ties, walings and local clearances. |
| Prepare equipment proposal | Show reusable panels by size, both-face quantities, per-face offered length and per-level connector stock. | Complete the project equipment list: height-dependent members, ties, anchors, platforms, lifting gear and all other design-dependent items. Their absence is not a defect in this planner. |
| Plan reuse | Demonstrate that each mathematical allocation fits the same stock. | Check stripping/strength timing, access, transport, crane availability, crews, practical continuity and simultaneous fronts. |
| Compare offers | Compare admitted equal-count offers using the three existing preset rules. | Compare actual hire/purchase rates, labour, handling and programme consequences; authorize any additional inventory independently. |
| Communicate proposal | Provide understandable native results and saved source-backed project. | Issue an approved engineering proposal and pour plan through the project's existing procedures. |

The planner should not require heights, structural pressures or a complete programme merely to produce its existing horizontal equipment proposal. Those tasks remain necessary engineering work outside this application's calculation claim.

## Inventory, reuse, utilization and construction practicality

Stock availability is tested by panel size rather than by total LM alone. This is necessary: 10 m of 5 m panels cannot automatically replace 10 m of 2.5 m panels for every wall. Reusing the componentwise stock envelope between non-overlapping groups is valid under the model. Two groups constructed at the same time consume the sum of their demand by size; reusing the same physical stock across them is then invalid.

Current utilization is installed nominal panel length divided by offered stock length. The two-face factor cancels because the arrangements match. Fillers are excluded from installed panel length, and unused stock reduces utilization. Mean utilization averages allocation groups equally; minimum utilization identifies the least loaded group. Neither is a time-weighted rental utilization rate, labour productivity, or concrete volume metric. Current source already shows group utilization, so a new dashboard is unnecessary.

Identical kits must constrain the actual pattern assigned to each required wall. Existence of some other fitting pattern is insufficient. Independent QA's **QA-003** uses walls 500, 500 and 1500 mm; allowed sizes 500/750/1000; two identical sets; one filler up to 350 mm. For stock `750×2 + 500×2` per face, each kit has one 750 and one 500. A valid first group forms the 1500 wall as `750 + 250 filler + 500` and a required 500 wall with one 500; the second group forms the remaining 500 wall. This hand-checked witness closes all walls, respects the simultaneous two-longest requirement and consumes no more than one kit per required wall. Two groups are necessary for this stock: the one-kit 1500 pattern consumes one 750 and one 500, leaving only one 500 for the two short walls, so they cannot both join that group. The baseline instead selects `750+750` for the long wall and puts all three walls in one pooled-stock group; postvalidation correctly rejects the kit violation, but the search then aborts. The project cannot receive the useful valid two-group proposal. This is a genuine fixed-method defect, not an objection to quantity-first ranking or a missing scheduling feature; [AP-01](approved-development-plan.md) approves its correction.

Panel standardization is represented by distinct sizes; fillers and utilization influence secondary ranking. The optimizer does not model the physical arrangement of beams/walings, crane lifts, panel mass, crew motions, or actual reassembly. `Proposal.PanelChanges` is a sum of changes in panel-size demand between the stored adjacent groups, visible in diagnostics; it is not a lift count or a verified reconfiguration estimate, and it is not a ranking metric. Since groups are load-ordered, interpreting it as an actual site sequence would be misleading. Do not convert it into a construction KPI without further requirements.

A hypothetical four-wall job illustrates the owner's trade-off: four 5 m walls with only 5 m panels and one wall required at a time can use two physical panels for two faces, reused four times. Increasing stock to four physical panels can form two walls at a time and reduce allocation groups. That may improve a programme, but the owner deliberately gives fewer panels first priority. The software must not silently admit the larger inventory as a preset trade-off. Use the existing simultaneous-longest-wall requirement when the project actually requires the additional concurrent demand; clarify that this is a requirement change.

The prescribed quantity policy can favor a few long panels over more short panels even when the short panels would be easier to handle. Restricting permitted lengths is already available when crane/access constraints make long panels unsuitable. A new handling optimizer is not justified without panel construction, height, mass and site access data.

Fillers are placed in the first available internal gaps, up to the maximum width, after panels have been generated in descending length order. This is a reproducible geometric arrangement, not a clearance-aware location choice. Filler widths such as 204.608 mm retain source precision; the drawing is not a fabrication cut list or a statement of achievable site tolerances. The approved engineering layout must resolve practical filler dimensions and locations.

## Zones and simultaneous versus sequential demand

**Current-release recommendation: Option A, accompanied by visible scope and reuse explanations.** Current mathematical groups are sufficient for an unrestricted preliminary proposal whose stock is assumed to become available between groups. They are insufficient to certify an imposed programme or simultaneous zone demand. This limitation should be explicit; a zone label alone would not fix it.

| Alternative | Benefit and limitation | Computation/development | Suitability |
| --- | --- | --- | --- |
| A — No zone assignment | Lowest input burden. Existing stock allocation is useful when walls may be grouped freely and equipment transferred sequentially. It cannot certify access, pour order, or specific concurrent fronts. | No model expansion. Existing retained-pattern packing remains bounded. Small contextual explanations and test coverage. | Recommended now, with the limitation visible beside allocation results. |
| B — Optional manual zones | Useful where named work fronts must remain separate or specific walls must be simultaneous. Labels alone help communication but do not change stock. Separate sequential zones may share stock; concurrent independent pools require combined quantities. These are distinct semantics that must be specified. | Labels/persistence are modest work. Allocation constraints and concurrent pool accounting require solver, persistence, UI, validation and regression changes. Group symmetry may decrease; runtime need not improve. Medium to high effort, depending on semantics. | Future candidate if real jobs repeatedly require it. Keep unrestricted mode as default. Do not add label-only zones and imply demand has been protected. |
| C — Advanced sequencing | Can examine dependencies, pour windows, stripping times, transfers, crews and cranes when the required project data exists. | Substantial scheduling model, additional inputs and combinatorial complexity; extensive validation. High effort and maintenance burden. | Defer. No supplied site programme or quantified need justifies it for this release. |

Hypothetical sequential example: two identical building sections use the same panel lengths on different dates. Reusing the maximum simultaneous stock is reasonable if transfer and stripping time are independently checked. Summing two complete section kits would overstate the planned shared inventory.

Hypothetical concurrent example: sections A and B must operate with separate crews today and cannot exchange stock. Their same-time demand must be added by size, or evaluated as independent equipment pools. A percentage target based on total wall length does not enforce this. For an all-wall single simultaneous requirement, the existing longest-wall count can cover all included walls; for selected fronts it is not a substitute for explicit membership.

A more specific hypothetical demonstrates the membership limitation. With permitted sizes 5000, 4500, 3500 and 3000 mm, walls of 10000, 9000, 7000 and 6500 mm have simple patterns `5000+5000`, `4500+4500`, `3500+3500`, and `3500+3000`. A feasible model inventory is two 5000, two 4500, two 3500 and one 3000 mm panel per face. The two longest can operate together; the shorter two can operate in later separate groups. If the 7000 and 6500 mm walls must instead operate together, they need three 3500 mm panels per face, exceeding that stock. This is an independently constructed feasible-allocation illustration, not a claim that a particular optimizer run returns that inventory. It shows why “two longest simultaneous” does not prove every chosen pair can be simultaneous.

For today's release, a planner may use separate project proposals for independent fronts and manually consolidate their inventory; this is a transparent preliminary workaround, with the existing requirement that wall identities and concurrent stock are checked externally. It does not create a verified combined programme.

## Connector review — one level, both faces

The equipment identifiers are correct against the established requirement and shared source constants:

| Joint role | Component | Article |
| --- | --- | --- |
| Direct joint | Formwork element connector FF20/50 Z | 587533000 |
| Filler joint | Adjustable waling extension FF20/50 | 587532000 |

Source: [equipment constants](../../src/Top50/ConnectorEquipment.cs), [allocation](../../src/Top50/ConnectorAllocation.cs), [policy](../../src/Top50/PlannerPolicy.cs). The fixed policy sets one level, two faces and adjustable-on-direct reuse. Historical connection settings do not override that convention. Do not change this to full-height multiplication.

For each allocation group, let `D` be the number of zero-width internal joints across both faces and `F` the number of positive internal filler joints across both faces. Current arithmetic is:

- Adjustable stock `A = max(F)` across groups.
- Standard direct stock `S = max(0, max(D + F) - A)` across groups.

This is a consistent minimum-total connector stock envelope under the conditional assumptions that stock is available again between groups, filler joints require adjustable extensions, and spare adjustable extensions may serve direct joints. It does not add all installed joints across every group, and it does not sum separate maxima for direct and filler stock without reuse.

Independent hand check: group 1 has three direct and one filler joint per face (`D=6`, `F=2`); group 2 has five direct and zero filler joints per face (`D=10`, `F=0`). The formula returns two adjustable and eight standard connectors, ten total per level for both faces. Group 1 needs six standard plus two adjustable; group 2 can use eight standard plus two adjustable at direct joints. Without permitted direct substitution, ten standard plus two adjustable would be required. This calculation supports the arithmetic only; it does not verify the assembly permission.

The retained project planning assumption is adjustable extensions at fillers up to 350 mm and potentially direct joints, **subject to approved assembly details**. Article identity and a geometrically valid gap do not establish applicability, permitted loads, overlaps, or clash clearance. Short panels, especially a 500 mm panel with joints at both ends, adjacent connectors, maximum-width fillers, ties/walings, corners and unequal-face conditions need project-specific checking. No verified product dimension or minimum clearance has been established here, so an automatic collision threshold would be invented and is not recommended.

Current per-level/both-face table headings are clear and correct. The material gap is that the reusable-stock/direct-substitution explanation lives in `ConnectorAllocation.Description` while the actual Results card renders only totals. Preserve the established policy and add the concise condition; do not demand full-height quantities or an additional confirmation workflow.

## Recommendations for supervisor decision

### SE-01 — Show the planning, reuse and conditional assembly basis

- **Classification / priority:** Critical communication requirement; current release.
- **Problem:** Normal Results/Casting groups do not say that allocations assume reusable stock between groups, are not approved sequences, and rely on conditional adjustable-on-direct assembly use.
- **Evidence:** [Top50Window](../../src/Views/Top50Window.cs) defines `Disclaimer` at line 21 but never renders it. Results and CastingPage render no equivalent scope note. [UiSmoke](../../tests/Top50.UiSmoke/Program.cs) line 39 explicitly tests absence of a persistent footer. [ConnectorAllocation](../../src/Top50/ConnectorAllocation.cs) line 16 contains a description not used by the result card. This is a visibility defect, not an arithmetic finding.
- **Site example:** A proposal is handed to two crews who treat separate C-groups as concurrent pours and buy only the shared connector stock; or the adjustable stock is placed on an unverified direct connection.
- **Proposed solution:** Small contextual text beside results/group selection: preliminary equipment allocation, stock reused between groups, confirm simultaneous demand/site sequence independently. Beside connector totals, retain “per level — both faces,” explain one connector per joint per face, manual applicable-level multiplication, and substitution/filler use subject to approved assembly. Expose concise assembly limitations through a local expandable note if space requires.
- **Simpler alternative:** One compact Results note and one connector tooltip/inline note; no large persistent footer, modal approval, or new input fields.
- **Benefit:** Prevents a preliminary shared inventory from being read as an installation or pour-plan approval.
- **Complexity:** Low; native UI text and relevant UI assertions.
- **Risk if omitted:** Misuse of otherwise correct quantities and implied assembly/sequence assurance.

### SE-02 — Ignore hidden identical-kit preference in percentage mode

- **Classification / priority:** High Priority; current release.
- **Problem:** A valid method change can fail because of a hidden fixed-mode preference.
- **Evidence:** [Settings UI](../../src/Views/Top50Window.cs) lines 118–124 changes only `OfferingMode` and hides the fixed controls; [OptimizeAsync](../../src/ViewModels/Top50ViewModel.cs) line 66 passes `RequireIdenticalKits` unchanged; [search](../../src/Top50/CandidateSearch.cs) line 65 rejects identical kits with percentage mode.
- **Site example / reproduction:** Tick “Use identical panel sets” in Fixed longest-wall sets, switch to Percentage reference, then Optimize. Execution reaches “Identical fixed kits require Fixed longest-wall sets” although the checkbox is no longer visible.
- **Proposed solution:** Apply the identical-kit flag only when the offering method is fixed. Preserve the stored fixed-mode preference for a switch back, provided persistence/opening obey the same calculation rule.
- **Simpler alternative:** Clear the flag when switching to percentage mode, with explicit treatment of loaded projects. Mode-gating is less surprising.
- **Benefit:** Both existing offering methods work independently of hidden controls.
- **Complexity:** Low; targeted transition and saved-project regression checks.
- **Risk if omitted:** Routine planning workflow blocked by an invisible setting.

### SE-03 — Keep the completed search outcome visible

- **Classification / priority:** High Priority; current release.
- **Problem:** Bounded no-result explanations disappear when an initial search returns idle; success/global-optimum qualification also disappears.
- **Evidence:** [View model](../../src/ViewModels/Top50ViewModel.cs) lines 75 and 89 assign informative Status. [UpdateBusy](../../src/Views/Top50Window.cs) line 204 collapses progress whenever idle, non-stale and no Revit status. Results with no selection merely says to optimize reviewed walls.
- **Site example:** First run uses a restrictive percentage band and returns no offer. The engineer sees the original input screen again without an explanation and may assume the button failed or the geometry is impossible.
- **Proposed solution:** Retain a compact outcome after completion, including no-result/cancelled state and the bounded-search caveat. Do not describe no result as proof of infeasibility. Show “fewest panel count found” / best found clearly for accepted offers.
- **Simpler alternative:** Allow meaningful terminal Status to remain visible using the existing status control; avoid a new dashboard.
- **Benefit:** Useful recovery from restrictive settings and honest interpretation of optimization.
- **Complexity:** Low to medium; UI state handling and first-run no-result regression.
- **Risk if omitted:** Silent-looking operations, unnecessary retries, and unsupported infeasibility/optimality conclusions.

### SE-04 — Explain what the three presets and two methods prioritize

- **Classification / priority:** High Priority for brief clarification; current release.
- **Problem:** Labels alone can imply cost or programme optimization beyond the implemented objective; quantity-first rules are mainly in diagnostics.
- **Evidence:** [Settings](../../src/Views/Top50Window.cs) line 125 lists names without descriptions; [ranking](../../src/Top50/Ranking.cs) lines 44–58 admits only minimum-count offers, then applies preset-specific secondary ordering; [diagnostics](../../src/ViewModels/Top50ViewModel.Diagnostics.cs) line 85 is where this rule is explained.
- **Site example:** Selecting Fastest Construction is expected to add a panel kit and reduce real programme duration, but it can only favor fewer mathematical groups among the admitted equal-count candidates. Economical offers do not know actual rental tariffs.
- **Proposed solution:** Brief adaptive help for the selected preset: fewer panels always first; within the equal-count tier, nondominated offers are preferred before preset comparisons. Economical favors LM/types; Balanced compares stated secondary metrics; Fastest favors allocation-group count. Do not promise absolute group ordering across all displayed rows because Pareto preference precedes it. Describe percentage as an approximate stock/total-length target, with optional expansion only when the band yields no accepted offer.
- **Simpler alternative:** One quantity-first sentence and preset tooltips. Preserve labels, defaults, candidate rules and all three presets.
- **Benefit:** Engineers choose settings with the right trade-off expectations.
- **Complexity:** Low; text with source-consistency checks.
- **Risk if omitted:** Misunderstood proposals and unsupported cost/time claims.

### SE-05 — Validate integer counts and limits at the input field

- **Classification / priority:** Optional usability improvement; approve only if it fits the small release scope.
- **Problem:** Count fields parse decimal values and cast to integers; out-of-range settings can reach generic search-limit exceptions.
- **Evidence:** [Settings and Number](../../src/Views/Top50Window.cs) lines 120, 127 and 130–133 cast decimals to `int`; [SearchLimits](../../src/Top50/Domain.cs) line 95 uses one argument name for multiple range failures. [EditSettings](../../src/ViewModels/Top50ViewModel.History.cs) has no field validation.
- **Site example:** Enter 1.9 simultaneous longest walls: the stored requirement becomes 1 without an explicit rejection. Enter a filler maximum above 350 mm: the failure identifies `maxInternalFillers` rather than the width field.
- **Proposed solution:** Reject fractional count inputs; enforce positive longest-wall count, nonnegative filler count, filler width 0–350 mm, percentage above 0 through 100, nonnegative band; give the actual field/range and preserve the previous valid value.
- **Simpler alternative:** Field-specific error messages and integer checks in the existing editor; no new validation framework.
- **Benefit:** Fewer hidden setting changes and easier correction.
- **Complexity:** Low to medium, with input-regression coverage.
- **Risk if omitted:** Avoidable user confusion; fractional concurrency entry can silently reduce a demand setting.

### SE-06 — Add optional manual zone constraints only after real concurrent-front requirements

- **Classification / priority:** Future Version; defer implementation in this release.
- **Problem:** The present longest-wall requirement does not express arbitrary simultaneous fronts or separate stock pools. This is a known scope limitation, not evidence that all current proposals are wrong.
- **Evidence:** [WallRow](../../src/Top50/Domain.cs) has no zone/sequence field; [solver](../../src/Top50/RetainedInventorySolver.cs) groups by length and stock, protects only the longest required rows, and orders later groups by load. The hypothetical 7000/6500 mm example above demonstrates an unprotected specific pair.
- **Site example:** Two shorter walls need the same scarce panel type on the same day while the two longest use other sizes.
- **Proposed solution:** First collect actual jobs and specify whether a zone is a label, a simultaneous allocation, a sequential section, or a separate non-shared inventory pool. If justified, make assignment optional and unrestricted mode default; independently validate componentwise concurrent demand and project migration.
- **Simpler alternative:** Option A plus explicit scope text; separate proposals for independent fronts and a checked manual stock consolidation; use the existing simultaneous count where it accurately expresses the requirement.
- **Benefit:** Future proposals could respect a demonstrated site constraint without a complete scheduler.
- **Complexity:** Medium to high for meaningful constraints; labels alone are lower effort but insufficient.
- **Risk if omitted:** Specific programmes remain externally checked. The risk becomes material only when users mistake unconstrained groups for protected fronts; SE-01 addresses that communication now.

### SE-07 — Keep advanced construction sequencing outside this release

- **Classification / priority:** Future Version; defer.
- **Problem:** Pour dependencies, stripping time, transfer time and resources affect actual reuse but are absent from current inputs.
- **Evidence:** [Domain](../../src/Top50/Domain.cs) contains geometry, inventory and groups without dates, durations or precedence; [solver](../../src/Top50/RetainedInventorySolver.cs) minimizes active allocations, not elapsed time.
- **Site example:** A wall kit fits the next group geometrically but is not available until concrete reaches the approved stripping condition and the crane can transfer it.
- **Proposed solution:** If a future project supplies requirements and data, separately assess a sequencing integration; do not infer a programme from numbered C-groups.
- **Simpler alternative:** Existing site programme and engineering release procedures plus the planner's stock checks.
- **Benefit:** Protects the useful planner workflow and avoids unverifiable time promises.
- **Complexity:** High for implementation; no implementation recommended now.
- **Risk if omitted:** Programme feasibility remains outside the tool, explicitly disclosed. That is acceptable for preliminary equipment planning.

### SE-08 / QA-003 — Make actual required-wall patterns fit one identical kit

- **Classification / priority:** High Priority; current release; approved as AP-01.
- **Problem:** A valid identical-kit proposal can fail because the allocation uses both kits for a required wall even though a separate one-kit pattern exists.
- **Evidence:** [Independent baseline QA, QA-003](baseline-qa.md) reproduces the ordinary fresh search and exhaustively checks the cited stock. Baseline [CandidateSearch](../../src/Top50/CandidateSearch.cs) checks kit-compatible pattern existence after an unconstrained pooled allocation; [RetainedInventorySolver](../../src/Top50/RetainedInventorySolver.cs) lacks an actual selected-pattern kit limit. The independent hand witness is described above. The baseline exception is “Required wall does not fit one identical kit.”
- **Site example:** Two nominally identical panel sets are to form the two longest walls together. The planner pools both 750 mm panels on the 1500 mm wall, contradicting the promise that either wall fits one set; it then aborts instead of returning the valid two-group solution.
- **Proposed solution:** Constrain actual required-wall allocation choices to the per-size capacity of one kit before solving. Preserve compatible patterns for ordinary walls and the original unrestricted/non-identical behavior. Return a validated two-group offer for stock `750×2 + 500×2`; independent QA must also cover small mixed-length kit cases.
- **Simpler alternative:** Solving required walls using only kit-compatible candidate patterns is sufficient if the correct rows remain distinguishable and other-wall patterns remain available. Catching the exception or skipping this feasible stock does not satisfy the existing kit requirement.
- **Benefit:** Real identical-set equipment offers are usable and each protected wall actually fits its promised kit.
- **Complexity:** Medium; solver constraints/pattern assignment plus focused independent regressions, with no new UI setting.
- **Risk if omitted:** Ordinary valid fixed-method jobs abort; merely suppressing validation would instead certify an incorrect kit allocation.

## Acceptance criteria for the next engineering review

For the supervisor-approved scope in [AP-01–06](approved-development-plan.md), the later acceptance review should require evidence that:

1. Results and group views visibly state the preliminary allocation/sequential reuse basis, without reintroducing a long persistent warning footer or a new approval gate.
2. Connector headings remain per level and both faces; names/articles remain unchanged; direct substitution and filler use are conditional on approved assembly; actual level multiplication remains manual.
3. Fixed-identical → Percentage → Fixed is usable, preserves the appropriate preference, and does not send a hidden incompatible flag to search. Loaded projects follow the same method rules. QA-003 returns a valid two-group identical-kit allocation, with actual required-wall patterns independently checked against one kit; policy-incompatible caches cannot be presented as current, per AP-03.
4. First-run no-result and successful best-found outcomes remain understandable after the search becomes idle; no false infeasibility, global-optimum, structural or site-sequence claim appears.
5. Quantity-first ordering remains absolute under every preset. Preset clarification does not change priorities or restore higher-count offers. Both offering methods and at most three displayed alternatives remain intact.
6. Any approved input validation rejects fractional counts and identifies the actual invalid field, without changing valid projects or solver geometry.
7. QA independently checks connector envelopes, simultaneous stock and candidate feasibility; fresh Windows UI evidence verifies readable native text and normal operation. This source assessment alone does not satisfy that requirement.

No evidence supports adding automatic wall-height connector multiplication, structural design, mandatory height inputs, restored Excel/Reports, a handling optimizer, or a full programme in this release. Zone constraints remain a documented future decision subject to actual site requirements. Final acceptance must be performed separately after the supervisor-approved changes and independent QA evidence are available.
