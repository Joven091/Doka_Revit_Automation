# Coordinated designer search

The October 12 reference drawing demonstrates a useful stock/support family: two 1,800 mm end runs and one 3,300 mm centre run. Two linked 1,000 mm centre bays share a frame beneath the continuous centre stock. The arrangement uses seven two-leg frames instead of eight, without sharing supports or bending continuity across stock breaks.

## Candidate generation

- Retain the existing stock-cover search and add mirrored catalogue families and reordered runs independently of its three-sequences-per-total cap. More stock pieces can require fewer steel support frames.
- Protect mirrored regular tower groups separately from the inset-based geometry cap. These are general catalogue families, not fixed coordinates for the reference drawing.
- Keep the shared slab row grid. Add local beam-only end secondaries where the complete beam/end-strip tributaries exceed the entered spacing. They carry no slab feet or slab/shutter tributary.
- Compare a coordinated modern-crossing search at the winning shared coverage and phase even after the ordinary search finds a passing network. Retain different receiving widths/orientations until their junctions have been checked.
- Try every searched placement of each larger normal crossing bay before a 600 mm fallback; an early passing narrow placement must not hide a larger bay at a later shift. Every candidate still requires stock, bearing, member, individual leg and complete footprint checks.

The search remains bounded. Selected stock, tower and junction configurations need not match a person's drawing when its omitted bearing, secondary or connection details differ from the model.

## Relative economic preference

The initial score is expressed in equivalent timber length:

`material_mm + 5000 × tower_legs + 500 × upper_feet`

Lower scores are preferred. Tower legs include main and dedicated crossing supports. Shared slab timber is counted once; added beam-only rows add lower timber but no upper feet. This preference deliberately accepts the user's trade-off of two saved tower legs for five additional metres of timber. It is not a supplier quotation, complete steel bill, or erection/hire/transport cost calculation. The coefficients should be calibrated when project rates are available.

Brace preference, stock/support symmetry and (for individual transfer layouts) utilization resolve economic ties. Symmetry measures the actual support coordinates and mirrored stock lengths; it does not average loads or reactions. A larger brace remains a design preference, not a tower stability calculation.

## Complete concrete loads

Network main beams now retain concrete beyond primary stock ends within the permitted end allowance. Signed sheathing influence reactions transfer each end strip to the outer two secondaries, preserving force and moment. The primary beam-concrete UDL is scaled over centred primary coverage to preserve the complete force and longitudinal resultant, following the existing crossing end-strip model. Slabs have the same physical length as the main beams. Their longitudinal end strips transfer to the outer two actual slab H20 rows, excluding rows removed by crossing footprints. Primary slab UDLs are scaled over each beam’s centred primary coverage to retain the complete slab force and resultant. Crossing increments and slab exclusions retain their unscaled intensities over actual footprints. Shared support coverage never shortens the concrete load extent. The primary design model remains distributed loading, not secondary-row point loads.

This changes the load basis from the earlier report, which excluded main-beam concrete outside stock. Added timber cannot be compared against that report as though the supported loads were identical. Sheathing strength, connections, bearing, actual tower stability, foundations and assumed uplift restraint retain their existing separate-design limitations.

## Reference and variation checks

Run:

```sh
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release -- --coordinated-designer
```

The reported geometry is 6,900 mm long, with main beams 800/650/800 mm wide and 1,750/1,150/1,750 mm deep, 300 mm slabs, clear bays 2,677/1,875 mm, and centred 650 × 1,150 mm crossings. The drawing's 2,752/1,950 mm bays are tested separately. Catalogue-only beam supports, custom slab stock, 275 mm maximum spacing, 70 kN entered leg capacity, L/270 and the report's connected-formwork uplift assumption are retained.

The reported and drawing-dimension fixtures each find 46-leg networks. The reported fixture discovers B1's mirrored 1,800/3,300/1,800 mm layout and shared centre frame, and X1 avoids the unnecessary standalone 600 mm bay; its peak leg is about 41.36 kN. Its complete-load timber quantity is about 378.80 m. An offset-crossing fixture uses 48 legs, rather than forcing the reference topology. A longer beam with unequal loads and no crossings tests adaptation independently of the reference junctions.

Checks include complete beam-concrete force, physical slab transfer, independently summed member force/moment equilibrium, all calculated member stages, individual leg capacities, centred secondaries, aligned shared slab rows, and zero invented slab-foot loads on local beam rows. Existing crossing completion retains full geometry and junction checks. Windows CI also runs the reference suite on .NET Framework 4.8.

## Review loop

For each new designer example, record exact dimensions, elevations, stock/custom scope, spacing, loading stages, uplift assumption, bearing/clearance details and the component quantities. Compare candidate generation, engineering acceptance and final ranking separately. Add a regression for the general design principle revealed by the difference, then rerun earlier examples. A matching leg count alone is insufficient: stock order, group boundaries, support positions, secondaries and crossings must also be compared.

## Beam options and site setting out

In a calculated network plan, right-click a main beam to find checked layout options. Option 1 retains the currently selected arrangement. Alternatives include exact tower/primary patterns from other beams, a mirror of the current arrangement, and different transverse arrangements. Each alternative is recalculated using the target beam's actual concrete and slab loads; the other beam patterns stay fixed. Every crossing is reconstructed, all receiving construction stages are rechecked, and full-network load conservation and movements are recalculated. Only complete passing arrangements are selectable. A copied pattern that passes the beam checks but cannot complete the crossing search is shown disabled, with its rejection details. Labels show beam/total legs and the whole-network H20 length change. The selected option updates the plan, member diagrams, schedules, diagnostics and quantities together. Options are cached for the current arrangement, and geometry/load edits invalidate them. Cancelling an alternative search retains the selected layout.

Network geometry generation translates complete independent tower groups to make gaps between their adjacent leg planes multiples of 25 mm. Fixed 1,524 mm frames and linked brace bays are preserved. Support-end setbacks constrain each translation; all member and junction checks use the resulting coordinates. Dimensions display those coordinates, rather than rounding a checked layout afterwards. This setting-out rule concerns gaps between independent groups, not every absolute coordinate or factory frame dimension.

The crossing search tries transverse offsets at each longitudinal shift before accepting uneven longitudinal cantilevers. It continues to check actual full-height leg columns and complete tower-to-tower envelopes; no collision rule is removed. Actual tower-head hardware, height-dependent stability and supplementary bracing/coupler prices remain outside the current model. A selectable option is a passing result under the existing calculation assumptions, rather than a new stability approval.

Automatic calculation completes its ordinary 200 mm stock/custom, spacing and coordination searches first. Only if no standard full network passes does it repeat the search with local 175 mm exceptions enabled. A manually chosen beam-pattern alternative may enable that local fallback for its selected pattern. A crossing still tries 200 mm at both ends first. If that detail cannot fit the local junction, the search tries 175 mm at one local end, then the opposite end, then both if necessary. A locally fitting detail is retained when a later crossing fails; the network pattern search handles that failure. The selected allowance changes the physical clearance, deeper-receiver shutter strip width, midpoint reaction (87.5 mm for a 175 mm strip), and all receiving construction-stage loads. It never changes ordinary slab-edge shutter reactions. Full stock, columns, independent towers, member strength/deflection, leg reactions and crossing force/moment conservation remain checked. The plan and schedule identify exceptional allowances. This fallback does not by itself make the reported copied B3 pattern feasible at X2.

Loading diagrams combine beam, slab and crossing contributions over their actual modelled intervals. Primary tails outside shared support coverage retain both beam and slab baseline UDLs; the shorter timber extent does not remove concrete load. Crossing increments stay on their footprints. Close support reaction values are placed on additional rows rather than omitted; the loading card grows to retain all values and dimensions.
