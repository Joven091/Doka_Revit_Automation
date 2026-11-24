# Dokaflex slab access extensions

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

The calculator checks the fully loaded pour. Slab pressure acts over the slab
footprint and the specified total access pressure acts on every enabled access
zone at the same time. At each trial spacing on the 25 mm grid, the calculator
checks strength, span and overhang displacement, support contact and each prop
reaction. The diagrams show the primary line with the highest utilization across
these checks. The report lists the loads and reactions for every receiving line.

The section and red/blue equivalent-UDL diagram share the same slab-edge and
support coordinates. Saved calculations and Dashboard restart checkpoints retain
the access inputs. Loading an older file over an active access layout resets new
inputs to their legacy defaults, avoiding a mixture of old and retained geometry.
Access/asymmetric files use the distinct `SlabAccess` kind so older Dashboard
versions reject them instead of silently loading them as symmetric, fully loaded
slabs. The ordinary full-load `Slab` format remains backward compatible.

The Revit helper now receives independent Start and End Cantilever values. The
bridge advertises this optional capability; an older integration is rejected before
any asymmetric helper write, with instructions to install 1.2.6 or newer. The wire
protocol remains 1 and compatible Dashboard-only updates remain available.

Validation uses the previous uniform-load analytical solution, independently
assembled Euler–Bernoulli finite elements for partial/asymmetric UDLs, statics,
reflection, actual-count, load-transfer, uplift, persistence and WPF controls.
Windows installer acceptance runs separately for Revit 2024 and 2027; it does not
run Autodesk Revit itself. The existing D3 slab system remains locked.
