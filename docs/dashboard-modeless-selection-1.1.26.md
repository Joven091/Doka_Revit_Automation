# Dashboard usability changes — 1.1.26 review

The D3 beam toolbar reads File, Tools, + Add, Use Revit selection, Undo, Redo,
Delete. Calculate remains on the right. Delete stays visible and is disabled
when the selection cannot be removed.

Select beams, slabs, columns and walls in Revit, then click Use Revit selection.
The existing geometry builder chooses its normal default direction and populates
the working canvas directly. There is no review window in this path. Existing
geometry remains until validation succeeds; the completed replacement is one Undo
step. Project settings, the combined canvas and source identities are retained.
This remains a geometry snapshot, not a live connection to the model.

Tools / Diagnose includes the latest request timestamps and stage, full selected
element snapshot and geometry, chosen direction, validation problems, source to
canvas mappings and full exception details. It also works while Revit is busy or
after an import fails. A failed reader does not fabricate a geometry rejection.

The dashboard and its tool windows are modeless. Reopening the ribbon command
activates the current tool instead of creating another copy. Revit selection and
the older slab/beam helper updates run through ExternalEvent on Revit's API
thread. Existing transactions and per-instance rollback are retained. Pending
requests cannot be duplicated or applied after the calculator closes.

Steel Shape retains its inputs, weld interactions, layout and calculation engine.
Its page, schematics and WPF fallback use the dashboard's neutral surfaces, grey
steel, dark text and yellow selection accents. Validation remains red.

## Updates — existing behavior

Check for updates reads latest.json from
`\\DOKAAREDUBA\ApplDat\MEA\ENG\Joven\BeamCalculator\Updates`.
An available update requires Install after Revit closes. The package is staged
and verified locally, and the separate updater waits for the user's Revit
processes to close before installation. Save work, close Revit, wait for the
updater to finish, then reopen Revit manually. Checking alone does not install.
The release owner must publish the new version to the share first.

## Acceptance

Automated checks exercise request lifetime, one-click import, failures and
diagnosis, Undo/Redo, combined geometry, window lifetime and Steel Shape.
Actual Revit2024 acceptance is still required: model while each tool is open;
select elements, complete/cancel an active Revit command and import; repeat after
switching documents; apply helper values and cancel picking; close/reopen tools.
This review does not create an installer, merge or publish a company update.
