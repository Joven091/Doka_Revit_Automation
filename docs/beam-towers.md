# Beam Calculator: shoring tower mode

Choose **Shoring towers**, enter concrete beam length (mm) and permissible load per leg (kN), then select **Optimize towers**. The existing beam dimensions, adjacent slab strips, default/custom load allowances and 3/5/10 mm deflection selection remain applicable. Free-prop catalog and extension inputs do not affect tower mode.

The result gives leg count, group count, orientation, actual support coordinates, cross bays, gaps, two end overhangs, left/right leg reactions, primary moment/shear/deflection and governing utilization. The plan uses the actual leg positions; its numbered stations correspond to the schedule. Save/load retains the input mode; a loaded tower record must be optimized again. Printable reports include the complete support schedule and calculation scope. Old Beam records still load as props.

## Geometry and load path

- Normal orientation: two leg rows 1524 mm apart, centered on the concrete beam. Each transverse frame has two legs. Cross-side horizontal bays are exactly 600, 1000, 1500 or 2000 mm.
- Joined bays share a transverse frame: two bays have three frames/six legs. Mixed bay sizes and mixtures of isolated and joined groups are candidates. Each group has at least two frames.
- Rotated orientation: isolated towers have a 1524 mm longitudinal frame span and one selected cross dimension across the beam. All groups in a candidate use the same transverse width. Joining rotated towers would introduce additional primary rows across the beam and is outside this two-line model. Mixed orientations in one layout are not searched.
- Each longitudinal primary line consists of two equally loaded H20 members. Single H20 properties match the existing calculator: M=5 kNm, V=11 kN, EI=449.8756 kNm². Pair limits are 10 kNm, 22 kN and 899.7512 kNm².
- Primary lines are idealized as continuous over the full concrete beam length. Every leg lies within that length. This version optimizes support spans, not stock H20 selection, splices or overlaps.
- A gap is the distance between the leg planes of adjacent groups. It is not a component-clearance allowance. No component envelopes or site obstacles are modeled.

The same existing concrete, formwork and placing/working line loads are used. Adjacent slab eccentricity is resolved about the actual transverse row spacing for each orientation. Symmetric loading gives equal row loads; individual longitudinal leg loads generally differ. The entered permissible leg capacity must apply to the actual tower arrangement. Tower self-weight and height-dependent system capacity are not derived here.

## Structural solution

`TowerBeamAnalysis` solves the unequal-span three-moment equations with free overhangs, uniform load and constant EI. Supports are rigid, vertical and non-settling. It calculates signed individual reactions, maximum absolute bending moment, maximum shear, and maximum absolute deflection relative to the support level including both overhangs. Deflection extrema are found by integrating curvature and solving for zero slope between curvature roots; no deflection sampling is used in production. Negative reactions reject a layout. The heavier primary line governs; the other row's reactions follow linear scaling under the adopted uniform load model.

All four checks must pass: leg capacity, primary pair moment, primary pair shear and deflection. Secondary H20 and sheeting checks are intentionally excluded. Local bearing, pair load-sharing details, timber joints, tower stability/bracing/height, horizontal actions, partial loading, support settlement and foundations are also outside this span calculator. A successful result is not verification of those items.

## Search and ranking

The search ranks **fewest legs**, then **fewest tower groups**, then **lowest governing utilization**. It uses a necessary load-capacity/span lower bound, tests increasing station counts, enumerates short bay sequences and samples larger combinations with a fixed random seed. It explores regular and irregular gap/end allocations. Manufactured dimensions remain exact; gaps and the left offset snap down to 25 mm, and the right overhang receives the remainder. A candidate is checked using its final positions.

Limits: beam length up to 50,000 mm, up to 40 longitudinal stations/80 legs, up to 200,000 generated candidates, and at most 4,096 trials per group count/orientation. The search is bounded and does **not** prove global optimality. Results explicitly say “best found.” When the returned station count reaches the universal load-capacity lower bound, the minimum leg count alone is proven within the adopted two-primary-line model; group count and layout still are not proven optimal. A failed search says no layout was found, not that no feasible layout exists. Smaller leg counts can be missed by sampling.

Optimization runs off the UI thread without Revit calls. Editing any calculation input clears the result. A generation counter discards an in-flight result if its inputs or support mode have changed. The prop mode remains synchronous and retains its prior calculation.

## Revit and verification

Tower mode supports saving and report export. It deliberately disables the existing Revit import, with an independent handler guard, because `Max Prop spacing` and two cantilever parameters cannot represent nonuniform tower layouts. No towers are placed in Revit by this change.

`tests/Beam.Tests` retains the prop regression suite and adds closed-form simple-span/shared-frame checks, 160 independently assembled Euler–Bernoulli stiffness solutions for unequal spans/overhangs, equilibrium, mixed bays, rotations, input validation, search feasibility and stale-result checks. The independent solution samples its exact element displacement polynomials to compare production extrema. `tests/Beam.UiSmoke` covers switching modes, inactive prop errors, invalid tower text, real leg count, report content, record round-trip, legacy records and diagrams.

Run:

```text
dotnet build src/MyRevitAddin.csproj -c Release --warnaserror
dotnet run --project tests/Beam.Tests -c Release
dotnet run --project tests/Beam.UiSmoke -c Release
```
