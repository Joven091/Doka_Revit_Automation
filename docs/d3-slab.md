# D3 rectangular slab optimizer

Open Slab, select **D3 Shoring System**, enter length, width, thickness and permissible leg load, then optimize. **Dokaflex** remains available through the same illustrated system picker. Changing systems retains the inputs for both calculators.

## Geometry and material rules

- The slab is rectangular and surrounded by beams. The H20 coverage is inset 200–350 mm from every slab edge; both limits are editable within that range. Only this covered rectangle is loaded. The surrounding shutter/edge strips receive no load in this model.
- Stock lengths default to **1250, 1800, 2450, 2650, 2900, 3300, 3600, 3900, 4500 and 4900 mm**, editable for both layers. Neither layer is cut or extended outside its covered rectangle.
- Secondary H20s are single members at **488, 407, 350, 305 or 275 mm** spacing. Closing tributary strips never exceed the selected spacing. A secondary can overlap another at a shared primary support; each end extends at least **150 mm past its centre**. There is no additional maximum lap length. Overlap timber contributes to material usage and is therefore penalized by the objective. Every physical primary line crossed by a stock member is included in its analysis.
- Primary support lines are **2 × H20** pairs. Primary stock runs have independent ends, no overlap or bending continuity, and at least two supporting leg planes per run. Secondary crossings within 40 mm of a primary butt joint are excluded from the candidate set.
- Frames are **1524 mm**; horizontal distances between frames along the cross side are **600, 1000, 1500 and 2000 mm**. The solver evaluates both slab directions and tower orientations. Mixed longitudinal bays can share legs only along the cross direction. Gaps between independent towers have no cross member. The optional 600 mm exclusion applies to standalone towers, including rotated towers; a 600 mm bay in a joined tower remains permitted.

## Load path and checks

The default area load uses the existing beam calculator's concrete density, heaping/working loads and formwork allowance. Custom placing/working and formwork loads can be entered. Sheathing strength/spacing is not checked.

Each secondary stock is analyzed for its own piecewise uniform load. Overlapped portions share their strip load equally between the two stocks, so the covered area is counted once. Edge rows use their actual, smaller tributary strip. Each secondary reaction enters its primary pair at the actual crossing as a **point load**, rather than being smeared over the primary. Every primary support reaction is the load on that individual tower leg, not an average over a tower. The final leg reactions must sum to covered area × area load.

The existing H20 properties are retained: single member 5 kNm moment, 11 kN shear and EI 449.8756 kNm²; primary pair 10 kNm, 22 kN and EI 899.7512 kNm². The new arbitrary-load Euler–Bernoulli solver uses consistent element loads and free end/support rotations. Displacements within each element include the exact uniform-load quartic term. Strength and displacement peaks are evaluated at polynomial extrema. Deflection criteria **L/500, L/350 (default), L/270** use the adjacent support span or end overhang, not the total stock length or element mesh length. Unrestrained uplift candidates are rejected.

Local bearing, fork-head details, tower height/bracing/stability, foundations, sheathing and the surrounding beams/shutters are outside the calculator. The entered permissible leg capacity must represent the actual tower configuration. Lap equal-sharing and paired-primary equal-sharing are model assumptions, not checks of bearing/contact details.

## Optimization and output

Candidates rank by **fewest legs**, then **least total H20 length**, then fewer pieces. Equal-material candidates prefer setbacks closer to 200 mm, then lower governing utilization. Increasing the covered area is therefore not allowed to force an extra piece solely to approach the minimum setback.

This is a bounded search, not a proof of a global minimum: at most 12 primary stock covers, 40 longitudinal candidates (different support counts and manufactured mixed bays), 8 tower bands, 8 returned secondary stock plans and both orientations are evaluated. Each stock chain contains at most 8 pieces; slab dimensions are limited to 30 m. Regular tower bands and centred stock/support offsets are searched. A no-layout result means none was found in this candidate set, not that all arrangements are impossible.

The scrollable plan shows overall slab dimensions, setbacks, primary stock, secondary spacing/stock, laps, H20-end-to-support distances, frames/crosses, and gaps between towers. Individual leg labels can be enabled. Expand the schedules for every leg load/coordinate and every stock's shear, moment and deflection check. Saving uses the distinct `D3Slab` calculation kind; loading restores inputs and requires a fresh optimization. Export reports record the covered area, orientation, stock/member checks and leg schedule.

## Validation

Run:

```powershell
dotnet build src/MyRevitAddin.csproj -c Release --warnaserror
dotnet run --project tests/D3Slab.Tests -c Release
dotnet run --project tests/D3Slab.UiSmoke -c Release
```

The math suite compares uniform-load reactions, signed diagrams and exact extrema with the independent three-moment solver, checks point-load closed forms, superposition and force/moment equilibrium, and verifies complete layouts at multiple sizes, leg capacities and deflection criteria. Layout tests check that all physical supports are modeled, minimum lap projections, stock availability, load conservation and each individual reaction. WPF checks cover invalid text, stale-search cancellation, drawing quantities, both scroll directions, save/load/report guards, real modal system selection and retained input state. Revit transactions are not introduced by this optimizer; an in-Revit display check is still required.
