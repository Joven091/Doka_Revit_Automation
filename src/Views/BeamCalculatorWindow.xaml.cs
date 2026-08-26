using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorWindow : Window
    {
        public BeamCalculatorWindow()
        {
            InitializeComponent();
        }

        private void BtnApplyToBeams_Click(object sender, RoutedEventArgs e)
        {
            var vm = this.DataContext as BeamCalculatorViewModel;
            if (vm == null) return;

            var app = EngineeringDashboard.Command.App;
            if (app == null) return;
            var doc = app.ActiveUIDocument.Document;
            var view = doc.ActiveView;

            FilteredElementCollector collector = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FamilyInstance));

            var matchingBeams = new List<FamilyInstance>();

            foreach (FamilyInstance instance in collector)
            {
                if (instance.Symbol != null && instance.Symbol.FamilyName == "PrimaryBeam")
                {
                    Parameter hParam = instance.LookupParameter("H") ?? instance.Symbol.LookupParameter("H");
                    if (hParam == null) continue;

                    Parameter bParam = instance.LookupParameter("B") ?? instance.Symbol.LookupParameter("B");
                    if (bParam == null) continue;

                    double hFeet = hParam.AsDouble();
                    double hMm = hFeet * 304.8;

                    double bFeet = bParam.AsDouble();
                    double bMm = bFeet * 304.8;

                    if (Math.Abs(hMm - vm.Depth) <= 0.1 && Math.Abs(bMm - vm.Width) <= 0.1)
                    {
                        Parameter strTypeParam = instance.LookupParameter("Structural Element Type");
                        if (strTypeParam != null && strTypeParam.AsString() == "Beam")
                        {
                            matchingBeams.Add(instance);
                        }
                    }
                }
            }

            if (matchingBeams.Count > 0)
            {
                using (Transaction t = new Transaction(doc, "Apply Beam Calculator Values"))
                {
                    t.Start();
                    foreach (var beam in matchingBeams)
                    {
                        Parameter maxPropSpacingParam = beam.LookupParameter("Max Prop spacing") ?? beam.LookupParameter("Max Prop Spacing");
                        Parameter startCantileverParam = beam.LookupParameter("Start Cantilever");
                        Parameter endCantileverParam = beam.LookupParameter("End Cantilever");

                        if (maxPropSpacingParam != null && !maxPropSpacingParam.IsReadOnly)
                        {
                            maxPropSpacingParam.Set(vm.MaximumPropSpacing / 304.8);
                        }
                        if (startCantileverParam != null && !startCantileverParam.IsReadOnly)
                        {
                            startCantileverParam.Set(vm.PrimaryBeamCantilever / 304.8);
                        }
                        if (endCantileverParam != null && !endCantileverParam.IsReadOnly)
                        {
                            endCantileverParam.Set(vm.PrimaryBeamCantilever / 304.8);
                        }
                    }
                    t.Commit();
                }
                MessageBox.Show($"Successfully applied values to {matchingBeams.Count} primary beams.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("No matching primary beams found in the active view.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
