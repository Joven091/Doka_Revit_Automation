# Slab access extensions — Dashboard 1.2.6

Dokaflex Support layout has independent left/right cantilevers for both H20 layers.
Enable **Access beyond slab edge**, choose **Left**, **Right** or **Both**, and enter
each enabled extension from the slab edge to the beam end. Cantilevers remain the
distance from the last support to the beam end. The derived slab-edge position may
be past or inside the last support; access is not artificially forced to start there.

**Total access load** defaults to **0.75 kN/m² including deck weight**, as specified
for this project. It is editable and shared by the enabled extensions. No additional
0.4 kN/m² deck load is added to that total. Under concrete the existing slab load
and plywood checks continue to apply. This release models area/UDL access loading;
it does not introduce a concentrated-worker or guardrail-load design check.

Partial loads and unequal overhangs use `SlabBeamAnalysis`, an exact constant-EI
three-moment solution with piecewise UDL integration. Polynomial extrema give
strength and deflection peaks, including support rotation in both free overhangs.
The full-load five/six-support envelope is retained only for the original symmetric
full-load mode. With access/asymmetry and a 5+ selection, enter the actual count.

Each secondary is checked under the slab/access distribution. Secondaries over a
primary access band carry the total access pressure. For each receiving primary,
its corresponding secondary reaction divided by secondary spacing forms the UDL
in that zone. Slab and access reactions are kept paired by primary line; independent
maximum reactions must not be combined because that can hide uplift. Where both
axes extend, the access bands form a union; their corner load is counted once.

Search retains the 25 mm spacing grid and checks strength, span and overhang
displacement, support contact and every individual prop reaction. Pouring and
before-pour cases are checked. Before pouring, the existing formwork-only load
remains inside the slab footprint. Conservative end-load omission cases check
either access end independently, including both-end configurations. The total
access input is not split into invented dead and live portions: omitting a side
removes its stabilizing load as a conservative bound. The case selector exposes
these distributions and the receiving primary line. Reported maximum prop load
and checks cover all cases, not only the diagram currently selected.

The section and red/blue equivalent-UDL diagram share the same slab-edge and
support coordinates. Saved calculations and Dashboard restart checkpoints retain
the access inputs. Loading an older file over an active access layout resets new
inputs to their legacy defaults, avoiding a mixture of old and retained geometry.

The Revit helper now receives independent Start and End Cantilever values. The
bridge advertises this optional capability; an older integration is rejected before
any asymmetric helper write, with instructions to install 1.2.6 or newer. The wire
protocol remains 1 and compatible Dashboard-only updates remain available.

Validation uses the previous uniform-load analytical solution, independently
assembled Euler–Bernoulli finite elements for partial/asymmetric UDLs, statics,
reflection, actual-count, load-transfer, uplift, persistence and WPF controls.
Windows installer acceptance runs separately for Revit 2024 and 2027; it does not
run Autodesk Revit itself. The existing D3 slab system remains locked.
