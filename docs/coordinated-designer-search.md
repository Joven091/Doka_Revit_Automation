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

Network main beams now retain concrete beyond primary stock ends within the permitted end allowance. Signed sheathing influence reactions transfer each end strip to the outer two secondaries, preserving force and moment. The primary beam-concrete UDL is scaled over centred primary coverage to preserve the complete force and longitudinal resultant, following the existing crossing end-strip model. Slab UDLs stop at the shared slab coverage, and crossing increments retain their actual footprints. The primary design model remains distributed loading, not secondary-row point loads.

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
