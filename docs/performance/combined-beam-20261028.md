# Combined beam drawing: 2026-10-28

The uploaded 1.1.26 report took 971.260 seconds for five areas and six isolated
beams. Area 1 consumed 762.664 seconds, Area 2 140.814 seconds, and Area 5 failed.
That is an end-user Windows measurement; the following Linux/.NET 10 figures
are development measurements on different hardware, not a claimed installation
speedup.

## Current development replay

The five normalized, detached inputs in `tests/Beam.Tests/Fixtures/Combined20261028`
retain the reported outlines, unequal spans, loads, stock permissions and limits.
Original project identity, element IDs and global coordinates are omitted.
All searches finish their existing bounded traversal, without a production time
limit or a first-result cutoff.

| Area | Early corrected replay, seconds | First optimization pass, seconds |
| --- | ---: | ---: |
| 1 | 98.920 | 36.003 |
| 2 | 55.811 | 23.748 |
| 3 | 3.124 | 1.333 |
| 4 | 3.229 | 1.640 |
| 5 | 3.214 | 1.424 |
| Total | 164.299 | 64.148 |

Both columns used four workers on the same development host. They represent
optimization iterations after correcting the search failures. Their complete
result fingerprints (primary geometry, UDLs, reactions and crossing bearings)
match for all five areas. Sampling was enabled in both runs. Timings are single
runs, not statistical performance guarantees. The 30-second target is unmet.
Windows/.NET Framework 4.8 measurements are collected separately by
`combined-beam-performance.yml`; a console benchmark excludes Revit/WPF painting.

Area 1 now returns 68 legs and 252 H20s with its slab supported. The original
44-leg result excluded slab S1, so comparing those equipment totals directly
would compare different scopes. Area 2 still reports its separate slab support
requirement. Areas 3–5 each return 24 legs.

## Changes

- Clip generated primary UDL endpoints at their exact member boundaries. The
  reported local subtraction gave 2450.0000000000005 for a 2450 mm member.
  The strict beam-analysis input validator stays unchanged.
- Reject an individual unstable or non-equilibrating numerical trial and keep
  evaluating other stock/support arrangements. Never accept that failed trial.
- Give the final spacing ranking pass its exact latest, validated warm start.
- Reuse fixed transverse options, row grids and exact response checks between
  station-ceiling retries within one receiving solve and one receiver count.
  Load changes, different receiver counts and separate solves get separate work.
- Prepare immutable secondary responses once per fixed crossing junction,
  width, offset and permitted stock. Reuse only exact primary-load failures;
  a different force or primary placement gets its own check. Cache lifetime is
  one junction and primary-failure storage is bounded to 16,384 entries.
- Test secondary capacity before allocating physical solids. Passing candidates
  still receive their complete physical-clearance, member and leg checks.
- Keep fixed frame/primary obstacle subsets for the same junction. The original
  strict interior intersection and ownership rules still decide every clash.
- Batch additional hot diagnostic counters; live reads and final totals retain
  all events. Optional profiling records the previously hidden crossing loop.

The Area 2 profile fell from 7,753,741 repeated transverse response preparations
to 17,702 actual preparations. Exact primary-failure reuse excluded 6,816,118
redundant checks in that replay. These are work counters, not distinct designs.

## Verification and limits

The detached replay independently checks permitted catalogue/custom stock,
member strength, evaluated deflection, individual leg capacity, force balance
and crossing clearances. Optimized/reference projection tests compare passing
and rejected cases, selected geometry and unrounded member demands across both
primary materials, catalogue/custom permissions and several spacings. Additional
checks cover near-contact clashes, frame ownership, receiver reuse, centring,
individual spans, physical primary bearings and the complete engineer workflow.

No production deadline, reduced candidate cap, capacity relaxation, stock-policy
change or change to the two-zone primary load model is introduced. The solver's
existing bounded search is not a global optimality proof. No installer or company
publication is part of this performance change.


## Second optimization pass

The next pass retains complete failed crossing poses only when all permitted
secondary reactions have failed their primary, or all primary starts clash
independently of secondary pitch. Row-dependent failures do not establish that
proof. Immutable secondary choices are prepared once per junction/width/offset.

The geometry cache now extends completed station tiers instead of regenerating
their patterns. It preserves within-tier order, keeps generation constraints in
the key and publishes an extension only after completion. Cancelled builds leave
the completed prefix usable. Reference tests compare exact patterns, stock/support
mapping and ordering for ascending, descending and concurrent station limits.

Complete receiver checks persist across the same calculation's spacing and
fallback passes. An engineer-search winner checked under a wider leg ceiling can
be reused under a tighter ceiling only when it still fits. Width alternatives
outside that ceiling are omitted; negative results retain exact-ceiling identity.
Changed load, geometry, stock permissions or criteria still change the local key.

Single-band primary response reuse now includes exact UDL interval endpoints.
A unit response can be scaled for another positive intensity at unchanged
geometry; the existing near-capacity fallback retains original arithmetic.
The response cache has a bounded 8,192-entry capacity, not a design-space limit.

The second-pass local five-area replay took 40.294 seconds with all complete
fingerprints identical to the first pass. A two-area experiment sharing four
numerical workers took 39.196 seconds, a marginal difference. Production area
scheduling remains unchanged. The Windows workflow uses the same harness against
frozen first-pass commit `1a4582073eaec93b19537b9b37fc93c6bae94739` and the
candidate on one runner in baseline/candidate/candidate/baseline order. A separate
bounded-worker experiment follows. These timings and all per-area evidence are
retained as workflow artifacts; the 30-second target is not claimed from local
measurements.

Additional checks cover 240 optimized/reference crossing cases (including eight
finer-pitch recoveries after a coarse-pitch capacity failure), 160 complete
single-band response/diagram comparisons with exact acceptance-boundary fallback,
receiver-ceiling reuse, and geometry extension/cancellation/concurrent limits.

## Third pass: reject primary-capacity failures before detailed crossing construction

Projected dedicated crossings now check their exact primary UDLs, self-weight
where applicable, deflection, uplift policy and leg capacity before constructing
secondary rows and complete solids. The shared primary-check routine also serves
final result construction. Every permitted transverse-stock reaction pair must
fail before an entire placement can be rejected; a secondary-row failure never
proves another pitch impossible. Passing primary responses are reused only inside
the same immutable junction/criteria snapshot, with a bounded 4,096-entry cache.
Slab-edge line loads are likewise prepared once for that fixed snapshot.

The current net48 source is frozen at
`6650347cd7b361a8b73f62cc4bc8dbebfd47b501` for the next controlled comparison.
One Windows runner executes baseline-net48, candidate-net48, candidate-net10,
candidate-net10, candidate-net48, baseline-net48. All use four workers and
sequential areas. Production remains a Revit 2024/net48 add-in. The modern-runtime
console run is an architecture experiment; no worker integration is included.

The identical harness is compiled against both source versions and records every
returned member's unrounded geometry, loads, reactions and response peaks, in
addition to the selected network fingerprint. Same-runtime output must match
exactly. A cross-runtime result must retain the same structure and agree within
`1e-8 + 1e-10 * max(abs(reference), abs(actual))` for every recorded number.
Independent stock, strength, evaluated deflection, leg, equilibrium and clash
validation runs in both runtimes. Six extra leg-capacity threshold cases cover
both primary materials alongside the existing 240 crossing parity cases.

Two additional experiments were excluded: cached stock-pattern prefixes did not
improve the five-area replay, and prepared primary placement windows changed the
local Area 1 ABBA mean from 18.146 to 17.984 seconds (under 1%, within run-to-run
variation). Their extra implementation is not retained.

Controlled Windows results, identical reported inputs and four workers:

| Source/runtime | First run (s) | Second run (s) | Mean (s) |
| --- | ---: | ---: | ---: |
| Second-pass baseline / .NET Framework 4.8 | 82.058 | 87.913 | 84.9855 |
| Third-pass candidate / .NET Framework 4.8 | 46.366 | 44.609 | 45.4875 |
| Third-pass candidate / .NET 10 | 29.940 | 29.865 | 29.9025 |

The source change reduces the net48 mean by **46.476%**. The modern runtime
reduces the same candidate's mean by a further **34.262%**. All same-runtime
fingerprints match exactly. Modern-runtime round-trip number strings have
formatting differences, but every parsed unrounded number agrees exactly:
maximum numeric difference **0** in both runs. All six runs pass independent
engineering validation, with unchanged selected stock/supports, UDLs, reactions,
68/20/24/24/24 area leg counts and the existing Area 2 separate-slab outcome.

The modern console benchmark only just clears 30 seconds. It excludes Revit UI,
model extraction and any future worker communication/startup cost; it does not
establish a 30-second installed Revit experience. The current Revit/net48 path is
still about 45.5 seconds. A separate modern-runtime worker must be implemented and
measured before the runtime benefit reaches the dashboard. No worker integration,
installer, version change, merge or company publication is included here.

Evidence: https://github.com/Joven091/Doka_Revit_Automation/actions/runs/40711220375
Artifact: `combined-beam-net48-evidence` (ID `12826901484`), SHA-256
`af2c9a0598623ffa6834b68a4caff699f733ae381347eb222858dcd1380e4293`.
Local production/net48 and both harness builds have zero warnings/errors; fourteen
targeted suites pass, including the six new exact leg-capacity boundaries.
