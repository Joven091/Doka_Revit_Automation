# Engineering Dashboard 1.1.27 — Top50 Planner

This release keeps Top50 inside the existing Revit 2024 Engineering Dashboard.
It corrects verified planning and project-restoration defects without changing
the established equipment-offer workflow.

## Changes

- Required longest walls now use an actual single kit when identical sets are
  requested. Previously an allocation could draw multiple copies from the pooled
  stock and fail its own subsequent validation.
- Identical-kit candidate completion recovers four independently reproduced
  small no-offer cases. It preserves the existing candidates and adds a bounded
  per-kit completion; it does not turn the search into a global-optimum proof.
- Percentage offers use percentage settings even when the saved fixed-set
  preference requests identical kits. Switching back retains that preference.
- Opening a project checks cached offers against its permitted panel lengths,
  active percentage band and identical-kit requirements. Incompatible cached
  offers are discarded with an Optimize explanation; valid inputs remain.
- Whole-number fields reject fractional or out-of-range values instead of
  silently truncating them. Invalid edits preserve the prior settings/history.
- First no-result and cancelled searches show a lasting explanation. Late Cancel
  requests preserve the completed outcome. Compact help explains the ranking,
  allocation-group reuse and connector assumptions.

## Established behavior retained

Both offering methods and all three presets remain. Balanced remains the default.
Fewest panels is the first admission rule; presets compare offers within that
tier. At most three offers are displayed. Panels cover both faces, offered LM
remains per face, and connector stock covers one level on both faces. Spare
adjustable connectors can serve direct joints under the existing assembly
assumption. Excel/report exports remain removed.

The release preserves the prior D3/beam import fixes, self-contained .NET 10 beam
worker, Steel Shape, modeless Revit integration, updater and locked Dome Beta/D3
slab entries. It does not merge GitHub code or publish a company update.

## Compatibility

Target: Revit 2024 x64 and .NET Framework 4.8. The beam worker includes its own
.NET 10 runtime. The native optimization library needs the VC++ x64 runtime.
Revit API references are pinned to the already resolved 2024.3.60 packages;
Revit API DLLs are not distributed.

New saves use project format V5 and search policy V4. Valid older supported
projects remain readable. Keep a separate project copy when maintaining an older
application version. Close Revit before running the installer; reopen Revit after
installation completes. The installer preserves upgrade backups and user data.

Final validation and installed-package evidence are recorded separately in
`final-validation.md` and the package's `release-info.json`. Actual Revit-host and
clean-machine prerequisite acceptance must not be inferred from automated WPF
hosts or isolated Windows CI checks.
