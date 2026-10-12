# Primary material and WS10 catalogue

D3 beam formwork supports up to **15 parallel main beams** with one slab bay between adjacent beams. The global **Primary beam material** selector uses either double H20 or one **multi-purpose WS10 Top50 assembly** at each primary line. It applies to main beams and dedicated crossing-beam primaries. Lower secondaries and slab beams remain single H20s with their existing checks and custom-length scopes.

WS10 always uses uncut catalogue stock, even when custom H20 lengths are enabled or the editable H20 stock list contains other lengths. Plan labels read **WS10 · length** in millimetres. WS10 drawings are blue; H20 drawings keep the timber colour. Sections use the WS10 153 × 100 mm envelope and a paired-channel symbol. Clash checks and tower-head elevations use its 100 mm depth. The material is saved with calculations and drawing inputs, included in undo/redo and diagnostics, and defaults to double H20 for older saved files.

## Catalogue

The implemented variant is the **multi-purpose waling WS10 Top50**, rather than the older plain steel waling or arbitrary paired U sections. Lengths and masses come from the [Doka Top50 catalogue](https://shop.doka.com/shop/de/en/wall-formwork/timber-beam-formwork/large-area-formwork-top-50/multi-purpose-waling-ws10-top50/p/095/) and [Doka Top50 item list](https://www.doka.com/_ext/downloads/itemlists/me/26.pdf), checked on 9 October 2026.

| Length (mm) | Mass per assembly (kg) |
| --- | --- |
| 500 | 10.2 |
| 750 | 14.9 |
| 1000 | 19.6 |
| 1250 | 24.7 |
| 1500 | 29.7 |
| 1750 | 35.0 |
| 2000 | 38.9 |
| 2250 | 44.2 |
| 2500 | 48.7 |
| 2750 | 54.2 |
| 3000 | 60.2 |
| 3500 | 68.4 |
| 4000 | 79.4 |
| 4500 | 89.1 |
| 5000 | 102.0 |
| 5500 | 112.4 |
| 6000 | 118.0 |

## Calculation basis

The source is **Doka Calculation Guide 999736002, 03/2012, pages 13 and 135** ([archived original document transcription](https://doczz.net/doc/5188510/doka-formwork-engineering)). It gives WS10 middle-zone permissible moment 12.3 kNm, shear 82 kN and second moment of area 412 cm⁴. Steel E = 210 GPa gives EI = 865.2 kNm². The first and last 300 mm use the guide's conservative special-application end-zone moment limit of 10 kNm. The 153 × 100 mm envelope is also documented in the [Doka Staxo 100 instructions](https://www.doka.com/_ext/downloads/downloadcenter/999800324_2009_08_online.pdf).

Moment and shear are coupled. This implementation uses the conservative linear check **|M(x)| / M_limit(x) + |V(x)| / 82 ≤ 1**, inside the published plastic interaction envelope, with no axial load. It does not reproduce the manufacturer's nonlinear plastic curve. Extrema are calculated exactly on each distributed-load interval, including point/support jumps and both 300 mm zone boundaries. Each steel member must also pass the selected span/overhang deflection criterion and individual tower-leg capacity checks. The diagram's separate moment/shear limits show the individual capacities; interaction utilization is displayed beside them.

Catalogue mass × 9.80665 m/s² is added once as a uniform self-weight over each full WS10 stock member in every construction stage, including projected crossing tails. Concrete and slab/crossing increments retain their existing physical footprints. Only the material's own weight is added; tower weight remains outside the model. H20 and WS10 quantities and lengths are reported separately; layout economy compares total member length after the existing support-count objective.

The structural model assumes restrained walers and compatible support heads. Lateral restraint, local bearing, connectors, tower-head compatibility, and axial loading require separate detailing; the selector does not calculate those details. These assumptions are included in the calculator's material basis and exported reports.

## Verification

`--ws10` tests compare exact interaction peaks against independently sampled continuous and piecewise loaded responses, verify catalogue mass and equilibrium, reject interaction/deflection/leg failures and cut stock, solve a 15-beam steel network and a dedicated crossing, and preserve H20 secondary checks. `--ws10-ui` exercises the selector, undo/redo, steel labels and colour, channel sections, actual response diagrams, 15-beam drawing persistence, rejection of unknown saved materials, and legacy-file defaults on Windows WPF.
