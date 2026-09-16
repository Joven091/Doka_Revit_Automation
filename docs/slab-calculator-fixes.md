# Slab calculator corrections

## Implemented audit items

1. Shared free-prop capacities reconciled to the source revisions in prop-capacity-sources.md. The beam calculator also uses these corrected values and the same robust extension lookup.
2. Both support selectors now offer 2, 3, 4 and 5+. Counts 2–4 use exact continuous-beam responses; 5+ takes the governing response separately from five and six supports. See slab-five-plus.md for the equal-span, full-uniform-load assumptions and envelope derivation. The diagrams identify their five-support illustration, and the selected H20 member displays its maximum fitting prop count.
3. Negative support reactions invalidate the layout. Fixed primary-beam geometry is checked before the spacing search. Each secondary-beam spacing candidate must retain support contact as well as pass strength, deflection and prop-capacity checks. The largest passing 25 mm spacing is returned. A no-solution contact conflict identifies a support and a candidate that passed the other checks, with guidance to change the overhang/support arrangement. Primary uplift identifies the affected prop; reducing primary beam spacing cannot cure that fixed-geometry condition. Invalid layouts hide results, clear diagrams and disable import. Signed reactions are retained with only a 1e-10 relative tolerance for floating-point cancellation at zero contact. The 5+ check uses the minimum reaction across five and six supports, with the case count identified in the warning.
4. Primary-beam span deflection now constrains spacing. Constant-EI equal-span analysis uses end moments from the two equal cantilevers and solves the slope roots to find displacement extrema in every primary span. The selected L/500, L/350 or L/270 criterion is applied using the existing total-load basis.
5. Both H20 layers now check total cantilever-tip displacement, including signed support rotation. The old fixed-root secondary deflection precheck is removed; total tip displacement is evaluated at each candidate spacing. Five-plus uses the larger absolute tip displacement from five and six supports. The adopted limit is overhang length divided by the selected 270/350/500 divisor, extending the existing secondary criterion to primary tips. This criterion is not represented as a verified Doka requirement. A tooltip explains which length applies. Zero overhang gives zero tip displacement; upward and downward movements are checked by magnitude after signed combination. The model assumes non-settling supports.
6. Positive finite active dimensions (prop spacing at least 1 mm), nonnegative finite loads/cantilevers and supported selections are required. WPF conversion errors invalidate results and import. Inactive custom-load fields are ignored; reactivating an invalid field blocks again. Conversion text is retained so it can be corrected.
7. Heaping is 10% of concrete weight, minimum 0.75 and maximum 1.75 kN/m². Slab thickness remains a double; intermediate load rounding is removed. See Dokaflex user information 999776002, August 2024, page 12: https://direct.doka.com/_ext/downloads/downloadcenter/999776002_2024_08_online.pdf.
8. Extension uses millimetres: floor height minus slab thickness minus 418. Actual range is checked before upward table lookup, with only arithmetic-noise tolerance at boundaries. Actual and lookup extensions are visible.
9. Import independently checks calculation validity, validates all four length parameters for type and writability, writes an instance in a subtransaction, checks Set/Commit outcomes and reports updated/skipped/failed counts. A failed parent commit cannot report success. Import now prompts for rectangle selection of PrimaryBeam slab helper lines matching the calculated slab thickness. Only selected helpers are updated; Esc or an empty selection writes nothing. The window hides for selection and is restored afterward.
10. H20 member length, its advisory spacing and the member-fit restriction have been removed at the user’s request. Analysis remains continuous across illustrated overlaps. Stock member lengths are not an input or calculation constraint.
11. Every invalidation clears both diagrams and results, and disables diagram commands and import. Valid inputs restore them.

Spacing candidates are now evaluated directly in integer 25 mm increments. This ensures the primary deflection check applies to the exact output layout and avoids the prior 10 mm search followed by another rounding step.

## Intentionally deferred by the user

- Item 12: sheeting and the remaining complete load-path checks are not implemented.

IsCalculationValid means the currently implemented checks pass. These deferred items remain limitations, so this PR is not a complete verification of slab formwork.

## Tests

- tests/Slab.Tests links the production calculator, helpers, XAML and shared input-validation code. The host stubs the Revit-specific window actions only.
- Source-cell comparisons, table ranges, exact/noisy boundaries, finite/range validation, inactive field transitions, member-fit changes, heaping and precision, diagram invalidation/recovery and import guard tests.
- Primary deflection calibrated against 5wL^4/(384EI) for a simply supported member; 432 geometry/count/deflection combinations checked, including 261 valid layouts after uplift and tip checks. Production Macaulay diagram displacement independently checks span displacement against the solver.
- Cantilever tests calibrate the two-support signed closed form, including the 3 m H20 example (1 m overhangs, 1 m support spacing: 0.7409456 mm per kN/m). Both tips agree with independently integrated Macaulay diagrams across counts 2–6. Tests cover upward motion, partial cancellation, zero overhang, five-plus envelopes through 100 supports, both layers' accepted limits, rejection/recovery and WPF import blocking.
- Uplift tests cover the independent three-support closed form, both sides of the zero-reaction boundary, load scaling, five-plus minimum reactions through 100 supports, fixed primary geometry, secondary contact conflicts, recovery and WPF import/result blocking.
- The 5+ tests check responses through 100 supports and the analytical envelope inequalities through 256 supports, including an overloaded-layout regression. Slab/catalog/WPF checks pass; prior beam regression: 4,352 checks across 648 scenarios plus 26 WPF checks. Full Revit add-in build succeeds without errors/warnings.

Actual model writes, per-instance rollback and parent commit failure still require testing in Revit. No live Revit transactions were executed by the test host.


Rectangle helper selection compiles against the Revit 2024 API. Live window selection and transaction behavior require an in-Revit check. Older saved slab files containing SelectedH20 still load; that obsolete field is ignored and omitted from new saves/reports.
