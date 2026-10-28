# Top50 architecture and development baseline

Agent 2, senior Windows developer and optimization specialist. Audit date: 2026-10-28. This is an initial source/build/persistence audit for supervisor decision, not an implementation authorization or release acceptance.

Baseline: branch `codex/top50-engineering-review-20261028`, commit `348f525`, source tree `171d0664aeda44df602bd66caddcd215b9a0086c`. All application and repository test code remained unchanged during this audit. No installer, commit, publication, installation, additional agent, or production change was performed.

## Architecture inventory

| Area | Current source and responsibility |
| --- | --- |
| Host | `src/MyRevitAddin.csproj`: Revit 2024, WPF, net48, x64/win-x64. Keep `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3. Revit API packages exclude runtime assets. Existing dashboard, beam worker, Steel Shape, access and updater work stays intact. |
| Standalone | `tools/Top50Tester/Top50Tester.csproj`: net48 x64 WPF entry point; source-links the same Top50 domain, view model and views. It does not require Revit assemblies. It is currently described as a tester. |
| Numerical domain | `src/Top50/Domain.cs`: precise integer micrometres, source snapshots, panel patterns, inventories and allocations. No WPF or Revit database objects enter the numerical model. |
| Import | `DxfImporter.cs`: ASCII DXF dimensions, raw-code evidence, physical unit conversion, source checksum and explicit measurement review. `RevitSource.cs` stores detached geometry/provenance; `src/Revit/Top50RevitSelection.cs` reads native straight basic walls synchronously through existing Revit external events. |
| Pattern discovery | `PatternsAndValidation.cs`: ascending component-cardinality multiset enumeration, descending nominal panel lengths, internal fillers only, four retained patterns at most per length/catalog. `CalculationPatternCache.cs` memoizes exact enumeration inputs for one run, clears at 4096 entries and recomputes without changing search policy. |
| Inventory candidates | `CandidateSearch.cs`: catalog cutoffs, longest-wall pattern variants and extra solo-fit coverage; unique stock vectors in deterministic quantity/metre/key order. |
| Allocation | `RetainedInventorySolver.cs`: native OR-Tools CP-SAT 9.12.4544, integer pattern multiplicities by precise length and allocation group; minimizes active groups for one fixed stock vector. |
| Ranking | `Ranking.cs`: quantity admission first, Pareto status and preset-specific ranking of the equal-panel-count tier. `OfferPresentation.cs` supplies comparative native text. |
| Session | `Top50ViewModel*.cs`: immutable search snapshots, background numeric work, UI progress, cancellation, revision rejection, selection, stale state, history and diagnostics. |
| Persistence | `ProjectAndExport.cs`: embedded checksummed DXF/Revit snapshot, active/deleted manual reviews, settings, selected proposal and cached allocations. Current project format V5; input versions V1–V4 still accepted subject to source checks. Atomic temporary-write/replace. |
| Native presentation | `Top50Window.cs`: Import, Settings, Results, Casting groups; inventory and connectors; proportional schematic and group utilization; diagnosis window. `Top50Window.Revit.cs`: selection import, refresh, group highlight and explicit Locate. |

The historical `Top50Workbook` implementation and Open XML dependency remain compiled. The view model's `Export` method deliberately throws `NotSupportedException`, the native window exposes no report/export tab or action, and the current product does not provide Excel reporting. Do not revive the dormant code as a release task.

## Preserved behavior and model limits

- There are 18 selectable nominal lengths: 500, 750, 1000, 1250, 1500, 1750, 2000, 2250, 2440, 2500, 2750, 3000, 3500, 4000, 4500, 5000, 5500 and 6000 mm (`Domain.cs:59`). They are a user-supplied catalog, not a verified product-availability database.
- Source geometry is rounded once to the nearest 0.001 mm, half away from zero; exact integer closure is then checked. Display rounding does not feed optimization.
- Fillers occur only between panels. Each gap is nonnegative and at most the selected maximum, capped by the model at 350 mm. Positive filler-location count is limited per wall. The enumerator distributes required filler into the earliest gaps of descending panel sequences. It does not enumerate physically different permutations, all filler distributions, corner/end details or connector clearances.
- A stock vector describes **one face**. Each group's combined per-size usage cannot exceed it. Groups reuse that stock sequentially. The selected longest walls are required together in one group in fixed-set mode; percentage mode requires at least the single longest wall to fit.
- Every included wall must appear exactly once; unknown, excluded, duplicated and missing assignments fail independent geometry/allocation validation. Per-group stock, filler limits and longest-wall simultaneity are independently checked.
- Panel inventory shown to the user is twice internal per-face stock. Offered LM remains per face. The two-face multiplier is uniform and does not change ordering (`PlannerPolicy.cs:24–26`). One physical wall keeps one row and one mathematical group assignment.
- Connector demand is two faces × one connector per joint × **one level**. Adjustable stock is the maximum simultaneous filler demand; standard stock is `max(0, max_group(direct+filler) - adjustable_stock)`, allowing spare adjustable extensions on direct joints. These are reused stocks across groups, not the sum across all allocations. Settings cannot override the established one-level/two-face/reuse policy (`ConnectorAllocation.cs:16–23`, `PlannerPolicy.cs:17–20`).
- Fixed sets preserve the longest-wall simultaneity target and optional identical kits. Percentage offers search candidate seeds up to an approximate center plus two, admit inventory within the selected target band first, and search outside it only if expansion is enabled and no in-band proposal was found. Percentage is an equipment-length reference, not coverage of every wall at once.
- Mathematical groups are equipment allocations, not a site-approved pour sequence. Later groups are ordered by installed panel load for reproducibility. `Top50Window.cs:21` defines a disclaimer constant but the current native UI does not display it; current-baseline Windows renders under `/workspace/validation/top50-engineering-review/baseline-ui/top50-planner-checks` establish this communication gap. Diagnosis carries more detail. Approved AP-04 adds compact local context without restoring a large footer. No zones or scheduling are introduced by this audit.

The candidate family is a reconstruction; it is not exhaustive inventory optimization. Retaining at most four patterns can discard a feasible stock-compatible arrangement before stock filtering. Catalog cutoffs do not enumerate every possible stock mix. A work-limited/empty result therefore does not prove infeasibility. CP-SAT `Optimal` and objective/bound apply to the retained patterns for a particular inventory, not the complete engineering design space. Current search logs, results status and diagnosis already state these limits.

## Preset objective mapping

All presets share the same inventory generation and CP-SAT allocation objective. Search completely evaluates the first generated panel-count tier containing validated results, then skips higher-count tiers. This **fewest-panels-first decision is intentional** and must remain; savings in LM, fewer groups or other scores cannot admit a larger panel quantity.

Within that tier, candidates on the eight-cost Pareto front sort first. Costs are panel count, stock LM, distinct lengths, group count, mean idle fraction, filler locations, worst idle fraction and absolute percentage-target deviation. All except panel count contribute equally after fixed project scaling to the Balanced score. Scales are total wall LM, selected catalog count, maximum group count, one, maximum possible filler locations, one, and target LM (or project LM). They are independent of which alternatives happened to be found.

| Preset | Actual secondary ordering | Interpretation and limits |
| --- | --- | --- |
| Economical Offer | Pareto first, then stock LM, length types, groups, stable stock key and ID | Economizes nominal LM within the deliberate fewest-panel tier. There are no purchase/rental/assembly prices, so it is not a demonstrated monetary-cost optimum. |
| Balanced Optimization — default | Pareto first, then equal normalized seven-cost score, stable key and ID | Uses seven stated engineering proxies. It does not score handling duration or actual panel assembly changes. Fixed-scale normalization avoids candidate-pool dependent rankings. |
| Fastest Construction | Pareto first, then groups, Balanced score, stable key and ID | Prioritizes fewer mathematical allocations at equal panel count. It does not optimize an actual construction schedule, transfers, curing, dependencies or simultaneous work fronts. |

`Proposal.PanelChanges` is a diagnostic difference between group stock-use histograms; it is neither an optimization objective nor a verified reconfiguration-time metric. The solver chooses one minimum-group allocation per inventory; alternative allocations of that same inventory are not compared by fillers, utilization or connector stock. Presets can consequently coincide on small or single-candidate inputs without being broken. Three presets, Balanced default, and the two offering methods should remain.

## Import, session and persistence review

DXF preserves handles, raw codes, projected length, aligned endpoint length, stored group-42 measurement, units and annotation scale. The raw audit handles modelspace dimensions on `Wall_Length`; binary DXF is explicitly rejected. Aligned dimensions whose projected/endpoint measurements disagree, unknown units, dimension-text overrides, non-default extrusion and coincident geometry require review. DIMLFAC changes annotation only. These are conservative, explicit limitations, not reasons to silently replace source measurements.

Revit captures document identity, UniqueId, centre line and both forming faces. Straight vertical native basic walls only are accepted. Longer-face allowance is used on both faces; unequal lengths are visibly described for separate arrangement review. Linked walls, curved/stacked/curtain/sloped conditions are not silently imported. Opening edges do not deduct the overall projected face length. Refresh requires the original document identity, preserves stable wall IDs, exclusions/deletions and unchanged measured-length overrides, and clears obsolete overrides when the planning length changes. Highlight preserves the current view; Locate explicitly changes it. Actual Revit geometry/API integration remains a Windows host acceptance item; stand-ins do not prove host behavior.

History restores input, settings, corresponding proposals, selected ID, stale state, page/group and clean-state token. DXF/open resets project history, while Revit import is deliberately an undoable project edit; the existing Revit tests assert this difference. Saving does not erase history; stale results are omitted from the saved result cache. A loaded saved proposal is geometry/allocation revalidated before assignment to the current session. Failed Open validates into locals before replacing working state. Source bytes and geometry overrides are checksummed/reviewed rather than silently trusted.

The current capture reads the original DXF file again when saving a freshly imported project, so moving/changing that file before the first save blocks capture with an explicit error. Once reopened, embedded source is restored into a checksum-checked cache and original drawing availability is unnecessary. This is an existing first-save dependency, not lossless in-memory source capture.

## Demonstrated findings for supervisor decision

### DEV-01 — hidden identical-kit setting prevents switching offering methods

**Priority: High; reproducible normal workflow.** Select fixed sets, enable “Use identical panel sets”, then switch to Percentage reference. The method handler changes only `OfferingMode`; the identical-set checkbox becomes hidden but its true value persists (`Top50Window.cs:119–124`). `OptimizeAsync` forwards it unchanged (`Top50ViewModel.cs:66,71`); `Top50Search.Run` throws “Identical fixed kits require Fixed longest-wall sets” (`CandidateSearch.cs:65`). The user sees no active control explaining the conflict.

Smallest fix: apply the identical-kit requirement only in fixed-set mode, retaining the user's fixed-mode preference when switching back. Use the same effective rule for fresh search and restored-result checks. Add a method-switch regression; no offering-method redesign is needed. Acceptance: identical fixed sets → percentage → optimize succeeds under otherwise feasible inputs; switching back restores the chosen fixed-set preference; percentage results do not claim identical fixed kits.

### DEV-02 — cached-result validation omits project policy

**Priority: High; demonstrated with saved fixture.** `Open` checks proposals with `Top50Validation.Validate(restored.Walls,p,restoredLimits)` and reranks them (`Top50ViewModel.cs:111–117`). That validator receives no allowed catalog or percentage settings (`PatternsAndValidation.cs:53–85`). It also checks identical-kit conditions only when the proposal's own `IdenticalKitCount` is positive, rather than requiring the current setting to be satisfied.

An external source-linked net10 probe saved a reviewed 1250 mm manual wall and a precisely closing 1250 mm panel proposal. When project settings allowed only 500/750 mm, Open still returned `Selected != null`, `IsStale=false`, `Dirty=false`. A separate fixture set percentage to 10 ± 0 percentage points, no expansion, yet reopened the 100% offer with the same fresh/clean flags. Source hashes and geometry were valid in both cases. These simulate stale/inconsistent cache metadata; normal UI edits already mark in-memory results stale.

Smallest fix: extend project-context validation to reject/discard cached results that violate permitted sizes, effective offering constraints and required identical-kit count, preserving valid source/settings for reoptimization where practicable. Retain existing standalone geometry validator for core callers. Accept valid legacy caches where the current policy is met; do not silently certify incompatible caches. Acceptance: incompatible caches cannot appear current; ordinary valid round trips and source-integrity failures behave as before; percentage expansion preserves its documented fallback semantics.

Reproduction sources/fixtures/log are outside the repository: `/workspace/validation/top50-engineering-review/architecture-probe/` and `architecture-probe.log`. This audit did not add repository tests or modify production code.

### DEV-03 — domain-setting validation gives weak input feedback

**Priority: Optional/small usability fix, supervisor to bound.** Numeric controls parse decimals and cast integer fields without checking integral values; 2.9 longest walls becomes 2 internally while the text can remain 2.9 (`Top50Window.cs:120,127,130–133`). Negative counts, >350 mm fillers and an empty catalog can be stored until optimize/open fails with low-context argument errors. The minimum improvement is field-specific range/integer feedback and a clear non-empty-catalog check; no extra settings are needed. Do not conflate invalid input with mathematically unproven feasibility.

### DEV-04 — release helper descriptions/manifests lag current source

**Priority: High before packaging; parent owns release scope.** `Build-Top50Standalone.ps1:19` hashes core/view-model sources and two native view files, but omits source-linked `Top50Window.Revit.cs` and `DashboardToolbar.cs`; its README still says new saves use V4 although code uses V5. Its `plannerBaseline` is historical. The standalone workflow compares `Ranking.cs` and `RetainedInventorySolver.cs` to that historical baseline, despite deliberate newer planner changes. `Build-Top50Review.ps1` has older PR168 text and does not collect the new .NET 10 beam worker tree, while the current installer script separately builds that worker. These helpers must not be treated as complete current dashboard release provenance. Correct metadata/source coverage and retain the worker in the approved final package; do not remove old beam, dashboard or Steel Shape work to satisfy an outdated check.

### DEV-05 — history memory growth warrants bounded measurement

**Priority: Optional/future unless measurement shows release impact.** Undo/redo stacks are unbounded; every checkpoint deep-copies all raw source rows and serializes the whole `NativeTop50Project` settings object (`Top50ViewModel.History.cs:17,22–32`). After Open, that settings object also holds embedded base64 source and cached DTOs. Every edited state can therefore duplicate drawing bytes in addition to input copies. Current source-linked tests establish correctness, not sustained-session memory. Measure a large real input with repeated edits before changing representation. A narrow future fix can snapshot scalar calculation settings separately and cap history; an architecture rewrite is unjustified without evidence.

## Performance and responsiveness

Existing safeguards are worth preserving: all Revit reads finish synchronously before detached work, numeric search runs in `Task.Run`, snapshots prevent input mutation during calculation, returned proposals are independently validated and rejected on revision change, cancellation checks surround pattern/model construction, and `CancellableSearch` repeats native StopSearch after the wrapper-start race until completion.

Pattern work is bounded by a deterministic visit limit (100 effort → 5,000,000 nodes per uncached enumeration), not a wall-clock deadline. The run-local cache already eliminates repeated preparations across longest-wall seeds. CP-SAT has one worker, deterministic work per eligible inventory and seed 50. Later-group ordering and stable stock keys support repeated results. These limits provide reproducibility, not a fixed response time or complete-space infeasibility proof.

Potential hotspots: enumeration across distinct lengths × catalog cutoffs, percentage-mode candidate seeding/deduplication, and CP-SAT model preparation. With U distinct lengths, P≤4 retained patterns and G≤min(maximum groups, wall count), allocation uses approximately U×P×G integer variables plus G active flags. Model construction repeatedly scans these variables by type, group and stock size (`RetainedInventorySolver.cs:29–59`). Precomputed coefficient/group indexes are a possible small optimization if profiling shows construction dominates. Do not change patterns, objective priorities or solver determinism merely to improve elapsed time. No performance rewrite is approved in this initial audit.

## Reproducible build and test inventory

Compiler: `/workspace/work/dotnet-sdk/dotnet`; command-local `DOTNET_CLI_HOME=/workspace/validation/dotnet-home`, `XDG_DATA_HOME=/workspace/validation/xdg-data`, `NUGET_PACKAGES=/workspace/validation/nuget-packages`.

```
dotnet build src/MyRevitAddin.csproj -c Release --warnaserror -p:NuGetAudit=false
dotnet build tools/Top50Tester/Top50Tester.csproj -c Release --warnaserror -p:NuGetAudit=false
```

Both compile **0 warnings / 0 errors** on this baseline. `NuGetAudit=false` was a command-only environment workaround after NuGet vulnerability lookup failed through the sandbox proxy; no repository audit policy was changed. The original failed logs are retained. Production restore resolved Nice3point Revit API/APIUI **2024.3.60**, because repository references are floating `2024.*`. A locked/resolved package inventory is needed for strict release reproducibility; compilation alone does not prove API or native load behavior in Revit.

Build logs: `/workspace/validation/top50-engineering-review/architecture-production-build-offline.log`, `architecture-standalone-build-offline.log`. Both payloads contain the pinned Windows x64 native solver; production has the eight expected native DLLs (abseil, google-ortools-native, highs, libprotobuf, ortools, re2, utf8_validity, zlib1). This is a cross-build, not a Windows execution or installation test.

| Existing verification project | Runtime/coverage | Baseline responsibility |
| --- | --- | --- |
| Top50.CoreTests | net48; precision, source audit, fillers, retained allocation, stock, ranking and persistence/workbook legacy checks | Independent QA owns execution; Windows runtime needed for native payload |
| Top50.SearchRegression.Tests | net48 and net10; exact pattern-cache equivalence, candidate/result identity and cancellation races/native stop | Independent QA owns execution |
| Top50.RepeatabilityTests | net48; controlled search ordering, quantity priority, exhaustive small-pattern cases, invariants and repeated runs | Independent QA owns execution |
| Top50.RevitSource.Tests | net10; Revit API stand-ins, selection/filtering, forming faces, refresh/highlight, history, V5 persistence | Independent QA owns execution; stand-ins do not establish Revit host acceptance |
| Top50.UiSmoke / PlannerSmoke | net48 WPF; native views, diagnosis, edit/search/save/reopen/cancel and rendering flows | Windows desktop required |
| Top50.StandaloneSmoke | net48 WPF; extracted standalone package, real entry-point startup, source-free portable flows, source/settings round trips and native presentation | Windows desktop required |
| Top50.DependencySmoke / PackageSmoke | net48; copied native/managed dependencies, shipping assembly and dashboard construction | Windows/Revit load-context coexistence still separate |

No Windows desktop, Revit 2024 or Inno Setup compiler was available to this agent during the initial audit. No new WPF, Revit-host, installer, installed restart/uninstall or clean-machine test is claimed. Older documentation records earlier Windows tests, but it is not substituted for current-baseline independent acceptance. Final installer creation remains gated on approved implementation and final independent QA/site review.
