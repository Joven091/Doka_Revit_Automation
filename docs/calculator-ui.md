# Calculator interface

The slab and beam calculators share CalculatorStyles.xaml, using the backpropping calculator's yellow numbered sections, neutral grey background, labelled fields, grey diagram panels and prominent yellow spacing results.

## Flow

1. Calculation setup: geometry, optional slab wings for beams, and expandable load inputs. The load heading indicates default or custom mode even when collapsed.
2. Support layout: H20/prop geometry and existing prop selections. Derived extension remains a readout.
3. Review spacing: spacing is the largest value, followed by load per prop and utilization. Calculation details contain the existing secondary results. Both calculators have live support schematics in the review area; the slab view retains its separate force/deflection diagram commands.

The slab schematic shows a cutaway of the concrete and soffit sheet, perpendicular secondary/primary H20 layers, and full-length props with head/base plates. Primary-line and prop counts follow the selectors; 5+ is illustrated with five supports and labelled accordingly. Each secondary H20 is shown over no more than three primary supports. For four supports, the pieces cover 1–3 and 3–4; for five supports, 1–3 and 3–5. Continuations share a primary support, with both ends extending past it in a side-by-side overlap at the same elevation. The displayed lap is illustrative; no design overlap length has been specified. This drawing change does not introduce a segmented-member analysis: the calculation retains the selected continuous support count, as stated in the caption. Joint/bearing design remains outside this schematic.

Support/overhang proportions are preserved along each independently normalized display axis. Slab thickness and prop extension affect the schematic height within readable bounds. Secondary spacing changes the drawing pattern, capped at 18 rows; the caption explicitly marks quantities as illustrative. This is not a placement drawing, sheeting design or material take-off. Invalid input clears the schematic immediately.

Both windows open at 1040 × 860, with a minimum 900 × 620. Inputs and review panels scroll independently; Close and Import to Revit stay in a fixed footer. Results still update as inputs change. There is no new Calculate action or change to the calculation model.

Numeric conversion errors highlight the field and show an inline issue panel. Invalid results remain hidden and import remains disabled. Inactive custom-load and unchecked slab fields are explicitly disabled, including inside collapsed expanders. The beam diagram clears on invalid input and its empty drawing area collapses.

## Verification

Production WPF views are rendered in the existing smoke-test hosts at normal and minimum sizes, with custom loads expanded and invalid input. Tests cover selection bindings, inactive inputs, recovery, diagrams, disabled import and footer placement. Slab schematic checks cover all 16 support-count combinations, changing secondary spacing, bounded density even at tiny positive spacing, invalidation/recovery and decimal-comma culture. The slab calculation/catalog suite and full net48 add-in build also run. Actual Revit transactions and live display scaling require an in-Revit check.

This change does not modify the backpropping calculator or its pending entry-point work. The deferred sheeting/full-load-path scope remains unchanged.

