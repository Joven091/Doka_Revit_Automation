# Source and validation audit — 2026-10-27

The requested calculator menu and input fields were read at [MAIAK-M calculators](https://www.maiak-m.bg/en/calculator/i5/Hollow-structural-sections-rectangular.html). All 16 tool types are implemented. The pages themselves were readable, but their input fields could not be operated through the available tools. Live numerical parity is not claimed.

## Published catalogue facts

| Family | Entries | Supplier product source |
|---|---:|---|
| Equal angles | 115 | https://www.maiak-m.bg/en/product/i157/product.html |
| Unequal angles | 99 | https://www.maiak-m.bg/en/product/i158/product.html |
| UPN | 20 | https://www.maiak-m.bg/en/product/i148/product.html |
| GOST channels | 12 | https://www.maiak-m.bg/en/product/i162/product.html |
| IPE | 18 | https://www.maiak-m.bg/en/product/i149/product.html |
| IPN | 20 | https://www.maiak-m.bg/en/product/i150/product.html |
| HEA | 24 | https://www.maiak-m.bg/en/product/i151/product.html |
| HEB | 24 | https://www.maiak-m.bg/en/product/i152/product.html |

Each numeric row is preserved in the source data. Intermediate empty angle-table cells remain unavailable sizes. The angle table's thickness header is excluded from the profile records. Standard-profile masses are read from the weight column; rounded section-area columns are not converted back into mass. HEA actual heights remain distinct from nominal names, for example HEA 100 has h = 96 mm.

## Conflicting sources

The supplier's older 24-page PDF was also examined. The current product pages differ from that PDF in several values and labels. Examples:

- GOST No. 27: current product page 27.70 kg/m; the older PDF prints 35.20 in its weight column. The current product page's 27.70 is also consistent with the GOST 8240 table's mass, while 35.20 is the corresponding section area in cm².
- GOST No. 18: current product page 16.6 kg/m; older PDF 16.16 kg/m. This suite retains the current page's value, without claiming that either number has been independently validated against the live calculator or the standard.
- HEB 450: current product page correctly names HEB 450 at h=450, b=300, s=14, t=26 mm and 171 kg/m; the older PDF labels that row HEB 460. The current label/dimensions/mass are also consistent with [ArcelorMittal's HE section data](https://orangebook.arcelormittal.com/node/16).
- Several equal/unequal angle values differ between the current product tables and the older PDF. Current product-table values are retained consistently, rather than mixing sources to manufacture agreement.

## Geometric models and observed website results

The rectangular hollow-section engine is the previously tested implementation, SHA-256 `c2ad832473cf4191c8287fadf49e39e39f7f1a95d385af1033b47d2dfb9f9f6a`. The three user-supplied examples remain:

| A × B × t (mm) | L (m) | kg/m | Total kg |
|---|---:|---:|---:|
| 100 × 50 × 3 | 6 | 6.66 | 39.97 |
| 200 × 100 × 8 | 6.35 | 34.81 | 221.03 |
| 60 × 40 × 2 | 1.5 | 2.96 | 4.44 |

The RHS formula is inferred from those observations; square hollow sections reuse it with equal sides. Circular pipes use π/4 × (D² − (D−2t)²). Round bars use πD²/4, square bars A², flat bars Wt, and plates use tWL times the piece count, with the correct millimetre-to-metre conversion. Steel density is 7850 kg/m³. These formulas are independently checked with decimal arithmetic, but that is not a live website comparison or validation of the website's boundary/rounding rules.

Known scope limits: no supplier live-form execution, no exhaustive input-space parity claim, and no stock-size availability certification. The UI does not present the weights as structural strength, capacity or engineering-approval results.

## Local optional weld feature

This local feature is independent of the supplier pages. The current interface assumes one continuous weld around the nominal outside section outline, automatically calculated from the selected section and highlighted in orange. Hollow inside boundaries are excluded. Profile root radii and taper are not included in the nominal outline estimate; the L layout does not derive an oblique/mitered cut perimeter. See README for the exact formulas and scope.

Fillet mass uses a triangular equal-leg bead times that unrounded path length. Butt net added mass uses the full nominal section's gap volume plus triangular reinforcement caps around the nominal outline. The automatic mode no longer asks for a uniform joined thickness, which would misrepresent solid sections or varying web/flange thicknesses. Bevel fill replaces removed steel of the same density; a flush butt weld with no gap adds zero net mass. Steel and weld density is 7850 kg/m³. Backing, splice plates, weld strength and fabrication details are outside this model.

Independent checks include 32 automatic outline/mass vectors covering both weld types on all 16 shapes. The 17 earlier manual volume vectors remain explicit internal arithmetic regression fixtures; no manual length or seam-count controls exist in the current interface. The nominal steel-weight formulas and published profile data are preserved.

The latest requested cleanup restores the original main section/member schematics when welds are enabled, removes the “Your section” heading and fillet-size helper, and removes the breakdown and formula notes below the results. The automatic weld path and separate optional weld layout remain visible before measurements are complete.

## Interactive weld selection

The requested interactive image starts with all nominal edges selected, supports Clear and individual edge toggles, and has no All around button. The edge picker/Entire edge checkbox/partial-length input avoid drag-to-type interactions and provide access to narrow profile features. Straight edges, rounded corners and circular quarter arcs are separate selectable paths. Partial highlights are centred illustrations of entered length. Selection and typing do not change the original main section/member schematics.

Partial butt gap fill uses a nominal local-material model, rather than continuing to add the whole joint gap. Selected straight-edge strips extend inward through the local nominal wall/flange/web thickness and are clipped to the section; their union prevents opposite edges from counting shared gap material twice. Rounded hollow walls/corners and circular sections use corresponding strip/sector areas. Normalized nominal regions preserve the original published catalogue masses. Actual penetration, preparation and fabrication details remain outside this theoretical allowance. Reference examples cover plate/bar overlaps, hollow corner sectors, and differing IPE web/flange thicknesses.

Additional verification comprises 19 independent selection/volume examples, all/none checks for all 16 shapes, DOM interaction checks and real Chromium pointer/keyboard/typing checks. The Chromium run loaded the built HTML in memory with networking offline because local file URLs are blocked by browser policy; it did not verify direct file opening or operate the live supplier site. The browser report records the tested HTML SHA-256, and packaging requires that hash to match.
