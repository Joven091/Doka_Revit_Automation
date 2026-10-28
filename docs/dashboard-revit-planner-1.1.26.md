# Dashboard 1.1.26 release scope

Active calculators share the D3-style File / Tools header. Undo, Redo and Delete
use the same vector icons, accessible names, tooltips and existing shortcuts.
Commands are offered where the tool supports them. Export report controls are
removed. Dome Beta remains visible below Top50 Planner and is locked; D3 slab
remains Coming soon. Existing beam, slab, backpropping and Steel Shape calculation
engines are retained.

Top50 accepts straight native Revit basic walls through Use Revit selection.
Read operations execute through ExternalEvent on Revit's API thread and return
detached data. Both straight vertical forming faces are measured by projecting
their boundary geometry onto the wall axis. The longer face is the planning
allowance; openings do not deduct from the overall face length. Original centre,
exterior and interior measurements are retained. Unequal faces are labelled for
review; the panel allowance covers the longer length on both faces, while the
shorter-face installation still requires review. Curved, slanted, curtain and
stacked walls require separate reviewed planning lengths; linked model contents
are not imported in this first version.

One physical wall remains one row. Physical panel and connector quantities still
cover two faces; offered LM is per face. The casting dropdown shows installed
panel length divided by offered panel length for that casting group. This is
equipment utilization, not structural capacity.

Revit document identity, wall UniqueId, original numeric ID, geometry and errors
are available through Diagnose. A valid import is one Undo step; invalid input
does not replace the wall list. Refresh imported walls reads the stored identities,
retains manual rows and deleted-row tombstones, and invalidates the previous
offer. Changed original geometry replaces an obsolete reviewed length override;
unchanged reviewed dimensions and exclusions are retained. Missing walls remain
identifiable and excluded rather than silently disappearing.

Selecting a casting group selects its source walls in the active Revit project.
Background result rendering does not change selection. Rapid user selections
settle on the latest chosen group. The current view and zoom are retained;
Locate casting group in Revit explicitly requests a view change. Wrong-project
requests are rejected. Empty or missing references do not clear unrelated Revit
selection. Requests from a closed planner cannot apply to Revit.

V5 project files embed the Revit measurement snapshot, so an offer can reopen
without the RVT file. V1–V4 DXF/manual projects remain readable. Preserve an older
file copy when retaining compatibility with older installations. Search effort,
quantity-first ranking and the maximum three displayed alternatives are unchanged.

The user authorized installer creation before merge. The release workflow builds
an explicitly marked feature-branch commit, verifies isolated fresh installation,
upgrade backups and installed tool behavior, and produces the team package before
the PR is merged. Normal commits do not build installers. The merged source tree
must match the verified installer source tree. Company-folder publication remains
a separate action by the release owner.

Automated tests cover detached source processing, API adapter behavior using
explicit test-only Revit stand-ins, source persistence/history and actual Windows
WPF interactions. Actual Revit2024 host acceptance remains unrun and must include
native face geometry, workshared/unsaved projects, selection/highlighting and
refresh after model edits. Existing structural design and assembly review remain
outside this one-dimensional equipment planner.
