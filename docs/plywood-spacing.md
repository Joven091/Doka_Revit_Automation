# Plywood selection and sheet-based secondary spacing

Dokaflex beam/slab and D3 shoring tower beam modes share a **Plywood** selector. In the Dokaflex beam calculator it sits below adjacent slab strip width; in the slab calculator it is part of Calculation setup. It offers Dokaplex 9 mm, Doka plywood 18 mm (the existing default) and Doka plywood 21 mm, based on the supplied raster charts, including the replacement 18 mm diagram. The same selector is available in the drawing workspace's shared settings. The calculated maximum secondary spacing remains greyed out/read-only and updates when the selection changes.

## Chart profiles and limits

| Plywood | Chart reference | Span domain | Pressure domain |
| --- | --- | --- | --- |
| Dokaplex 9 mm | 9732-121 | 100–500 mm | 5–80 kN/m² |
| Doka plywood 18 mm | 18 mm, EJ=3.1, 15% moisture | 200–800 mm | 2.5–100 kN/m² |
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
| 18 mm | 2.10 | 6.10 | 16.0 (retained from 9732-103) |
| 21 mm | 1.40 | 7.80 | 19.0 |

The replacement 18 mm diagram states **EJ=3.1 kNm²/m at 15% moisture**. Its fitted load curves give 0.538 mm for p=10 at 40 cm, 2.722 mm for p=10 at 60 cm, and 2.150 mm for p=2.5 at 80 cm, matching approximate visible readings 0.54, 2.7 and 2.15 mm. The fitted M locus gives 0.512, 1.153 and 2.050 mm at 20, 30 and 40 cm, slightly below the visible bending envelope. **The replacement does not show Q:** the previous chart's shear envelope remains an additional restriction; it is not attributed to the new chart. Consequently, some pressures plotted in the replacement still have no passing spacing in its 200–800 mm domain. The existing saved profile ID is retained for compatibility, while reports identify the replacement chart reference. The 9 mm fit gives 1.563 mm for p=20 at 25 cm and 1.620 mm for p=10 at 30 cm, close to the approximately 1.5–1.6 mm curves; its M and Q fits are deliberately below the visible limiting loci. The 21 mm fit gives 0.567 mm for p=50 at 30 cm and 0.224 mm for p=100 at 20 cm in the detailed view; its M locus is 0.983 mm at 30 cm and 2.953 mm at 52 cm, consistent with the detailed and full views. These rounded readings have image uncertainty.

For criterion `L/N`, compute the candidate spans in metres:

```
L_deflection = (1000 / (N × C × p))^(1/3)
L_bending    = sqrt(M / p)
L_shear      = Q / p
```

For each chart covering the pressure, take the smallest structural candidate and that chart's span ceiling. Convert to mm, round down to **5 mm**, and reject candidates below that chart's minimum span. Select the largest supported candidate. This also handles any gaps in future chart domains without interpolating across them.

The **Plywood limit** selector beside each plywood selector independently selects L/500, L/350 (default) or L/270. The separate **H20 limit** governs the H20 checks; neither selector changes the other. This applies to Dokaflex beam/slab and D3 beam modes, including shared drawing settings. The selectors have no visible chart/spacing prose beneath them; details remain in tooltips, reports and diagnostics. The beam review displays only the calculated spacing.

Saved records include `PlywoodDeflectionLimit`. Existing records without that field inherit their saved H20 ratio; legacy fixed-mm H20 beam records migrate to L/500 for plywood. An explicit saved plywood limit takes precedence. Invalid saved limits are rejected before changing the retained model. New sessions and workspace undo/redo include the independent plywood setting. Pressures below the product's chart minimum use that minimum conservatively and identify this in the result. Unsupported pressure, missing selection and invalid inputs clear previous spacing/results.

## 2440 × 1220 mm sheet layout

After the chart calculation, choose the largest passing value from **488, 407, 350, 305, 275 and 250 mm**. These are the user's exact nominal maximums. The chart limit remains available in the result for comparison. No listed spacing below the chart limit means the calculation is invalid; it never rounds an unsafe limit up to 250 mm. This particularly affects 9 mm sheets under larger loads and the high-pressure 21 mm detailed chart.

The 2440 mm direction follows secondary-row setting out. D3 transfer/network layouts pin secondary rows at each internal multiple of 2440 mm from the concrete start. Each slab bay starts with its own nominal maximum. Adjacent bay rows align where necessary or use shorter intervals to avoid overlapping 80 mm H20 flanges. Each main beam retains every required slab-foot receiving row and gains beam-only rows until all its gaps satisfy that beam’s own plywood limit. Shorter intervals can therefore occur at sheet joints, receiving feet and stock ends. Nearby end rows yield to a sheet-joint row; short closing spans are shared between the last two intervals to keep adjacent 80 mm H20 flanges apart. Stock-joint clearances and all existing member, bearing, uplift and tower checks still apply. The optimizer can try closer values from the same list when H20, bearing, stock or tower checks require them; it never exceeds an element’s plywood limit. Locked dimension-row fields and the drawing inspector show each element’s calculated maximum; results and reports identify the actual limits used by the passing assembly. The slab schematic also follows the sheet-joint pattern, with visual density capped.

## Loads, field behavior and records

Pressure comes from full concrete depth and the existing default/custom area-load allowances. It is not inferred by averaging primary line loads. Adjacent slab thickness cannot exceed receiving beam depth. D3 transfer/network modes calculate a separate limit for every main beam, slab bay and modern crossing from its own concrete depth, selected plywood and plywood criterion. The governing beam limit remains available as a summary; it is not applied to slabs. Explicit Legacy slab rows use the greater slab/crossing depth for that bay because the same sheathing and upper H20s carry both loads.

D3 transfer/network optimization carries the individual maximums into physical row geometry; the view model has no writable row-spacing setter. Each upper slab H20 uses its own bay’s full nominal tributary, while each lower beam H20 uses that beam’s full nominal tributary plus the actual slab-foot and side-shutter influences. Upper rows absent from a bay have zero load and add no phantom slab feet. Main beams can share the compatible union of slab stations while using additional local beam-only rows. Crossings on existing supports match both receiving beams by physical station rather than assuming matching array indices. Both moment and shear (5 kNm / 11 kN for single H20s), selected H20 deflection and construction-stage reactions remain checked. Wider plywood spacing never overrides a failed H20 check. Other H20, tower-leg and layout checks still have to pass. A plywood spacing limit does not establish the transverse H20 member capacity or guarantee that a tower arrangement exists. Dokaflex and beam-only D3 still require separate secondary H20 member checks; transfer mode already checks its upper/lower members.

In both Dokaflex beam and slab calculators, formwork buildup is **400 mm for the two H20 layers plus selected plywood thickness**. Prop extension is floor height minus beam depth or slab thickness minus this buildup. This replaces the fixed 418 mm assumption; 18 mm retains exactly the existing buildup. Default/custom area-load allowances are unchanged because no product-specific formwork-weight data was supplied.

Saved calculations include a stable `PlywoodProfileId`. Loading restores the product before accepting the result. Older files without that key use 18 mm; unknown IDs are rejected before changing the retained model. Legacy manual row-spacing fields remain readable but cannot override the calculated maximum. Undo/redo tracks the selected profile, and a new session restores the 18 mm default. Reports and diagnostics identify the selected product and its chart references/domains.

Example at 14.90 kN/m² and L/350:

| Plywood | Maximum secondary spacing |
| --- | ---: |
| 9 mm | No listed spacing passes (chart limit 210 mm) |
| 18 mm | 407 mm (chart limit 450 mm) |
| 21 mm | 488 mm (chart limit 515 mm) |

Sheet-joint row positions are included in D3 transfer/network layouts. Sheet direction/grade, end bearing, overhangs, sheet-edge connections and crossing-beam joint details still require the actual construction arrangement.

## Verification

`dotnet run --project tests/Beam.Tests -- --plywood` checks replacement-chart anchors, independent criteria and all supplied products, pressure/span domains, strength/deflection and downward rounding, 21 mm full/detail selection, domain gaps, selector changes, buildup/prop extension, network loads, missing/unknown selection, legacy criteria, resets and stale-result recovery. The normal beam suite includes these checks. WPF smoke coverage checks both selectors, persisted product identity, old/unknown records, locked fields and workspace undo; Windows is required to execute that host.

The Dokaflex beam **Select helper lines** action uses rectangle selection before opening a transaction. It accepts only `PrimaryBeam` helper instances with `Structural Element Type = Beam` and matching H/B length parameters (0.1 mm tolerance). Slab helpers, different beam sizes and unrelated model elements are excluded. Esc cancels without changes; selected instances update atomically with the existing parameter checks.

`dotnet run --project tests/Slab.CoreTests` runs portable slab solver, plywood and H20 regressions. `tests/Slab.Tests` additionally checks the Windows selector, locked field and saved/legacy records.

`dotnet run --project tests/Beam.Tests -- --individual-spacing` sweeps compatible row grids for all approved pitches, checks different beam/slab and mixed-bay limits, plywood joints and flange clearances, transferred load conservation, physical receivers, member force/reaction balance, H20 moment/shear/deflection, tower legs, stale-result clearing and an overload that passes plywood but must fail H20 capacity. CI runs this on .NET 10 and .NET Framework 4.8. Windows UI tests cover locked per-element fields and drawing inspection.

Upper slab diagrams and row schedules distinguish nominal slab spacing, actual adjacent slab-row gaps and effective design tributary width. The upper response is scaled by its own slab’s governing width, never the lower beam-secondary spacing. The same listed nominal limits apply to legacy, custom-stock and separate-slab fallback calculations; a maximum below 250 mm is rejected instead of rounded upward.
