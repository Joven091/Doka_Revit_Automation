# Shared prop capacity sources

These are free construction prop capacities, selected by position of the **outer tube**, in kN. They are shared by the slab calculator and beam calculator. The separate backpropping catalog is not changed by this revision.

| Catalog | Authoritative publication | Page / basis |
|---|---|---|
| Eurex 20 eco: 250, 300, 350, 400, 550 | https://direct.doka.com/_ext/downloads/downloadcenter/999810702_2025_10_online.pdf | 8; 999810702, October 2025; free construction props; approval Z-8.311-942 |
| DokaFit: 300, 350, 400 | https://www.doka.com/web/media/files/241203_V11_DokaFit_Prop_Leaflet.pdf | 2; February 2025; EN 1065 mathematical model |

The source pages were rendered and reviewed. Eurex merged cells were expanded through their drawn range; a dash stops the range. DokaFit empty cells remain unsupported. The independent source fixture is tests/Slab.Tests/prop-capacity-reference.csv. It includes every supported/unsupported row for the implemented sizes; Eurex 450/30 eco are not added to the product selector by this correction.

The DokaFit “at fixed” choice was removed from this free-prop catalog. The leaflet has top/bottom configurations and separate ANSI ratings, not an interchangeable fixed orientation. System-dependent/clamped capacities require a separately documented installation condition.

Actual extension is checked against the available published rows before rounding. The conservative lookup uses the next 100 mm row, without interpolating or extrapolating. The 0.0000001 mm arithmetic tolerance only removes floating-point boundary noise. The minimum supported table row is used as the minimum for calculation; no lower mechanical range is inferred.

Examples of corrected capacities: DokaFit 400 at 4.0 m / outer tube at top = 16.8 kN; DokaFit 300 at 3.0 m / top = 25.0 kN; Eurex 20 eco 550 at 5.5 m / bottom = 20.3 kN. DokaFit 350 has no capacity below 2.2 m.
