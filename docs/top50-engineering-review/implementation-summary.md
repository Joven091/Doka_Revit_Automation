# Approved implementation strategy

Agent 2 is implementing AP-01 through AP-05 from the supervisor-approved plan. No version bump, installer, publication or unrelated calculator changes are included. The retained four-pattern policy, quantity-first admission, three presets, two offering methods and one-level/two-face connector basis remain.

Before code changes, the strategy is:

- AP-01: pass an explicit effective identical-kit requirement in `SearchLimits`; constrain the actual compatible pattern counts for required longest walls in group zero before CP-SAT runs, then assign their identities to compatible pattern copies first. Do not restrict non-required walls of the same length or change ordinary-search ordering.
- AP-02: derive the effective kit requirement from fixed-set mode while preserving the stored fixed-set preference.
- AP-03: add project-context checks beside geometry/allocation validation. Corrupt source and corrupt geometry still reject Open atomically. Valid source/settings with policy-incompatible cached results reopen without those results and explain the need to Optimize. Filter cached percentage alternatives with in-band priority before quantity/ranking.
- AP-04: expose concise completion feedback and local model/connector/preset context through the existing native views, status and tooltips. No verbose footer, new report/export or scheduling workflow.
- AP-05: validate model settings before acceptance/search/open; make settings edits transactional and reject fractional integer fields before conversion with a field-specific explanation. Decimal percentages remain supported.

Regression risks are required-wall identity reconstruction when lengths tie, legacy cache compatibility, percentage expansion fallback, history rollback after invalid edits, cancellation/status refresh and WPF focus behavior. Focused source-linked tests and cross-builds will precede independent QA and Windows UI/site acceptance. Existing ordinary deterministic-search tests must retain their results.

Implementation and verification results will be recorded below after changes.

## Implemented AP-01 through AP-05

- `SearchLimits.RequireIdenticalKits` is optional for existing callers. CP-SAT now requires enough kit-compatible pattern copies for the required longest walls in group zero. Reconstruction assigns those required IDs first, while non-required walls of the same length retain pooled-stock options. The solver itself sets the expected kit count; independent validation checks count, divisibility and actual required-wall usage.
- Percentage mode ignores the stored fixed-set identical-kit preference in fresh search and cached-result checks. The preference is preserved for switching back.
- `Top50PlannerPolicy` now validates settings and proposal context. Open rejects corrupt source/geometry before changing the session, but opens valid inputs/settings with incompatible cached results omitted, a stale flag and an Optimize explanation. In-band percentage alternatives take priority before quantity/ranking, including expanded cache collections. Detailed discard reasons remain in diagnosis.
- Compact native result text states best-found/quantity-first scope. Group text distinguishes sequential reuse from a pour sequence; preset and connector tooltips state their actual assumptions and conditional substitution. Existing outcome text remains visible after first no-result/cancellation. Revit status no longer masks subsequent planner outcomes. No large disclaimer footer, report tab, Excel action or new setting was added.
- Whole-number editors validate before conversion and restore the previous displayed value with field-specific feedback on rejection. Model settings edits roll back without changing history on invalid catalog, mode, preset, filler or count values. Decimal percentage fields remain supported.
- Supervisor approved `SearchPolicyVersion=4` to distinguish the corrected allocation/method policy. `ProjectVersion` remains 5; old supported project versions remain readable. Application release version and packaging are parent-owned.

## Implementer verification

- Native net10 search regression: **74 checks passed**, including the reproduced stock `{750:2,500:2}` / walls `500,500,1500` case. Pooled stock still solves in one group; required identical kits solve in two groups and the 1500 mm wall uses `750+500+250 mm filler`. The full identical search and percentage preference transition pass.
- Existing net10 Revit source/reader/refresh/highlight/history/persistence suite: **30 checks passed**, using test-only API stand-ins.
- Production dashboard, standalone WPF, PlannerSmoke and RepeatabilityTests cross-builds: **zero warnings/errors**. SearchRegression also cross-built for net48. Command-only `NuGetAudit=false` remains the environment workaround; no repository policy was weakened.
- Added focused existing-suite checks for discrete-edit rollback/decimal settings and native first no-result/cancellation visibility, compact context and preset/connector help. PlannerSmoke emits `first-no-result.png` and `first-cancelled.png` for Windows site review.
- Independent QA separately reported **10,603 passing assertions** on the implementation, including 120 exact fixed-stock kit cases, same-length required-ID reconstruction, policy caches, percentage fallback, V1–V5 round trips and transactional edits. This is the independent agent's result, not a claim that this implementing agent authored or independently reviewed that oracle.

Evidence logs are under `/workspace/validation/top50-engineering-review/development-*.log`. Current Windows execution of updated WPF tests, final application-version rebuilds, site acceptance and release/installer gating remain parent-owned. Actual Revit 2024 host acceptance cannot be inferred from stand-ins or Linux cross-builds. No installer, publication or real installation was performed by Agent 2.

## AP-01b — bounded identical-kit seed completion

The supervisor separately approved this narrow addition after independent QA found four pre-existing no-offer examples among 286 tiny kit inputs. `CandidateSearch.cs` keeps every rounded candidate and appends at most one componentwise per-kit envelope per distinct seed. Required longest-wall lengths are considered once in deterministic descending order. The existing run-local cache and four retained patterns supply choices; added panel count, added LM and stable inventory key choose each pattern. No new catalog types are introduced. Ordinary and percentage candidate paths do not run this completion. Existing deduplication, quantity tiers and fixed total solver work remain; additional identical-only candidates can change the best-found offer and per-inventory work allocation.

No-pattern or node-limit completion skips retain the original candidate and explain the limitation in the log; cancellation propagates. This remains a bounded heuristic, not a proof of global feasibility or optimality. SearchPolicyVersion remains 4 within the same unreleased patch, as instructed.

The focused regression recovers all four 500/500/3000, 500/1000/3000, 1000/1000/3000 and 1500/1500/3000 mm cases under catalog 500/750/1000 and two kits. Actual allocations pass the per-kit validator; a two-group example is not treated as a group-count requirement. Ordinary and percentage checks confirm their completion path stays disabled. Production cross-build passes with zero warnings/errors (`development-ap01b-production-build.log`); updated native search checks are recorded in `development-ap01b-search.log`. Independent QA owns the separate exact references and final kit sweep.
