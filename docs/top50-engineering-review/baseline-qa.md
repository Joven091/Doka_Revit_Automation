# Independent baseline QA and mathematical validation

Date: 2026-10-28. Reviewer: Agent 3, independent of implementation. Baseline: commit `348f525ce019dd37a4aea38391ffc285baeec2fc`, tree `171d0664aeda44df602bd66caddcd215b9a0086c`, isolated branch `codex/top50-engineering-review-20261028`.

The baseline produces feasible results and correct inventory, utilization, and one-level connector quantities on the independently checked cases. Three reproducible defects require supervisor disposition: an offering-method transition fails, cached proposals are accepted without checking all of the saved offering requirements, and a valid identical-kit search can fail after selecting an allocation that pools kits. This report does not approve release, Windows execution, or installation.

## Reproduction and independence

Runnable probes and immutable baseline output are under `/workspace/validation/top50-engineering-review/qa/`. `IndependentQa.csproj` compiles the canonical checkout's Top50 and view-model source for .NET 10 on Linux; it does not copy binaries or previous build assets. `Program.cs` contains independent mathematical oracles and the defect probes. `baseline-output.txt` records **8,416 passed assertions**, including **756 exhaustive small allocation cases**, **256 connector procurement vectors**, and the independent pooled-versus-identical-kit witness. Defect probes are observations, not assertions of correct behavior. A separate source-linked `RepeatabilityQa.csproj` runs the existing suite: **397 quantity/work/history/persistence assertions pass**, recorded in `repeatability-output.txt`. Existing regression success is supplementary; it is not the source of the independent expected values.

The durable suite is now `tests/Top50.IndependentValidation`, targeting net48 and net10. Its default mode enforces the approved acceptance outcomes. `--baseline` retains the explicit observations needed to reproduce the original defects. For final QA use default mode. `--recorded` checks all returned arrangements in the 63-wall distribution; `--kits-sweep` exercises mixed-length fixed-kit searches. See its README for commands. The immutable initial output and original scratch oracle remain available separately.

Run from the scratch directory:

```bash
env DOTNET_CLI_HOME=/workspace/validation/dotnet-home \
    XDG_DATA_HOME=/workspace/validation/xdg-data \
    NUGET_PACKAGES=/workspace/validation/nuget-packages \
    /workspace/work/dotnet-sdk/dotnet run --project IndependentQa.csproj \
    -p:NuGetAudit=false --no-launch-profile

env DOTNET_CLI_HOME=/workspace/validation/dotnet-home \
    XDG_DATA_HOME=/workspace/validation/xdg-data \
    NUGET_PACKAGES=/workspace/validation/nuget-packages \
    /workspace/work/dotnet-sdk/dotnet run --project RepeatabilityQa.csproj \
    -p:NuGetAudit=false --no-launch-profile -- \
    /workspace/validation/top50-engineering-review/qa/repeatability
```

The audit override applies to these commands only, enabling use of the available cached packages. Repository audit configuration is unchanged. Runtime: SDK 10.0.402, .NET 10, Linux x64, pinned Google.OrTools 9.12.4544 and native Linux solver. This verifies the shared calculation and state code; it does not substitute for a net48 Windows/WPF/installed-application test.

The independent feasibility oracle enumerates every stock-bounded integer count vector of nominal panel sizes. A vector is geometrically feasible when its positive panel count fits the wall exactly after adding nonnegative internal fillers, with at most `min(maxFillers, panelCount−1)` filler positions and each width at most the permitted limit. A separate recursive partition search assigns labelled walls to inventory-constrained groups and requires the chosen longest walls to share a group. It does not call `FewestComponentPatterns`, `CatalogCutoffCandidates`, `Top50Validation`, or OR-Tools to obtain expected results. Returned arrangements are also checked independently for exact micrometre closure, each stock count, each filler, unique/full wall coverage, and panel-only utilization.

## Verified numerical behavior

Default filler allowance in the following table is one internal filler up to 350 mm. Stock is **per face**; displayed total physical panels are twice these counts. Geometry and utilization describe one matching face.

| Case | Walls, mm | Per-face stock | Independent expectation | Actual |
|---|---|---|---|---|
| Single wall | 1000 | 1000 × 1 | 1 group, 100% utilization | Pass |
| Identical sequential walls | 1000, 1000, 1000 | 1000 × 1 | 3 groups using reused stock | Pass |
| Two longest simultaneous | 1000, 1000, 1000 | 1000 × 2 | 2 groups; mean 75%, minimum 50% | Pass |
| Different lengths, no fillers | 2000, 1500, 1000 | 1000 × 2; 500 × 1 | 2 groups; full wall coverage | Pass |
| Long wall, no fillers | 120000 | 6000 × 20 | 20 panels, 19 direct joints, 1 group | Pass |
| Short wall | 400 | 500 × 1 | Infeasible: overrun forbidden | Pass: no proposal |
| Maximum filler | 1350 | 500 × 2 | 350 mm internal filler, 1 group | Pass |
| One micrometre over filler limit | 1350.001 | 500 × 2 | Infeasible | Pass: no proposal |
| Fillers prohibited | 1350 | 500 × 2 | Infeasible | Pass: no proposal |
| Group cap | 1000, 1000 | 1000 × 1; max 1 group | Infeasible simultaneous capacity | Pass: no proposal |
| Required simultaneous cap | 1000, 1000 | 1000 × 1; longest count 2 | Infeasible | Pass: no proposal |

For feasible table cases the retained solver reports `Optimal`, and both objective and bound equal the independently proven minimum group count. These examples have sufficiently small/full pattern spaces to justify that equality under their stated inventory and constraints. This is not a proof over other inventories or unrestricted designs.

The exhaustive sweep uses catalog `{500,750}`, both stock counts independently from 1 through 3, every unordered pair from wall lengths `{500,750,1000,1250,1350,1500}`, filler limits 0 or 1, and required-longest counts 1 or 2. All **756** instances have at most four full catalog patterns per wall. The solver agrees with the full independent feasible/infeasible decision and exact minimum group count in every instance; every returned arrangement passes the independent checks. This bounded experiment provides stronger evidence than checking the solver with its own validator.

The baseline recorded 63-wall search independently passes **835** returned-arrangement assertions in 3.183 seconds on this Linux environment. Its leader is 23 panels per face (46 both faces), 99.25 LM per face, 10 groups, mean utilization 79.042821%, minimum 62.720403%. The existing fresh-process signature is `4220af5a1539ae8ba41c25e4b9c2fe56adef880602c56b6abe0c56eebe75f87f`, reproduced in separate default and reversed wall/catalog order + German-culture processes (3.119 and 3.248 seconds). These elapsed observations are not performance guarantees; the solver uses deterministic work. Exact wall identities differ between the independent neutral-ID fixture and the existing suite, so their complete allocation signatures are not compared with each other.

## Inventory, reuse, utilization, and connector quantities

- A stock item can be reused between groups and must cover the **sum** of simultaneous usage inside each group. Neither the stock oracle nor the application sums inventory across sequential groups.
- Mean utilization is `sum(installed panel LM)/(number of groups × offered panel LM)`; minimum utilization uses the least loaded group. Filler width is excluded from panel LM. These formulas were reconstructed from returned panel sizes and checked independently.
- A one-panel 1 m per-face stock displays **2 physical panels** and **1 LM per face**. Both-face panel inventory rows agree with that convention.
- A wall with `n` panels has `n−1` internal joints: a zero gap is direct, a positive gap is a filler joint. There is no invented end filler.
- For direct demands `D_g` and filler demands `F_g` already doubled for both faces, adjustable stock is `max(F_g)`. Standard stock is `max(0, max(D_g+F_g)−adjustableStock)`. This allows unused adjustable extensions to serve approved direct joints in another group.
- An independent brute-force procurement oracle enumerates both connector stock variables and requires enough adjustable extensions for every filler demand and enough combined connectors for every group's total demand. All 256 combinations of 0–3 direct and filler joints in each of two groups match the application. Legacy values requesting 9 levels or 1 face do not change the agreed **one level, both faces** convention.
- Names/articles remain FF20/50 Z **587533000** for direct joints and adjustable waling extension FF20/50 **587532000** for filler joints. Native section labels explicitly say **“Connectors per level — both faces.”** No wall-height multiplication is expected or requested.

These checks establish counting consistency. They do not establish approved assembly details, connector clearances, pressure capacity, ties, backing, unequal wall faces, or structural adequacy.

## Presets, ties, and offering methods

Quantity-first admission is verified for **all three presets**: a six-panel inventory with better LM/group metrics cannot outrank any valid five-panel inventory. Presets compare equal-count alternatives. Independent hand-constructed alternatives with five panels select different leaders as intended:

| Alternative | LM per face | Types | Groups | Mean use | Fillers | Minimum use | Winning preset |
|---|---:|---:|---:|---:|---:|---:|---|
| Economy | 10 | 2 | 5 | 80% | 0 | 60% | Economical Offer |
| Fast | 12 | 2 | 2 | 80% | 2 | 60% | Fastest Construction |
| Balanced | 11 | 1 | 3 | 90% | 0 | 80% | Balanced Optimization |

For independent scales `(totalWallLM=20, types=10, maxGroups=10, maxFillers=10)`, Balanced's score is `(0.55+0.1+0.3+0.1+0+0.2+0)/7`. Actual arithmetic and ordering agree. Pareto membership precedes the preset comparison within the minimum-count tier. Exact metric ties resolve by ordinal inventory key and then ID, independent of enumeration order. Reversing tied wall input order gives the same tested native allocation.

For four 1 m walls, percentage reference 45% with ±10 percentage points produces 2 m reusable stock (50%) and two groups. With ±1 point, no complete whole-wall offer is returned. No partial-wall equipment is invented. Fixed two identical sets yield two one-panel kits, with corresponding inventory divisibility and per-required-wall fit on this fixture. Both offering methods remain justified; QA-001 prevents a particular UI transition between them.

Presets rank the found inventories; the retained allocation model minimizes group count for each stock. It does **not** prove a separate optimum for filler placement, utilization balance, rearrangement effort, connectors, or site sequencing within that stock. Preset names should be interpreted within that limited scope.

## Import precision and state behavior

Synthetic DXF fixtures independently verify 1.2345 mm → 1235 μm; metre units convert before rounding (1.2345 m → 1,234,500 μm). Unitless drawings remain excluded pending review; an explicitly reasoned metre override enables the correct physical length. An aligned vertical 2000 mm dimension whose legacy zero-angle projection is zero retains projection, endpoint distance, and stored measurement and remains excluded for review. It is not silently replaced with an alternative measurement. Decimal boundary checks include 1.234499 mm → 1234 μm and 19204.608294923666 mm → 19,204,608 μm.

Editing a solved wall marks retained results stale and changes rank explanations to an explicit reoptimization message. A failed restrictive rerun preserves the stale state and says infeasibility is not proven. Undo and redo restore the matching geometry/results/stale state. Saving stale inputs excludes the proposals. Calling the disabled Excel action throws and creates no workbook. No Excel feature is proposed.

The native window source labels groups as mathematical equipment allocations and says they are not approved site sequences. This is the correct preliminary model for sequential stock reuse. The current mathematics does not support specified simultaneous work fronts/zones beyond the longest-wall condition; adding zone constraints requires the separate site/supervisor decision.

## Reproducible findings

### QA-001 — Hidden identical-kit preference blocks percentage mode

**Severity: High (ordinary offering workflow fails); release fix recommended.**

Steps: add four 1000 mm manual walls; allow only 1000 mm panels; select fixed method, longest count 2, and “Use identical panel sets”; optimize successfully; switch method to Percentage reference; optimize again.

Expected: percentage search uses the active percentage settings. The stored fixed-method identical-set preference remains available when switching back.

Actual: `OptimizeAsync` throws `ArgumentException: Identical fixed kits require Fixed longest-wall sets.` The fixed-only checkbox is hidden in percentage mode but remains true, and the view model passes it to the search. Baseline evidence: `baseline-output.txt` line 145; `Top50ViewModel.cs:66`; `Top50Window.cs:118–124`; `CandidateSearch.cs` rejects simultaneous percentage and identical-kit arguments. The independent exact transition reproduces the same failure identified by the other reviewers.

Acceptance: toggle fixed → percentage → fixed after enabling identical kits; both searches succeed under their active requirements, preference is preserved, no stale result is relabelled current, and the engine continues rejecting genuinely conflicting direct API arguments.

### QA-002 — Cached proposal acceptance omits saved offering requirements

**Severity: Medium (imported/native-file integrity); fix recommended with explicit legacy policy.**

Start with a native project containing four 1000 mm manual walls, permitted catalog `{1000}`, longest count 2, and its valid cached stock `{1000:2}`. Save and reload the native document. Independently apply one change and reopen:

| Reproduction | Expected | Actual |
|---|---|---|
| Set `CatalogMm` to `{500}` while cached stock uses 1000 mm | Reject cached offer or discard/mark it stale and request reoptimization | Cached offer accepted, `IsStale=false` |
| Set percentage mode, reference 45%, band ±1, expansion disabled; cached stock is 50% | Reject/discard out-of-band cache | Cached offer accepted, `IsStale=false` |
| Set `RequireIdenticalKits=true`, longest count 2, cached stock count 3, `KitCount=0` | Reject/discard absent/nondivisible two-kit declaration | Cached offer accepted, `IsStale=false` |

In all three cases, status states **“Project opened and cached proposals independently revalidated.”** Evidence: `baseline-output.txt` lines 146–148; generated `cached-catalog.top50`, `cached-band.top50`, and `cached-kits.top50` beneath the scratch build directory; `Top50ViewModel.Open` at baseline lines 113–117. Geometric validation receives walls and `SearchLimits`, so it cannot check catalog, percentage range, or a requirement absent from `Proposal.IdenticalKitCount`.

This is a boundary-validation issue, **not** a claim that ordinary UI Save creates inconsistent files: normal UI edits mark results stale and Save omits them, which passed separately. Native JSON can be edited or produced by other/older versions; compatibility should not claim that incompatible cached offers satisfy current saved requirements. A current-version cache should satisfy all active constraints. Legacy caches may be preserved for audit, rejected, or made stale; they must be described truthfully.

Acceptance: exercise all three files against current-version and supported older-version policy; do not accept any incompatible cache as current. Preserve the original in-memory project if Open is rejected. Treat expansion-enabled percentage results separately, since being outside the band is allowed after unsuccessful in-band discovery. Ignore hidden fixed-kit preference while percentage mode is active.

### QA-003 — Identical-kit existence check does not constrain actual allocation

**Severity: High (ordinary valid fixed-method optimization fails); release fix recommended.**

Steps: use walls **500, 500, 1500 mm**, catalog `{500,750,1000}`, fixed method, required longest count **2**, identical sets enabled, one filler up to 350 mm, and at least two permitted groups. Call the ordinary `Top50Search.Run` path with deterministic effort 3, seed 50; no injected candidates or malformed file is involved.

Expected: return a validated four-panel offer, allowing two groups. For the generated stock `{750:2,500:2}`, each kit is `{750:1,500:1}`. A valid allocation is group 1: the 1500 mm wall as `750+500+250 filler`, plus one required 500 mm wall; group 2: the remaining 500 mm wall. An independent kit-constrained exhaustive oracle proves **two groups** are necessary for that stock. Another four-panel stock `{1000:2,500:2}` also permits a valid two-group offer.

Actual: `InvalidOperationException: Returned allocation failed independent validation: Required wall does not fit one identical kit: W2`. The solver minimizes groups using pooled stock and chooses a one-group allocation: 1500 mm wall as `750+750` direct, plus both 500 mm walls. The long wall consumes both kits' 750 mm panels. A separate prior existence check finds a kit-compatible 1500 mm pattern but does not constrain the pattern used by the allocation solver. Postvalidation correctly detects the violation, but the whole search aborts instead of returning the feasible kit-constrained result.

Evidence: `/workspace/validation/top50-engineering-review/qa/kits-sweep.txt`; `IndependentQa.csproj -- --kits-sweep` finds this as its first failure; `IdenticalKitAllocationProbe` contains the independent pooled-versus-kit optimum calculation; baseline `CandidateSearch.cs:105–113` checks fit after solving; `RetainedInventorySolver.cs` has pooled stock capacity and no identical-kit constraint.

Acceptance: enforce kit fit in the allocation model for the actual required walls, rather than relying on the existence of a different fitting pattern. The fixture must return at least one valid four-panel, two-group proposal with two kits. Independently check each required wall's actual selected per-size counts against one kit and prove the group optimum on small fixed stocks. Rerun ordinary mixed-length identical-kit searches; suppressing the exception or discarding this stock without solving the correct model is not equivalent to fulfilling the requirement.

## Confirmed limitations, not defects

**QA-L01 — Retained-pattern/candidate scope.** A 3000 mm wall with per-face stock `{1000:1,750:1,500:2}` is feasible as `1000+750+500+500+250 mm filler`. The independent full count-vector oracle proves one-group feasibility. The four retained patterns all fail this stock and `RetainedInventorySolver` returns no proposal. The logs explicitly say this does not prove full-space infeasibility. This is a reproducible, deliberately bounded search limitation, not an incorrect feasibility claim. The cutoff inventory family and omitted higher-count candidates also prevent global optimality claims. Existing truthful language should remain.

**QA-L02 — Engineering sequencing and structure.** Group order is determined mathematically, not by construction dependencies or actual zones. Reuse assumes sequential availability; arbitrary simultaneous work fronts are not guaranteed. Double-sided quantities assume matching arrangements on both faces (with imported longer-face allowances separately disclosed). Structural and assembly verification remain outside these numerical tests.

**QA-L03 — Platform/release coverage.** Linux executes the shared .NET 10 core/state code only. This initial review has not run WPF interactions, Revit-host integration, a clean Windows installation, repair/uninstall, or Windows-native dependency loading. Installer creation must remain behind implementation, independent final validation, engineering acceptance, and supervisor approval.

## Initial disposition

No numerical infeasibility, stock-capacity, utilization, or connector-count defect was found in the ordinary pooled-stock instances independently proven above. Preserve the functioning calculations, quantity-first preference, both offering methods, three presets, no-Excel policy, and one-level connector convention. Resolve or explicitly disposition QA-001/QA-002/QA-003 before final acceptance; rerun these independent oracles against the approved implementation and record the exact final revision/build. This baseline is evidence for the supervisor's decision, not release approval.
