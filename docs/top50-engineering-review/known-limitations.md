# Known limitations and engineering assumptions

## Planning model

Top50 produces preliminary reusable-equipment offers. Casting groups are
simultaneous equipment allocations whose stock can be reused between groups.
They do not establish a pour sequence, construction duration, stripping period,
crane availability or concurrent-zone programme. Unrestricted allocation remains
the default. The site assessment compares no zones, optional manual zones and
advanced sequencing; the latter two are deferred for this release.

Fewest panels remains the user's first priority. Economical, Balanced and Fastest
Construction apply secondary metrics within the admitted panel-count tier.
Fastest favors fewer allocation groups, not a measured or guaranteed duration.
Rental prices, labour costs and a project programme are not available to price
these trade-offs.

The optimizer evaluates a bounded deterministic candidate family and retains at
most four patterns for each modeled wall/inventory combination. A returned offer
is validated against the active constraints. A no-result is not proof that no
feasible arrangement exists, and the general best-found stock is not proven
globally optimal. Exact proofs in the independent QA report apply only to the
enumerated small fixtures. Original external Top50 solver parity remains
unresolved without the original saved project, workbook and solver specification.

## Quantities and connections

Panel quantities include both wall faces. Offered LM is per face. Corresponding
faces use the same planning arrangement; imported longer forming-face lengths
are equipment allowances rather than a complete geometry/assembly design.

Connector quantities are for **one level on both faces**: one connector per joint
per face. Users calculate the applicable number of levels separately. Direct and
filler joint demand is simultaneous within each group; the stock is reusable
between groups. Spare adjustable waling extensions can substitute at direct
joints under the established planning assumption, subject to the approved Doka
assembly.

Equipment names/articles are Formwork element connector FF20/50 Z, 587533000,
and Adjustable waling extension FF20/50, 587532000. The modeled filler allowance
up to 350 mm is not approval of every such assembly. Short-panel clearances,
connector collisions, filler backing, corners, unequal faces, ties, walings,
height, pressure and structural capacity require project-specific engineering.
Geometric fit and counted connectors do not establish structural adequacy.

## Platform and release verification

Cross-builds and test-only Revit API stand-ins cannot establish behavior in an
actual Revit session. Automated Windows WPF and isolated package checks are
reported with their exact scope in the final validation record. Actual Revit
2024 host acceptance and installation on a completely clean machine with no
preinstalled prerequisites remain separate checks.

Deterministic work limits avoid elapsed-time cutoffs. Repeatability is tested in
fresh processes, reordered inputs and another culture on the same platform.
Solver/native-library changes across platforms or future versions may produce
different tied arrangements; these tests do not assert cross-platform bitwise
identity or a guaranteed runtime on every project or workstation.
