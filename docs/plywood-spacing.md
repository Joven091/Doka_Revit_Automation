# Plywood selection and sheet-based secondary spacing

Dokaflex beam/slab and D3 shoring tower beam modes share a **Plywood** selector. In the Dokaflex beam calculator it sits below adjacent slab strip width; in the slab calculator it is part of Calculation setup. It offers Dokaplex 9 mm, Doka plywood 18 mm (the existing default) and Doka plywood 21 mm, based on the four supplied raster charts. The same selector is available in the drawing workspace's shared settings. The calculated maximum secondary spacing remains greyed out/read-only and updates when the selection changes.

## Chart profiles and limits

| Plywood | Chart reference | Span domain | Pressure domain |
| --- | --- | --- | --- |
| Dokaplex 9 mm | 9732-121 | 100–500 mm | 5–80 kN/m² |
| Doka plywood 18 mm | 9732-103 | 200–600 mm | 2.5–60 kN/m² |
| Doka plywood 21 mm, full view | 9732-104 | 200–600 mm | 2.5–80 kN/m² |
| Doka plywood 21 mm, detailed view | 9732-120 | 100–300 mm | 2.5–100 kN/m² |

The two 21 mm charts form one selectable product. The calculation evaluates each chart's own domain and returns the largest permitted rounded spacing covered by a supplied chart. Above 80 kN/m² it uses only the detailed chart. At lower pressures, the overlapping 200–300 mm region lets the two charts cover 100–600 mm without a gap. It never extrapolates a detailed high-pressure curve into unsupported longer spans.

The illustrated continuous, equal-span, uniformly loaded support arrangement must apply to the actual plywood. The images do not establish product grade, sheet direction or condition. The 9 mm source explicitly describes curved surfaces/inliners and use of the graph for low-loading form facing; its selector note retains that context.

## Approximate reconstruction

Each product has its own rounded conservative fit to the visible load curves, bending M and shear Q envelopes. These are image reconstructions, not independently certified material properties. With pressure `p` in kN/m², span `L` in metres and displacement in millimetres:

```
displacement = C × p × L^4
bending envelope: p × L^2 <= M
shear envelope:   p × L   <= Q
```

| Plywood | C | M | Q |
| --- | ---: | ---: | ---: |
| 9 mm | 20.0 | 1.40 | 8.0 |
| 18 mm | 2.30 | 5.60 | 16.0 |
| 21 mm | 1.40 | 7.80 | 19.0 |

For comparison with the supplied images, the 18 mm M locus gives 0.515, 1.159 and 2.061 mm at 20, 30 and 40 cm, compared with approximate image readings 0.52, 1.18 and 2.10 mm. Its Q locus gives 0.294, 0.994 and 2.355 mm compared with roughly 0.30, 1.02 and 2.45 mm. The 9 mm fit gives 1.563 mm for p=20 at 25 cm and 1.620 mm for p=10 at 30 cm, close to the approximately 1.5–1.6 mm curves; its M and Q fits are deliberately below the visible limiting loci. The 21 mm fit gives 0.567 mm for p=50 at 30 cm and 0.224 mm for p=100 at 20 cm in the detailed view; its M locus is 0.983 mm at 30 cm and 2.953 mm at 52 cm, consistent with the detailed and full views. These rounded readings have image uncertainty.

For criterion `L/N`, compute the candidate spans in metres:

```
L_deflection = (1000 / (N × C × p))^(1/3)
L_bending    = sqrt(M / p)
L_shear      = Q / p
```

For each chart covering the pressure, take the smallest structural candidate and that chart's span ceiling. Convert to mm, round down to **5 mm**, and reject candidates below that chart's minimum span. Select the largest supported candidate. This also handles any gaps in future chart domains without interpolating across them.

The denominator follows L/500, L/350 (default) or L/270. Legacy fixed-mm H20 records retain their H20 criterion, while plywood uses an explicitly stated L/500 fallback. Pressures below the product's chart minimum use that minimum conservatively and identify this in the result. Unsupported pressure, missing selection and invalid inputs clear previous spacing/results.

## 2440 × 1220 mm sheet layout

After the chart calculation, choose the largest passing value from **488, 407, 350, 305, 275 and 250 mm**. These are the user's exact nominal maximums. The chart limit remains available in the result for comparison. No listed spacing below the chart limit means the calculation is invalid; it never rounds an unsafe limit up to 250 mm. This particularly affects 9 mm sheets under larger loads and the high-pressure 21 mm detailed chart.

The 2440 mm direction follows secondary-row setting out. D3 transfer/network layouts pin secondary rows at each internal multiple of 2440 mm from the concrete start. Other rows use the selected nominal maximum, with shorter final spans at sheet joints and partial ends. Nearby end rows yield to a sheet-joint row; short closing spans are shared between the last two intervals to keep adjacent 80 mm H20 flanges apart. Stock-joint clearances and all existing member, bearing, uplift and tower checks still apply. The optimizer can try closer values from the same list when needed; the locked input retains the calculated maximum and the result reports the actual value used. The slab schematic also follows the sheet-joint pattern, with visual density capped.

## Loads, field behavior and records

Pressure comes from full concrete depth and the existing default/custom area-load allowances. It is not inferred by averaging primary line loads. Adjacent slab thickness cannot exceed receiving beam depth. A network uses the greatest pressure across beams, slab bays and crossing beams for its common secondary spacing.

D3 transfer/network optimization uses the calculated maximum directly; the view model has no writable row-spacing setter. Other H20, tower-leg and layout checks still have to pass. A plywood spacing limit does not establish the transverse H20 member capacity or guarantee that a tower arrangement exists. Dokaflex and beam-only D3 still require separate secondary H20 member checks; transfer mode already checks its upper/lower members.

In both Dokaflex beam and slab calculators, formwork buildup is **400 mm for the two H20 layers plus selected plywood thickness**. Prop extension is floor height minus beam depth or slab thickness minus this buildup. This replaces the fixed 418 mm assumption; 18 mm retains exactly the existing buildup. Default/custom area-load allowances are unchanged because no product-specific formwork-weight data was supplied.

Saved calculations include a stable `PlywoodProfileId`. Loading restores the product before accepting the result. Older files without that key use 18 mm; unknown IDs are rejected before changing the retained model. Legacy manual row-spacing fields remain readable but cannot override the calculated maximum. Undo/redo tracks the selected profile, and a new session restores the 18 mm default. Reports and diagnostics identify the selected product and its chart references/domains.

Example at 14.90 kN/m² and L/350:

| Plywood | Maximum secondary spacing |
| --- | ---: |
| 9 mm | No listed spacing passes (chart limit 210 mm) |
| 18 mm | 407 mm (chart limit 435 mm) |
| 21 mm | 488 mm (chart limit 515 mm) |

Sheet-joint row positions are included in D3 transfer/network layouts. Sheet direction/grade, end bearing, overhangs, sheet-edge connections and crossing-beam joint details still require the actual construction arrangement.

## Verification

`dotnet run --project tests/Beam.Tests -- --plywood` checks all supplied products, pressure/span domains, strength/deflection and downward rounding, 21 mm full/detail selection, domain gaps, selector changes, buildup/prop extension, network loads, missing/unknown selection, legacy criteria, resets and stale-result recovery. The normal beam suite includes these checks. WPF smoke coverage checks both selectors, persisted product identity, old/unknown records, locked fields and workspace undo; Windows is required to execute that host.

The Dokaflex beam **Select helper lines** action uses rectangle selection before opening a transaction. It accepts only `PrimaryBeam` helper instances with `Structural Element Type = Beam` and matching H/B length parameters (0.1 mm tolerance). Slab helpers, different beam sizes and unrelated model elements are excluded. Esc cancels without changes; selected instances update atomically with the existing parameter checks.

`dotnet run --project tests/Slab.CoreTests` runs portable slab solver, plywood and H20 regressions. `tests/Slab.Tests` additionally checks the Windows selector, locked field and saved/legacy records.
