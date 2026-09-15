# Manual backpropping calculator

Open **Backpropping** from the calculator launcher. The initial stack is empty. Enter the active pour, then supporting slabs from top to bottom. Suggested names remain editable; dimensions, SIDL, final live load and age carry forward. Ages must not decrease down the stack. Only the lowest slab can be removed.

The section shows the final downward load per slab. The first storey shows secondary H20 ends perpendicular to the primary H20 beam and full-height main props. **Main formwork/shores are not sized.** Backpropping sizing starts below the first supporting slab.

## Calculation basis

This is the project's adopted capacity-based approximation, **not an official Doka load-distribution formula or a structural verification**. Inputs describe a repeated, vertically aligned, uniformly loaded slab zone. SIDL means superimposed dead load, excluding concrete self-weight. Final live load is part of the design-capacity basis, not an additional construction load. Area loads are kN/m²; prop capacities and forces are kN; input dimensions are mm.

For slab `i`:

```
Gs[i] = thickness[i] / 1000 × 25
designBasis[i] = Gs[i] + SIDL[i] + finalLive[i]
capacity[i] = strength[i] / 100 × designBasis[i]
```

Active pour `N`:

```
heaping = min(1.75, max(0.75, 0.10 × Gs[N]))
pour = Gs[N] + 0.50 main formwork + 0.75 working load + heaping
incoming[N−1] = pour
```

For supporting slabs, in order:

```
constructionLive = 0.75 at N−1; 0 below
demand[i] = Gs[i] + constructionLive + incoming[i]
transfer[i] = max(0, demand[i] − capacity[i])
if transfer[i] is zero: finish the required stack
otherwise: downward[i] = transfer[i] + 0.40
           incoming[i+1] = downward[i]
```

The fixed 0.40 kN/m² allowance is included once per required reprop storey, independent of selected prop or grid. It is also included conservatively in the prop spacing load. No further allowance or optional slab is added after the transfer stops. Floating-point residuals up to 1e−9 kN/m² are treated as zero; displayed rounding never determines the stopping point. Existing lower slabs remain editable/removable when a change makes them unnecessary; they are marked outside the required support stack.

### Concrete strength

Piecewise linear interpolation between the user-approved readings of the medium-development chart:

| Curve | Curing temperature | Day / strength % anchors |
| --- | --- | --- |
| A (default) | ≥15°C | 0/0, 2/50, 4/70, 12/80, 20/90, 28/100 |
| B | ≥10°C | 0/0, 4/50, 8/70, 28/100 |
| C | ≥5°C | 0/0, 6/50, 12/70, 28/100 |

Changing the global curve recalculates all slabs. At and above 28 days, strength is capped at 100%; ages greater than 28 display as **28+ days** in the section, with the original age retained for editing/order validation. These chart approximations assume suitable curing and medium concrete strength development; they do not replace measured strength or account for varying temperature history.

### Square-grid spacing

Select a prop for each required storey. Enter **floor-to-floor height below** for the upper slab: the vertical distance between its top surface and the top surface of the slab below. Prop extension is calculated automatically as that height minus the **upper slab's thickness**. For example, 3200 − 200 = 3000 mm. It is shown as read-only text, with no extension override. This geometry assumes direct slab contact without packers. Floor height must exceed slab thickness. The lookup rounds the calculated extension **up** to the next 100 mm table row, never extrapolates outside the transcribed range, and does not interpolate prop capacities.

```
capacitySpacing_mm = 1000 × sqrt(propCapacity_kN / downward_kN_m2)
limit = storey maximum if supplied, otherwise project maximum
spacing_mm = floor(min(capacitySpacing_mm, limit) / 25) × 25
propForce_kN = downward_kN_m2 × (spacing_mm / 1000)²
```

If neither maximum is supplied, use the capacity spacing; the calculation assumptions explain that slab support limits are not checked. A storey maximum overrides the project maximum. Each result identifies its storey and shows only the prominent **spacing**, **load per prop**, and **utilization**. Changing inputs clears old results. Incomplete stacks and invalid storey inputs cannot produce a final spacing for that storey.

No area input is needed for this uniform square-grid calculation. It does not calculate total prop quantities, edge tributary areas, openings, offset grids, concentrated loads, slab punching/local bearing, settlement, stiffness-based load sharing, lateral stability or the stripping/repropping sequence. The strength-times-design-load approximation itself requires project engineering acceptance; reaching zero residual is a stopping rule for this model, not proof that the actual structure needs no support.

## Prop table transcription

The dedicated catalog uses the supplied **restrained temporary reshoring** tables, separate from the existing free-standing formwork catalog. Confirm the manufacturer's head/base bearing and restraint conditions for the actual installation. The supplied images omit some footnotes, so this is not an unconditional capacity certification.

| Prop (Eurex and corresponding top unless separated) | Supported extension, mm | Capacity, kN |
| --- | --- | --- |
| DokaFit 300 | 1900–3000 | 35 |
| DokaFit 350 | 2200–3500 | 35 |
| DokaFit 400 | 2600–4000 | 35 to 3700; 34 at 3800; 31.5 at 3900; 30 at 4000 |
| Eurex 20 / 20 top 150 | 1000–1500 | 25 |
| Eurex 20 / 20 top 250 | 1500–2500 | 36.7 to 2200; 34/32/30 at 2300/2400/2500 |
| Eurex 20 / 20 top 300 | 1800–3000 | 36.7 to 2700; 34/32/30 at 2800/2900/3000 |
| Eurex 20 / 20 top 350 | 2000–3500 | 36.7 to 3200; 34/32/30 at 3300/3400/3500 |
| Eurex 20 400 | 2300–4000 | 40 to 3500; 38/36/34/32/30 at 3600/3700/3800/3900/4000 |
| Eurex 20 top 400 | 2300–4000 | 36.7 to 3700; 34/32/30 at 3800/3900/4000 |
| Eurex 20 550 | 3000–5500 | 40 to 5000; 38/36/34/32/30 at 5100/5200/5300/5400/5500 |
| Eurex 20 top 550 | 3000–5500 | 36.7 to 5200; 34/32/30 at 5300/5400/5500 |
| Eurex 20 top 700 | 3900–6600 | 36.7 |
| Eurex 30 / 30 top 250 | 1500–2500 | 41.2 |
| Eurex 30 / 30 top 300 | 1800–3000 | 41.2 |
| Eurex 30 / 30 top 350 | 2000–3500 | 41.2 |
| Eurex 30 / 30 top 400 | 2300–4000 | 41.2 |
| Eurex 30 / 30 top 450 | 2500–4500 | 41.2 |
| Eurex 30 / 30 top 550 | 3100–5500 | 41.2 |

DokaFit 300 below 1900 mm is intentionally unavailable because the supplied image is cropped. The single supplied 700 column is exposed as Eurex 20 top 700 only.

References: user-supplied `prop capacities.png` and DokaFit `image.png`; [Doka floor props Eurex top instructions](https://direct.doka.com/_ext/downloads/downloadcenter/999801702_2025_08_online.pdf) for reshoring conditions; [Dokaflex instructions, load assumptions](https://direct.doka.com/_ext/downloads/downloadcenter/999803902_2022_10_online.pdf) for working/heaping allowances; [Doka supporting slabs and concrete-strength guidance](https://direct.doka.com/_ext/downloads/downloadcenter/999807002_2022_09_online.pdf). These references do not establish the project's adopted capacity approximation as a Doka load-distribution method.

## Validation

With 200 mm slabs, SIDL 3, final live 3, curve A and ages 0/10/10/28:

| Level | Demand | Adopted capacity | Transfer | Downward |
| --- | ---: | ---: | ---: | ---: |
| N | 7.000 | — | — | 7.000 |
| N−1 | 12.750 | 8.525 | 4.225 | 4.625 |
| N−2 | 9.625 | 8.525 | 1.100 | 1.500 |
| N−3 | 6.500 | 11.000 | 0 | 0 |

A DokaFit 300 at 3000 mm under 4.625 kN/m² gives **2750 × 2750 mm** before a project limit. A 2010 mm maximum reduces this to **2000 × 2000 mm**.

Run with .NET 10 SDK (WPF and add-in builds require Windows):

```powershell
dotnet build src/MyRevitAddin.csproj
dotnet run --project tests/Backpropping.Tests
dotnet run --project tests/Backpropping.UiSmoke
```

The standalone engine tests cover interpolation, transfer, stopping, invalid inputs, table boundaries and grid rounding. The WPF smoke executable loads the actual calculator XAML/code and checks empty startup, entry/inheritance, age validation, temperature changes, retained invalid inputs, result presentation and deletion without requiring Revit. Revit-hosted launcher interaction remains a manual integration check.
