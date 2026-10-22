# Create a beam drawing from Revit

1. In Revit 2024, select the structural beams and floors for one rectangular beam/slab area. Include the columns and walls at the beam ends.
2. Open Engineering Dashboard → Beam calculator → D3 shoring.
3. Click **From Revit…**. Review the main beam direction, dimensions and drawing preview. Elevations in the table use Revit's internal model datum; the canvas uses the common concrete top as zero.
4. Click **Use this drawing**. Review floor height and the existing project settings, then **Calculate**.

Columns and walls appear as muted, labelled references. Where their rectangular faces cover a beam end, the imported clear span ends at that face. Undo restores the previous drawing; Redo restores the import. Saved inputs retain the reference outlines and element labels. The import is a read-only geometry snapshot; it does not modify Revit or maintain a live link. Project stock permissions, custom cut scope, loads and design limits are retained.

The first version supports straight horizontal rectangular beams, flat rectangular slabs without openings, a common concrete top elevation, and parallel beam rows with matching clear start/end limits. A crossing can pass through multiple complete bays; it becomes one crossing segment per bay. A standalone beam can be imported without a slab. Floor height remains a project input because the selection does not establish the supporting floor below.

The preview blocks import and identifies elements requiring manual modelling when the selection contains sloped/curved/diagonal framing, slab openings or irregular outlines, partial bays, incompatible elevations, different clear spans, or a column/wall dividing a span. It never invents an unselected slab or fills missing geometry. Change the selected area in Revit or model those areas manually. The live selection button is available only in the Revit add-in; the standalone can reopen previously saved imported drawings.

Revit database reads run synchronously on the external-command thread, before the solver runs. The numerical solver receives the same beam/slab dimensions as manual drawing. Reference columns/walls are drawing context and span boundaries, not additional designed formwork members.

If the selection cannot be imported, click **Diagnose** beside **Update preview**, add optional remarks and choose **Copy report**. Paste the report into the chat for review. Diagnose remains available for empty/unsupported selections, invalid table text and unexpected read/preview errors. It includes the add-in version/build identity, Revit version, document/view names, selected element IDs, categories/family types, skipped/rejected elements, geometry checks, original and reviewed dimensions, chosen direction, import problems and exception details. It reads the captured selection without changing Revit or replacing the canvas. Report refresh and copying are explicit actions.
