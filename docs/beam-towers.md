# Beam Calculator: shoring tower mode

Choose **Shoring towers**, enter concrete beam length (mm), permissible load per leg (kN), and available primary H20 stock lengths, then select **Optimize towers**. The project stock list defaults to 1250, 1800, 2450, 2650, 2900, 3300, 3600, 3900, 4500 and 4900 mm. There are no overlaps or cuts. The existing beam dimensions, adjacent slab strips, default/custom load allowances and 3/5/10 mm deflection selection remain applicable. Free-prop catalog and extension inputs do not affect tower mode.

The result gives leg count, group count, orientation, support coordinates, cross bays, gaps, end overhangs, left/right leg reactions, primary moment/shear/deflection and governing utilization. The stock schedule adds each member's length, start/end, assigned supports, checks and quantities. Each scheduled run uses four H20s: two per primary line. The plan shows individual stock pieces and dotted free member breaks. Save/load retains stock settings; a loaded tower record must be optimized again. Old Beam records still load, with the project stock defaults when stock fields are absent.

## Geometry and load path

- Normal orientation: two leg rows 1524 mm apart, centered on the concrete beam. Each transverse frame has two legs. Cross-side horizontal bays are exactly 600, 1000, 1500 or 2000 mm.
- Joined bays share a transverse frame: two bays have three frames/six legs. Mixed bay sizes and mixtures of isolated and joined groups are candidates. Each group has at least two frames.
- Rotated orientation: isolated towers have a 1524 mm longitudinal frame span and one selected cross dimension across the beam. All groups in a candidate use the same transverse width. Joining rotated towers would introduce additional primary rows across the beam and is outside this two-line model. Mixed orientations in one layout are not searched.
- Each longitudinal primary line consists of two equally loaded H20 members. Single H20 properties match the existing calculator: M=5 kNm, V=11 kN, EI=449.8756 kNm². Pair limits are 10 kNm, 22 kN and 899.7512 kNm².
- Each uncut stock run is checked as an independent member with its own free ends and at least two supports. The two primary lines use identical stock arrangements, and each pair shares its line load equally. Supports are not shared across stock breaks: no butt splice, rotational continuity, overlap or unsupported transfer between pieces is assumed. Joined tower frames and stock member breaks are separate decisions.
- Stock members cover a centered continuous length within the concrete beam, without internal gaps or projection. A new maximum concrete end-overhang input defaults to 75 mm per end (0 restores exact coverage; allowed range 0–500 mm). Only listed stock lengths are used, with unlimited quantities assumed. For 6750 mm, exact coverage requires at least four runs (1250+1250+1800+2450), forcing 16 legs. Allowing 75 mm at each end permits two 3300 mm runs and eight legs when structural checks pass. Existing saved records without the new field retain zero allowance.
- Only concrete load directly over the actual H20 stock length is included. Concrete end overhang contributes no load to the primary or leg checks. For two 3300 mm runs under a 6750 mm concrete beam, the loaded length is 6600 mm; the remaining 150 mm is excluded. Diagrams use this same loading basis.
- Every support centre must be inside its assigned stock member by at least the editable stock-end allowance (default 150 mm). This is a planning setback, not a manufacturer-verified head/bearing design. Adjacent free ends must be detailed with suitable secondary placement and load transfer; secondary members are not designed here.
- Redundant connections are removed whenever the resulting independent groups each retain at least two frames. Four-frame runs become two independent four-leg towers; three-frame runs retain their shared frame/six-leg arrangement. A gap equal to a stock cross dimension is not automatically braced.
- A gap is the distance between the leg planes of adjacent groups. It is not a component-clearance allowance. No component envelopes or site obstacles are modeled.

The same existing concrete, formwork and placing/working line loads are used. Adjacent slab eccentricity is resolved about the actual transverse row spacing for each orientation. Symmetric loading gives equal row loads; individual longitudinal leg loads generally differ. The entered permissible leg capacity must apply to the actual tower arrangement. Tower self-weight and height-dependent system capacity are not derived here.

## Structural solution

`TowerBeamAnalysis` solves the unequal-span three-moment equations separately for each stock member, with its own overhangs, uniform load over the stock length and constant EI. Supports are rigid, vertical and non-settling. It calculates signed individual reactions, maximum absolute moment, maximum shear, and maximum absolute deflection relative to the support level including both member overhangs. Deflection extrema are roots of integrated slope; production does not sample deflection. Negative reactions reject a member. The heavier primary line governs; the other row's reactions follow linear scaling. Each leg is assigned to exactly one stock run, and each part of the covered H20 length is loaded exactly once per primary line.

All four checks must pass for every member: leg capacity, primary pair moment, primary pair shear and deflection. Secondary H20 and sheeting checks remain excluded. Local bearing, pair load-sharing and member-end detailing, tower stability/bracing/height, horizontal actions, partial loading, support settlement and foundations are also outside this span calculator. A successful result is not verification of those items. The rule excluding butt joints on support heads is consistent with Doka's [timber formwork beam user information, page 8](https://direct.doka.com/_ext/downloads/downloadcenter/999791002_2024_12_online.pdf); the adjacent independently supported free ends in this model are not a designed splice.

## Search and ranking

The search ranks **fewest legs**, **fewest braced bays**, **fewest primary H20 pieces**, then **lowest governing utilization**. It uses necessary load-capacity/span and stock-count lower bounds, tests increasing station counts, enumerates short bay sequences and samples larger combinations with a fixed random seed. Regular and irregular gap/end allocations are explored. Manufactured dimensions remain exact; gaps and the left offset snap down to 25 mm, and the right overhang receives the remainder. A candidate is checked using its final positions.

For each candidate, `TowerStockPlanner` uses a directed acyclic stock-cover search over each reachable covered stock length within the end allowance. Each transition is one listed stock length covering at least two support stations, with the required setbacks and all structural checks passing. The best prefix for each end coordinate is retained by piece count then utilization. This is valid because all supports preceding that coordinate have already been assigned and no member shares a support at a break. Thus the stock plan is optimized for the tested tower geometry. The overall tower geometry search remains bounded. The minimum stock run count is found independently using exact integer length combinations; it supplies a lower bound of four legs per run. Reachable covered lengths are enumerated within the entered end allowance. Centered isolated tower layouts on one or two stock runs are also seeded directly, avoiding reliance on random gap placement for simple layouts.

Limits: beam length up to 50,000 mm, up to 40 longitudinal stations/80 legs, up to 200,000 generated candidates per covered length, and at most 4,096 trials per group count/orientation. Stock entries are positive whole millimetres (at most 30 entries), and stock lengths must sum exactly to the covered length; the remaining concrete length is split equally between the two ends. The tower search does **not** prove global optimality. Results explicitly say “best found.” When the station count reaches the stronger of the universal load-capacity and stock-count lower bounds, minimum leg count alone is proven within this model; group count and layout still are not proven optimal. A failed geometry search says no layout was found, not that no feasible layout exists. Smaller leg counts can be missed by sampling.

Optimization runs off the UI thread without Revit calls. Editing any calculation input clears the result. A generation counter discards an in-flight result if its inputs or support mode have changed. The prop mode remains synchronous and retains its prior calculation.

The compact screen keeps detailed checks collapsed. Plan dimensions identify each cross bay or longitudinal frame span, gaps between tower leg planes, the transverse frame/cross dimension, actual stock lengths, overall concrete length and concrete end overhang. Long plans scroll horizontally to preserve label spacing. The diagram button selects each stock run and primary line, showing signed shear, moment and deflection with exact peaks and permissible limits. Changing inputs closes stale diagrams.

## Revit and verification

Tower mode supports saving and report export. It deliberately disables the existing Revit import, with an independent handler guard, because `Max Prop spacing` and two cantilever parameters cannot represent nonuniform tower layouts. No towers are placed in Revit by this change.

`tests/Beam.Tests` retains the prop regression suite and adds closed-form simple-span/shared-frame checks, 160 independently assembled Euler–Bernoulli stiffness solutions for unequal spans/overhangs, equilibrium, mixed bays, rotations, input validation, search feasibility and stale-result checks. An additional 40 independent stiffness comparisons verify end point loads, and the 6750 mm regression checks eight legs and equilibrium for the covered H20 length only. Stock tests verify coverage within the configured allowance, available lengths, no shared support at breaks, no cutting/overlength substitution, support setbacks, quantities and independent stiffness comparisons for each accepted stock member. The independent solution samples its exact element displacement polynomials to compare production extrema. `tests/Beam.UiSmoke` covers switching modes, inactive prop errors, invalid tower/stock/setback inputs, uncovered-length feedback, real leg/member counts, report content, stock record round-trip, legacy records and diagrams.

Run:

```text
dotnet build src/MyRevitAddin.csproj -c Release --warnaserror
dotnet run --project tests/Beam.Tests -c Release
dotnet run --project tests/Beam.UiSmoke -c Release
```

## H20 selection and support offsets

The plan dimensions both H20 ends to their nearest support-leg centres, including free ends at internal stock breaks. Dimensions are common to both primary lines and both members of each double pair. Press Show shear, moment & deflection, then select a drawn H20 in the main tower plan; both members of the selected pair are highlighted blue. The response window has no duplicate plan or dropdowns. Selecting another pair in the main plan updates the open diagrams. The complete plan scrolls horizontally, including its rightmost stock ends and dimensions. The diagram window displays the double-H20 pair response using the full primary-line load and pair EI. Permissible shear is 22 kN and moment is 10 kNm under equal sharing. Deflection is unchanged from the individual member response. Support reactions equal the supporting leg loads. Clicking either member selects the same pair. Every plot includes the end offsets and intervening support spans. Input edits invalidate the selection and close stale diagrams.

## Standalone 600 mm exclusion

The **Exclude standalone 600 mm towers** checkbox defaults off. When enabled, no four-leg standalone tower may use a 600 mm cross dimension, including rotated towers. Joined 600 mm bays remain available. Redundant-connection removal keeps enough frames joined to avoid creating an excluded standalone tower. Changing the checkbox invalidates results and requires optimization again. Save/load and report inputs retain this setting; older files default to unrestricted behavior.

## Visual support choice

The Beam calculator opens with two illustrated cards: Dokaflex and D3 Shoring System. The illustrations show their beams and supports without concrete slabs, and the cards have no subtitles. Neither is preselected. Choosing a card reveals that system's inputs; Change system returns to the cards while keeping entered geometry. Selecting the same system retains its calculated layout, while switching modes invalidates the old result. The launcher's retained view model remembers the choice when reopening the calculator. Loading a saved calculation selects its recorded system; older prop records select Props. Save, export and import remain disabled until a system is chosen.

## Span-based deflection criteria

Both Dokaflex and D3 Shoring System use L/500, L/350 (default) or L/270, matching the slab calculator's selectable criteria. L means each H20 support span. End overhangs use their own length divided by the same denominator, with support rotation included. The exact peak displacement is checked within each region, including the tip; this also catches an interior overhang maximum if it exceeds the tip displacement. Mixed tower spans are checked separately, so a longer stock length cannot relax a shorter span's limit. Governing utilization uses the largest local peak/allowable ratio. Tower diagrams show separate dashed limits for each region, and stock schedules give the governing deflection and allowance. These are selected calculation criteria, not declarations of a mandatory project standard.

Prop spacing continues to use the existing strength, spacing and capacity limits, and tests the rounded layout's exact deflection for two or three supports, or four through twelve supports for the existing 4+ selection. Saved calculations store the selected criterion explicitly. Older fixed-mm records retain their original check rather than silently converting to a ratio. Selecting a different criterion invalidates a tower layout and requires optimization again.


## Optional custom H20 lengths

The drawing workspace shared-settings panel and the beam calculator both provide **Allow custom H20 lengths (25 mm)**. It defaults off, is shared between the windows, participates in workspace undo/redo, and is saved with the calculation. Older files load with it off.

When enabled, the entered stock list remains available. Additional primary, lower secondary, and slab H20 lengths are positive multiples of 25 mm, subject to the existing geometry/member limits and 50,000 mm member-length bound. A concrete length not divisible by 25 mm still needs either a fitting catalogue combination or an allowed concrete end overhang; projection and internal gaps remain forbidden. Schedules identify custom primary and lower lengths as well as custom slab lengths.

Beam-only optimization retains the legs, braced bays, H20 quantity and utilization ranking. Transferred-slab optimization retains legs, total timber and utilization ranking. Network optimization retains tower legs plus upper feet, then timber, counting shared upper H20s once. Each enabled search also evaluates the original catalogue-only search and retains that solution if it ranks better. This is not simply a fallback after failure.

Custom beam-only primaries are searched on the 25 mm grid. Transfer/network primary candidates add balanced runs with one to ten independent members, using the shortest, longest and midpoint grid coverage within the existing end allowance, plus small joint shifts to avoid transverse rows at member breaks. Catalogue covers remain in the candidate set. Lower secondaries add the shortest 25 mm length enclosing the existing bearings and point loads with end allowance; unloaded tails do not require a dense length search. Slab members search every 25 mm length within the existing 200–350 mm edge-strip range, including when catalogue lengths already fit. The existing bounded geometry/option caps remain; a global optimum is not proven.

All member capacities, deflection criteria, bearing-uplift rejection, permissible leg loads, support setbacks, load stages and network row alignment still apply. Custom lengths solve availability restrictions, not failed structural checks. With the option off, the previous slab-only custom fallback remains in place.


## Copyable diagnosis for slow searches

**Diagnose** is available in the dedicated beam formwork toolbar and the beam calculator. It opens a separate window with optional remarks, a read-only text report, **Refresh report**, and **Copy report**. Copy refreshes the report and includes the remarks. Paste the resulting text into the chat for review.

The button works before calculation, during an active background search, after failure, and with invalid/uncommitted field text. It does not commit edits, start another search, or change a result. Refresh while a search is running to capture newer elapsed time and progress.

The report includes a build module ID and runtime information; current model inputs; visible field text; exact model inputs captured at the last search start; whether those inputs now differ; elapsed time; catalogue/custom pass timing; current work and receiving-beam context; coverage, candidate, member-analysis and rejection/check counters; outcome and result summary; and exception/failure details. Counters are approximate live snapshots and rejection checks are not counts of unique layouts. Completed pass timings do not include an in-progress pass. The solver writes atomic progress only; the UI never enumerates its live mutable collections. Structural checks, search bounds and ranking remain unchanged.

Use **Cancel** in the drawing toolbar or **Cancel analysis** in the beam calculator to stop a running search. Cancellation is checked during coverage generation, layout enumeration and member/load-case checks. The current numerical check may finish before the search stops. Cancel is disabled once requested; Calculate is available again after the search has stopped. No partial layout is published. Diagnose retains the elapsed time, last search stage, counters and a `Cancelled` outcome, and a fresh calculation starts with a new cancellation state.

## Project-specific connected formwork assumption

**Exclude uplift check (connected plywood / deadweight assumed)** is available in the D3 calculator and drawing workspace. It defaults off. Enable it only for a calculation using this stated project assumption: plywood connects the formwork and adequate stabilizing deadweight/restraint is provided. This option excludes negative bearing/leg reactions from candidate rejection. It does not calculate connection capacity, add stabilizing deadweight, average leg loads or replace signed reactions with zero. Moment, shear, relative deflection, maximum individual leg load, geometry and staged load checks still apply. The legacy prop calculator and the other calculators are unchanged.

The option is saved with the Beam inputs, included in diagnostic reports, and recorded explicitly in the calculation basis and exported report. Older files default to bearing-only checks. Changing it clears stale results; drawing undo/redo restores it, and **New session** resets it. Accepted member schedules retain their minimum signed reactions. Tower leg schedules and drawing tooltips retain both the maximum compression and minimum signed reaction across stages. Negative transfer reactions remain signed in the primary UDL design model, with full deflection evaluation. Shared crossing reactions retain their physical footprints, and conservative reaction bounds for larger crossing sets remain reported.

For the uploaded 6,900 mm legacy crossing case, the current primary UDL solver retains leg-capacity rejection at 275 mm upper spacing with uplift excluded. At 250 mm it finds a complete layout with 118 tower legs and a 42.24 kN peak leg load; every calculated H20 passes moment, shear and relative deflection. These results supersede the earlier point-load calculation. The replay explicitly uses Legacy slab rows, matching the uploaded report; modern crossings are separately tested with shared and dedicated supports.

Run `tests/Beam.Tests` with `--uplift` for assumption/force-propagation checks and `--uplift-reported` to replay the uploaded case and the closer-spacing search. The default regression suite retains bearing-only behavior. Windows smoke tests cover both checkboxes, result invalidation, save/load, old-file defaults and drawing undo/redo.


## Designer preferences and centred secondary H20s

At equal support count, beam and network searches avoid standalone 600 mm towers, minimize braced-bay count, prefer wider standalone cross braces, then minimize member quantity/length. Linked groups receive no standalone-width penalty; redundant-connection removal retains the fewest existing links needed to avoid narrow standalone groups when possible. Rotated towers use the actual cross dimension, rather than the 1524 mm frame span. Smaller standalone braces remain available when required for a feasible layout. Support/brace counts are cost proxies, not catalogue prices or a height-dependent stability check. The same comparison applies to search bounds, catalogue/custom selection, receiver-chain states and final results, including dedicated crossing towers.

Each lower secondary H20 is centred on its concrete beam, including when tower legs are offset. Stock selection must enclose every bearing and load, with the required end allowance on both ends; member analysis and drawings use these actual centred coordinates. Plan H20 colours distinguish orange paired primaries from purple single secondaries; the existing secondary-visibility toggle and member selection remain available. Diagnostics include tower preferences and secondary midpoint offsets.

`dotnet run --project tests/Beam.Tests -c Release -- --designer-preferences` checks wide/narrow/linked/rotated tower choices, mirrored one-sided secondary centring and the diagnostic's three parallel-beam dimensions.
