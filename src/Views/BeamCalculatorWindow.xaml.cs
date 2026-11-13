using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
#if !DASHBOARD_DESKTOP
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
#endif

namespace EngineeringDashboard
{
    public partial class BeamCalculatorWindow : Window
    {
        public BeamCalculatorWindow() { InitializeComponent(); InitializeUi(); }

#if DASHBOARD_DESKTOP
        private async void BtnApplyToBeams_Click(object sender,RoutedEventArgs e)=>await ApplyBeamFromDesktopAsync();
#else
        private bool applyingToBeams;
        private async void BtnApplyToBeams_Click(object sender,RoutedEventArgs e)
        {
            if(applyingToBeams)return;applyingToBeams=true;
            try {await Command.Request(app=>{if(IsLoaded)ApplyToBeams(app);return true;});}
            catch(Exception error){if(IsLoaded)MessageBox.Show(this,error.Message,"Revit request",MessageBoxButton.OK,MessageBoxImage.Information);}
            finally {applyingToBeams=false;}
        }
        private void ApplyToBeams(Autodesk.Revit.UI.UIApplication app)
        {
            if (!TryGetValidCalculation(out BeamCalculatorViewModel vm) || !vm.CanImport)
            {
                MessageBox.Show("Correct the inputs before importing values.", "Invalid calculation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (app?.ActiveUIDocument == null) return;
            var uiDocument = app.ActiveUIDocument;
            var doc = uiDocument.Document;
            List<FamilyInstance> matchingBeams;
            Hide();
            try
            {
                matchingBeams = uiDocument.Selection.PickElementsByRectangle(
                    new BeamHelperSelectionFilter(vm.Width, vm.Depth),
                    "Drag a window around matching beam helper lines. Esc cancels.")
                    .OfType<FamilyInstance>().GroupBy(instance => instance.Id).Select(group => group.First()).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return; }
            catch (Exception ex)
            {
                MessageBox.Show("Helper-line selection did not complete. No values were changed.\n" + ex.Message,
                    "Selection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally { Show(); Activate(); }
            if (matchingBeams.Count == 0)
            {
                MessageBox.Show("No beam helper lines matching this beam width and depth were selected. No values were changed.",
                    "Selection empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            int updated = 0, skipped = 0, failed = 0;
            var reasons = new List<string>();
            try
            {
                uiDocument.Selection.SetElementIds(matchingBeams.Select(instance => instance.Id).ToList());
                using (var transaction = new Transaction(doc, "Apply Beam Calculator Values"))
                {
                    transaction.Start();
                    foreach (var beam in matchingBeams)
                    {
                        Parameter[] parameters = {
                            beam.LookupParameter("Max Prop spacing") ?? beam.LookupParameter("Max Prop Spacing"),
                            beam.LookupParameter("Start Cantilever"), beam.LookupParameter("End Cantilever") };
                        string[] names = { "Max Prop spacing", "Start Cantilever", "End Cantilever" };
                        var invalid = new List<string>();
                        for(int i=0;i<parameters.Length;i++)
                            if(!IsLength(parameters[i]) || parameters[i].IsReadOnly) invalid.Add(names[i]);
                        if(invalid.Count > 0)
                        {
                            skipped++; reasons.Add($"{beam.Id}: missing, read-only or non-length parameter: {string.Join(", ", invalid)}."); continue;
                        }
                        // All three values succeed together; a failed instance cannot be partly updated.
                        using(var change = new SubTransaction(doc))
                        {
                            change.Start();
                            try
                            {
                                if(!parameters[0].Set(vm.MaximumPropSpacing/304.8) || !parameters[1].Set(vm.PrimaryBeamCantilever/304.8)
                                    || !parameters[2].Set(vm.PrimaryBeamCantilever/304.8)) throw new InvalidOperationException("A parameter rejected its value.");
                                if(change.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Instance update was not committed.");
                                updated++;
                            }
                            catch(Exception ex) when(ex is ArgumentException || ex is InvalidOperationException
                                || ex is Autodesk.Revit.Exceptions.ArgumentException || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                            {
                                if(change.GetStatus() == TransactionStatus.Started) change.RollBack();
                                failed++; reasons.Add($"{beam.Id}: {ex.Message}");
                            }
                        }
                    }
                    if(transaction.Commit() != TransactionStatus.Committed)
                    {
                        MessageBox.Show("The transaction did not commit. No successful updates are reported.", "Import incomplete", MessageBoxButton.OK, MessageBoxImage.Warning); return;
                    }
                }
                string details = reasons.Count == 0 ? "" : "\n\n" + string.Join("\n", reasons.Take(5)) + (reasons.Count > 5 ? "\nAdditional instances also skipped or failed." : "");
                MessageBox.Show($"Updated: {updated}\nSkipped: {skipped}\nFailed: {failed}" + details,
                    updated == matchingBeams.Count ? "Import complete" : "Import summary", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch(Exception ex)
            {
                MessageBox.Show("Import did not complete. No successful updates are reported.\n" + ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private sealed class BeamHelperSelectionFilter : ISelectionFilter
        {
            private readonly double widthMm, depthMm;
            public BeamHelperSelectionFilter(double widthMm, double depthMm) { this.widthMm=widthMm; this.depthMm=depthMm; }
            public bool AllowElement(Element element)
            {
                var instance=element as FamilyInstance;
                if (instance?.Symbol == null || instance.Symbol.FamilyName != "PrimaryBeam") return false;
                var kind=instance.LookupParameter("Structural Element Type");
                var depth=instance.LookupParameter("H") ?? instance.Symbol.LookupParameter("H");
                var width=instance.LookupParameter("B") ?? instance.Symbol.LookupParameter("B");
                return kind?.StorageType == StorageType.String && kind.AsString() == "Beam"
                    && IsLength(depth) && IsLength(width)
                    && Math.Abs(depth.AsDouble()*304.8-depthMm) <= 0.1
                    && Math.Abs(width.AsDouble()*304.8-widthMm) <= 0.1;
            }
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
        private static bool IsLength(Parameter p) => p != null && p.StorageType == StorageType.Double && p.Definition.GetDataType() == SpecTypeId.Length;
#endif
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
