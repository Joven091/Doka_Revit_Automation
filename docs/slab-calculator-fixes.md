# Slab calculator corrections

## Implemented audit items

1. Shared free-prop capacities reconciled to the source revisions in prop-capacity-sources.md. The beam calculator also uses these corrected values and the same robust extension lookup.
4. Primary-beam span deflection now constrains spacing. Constant-EI equal-span analysis uses end moments from the two equal cantilevers and solves the slope roots to find displacement extrema in every primary span. The selected L/500, L/350 or L/270 criterion is applied using the existing total-load basis. No change to the secondary cantilever-tip method is included.
6. Positive finite active dimensions (prop spacing at least 1 mm), nonnegative finite loads/cantilevers and supported selections are required. WPF conversion errors invalidate results and import. Inactive custom-load fields are ignored; reactivating an invalid field blocks again. Conversion text is retained so it can be corrected.
7. Heaping is 10% of concrete weight, minimum 0.75 and maximum 1.75 kN/m². Slab thickness remains a double; intermediate load rounding is removed. See Dokaflex user information 999776002, August 2024, page 12: https://direct.doka.com/_ext/downloads/downloadcenter/999776002_2024_08_online.pdf.
8. Extension uses millimetres: floor height minus slab thickness minus 418. Actual range is checked before upward table lookup, with only arithmetic-noise tolerance at boundaries. Actual and lookup extensions are visible.
9. Import independently checks calculation validity, validates all four length parameters for type and writability, writes an instance in a subtransaction, checks Set/Commit outcomes and reports updated/skipped/failed counts. A failed parent commit cannot report success. Active-view matching by PrimaryBeam/Slab and thickness remains the targeting rule.
10. The static H20 advisory mapping is labelled in mm. Selected H20 length must accommodate (prop count − 1) × prop spacing + two primary cantilevers for the modeled continuous member. The default is 3.30 m to fit the default 3.00 m layout. A lapped/multiple-member layout needs a separate model. The mapping is advisory, not a capacity calculation.
11. Every invalidation clears both diagrams and results, and disables diagram commands and import. Valid inputs restore them.

Spacing candidates are now evaluated directly in integer 25 mm increments. This ensures the primary deflection check applies to the exact output layout and avoids the prior 10 mm search followed by another rounding step.

## Intentionally deferred by the user

- Item 2: “4+” still uses a four-support model. It is not a general envelope for more supports; the new member-fit and primary-deflection checks use the same four-support interpretation.
- Item 3: uplift / loss of contact is not yet rejected by the structural model.
- Item 5: secondary cantilever-tip deflection still omits support rotation; primary cantilever-tip serviceability is not added in this change.
- Item 12: sheeting and the remaining complete load-path checks are not implemented.

IsCalculationValid means the currently implemented checks pass. These deferred items remain limitations, so this PR is not a complete verification of slab formwork.

## Tests

- tests/Slab.Tests links the production calculator, helpers, XAML and shared input-validation code. The host stubs the Revit-specific window actions only.
- Source-cell comparisons, table ranges, exact/noisy boundaries, finite/range validation, inactive field transitions, member-fit changes, heaping and precision, diagram invalidation/recovery and import guard tests.
- Primary deflection calibrated against 5wL^4/(384EI) for a simply supported member; 324 geometry/count/deflection combinations checked, including 249 valid layouts and rejected member/no-solution cases. Production Macaulay diagram displacement independently checks span displacement against the new solver.
- 5,136 slab/catalog/WPF checks pass; beam regression: 4,352 checks across 648 scenarios plus 26 WPF checks. Full Revit add-in build succeeds without errors/warnings.

Actual model writes, per-instance rollback and parent commit failure still require testing in Revit. No live Revit transactions were executed by the test host.
