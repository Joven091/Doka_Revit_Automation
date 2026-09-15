# Calculator interface

The slab and beam calculators share CalculatorStyles.xaml, using the backpropping calculator's yellow numbered sections, neutral grey background, labelled fields, grey diagram panels and prominent yellow spacing results.

## Flow

1. Calculation setup: geometry, optional slab wings for beams, and expandable load inputs. The load heading indicates default or custom mode even when collapsed.
2. Support layout: H20/prop geometry and existing prop selections. Derived extension remains a readout.
3. Review spacing: spacing is the largest value, followed by load per prop and utilization. Calculation details contain the existing secondary results. Beam schematics and slab diagram commands remain available in this review area.

Both windows open at 1040 × 860, with a minimum 900 × 620. Inputs and review panels scroll independently; Close and Import to Revit stay in a fixed footer. Results still update as inputs change. There is no new Calculate action or change to the calculation model.

Numeric conversion errors highlight the field and show an inline issue panel. Invalid results remain hidden and import remains disabled. Inactive custom-load and unchecked slab fields are explicitly disabled, including inside collapsed expanders. The beam diagram clears on invalid input and its empty drawing area collapses.

## Verification

Production WPF views are rendered in the existing smoke-test hosts at normal and minimum sizes, with custom loads expanded and invalid input. Tests cover selection bindings, inactive inputs, recovery, diagrams, disabled import and footer placement. The slab calculation/catalog suite and full net48 add-in build also run. Actual Revit transactions and live display scaling require an in-Revit check.

This change does not modify the backpropping calculator or its pending entry-point work. The deferred sheeting/full-load-path scope remains unchanged.
