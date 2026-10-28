# Top50 standalone review 1.1.25

This review produces a portable tester. Installer compilation and installer-based CI jobs require an explicit workflow dispatch with `build_installer` enabled; normal pushes and pull requests do not create installers. No company feed publication or merge is included.

The shared planner now has four tabs: Import, Settings, Results and Casting groups. Reports and the Excel export command are unavailable. Detailed source and solver evidence remains in Tools > Diagnose.

Undo / Redo / Delete and Ctrl+Z / Ctrl+Y / Delete cover wall additions, reviews, deletions and settings. Multiple selected Import rows can be deleted together. Text editors retain their own shortcuts. Edits are blocked during optimization. A history snapshot restores its corresponding results and stale state; saving establishes the clean state without erasing history. Opening or importing a different project resets editing history. Deleted walls stay in the embedded source audit and preserve remaining wall labels and manual ID uniqueness.

Settings exposes the offering method, permitted lengths and filler settings. There is no component-count ceiling or user effort/display/connector tuning. The fixed thorough policy uses 100 deterministic work units, completes the eligible minimum-panel-count tier, and displays at most three valid ranked options. Pattern retention and deterministic work are finite; this is still best-found search, not a proof of global optimality.

Each physical wall retains one geometry row and one casting allocation. That allocation describes matching arrangements on both faces. Physical panel Qty is twice the internal per-face stock, while offered length is explicitly LM per face. Therefore doubling panel quantities does not double the LM shown or the casting-group count. A uniform factor of two preserves quantity-first ordering. Connector stock covers one connector per joint on each of two faces for one level, with compatible adjustable reuse always enabled. Wall-height connection levels are left to the user's calculation.

Results uses compact Panel length / Qty tables with subtle quantity highlighting. The connector card fits two rows and does not stretch to the panel card. Ranked alternatives are secondary; Status uses complete sentences and actual comparisons. Casting groups use a compact C-01 dropdown above the proportional panel/filler schematic, labelled as the same arrangement on both faces.

New projects use format V4. V1–V3 projects remain readable; old effort, candidate-count, component-limit and connector overrides do not override the current fixed policy. Source hashes and geometry checks remain enforced. Newly saved V4 files require this updated application. Save a separate test copy when retaining an older project for older versions.
