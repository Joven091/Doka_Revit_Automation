# Beam calculator: calculation and UI fixes

The calculator also provides [shoring tower optimization](beam-towers.md), with a separate unequal-span solver, double H20 primary lines, mixed/shared-frame bays, rotations and per-leg capacity. The prop-mode assumptions below remain unchanged.

## Inputs and validity

Active dimensions must be positive and finite; active slab thickness cannot exceed total beam depth. Custom live/formwork allowances may be zero but cannot be negative or non-finite. Inactive optional fields do not contribute loads or block the calculation. Invalid numeric values and WPF text-conversion errors clear results and disable import. The import handler independently checks validity before any model access.

Deflection choices carry numeric values (3, 5, 10 mm); display text does not select the calculation branch.

## Load distribution

The existing transverse geometry is retained and shown in the layout-basis panel:

- Each checked slab contributes the entered strip width (default 300 mm, shared by checked sides).
- Secondary member overall transverse length = concrete beam width + 1400 mm.
- Primary support lines lie at its quarter points, so their separation b = (beam width + 1400) / 2.
- The concrete beam is centered between the primary lines.

For line loads Qbeam, Qleft, Qright (kN/m), with the slab strip centroids at ±e, where e = (beam width + slab strip width) / 2 in mm:

```
Qtotal = Qbeam + Qleft + Qright
Rleft  = Qtotal/2 - (Qright - Qleft) × e/b
Rright = Qtotal/2 + (Qright - Qleft) × e/b
w = max(Rleft, Rright)
```

Each primary line uses its equilibrium reaction instead of assuming a 50/50 split for asymmetric loading. The more heavily loaded line governs the common longitudinal prop spacing and maximum prop force. Nonpositive line reactions are rejected because uplift is outside this model.

For a 500 × 200 mm concrete beam with a 200 mm left slab: Rleft = 3.171875 kN/m; Rright = 1.878125 kN/m. With two props per primary line, spacing is 2000 mm, cantilever 700 mm and the heavier prop reaction is 5.3921875 kN. Mirroring the slab swaps line reactions without changing the recommended spacing or maximum prop force.

## Load allowance and spacing

Default heaping = min(1.75, max(0.75, 0.10 × concrete self-weight)), in kN/m². Working load remains 0.75 and formwork remains 0.40. Custom live load replaces the combined placing/working allowance, as before. The heaping basis is documented in the [Doka formwork manual](https://direct.doka.com/_ext/downloads/downloadcenter/999776002_2024_08_online.pdf).

Existing longitudinal moment, shear, deflection and reaction coefficients are retained, as are H20 properties and 25 mm downward rounding. The 2000 mm two-support ceiling and 3000 mm general ceiling are applied separately from structural limits, so a ceiling is correctly labelled Max Spacing Limit.

Examples:

- 800 × 600 mm, three props per line, no side slabs: corrected default spacing 2400 mm.
- 500 × 700 mm, four-plus props per line, no side slabs: 5 mm deflection choice gives 2800 mm; 10 mm gives 3000 mm.

This remains a uniform-load/equal-span primary-beam calculation. Plywood, secondary beams, beam lengths/joints, restraint and local bearing require separate checks. The entered strip width and fixed support-line geometry must match the actual load path.

## Selected prop capacity

The prop dropdown references the same PropDataHelper tables used by the slab calculator: Eurex eco 20 and DokaFit, including size and orientation. Capacity data is not duplicated. Automatic extension in mm = floor height − total beam depth − 418 (two 200 mm H20 layers plus 18 mm sheeting). This assumes the base is at the supporting floor surface and the floor height is measured to the top of the concrete beam. Different build-ups require an adjusted model.

Actual extension must lie within the selected configuration's supported table range. Lookup rounds upward to a 100 mm row, matching the slab calculator; missing/nonpositive capacity blocks results and import. After primary-beam and spacing limits, the solver reduces spacing in 25 mm increments until the maximum prop load is within capacity, recomputing the rounded cantilever each time. Utilization is maximum prop load / permitted capacity × 100. This is the largest permitted grid increment under the adopted coefficient model, not a general optimization of arbitrary support layouts.

Example: 500 × 1000 mm beam, three props per line, DokaFit 400 at bottom, floor height 4918 mm gives 4000 mm extension and 16 kN capacity. Spacing becomes 1700 mm, prop load 15.83125 kN; the next 25 mm increment exceeds capacity.

## Import behavior

Targeting is retained: matching PrimaryBeam family instances, dimensions and Structural Element Type in the active view. No selected-only targeting or slab/support-assumption matching has been added.

All three output parameters must exist, be writable, have Double storage and Length specification before an instance is changed. Missing/read-only/wrong-type parameters cause that instance to be skipped. Each eligible instance is written in a subtransaction, so parameter-write failures roll back that instance rather than leave a partial update. The final report distinguishes updated, skipped and failed counts, with example reasons. A parent transaction that does not commit cannot produce a success report.

## Visualization

The old string-coordinate section has been replaced by a typed vector axonometric diagram:

- Concrete beam and optional left/right slab wings, without a legend.
- Secondary H20s crossing the primary H20s at right angles in plan.
- Two primary H20 lines running along the concrete beam.
- Two, three, or four representative props per primary line, according to the support setting.
- Prop head plates meet the primary underside; primary tops meet secondary undersides.

Geometry uses numeric Point/StreamGeometry objects and does not parse localized coordinate strings. Longitudinal length, prop height and the number of secondary beams are schematic. This is a depiction of the load path, not an installation or quantity drawing.

## Verification

```
dotnet build src/MyRevitAddin.csproj
dotnet run --project tests/Beam.Tests
dotnet run --project tests/Beam.UiSmoke
```

Beam.Tests checks load equilibrium/mirroring, numerical rejection, 3/5/10 mm limits, heaping, span limits and 648 geometry/support combinations. Beam.UiSmoke loads the production XAML and shared UI behavior using a non-Revit host: conversion-error retention, import blocking/recovery, optional input activation, numeric option selection, slab visibility, support counts and decimal-comma rendering. Passing a PNG path as its argument exports a preview of the actual WPF content.

The full add-in compiles against the Revit 2024 API. Parameter writes and rollback behavior still need testing in Revit with representative families: all writable parameters, a missing parameter, read-only parameter, non-length parameter and a rejected write. The WPF test host does not simulate or claim to validate actual Revit transactions.

### Latest verification

4,352 calculation assertions and 26 WPF assertions pass; the add-in builds with zero warnings/errors. All positive shared capacity rows and every configuration’s extension boundaries are checked. An independent three-moment beam solution integrates displacement from bending moments and checks equilibrium, maximum reaction, bending, shear and displacement for the 648 geometry/support inputs (4+ tested with 4 through 12 supports). The retained three/four-plus reaction factors are conservative estimates, not exact reactions. Verification covers the adopted full uniform-load/equal-span model; partial loading, flexible/settling supports, secondary-beam and sheeting design are outside this calculation. WPF tests additionally verify dropdown selection, automatic extension and unsupported-extension import blocking.

Shared free-prop catalog corrected to the published revisions in [prop-capacity-sources.md](prop-capacity-sources.md). The unsupported DokaFit fixed orientation is removed. The same millimetre-based actual-range and conservative row lookup is now used by both calculators. Beam regression passes after these corrections; this does not address the slab calculator’s explicitly deferred audit items.


## Records

Save/load uses versioned .dokacalc input files, with invariant numeric values, prop configuration and numeric deflection selection. Loads are validated on a separate model before the retained model is changed. Export report produces standalone printable HTML containing inputs, results, governing spacing criterion, prop configuration/capacity and the adopted assumptions. Invalid active inputs block saving/report export. The 300 mm adjacent slab strip is editable; its width changes both line load and eccentricity, and the schematic follows it.
