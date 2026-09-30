using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorWindow : Window
    {
        public BeamCalculatorWindow() { InitializeComponent(); InitializeUi(); }

        private void BtnApplyToBeams_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetValidCalculation(out BeamCalculatorViewModel vm) || !vm.CanImport)
            {
                MessageBox.Show("Correct the inputs before importing values.", "Invalid calculation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var app = EngineeringDashboard.Command.App;
            if (app?.ActiveUIDocument == null) return;
            var doc = app.ActiveUIDocument.Document;
            var matchingBeams = new List<FamilyInstance>();
            // Preserve existing targeting: same family, dimensions and element type in active view.
            foreach (FamilyInstance instance in new FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(typeof(FamilyInstance)))
            {
                if (instance.Symbol == null || instance.Symbol.FamilyName != "PrimaryBeam") continue;
                Parameter h = instance.LookupParameter("H") ?? instance.Symbol.LookupParameter("H");
                Parameter b = instance.LookupParameter("B") ?? instance.Symbol.LookupParameter("B");
                if (!IsLength(h) || !IsLength(b)) continue;
                Parameter kind = instance.LookupParameter("Structural Element Type");
                if (Math.Abs(h.AsDouble()*304.8-vm.Depth) <= 0.1 && Math.Abs(b.AsDouble()*304.8-vm.Width) <= 0.1
                    && kind?.StorageType == StorageType.String && kind.AsString() == "Beam") matchingBeams.Add(instance);
            }
            if (matchingBeams.Count == 0)
            {
                MessageBox.Show("No matching primary beams found in the active view.", "Info", MessageBoxButton.OK, MessageBoxImage.Information); return;
            }
            int updated = 0, skipped = 0, failed = 0;
            var reasons = new List<string>();
            try
            {
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
        private static bool IsLength(Parameter p) => p != null && p.StorageType == StorageType.Double && p.Definition.GetDataType() == SpecTypeId.Length;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
