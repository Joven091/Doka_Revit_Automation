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
