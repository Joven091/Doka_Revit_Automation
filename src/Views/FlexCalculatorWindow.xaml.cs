using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using MyRevitAddin.Views;

namespace EngineeringDashboard
{
    public partial class FlexCalculatorWindow : Window
    {
        private DiagramWindow _primaryDiagramWindow;
        private DiagramWindow _secondaryDiagramWindow;
        private FlexCalculatorViewModel _viewModel;

        public FlexCalculatorWindow()
        {
            InitializeComponent();
            InitializeUi();
            
            _viewModel = new FlexCalculatorViewModel();
            this.DataContext = _viewModel;

            _viewModel.PrimaryBeamDiagramRequested += OnPrimaryBeamDiagramRequested;
            _viewModel.SecondaryBeamDiagramRequested += OnSecondaryBeamDiagramRequested;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_primaryDiagramWindow != null && _primaryDiagramWindow.IsLoaded)
            {
                if (e.PropertyName == nameof(_viewModel.PrimaryDiagramViewModel))
                    _primaryDiagramWindow.DataContext = _viewModel.PrimaryDiagramViewModel;
            }

            if (_secondaryDiagramWindow != null && _secondaryDiagramWindow.IsLoaded)
            {
                if (e.PropertyName == nameof(_viewModel.SecondaryDiagramViewModel))
                    _secondaryDiagramWindow.DataContext = _viewModel.SecondaryDiagramViewModel;
            }
        }

        private void OnPrimaryBeamDiagramRequested(object sender, EventArgs e)
        {
            if (_primaryDiagramWindow == null || !_primaryDiagramWindow.IsLoaded)
            {
                _primaryDiagramWindow = new DiagramWindow
                {
                    WindowTitle = "Primary Beam Diagrams",
                    DataContext = _viewModel.PrimaryDiagramViewModel
                };

                // Keep it non-modal but owned by the main dashboard if we want it to stay on top,
                // or null owner if we want independent floating. Setting owner to this ensures it closes when dashboard closes.
                _primaryDiagramWindow.Owner = this;
                _primaryDiagramWindow.Show();
            }
            else
            {
                _primaryDiagramWindow.Activate();
            }
        }

        private void OnSecondaryBeamDiagramRequested(object sender, EventArgs e)
        {
            if (_secondaryDiagramWindow == null || !_secondaryDiagramWindow.IsLoaded)
            {
                _secondaryDiagramWindow = new DiagramWindow
                {
                    WindowTitle = "Secondary Beam Diagrams",
                    DataContext = _viewModel.SecondaryDiagramViewModel
                };

                _secondaryDiagramWindow.Owner = this;
                _secondaryDiagramWindow.Show();
            }
            else
            {
                _secondaryDiagramWindow.Activate();
            }
        }

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            string info = "--- DESIGN FORMULAS ---\n\n" +
                          "SUPPORT REACTIONS (R_max):\n" +
                          "For a/L <= 0.4534:\n" +
                          "R_max = q * (1.1 * L - 0.6 * (a^2 / L))\n\n" +
                          "For a/L > 0.4534:\n" +
                          "R_max = q * (a + 0.4 * L + 0.6 * (a^2 / L))\n\n" +
                          "PROP LOAD (P):\n" +
                          "P = R_max * P_m\n\n" +
                          "H20 SHEAR (V_max):\n" +
                          "V_max = 0.5 * R_max * P_m\n\n" +
                          "Where:\n" +
                          "q = Slab Load (kN/m2)\n" +
                          "L = Primary Beam Spacing (m)\n" +
                          "P_m = Prop Spacing (m)\n" +
                          "a = Secondary Beam Cantilever (m)";

            MessageBox.Show(info, "Formulas", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public FlexCalculatorViewModel ViewModel
        {
            get => _viewModel;
            set
            {
                // Unsubscribe from old viewmodel if exists
                if (_viewModel != null)
                {
                    _viewModel.PrimaryBeamDiagramRequested -= OnPrimaryBeamDiagramRequested;
                    _viewModel.SecondaryBeamDiagramRequested -= OnSecondaryBeamDiagramRequested;
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                }

                _viewModel = value;
                this.DataContext = _viewModel;

                if (_viewModel != null)
                {
                    _viewModel.PrimaryBeamDiagramRequested += OnPrimaryBeamDiagramRequested;
                    _viewModel.SecondaryBeamDiagramRequested += OnSecondaryBeamDiagramRequested;
                    _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                }
            }
        }

        private void BtnImportValues_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetValidCalculation(out FlexCalculatorViewModel vm))
            {
                MessageBox.Show("Correct the inputs before importing values.", "Invalid calculation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var app = EngineeringDashboard.Command.App;
            if (app?.ActiveUIDocument == null) return;
            var uiDocument = app.ActiveUIDocument;
            var doc = uiDocument.Document;
            List<FamilyInstance> matchingBeams;
            // Selection finishes before any transaction starts. Esc changes nothing.
            Hide();
            try
            {
                matchingBeams = uiDocument.Selection.PickElementsByRectangle(
                    new SlabHelperSelectionFilter(vm.SlabThickness),
                    "Drag a window around slab helper lines to apply this calculation. Esc cancels.")
                    .OfType<FamilyInstance>().GroupBy(instance => instance.Id).Select(group => group.First()).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return; }
            catch (Exception ex)
            {
                MessageBox.Show("Helper-line selection did not complete. No values were changed.\n" + ex.Message,"Selection failed",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            finally { Show(); Activate(); }
            if (matchingBeams.Count == 0)
            {
                MessageBox.Show("No slab helper lines matching this slab thickness were selected. No values were changed.", "Selection empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            int updated = 0, skipped = 0, failed = 0;
            var reasons = new List<string>();
            try
            {
                uiDocument.Selection.SetElementIds(matchingBeams.Select(instance => instance.Id).ToList());
                using (var transaction = new Transaction(doc, "Apply Slab Calculator Values"))
                {
                    transaction.Start();
                    foreach (var beam in matchingBeams)
                    {
                        Parameter[] parameters = {
                            beam.LookupParameter("Max Prop spacing") ?? beam.LookupParameter("Max Prop Spacing"),
                            beam.LookupParameter("Start Cantilever"), beam.LookupParameter("End Cantilever"),
                            beam.LookupParameter("Primary Beam Spacing") ?? beam.LookupParameter("Primary Beam spacing") ?? beam.LookupParameter("Max Beam Spacing") ?? beam.LookupParameter("Max Beam spacing") };
                        string[] names = { "Max Prop spacing", "Start Cantilever", "End Cantilever", "Primary Beam Spacing" };
                        var invalid = new List<string>();
                        for(int i=0;i<parameters.Length;i++)
                            if(!IsLength(parameters[i]) || parameters[i].IsReadOnly) invalid.Add(names[i]);
                        if(invalid.Count > 0)
                        {
                            skipped++; reasons.Add($"{beam.Id}: missing, read-only or non-length parameter: {string.Join(", ", invalid)}."); continue;
                        }
                        // All four values succeed together; a failed instance cannot be partly updated.
                        using(var change = new SubTransaction(doc))
                        {
                            change.Start();
                            try
                            {
                                if(!parameters[0].Set(vm.PropSpacing/304.8) || !parameters[1].Set(vm.PrimaryBeamCantilever/304.8)
                                    || !parameters[2].Set(vm.PrimaryBeamCantilever/304.8) || !parameters[3].Set(vm.SuggestedPrimaryBeamSpacing/304.8)) throw new InvalidOperationException("A parameter rejected its value.");
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
        private sealed class SlabHelperSelectionFilter : ISelectionFilter
        {
            private readonly double thicknessMm;
            public SlabHelperSelectionFilter(double thicknessMm) { this.thicknessMm = thicknessMm; }
            public bool AllowElement(Element element)
            {
                var instance = element as FamilyInstance;
                if(instance?.Symbol == null || instance.Symbol.FamilyName != "PrimaryBeam") return false;
                var kind = instance.LookupParameter("Structural Element Type");
                var thickness = instance.LookupParameter("H");
                return kind?.StorageType == StorageType.String && kind.AsString() == "Slab"
                    && IsLength(thickness) && Math.Abs(thickness.AsDouble()*304.8-thicknessMm) <= 0.1;
            }
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
        private static bool IsLength(Parameter p) => p != null && p.StorageType == StorageType.Double && p.Definition.GetDataType() == SpecTypeId.Length;
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
