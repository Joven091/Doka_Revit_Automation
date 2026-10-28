# Final independent numerical and software validation

Reviewer: Agent 3, independent QA and mathematical optimization reviewer. Date: 2026-10-28. Scope: approved AP-01/AP-01b through AP-05, shared calculation/state code, and independent review of the native UI evidence. Packaging remains a separate supervisor gate.

**Current conclusion: numerical/core acceptance passes. The subsequent complete Windows evidence review also passes; [final-qa.md](final-qa.md) is the current packaging-gate decision. This document preserves the detailed numerical references and earlier staged review history. Installed-package acceptance remains a later gate.**

The original baseline is `348f525ce019dd37a4aea38391ffc285baeec2fc` / tree `171d0664aeda44df602bd66caddcd215b9a0086c`. Approved changes are in the isolated `codex/top50-engineering-review-20261028` worktree. The planner uses project format V5 and search policy V4. Exact reviewed source hashes, resolved test dependencies, and evidence checksums are recorded under `/workspace/validation/top50-engineering-review/qa/`; the supervisor's release manifest must identify the final Windows source/build separately.

## Independent validation results

The independent suite is `tests/Top50.IndependentValidation`, with .NET 10 for Linux and net48/win-x64 for native Windows validation. The Windows target retains `Microsoft.NETFramework.ReferenceAssemblies` and selects/copies native Windows OR-Tools dependencies; it does not depend on an installed local SDK DLL path. Independent .NET 10 execution and net48 compilation passed.

| Check | Independent expected/reference | Observed result |
|---|---|---|
| Default acceptance suite | Exact references, independent enumeration, active-method/state contracts | **10,888 assertions passed** |
| Pooled stock allocation | Complete panel count-vector and labelled partition enumeration | **756/756** exact feasible/infeasible and minimum-group agreements |
| Identical-kit allocation | Independent per-required-wall kit count bounds within full small pattern spaces | **120/120** exact fixed-stock cases; objective/bound agree with exhaustive minimum |
| Kit edge witnesses | Infeasible one-group cap, indivisible stock, required/nonrequired walls sharing the same length | Pass, including compatible required-ID reconstruction |
| Original QA-003 witness | `{750:2,500:2}`, walls 500/500/1500, two kits: minimum **2 groups**, versus pooled minimum 1 | Correct two-group kit solution; no internal-validation exception |
| Recovered kit seeds (AP-01b) | Complete inventory enumeration up to eight panels and exact constructive group witnesses | Four prior no-offer inputs recovered; minimum panel tiers **8, 8, 6, 8** reached |
| Ordinary mixed-length identical kits | Check every returned panel, gap, stock load, required-wall kit use and utilization | **286 inputs**, **603 returned offers**, **12,729 independent assertions passed**, **zero no-offer inputs** |
| Connector procurement | Brute force adjustable and standard stock under every group's demand | **256/256** demand vectors agree; one level, both faces |
| Presets | Hand-computed equal-count economy/balanced/group tradeoffs; higher-count exclusion | All three distinct intended leaders, quantity first, deterministic ties |
| Offering methods | Whole-wall 45% ±10 versus ±1 points; preserved fixed preference across mode changes | Correct 50% offer or bounded no-result; fixed → percentage → fixed succeeds |
| Cached acceptance | Catalog/band/kit contradictions; mixed expanded collection; legacy validity | Invalid cache discarded, inputs/settings preserved, Optimize explanation; in-band priority; valid V1–V5 caches preserved |
| Atomicity and state | Broken cached geometry, fresh policy violation, edit/undo/redo/stale/save | Corrupt Open preserves original project; incompatible fresh offer rejected; stale persistence/history correct |
| Numeric input | Fractional/zero/overflow counts, filler bounds, invalid modes/catalog/presets; decimal percentages | Field-specific rejection without changing prior settings/history; percentage decimals retained |
| No-result/cancellation | First no-result and first cancelled run must explain outcome without false infeasibility | Pass in view-model state; subsequent native visibility also passed, as recorded in `final-qa.md` |
| Import and precision | Exact decimal micrometre rounding, metre/unitless/override DXF and conflicting aligned geometry | Pass; no silent substitution of conflicting measurements |
| Existing quantity/history/persistence suite | Rerun independently from latest source | **402 checks passed** (baseline: 397) |
| Existing pattern/cache/cancellation/search regressions | Rerun independently from latest source | **84 checks passed** |

No production validator is used to derive the exact oracle answers. The stock oracle enumerates all bounded integer panel count vectors, rejects illegal closure/fillers, then independently partitions labelled walls into simultaneous inventory groups. Kit cases additionally bound each required wall's selected size counts by one kit. Returned arrangements are independently checked for positive components, exact micrometre closure, every gap bound, full/unique wall assignment, simultaneous stock demand, required longest-wall grouping, and panel-only utilization. Connector expectations come from exhaustive procurement choices rather than reproducing the application formula.

The ordinary search sweep is a returned-result correctness test, not a completeness proof over the candidate inventory family. The 756 and 120 exact tests are complete only for their explicitly enumerated small catalogs, stock, wall sets, and limits.

## Recorded distribution, determinism, and performance

The representative 63-wall distribution is 9 × 1600 mm, 2 × 2810 mm, 10 × 7200 mm, 10 × 9200 mm, 21 × 19200 mm, and 11 × 19204.608 mm, with the standard permitted catalog, five longest simultaneous, one filler up to 350 mm, 64 groups maximum, seed 50, deterministic effort 100, and Balanced preset.

All three returned proposals independently pass **835 arrangement/utilization assertions**. The leader remains **23 panels per face / 46 physical panels**, **99.25 LM per face**, **10 groups**, **79.042821% mean utilization**, and **62.720403% minimum utilization**. This is the best found minimum-count tier, not a proven global stock optimum.

Fresh default and reversed wall/catalog + German-culture processes produce identical independent allocation/rank/status signatures:

`d1e32d1f5f7e457a51021ec817ee56a283c113fa609179144354ebebf88e2357`

The final AP-01b validation processes took 3.021 and 3.142 seconds. A separate existing-fixture run took **3.480 seconds**, compared with baseline 3.119 seconds; these small observations are not performance regression claims or guarantees. Work is deterministic, not bounded by elapsed seconds. The original existing-fixture signature remains unchanged before/after implementation:

`4220af5a1539ae8ba41c25e4b9c2fe56adef880602c56b6abe0c56eebe75f87f`

The independent fixture uses different neutral wall IDs from the existing suite, so its full signature is compared only with its own reversed/culture run. The identical existing signature is the direct before/after evidence for ordinary non-kit allocations.

The optional `--recorded --identical` variant also passes **289 independent arrangement/kit assertions** before and after AP-01b. Both runs return the same 30 panels per face / 60 physical panels, 105 LM per face, 15 groups, retained status **Feasible**, and signature `964f8a91b7f20a5668234772b6e5d648addce8896dab5327f64b82d3b7c6d988`. The isolated rounding-only pre-completion snapshot took 2.707 seconds; post-completion took 2.343 seconds. Completion appends zero candidates for this repeated-longest-length fixture, so unchanged results are expected. These timings do not establish a speed improvement. `Feasible` does not prove the retained group optimum.

## Independent AP-01b proof and recovered cases

For catalog `{500,750,1000}`, two identical kits, one filler up to 350 mm, and three walls, the independent oracle enumerates every per-kit count vector with at most four panels (every two-kit stock up to eight panels). It then enumerates all geometrically legal selected patterns and group partitions with actual required-wall kit bounds. This complete tiny inventory search proves minimum physical inventory **per face** of 8, 8, 6, and 8 panels for the four inputs below. The displayed two-face counts are twice those values.

| Walls, mm | Proven minimum panels per face | Actual leading panel count | Actual leading groups |
|---|---:|---:|---:|
| 500, 500, 3000 | 8 | 8 | 1 |
| 500, 1000, 3000 | 8 | 8 | 1 |
| 1000, 1000, 3000 | 6 | 6 | 1 |
| 1500, 1500, 3000 | 8 | 8 | 1 |

A 3000 mm required wall needs at least three panels in one kit: two 1000 mm panels plus a 350 mm filler cannot reach it. At a six-panel total, each kit has exactly three panels, and only `{1000,1000,1000}` or `{1000,1000,750}` can form 3000 mm under the filler rule. Neither includes a 500 mm panel, and neither can form 1500 mm. This explains the eight-panel lower bounds when the other required geometry needs those lengths; two 1000 mm short walls can use the six-panel stock. Complete enumeration confirms these arguments.

Constructive fixed stocks independently prove one group is possible for all four inputs: two kits each containing three 1000 mm panels plus one 500 mm panel suffice for the first, second, and fourth input; two kits of three 1000 mm panels suffice for the third. The solver returns one group for each fixed-stock witness. Other equally minimal-panel inventories can require two groups; QA does not assert that two groups are necessary for the recovered inputs.

The tiny inventory oracle also finds a 5.5 LM-per-face feasible stock reference for each input. This is not a promise that the retained candidate/pattern family finds the global LM minimum, nor is LM allowed to override the established panel-count priority. Every returned recovered offer independently passes allowed-size, stock divisibility, actual kit usage, closure, filler, simultaneous demand, and utilization checks.

## Review of the approved implementation

- **AP-01:** the allocation model now requires enough kit-compatible actual pattern copies for the specifically required longest walls in the simultaneous group. Reconstruction assigns compatible copies to the required IDs first. Nonrequired walls of the same length retain access to pooled-stock patterns. This matches the intended requirement and the exhaustive references; it does not globally restrict all walls to one kit.
- **AP-01b:** source review confirms every rounded seed is retained and at most one grown per-kit envelope is appended per distinct seed. Required lengths use the existing at-most-four retained patterns over the seed's allowed panel types, ordered by fewest added panels, then added LM, then stable stock key. Ordinary and percentage branches are unchanged; exact ordinary signatures and existing percentage tests remain green. Completion is a bounded heuristic, with explicit timeout/no-pattern omission and no global claim.
- **AP-02:** identical-kit preference is effective only in fixed mode; percentage mode preserves the stored preference while using active percentage constraints. Both view-model and direct search paths handle the active method consistently.
- **AP-03:** cached geometric validity is checked before replacing the current project. Project policy additionally checks permitted stock sizes, percentage applicability/range, declared kit count, exact kit divisibility, and actual required-wall kit use. Policy-incompatible caches are discarded while valid source/settings open. An in-band cached alternative takes precedence over a lower-panel expanded alternative; expansion-only caches remain allowed under the existing fallback convention.
- **AP-04:** source review confirms persistent completion status, a concise best-found/quantity-first statement, preset help explaining the actual secondary metrics, connector reuse/conditional substitution help, and a distinction between allocation groups and a pour sequence. Subsequent native visual/runtime acceptance passed in Windows run `40759276277`; see `final-qa.md`.
- **AP-05:** numeric editors validate whole-number inputs before casts; failed settings edits restore the previous settings and do not add a history entry. The shared policy also validates settings before search, native restoration, and persistence. Percentage inputs retain decimal values.

Connector arithmetic, three preset choices, Balanced default, both offering methods, quantity-first admission, automatic panel component counts, two-face panel quantities, one-face LM, and the single-level connector convention remain intact. Excel remains disabled and creates no workbook. No zone scheduler or structural approval claim was added.

## Remaining limitations and disposition

1. **Bounded candidate/pattern search remains deliberate.** The original baseline retained-pattern omission (3000 mm wall, stock `{1000:1,750:1,500:2}`, 250 mm filler) remains correctly described as no proof of full-space infeasibility. The stock family and four-pattern retention do not prove a global panel minimum.
2. **Four prior identical-kit no-offer cases are recovered by approved AP-01b.** A separate scratch project compiled all original Top50 core files directly from immutable baseline Git blobs and reproduced zero offers on these four inputs. They now return independently validated minimum-panel offers as documented above. This narrow completion does not remove the general retained-pattern/candidate-family limitation.
3. **Reuse assumes sequential availability.** Allocation groups do not validate simultaneous arbitrary zones/work fronts, stripping time, crane access, dependencies, or a pour programme. Matching two-face arrangements and imported longer-face allowance remain planning assumptions.
4. **Connector counting is not assembly approval.** Direct/filler selection and one-level stock arithmetic pass, including adjustable reuse. Approved assemblies, short-panel clearances, filler backing, ties, pressure, height, corners, and unequal faces still require project engineering. No automatic height multiplication is required or introduced.
5. **Platform coverage remains bounded.** Native Windows net48/.NET 10, WPF presentation and staged production execution subsequently passed, as documented in `final-qa.md`. Actual Revit 2024 execution and a completely clean-machine prerequisite test remain unperformed. Installed-package launch/restart/worker/uninstall checks occur only after supervisor packaging approval.

## Evidence and reproducibility

Run the durable project with `-f net10.0` for default acceptance, `-- --kits-sweep` for ordinary mixed-kit returned-result checks, or `-- --recorded --output=<directory>` and `-- --recorded --reverse --output=<other-directory>` for fresh-process distribution evidence. On Windows build/run the net48 target and inspect its native dependency outputs. Use default acceptance mode, not `--baseline`, for final QA.

All Linux evidence is under `/workspace/validation/top50-engineering-review/qa/`:

- `final-independent-output.txt`, `final-kits-sweep.txt`, and `final-independent-net48-build.txt`.
- `final-recorded-default-output.txt`, `final-recorded-reverse-output.txt`, and their `independent-final-*/independent-signature.txt` files.
- `final-repeatability-output.txt`, `final-search-regression-output.txt`, and `signature-final-output.txt`.
- `original-baseline/original-no-offers-output.txt` and original Git-blob source snapshots for the four pre-existing no-offer cases.
- `ap01b-independent-output.txt`, `ap01b-kits-sweep.txt`, `ap01b-recorded-default.txt`, `ap01b-recorded-reverse.txt`, `ap01b-search-regression.txt`, `ap01b-repeatability.txt`, and `ap01b-canonical.txt` contain the final completion reruns; `ap01b-net48-build.txt`/`ap01b-net10-build.txt` record clean builds.
- `identical-pre-ap01b-output.txt`, `identical-post-ap01b-output.txt`, and the isolated `pre-ap01b-benchmark/snapshot-provenance.json` record the representative identical-only before/after comparison.
- `evidence/` contains preserved initial baseline logs/oracle source and checksums; the original findings and exact fixtures are in `baseline-qa.md`.

The numerical acceptance result is independent of the implementing agent's test claims. There are no unresolved numerical release blockers in the approved model after these checks. **The complete subsequent Windows acceptance and current supervisor-packaging recommendation are recorded in [final-qa.md](final-qa.md).**

## Cancellation terminal-state follow-up

Local revision `969b3a1` adds a narrow terminal-state correction over `24bdaac`. Independent review of the complete diff finds changes only to `Top50ViewModel.cs`, PlannerSmoke assertions, and Revit-source test assertions. No optimizer, pattern, inventory, connector, or ranking code changes are involved. `Cancel` no longer overwrites completed status, ignores idle requests, and tolerates the cancellation source being disposed concurrently. Both cancellation-exception handling and the returned-result-before-acceptance branch publish an explicit terminal outcome.

The unchanged independent default suite passes **10,888 assertions** again on this revision. A separate source-linked scratch probe adds **15 passed terminal-state checks**: initial idle Cancel, fast no-result followed by late Cancel, first active cancellation, late cancellation after a successful selected offer, active cancellation retaining valid prior equipment, active cancellation retaining a stale prior result, and cancellation after a fast calculation returns but before its UI continuation accepts the result.

For the last case, the probe holds the synchronization-context continuation, requests Cancel while the view model is still busy, and then releases the continuation. The diagnostic report records `Attempted inventories: 0`, proving that a completed `SearchRun` reached the acceptance branch; this is not merely another cancellation-exception test. The final state is idle with no accepted equipment and an explicit cancellation outcome. A subsequent idle Cancel leaves that outcome unchanged.

Probe source, output, diagnostic report, and checksums are preserved in `/workspace/validation/top50-engineering-review/qa/evidence/cancellation-969b3a1/`; the runnable scratch project is `qa/cancellation-terminal/TerminalQa.csproj`. No application or tracked test file was changed by this independent follow-up. A passing cancellation scenario alone did not establish the full native UI gate. That gate was subsequently reviewed against the complete successful Windows run `40759276277`; [final-qa.md](final-qa.md) records the current acceptance decision.
