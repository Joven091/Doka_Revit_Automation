# Steel Shape

Open **Steel-weight-calculators.html** in a current Edge, Chrome or Firefox browser. The complete suite runs offline, without an installer or external scripts.

Results update automatically when you edit a value or selection. There is no Calculate button.

All 16 choices from the requested menu are included: sheets/plates, seamless circular pipes, circular/square/rectangular hollow sections, round/square/flat bars, equal/unequal angles, GOST/UPN channels and IPN/IPE/HEA/HEB beams. Each has a live cross-section and member-length schematic. Entered values are retained separately while moving between calculators.

For plates, all dimensions including length are in **millimetres**, and the positive whole-number piece count determines the total. The first result is kg/piece. For the other shapes, cross-section dimensions are in millimetres and member length is in **metres**; the first result is kg/m. Total mass always uses the unrounded result or the exact catalogue mass, not its two-decimal display.

For beams/channels, choose a profile and read its fixed nominal dimensions. For angles, choose a listed side/pair and an available thickness. Unlisted combinations are not interpolated or approximated. The drawings are schematic and not to scale; readable proportions and minimum line sizes do not alter the calculation.

## Optional weld joining two components

Enable **Include a weld joining two components** and enter **First piece length** and **Second piece length**, both in millimetres. Their sum is converted exactly into the main calculator's length unit: for example, 3000 mm + 3000 mm becomes 6 m in a member calculator. All weld inputs remain in millimetres.

Choose a straight or 90° L connection and either a fillet or butt weld. The optional weld diagram keeps its measurement guide when fields are empty or invalid. The original cross-section and member-length schematic stays in its original format; the L arrangement appears only in the optional weld diagram. The “Your section” heading, fillet-size helper note and all breakdown/formula/finished-length notes below the results have been removed.

**Weld length follows the interactive image.** The outline starts fully selected. Click/tap an edge to toggle its weld: orange is welded and grey is unwelded. **Clear** removes all welds. There is no All around button. Circular sections have four selectable quarter arcs; rounded rectangular sections expose their straight sides and corner arcs separately. Channels, angles and I/H sections expose their nominal individual edges. Hollow inside boundaries remain excluded.

The edge picker identifies the active edge and also makes narrow edges easy to access. With **Entire edge** checked, its full length is used. Uncheck it and enter **Welded length on this edge (mm)** to specify a partial weld. Partial highlights are centred on each edge for illustration; no fabrication start position is inferred. Enter or Space on a focused SVG edge toggles it. Typing needs no held mouse button or dragging. Real pointer clicks choose the nearest visible edge when narrow hit areas overlap.

The read-only total sums only the selected unrounded lengths. No selected edges means zero weld allowance and the steel-only total. Selections are retained per calculator. Full edges follow dimension/profile changes automatically; partial amounts remain in millimetres. If a changed section is shorter than an entered partial amount, the calculator reports it instead of silently truncating it.

Available full-edge lengths use these nominal outline rules:

- Circular sections and round bars: π × outside diameter.
- Square bars: 4 × side.
- Plates and flat bars: 2 × (width + thickness).
- Hollow rectangular/square sections: 2(A+B) − (8−2π)r, with r = min(1.5t, half the smaller side).
- Angles: 2(A+B), following the complete exposed outline, including the inside corner.
- Channels and I/H profiles: nominal outline 2h + 4b − 2s. Root radii and flange taper are not included in this length estimate.

For fillets, added mass is (leg size² / 2) × automatic outline length × 0.00000785 kg/mm³. For fully selected butt welds, gap mass uses the entire nominal steel section: kg/m × gap/1000, or the plate's section area × gap × density. Partial selections limit the gap mass to their selected nominal material regions. Rectangular/open-profile edges extend inward through their local nominal wall thickness (flange thickness for flange edges, web thickness for web edges); opposite/overlapping strips are combined as a geometric union and counted once. Hollow straight-wall/corner portions and circular arcs use their corresponding wall strips or annular/radial sectors. The nominal selected-region fraction is applied to the original steel mass, preserving catalogue section masses. Gap mass for repeating fractions is rounded to 24 decimal places. This is an allowance assuming full local nominal material penetration, not a prediction or approval of actual weld penetration. Reinforcement is modelled as one or two triangular caps of the entered width/height around the nominal outline. This avoids counting the original member section a second time and avoids assuming a uniform material thickness for a solid bar or a profile with different web/flange thicknesses. A flush butt weld with no gap adds zero net mass. Bevel fill replaces removed parent steel of the same assumed density; backing and splice plates are not included.

Steel and weld density is 7850 kg/m³. Plate piece counts multiply both steel and the joint allowance, with one joint per assembled piece. The first result remains steel kg/m or kg/piece; the total includes the weld allowance before rounding. The previous separate weight and formula notes below the results are not displayed.

The L option changes the displayed arrangement, while the weight estimate still uses the material lengths and selected portions of the nominal transverse section outline. It does not derive oblique/miter-cut perimeters, overlap losses, fabrication cuts or finished overall L dimensions. These are theoretical mass allowances under the stated nominal geometry and local penetration assumptions, not weld sizing, connection strength verification or a welding-procedure design. They are local functionality and are not claimed to reproduce a MAIAK-M weld calculator.

## Calculation and verification basis

The existing RHS engine is retained byte-for-byte. Its rounded-corner formula still matches all three website results supplied by the user at two decimal places. The square hollow section uses the same model with equal sides. Pipes and solid bars use nominal cross-section geometry and a density of 7850 kg/m³; π is represented as 3.141592653589793. Sheets use volume × density. See `formula-vectors.json` for independent Python Decimal examples.

Angles and standard profiles use **332 entries from the supplier's current published product tables**, not a rectangle-based mass approximation. The data include source URL, source row and the retrieval date. See `catalogues.json` and `SOURCE-AUDIT.md`.

The published tables and the older supplier PDF differ in several places. This version uses the current product-page data consistently and preserves those differences in the audit. These are supplier-catalogue entries, not independent certification that every listed dimension/mass matches the latest edition of its named standard.

The available web reader read the supplier input pages and product tables, but the live supplier calculators were not executed. The current Chromium checks cover this generated app only. **Live input/output equivalence for all 16 calculators has therefore not been established.** Published profile table checks, independent arithmetic checks and the three user-supplied RHS examples are separate forms of evidence. No comparison or development banners have been added to the customer-facing calculator.

The approved offline page is also embedded in the Revit dashboard. See `../../docs/steel-shape.md` for host integration and installer checks. The original RHS engine and the existing Revit/Top50 numerical logic are preserved.

## Developer checks

Run `node test-engine.cjs`. It checks independent decimal formula examples, the existing RHS vectors, user-supplied RHS results, independently transcribed supplier-table anchors, invalid inputs, units and catalogue integrity.

Run `node test-weld.cjs` for the added-weld calculations, all 16 section types, exact mm/m conversion, straight/L connections, piece quantities, zero net butt mass and validation. Its 32 automatic-outline vectors and 17 retained manual-arithmetic vectors are independently generated in metre-based Python Decimal arithmetic by `python3 generate-weld-vectors.py`. The UI checks also exercise both weld types, automatic updates without a button, plain labels, all four empty dimension guides, automatic length updates, highlighted coverage, main schematic preservation, removed notes, per-tool state and invalid-input recovery.


Run `python3 generate-selection-vectors.py` and `node test-selection.cjs` for 19 independent selected-region examples, partial edge/arc amounts, overlap handling, shape limits and all/none cases. `node test-ui.cjs Steel-weight-calculators.html` covers DOM-level clicks, keyboard toggles, Clear, partial editing, per-tool persistence and the unchanged main drawings.

`node test-browser.cjs Steel-weight-calculators.html` uses Playwright and a Chromium executable (default `/usr/bin/chromium`, or set `STEEL_TEST_CHROMIUM`). Install Playwright in the source directory for this development-only check. It tests actual pointer hits on lines, arcs and narrow IPE edges, keyboard focus, partial typing and 390px mobile layout. In this environment browser policy blocks file URLs, so the same HTML bytes were rendered in memory with networking offline; direct file opening remains untested by that browser run. Desktop/mobile captures are included in the source kit. None of these test dependencies are needed to use the final HTML.

For local DOM checks, install `linkedom@0.18.12` into `.test-deps` with scripts disabled, then run `node test-ui.cjs Steel-weight-calculators.html`. For example, `npm install --prefix .test-deps --ignore-scripts --no-audit --no-fund linkedom@0.18.12`. The test also supports the task-local copy already present in this workspace. These are DOM interaction/SVG-structure checks, not a real rendering-browser or live-website test.

`python3 import_catalogues.py` reproduces the table dataset from the factual source extracts in `research/`. `python3 build.py` produces the self-contained HTML alongside the source; an optional output path can be supplied. `python3 package.py` packages the source and validation report after checks pass. Optionally install `@resvg/resvg-js@2.6.2` into `.test-deps` and run `node render-gallery.cjs` to render the same 16 schematic previews. No development dependencies are needed to use the final HTML file.
