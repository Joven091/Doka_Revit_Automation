using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorWindow
    {
        private BeamCalculatorViewModel observedModel;
        private bool refreshingValidation;
        internal TowerDiagramWindow TowerDiagrams { get; private set; }
        private void PickProps_Click(object sender, RoutedEventArgs e) => SelectSupportSystem(false);
        private void PickTowers_Click(object sender, RoutedEventArgs e) => SelectSupportSystem(true);
        private void SelectSupportSystem(bool towers)
        {
            if (observedModel == null) return;
            if (observedModel.UseTowers != towers) observedModel.UseTowers = towers;
            observedModel.HasSelectedSupportSystem = true;
            InputScroll.ScrollToTop();
            RefreshInputValidity(); UpdateDiagram();
        }
        private void ChangeSupportSystem_Click(object sender, RoutedEventArgs e)
        {
            CloseTowerDiagrams();
            if (observedModel != null) observedModel.HasSelectedSupportSystem = false;
        }
        private void CloseTowerDiagrams()
        {
            SupportDiagram.IsSelectionEnabled = false;
            TowerDiagrams?.Close(); TowerDiagrams = null;
            SupportDiagram.SelectMember(-1, 0, -1);
        }
        private void TowerDiagrams_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetValidCalculation(out var vm) || !vm.UseTowers || vm.TowerResult == null) return;
            SupportDiagram.IsSelectionEnabled = true;
            DiagramCaption.Text = "Click an H20 pair in the plan to show its diagrams.";
            TowerPlanScroll.BringIntoView();
        }
        private void InitializeUi()
        {
            SupportDiagram.MemberSelected += (run, line, member) => {
                if (!SupportDiagram.IsSelectionEnabled || !TryGetValidCalculation(out var vm) || !vm.UseTowers) return;
                bool opening = TowerDiagrams == null;
                if (opening)
                {
                    TowerDiagrams = new TowerDiagramWindow(vm);
                    TowerDiagrams.Closed += (s, args) => {
                        TowerDiagrams = null; SupportDiagram.SelectMember(-1, 0, -1);
                    };
                    if (IsVisible) TowerDiagrams.Owner = this;
                }
                TowerDiagrams.SelectMember(run, line, -1);
                SupportDiagram.SelectMember(run, line, -1);
                if (opening) TowerDiagrams.Show();
            };
            AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(InputValidation_Error));
            DataContextChanged += (s,e) => {
                CloseTowerDiagrams(); if(observedModel != null) observedModel.PropertyChanged -= ModelChanged;
                observedModel = DataContext as BeamCalculatorViewModel;
                if(observedModel != null) observedModel.PropertyChanged += ModelChanged;
                RefreshInputValidity(); UpdateDiagram();
            };
            Closed += (s,e) => { CloseTowerDiagrams(); if(observedModel != null) observedModel.PropertyChanged -= ModelChanged; };
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(BeamCalculatorViewModel.IsCalculationValid)) { UpdateDiagram(); if (!observedModel.IsCalculationValid) CloseTowerDiagrams(); }
            if(e.PropertyName == nameof(BeamCalculatorViewModel.HasLeftSlab) || e.PropertyName == nameof(BeamCalculatorViewModel.HasRightSlab) || e.PropertyName == nameof(BeamCalculatorViewModel.UseCustomLoads) || e.PropertyName == nameof(BeamCalculatorViewModel.UseTowers))
                Dispatcher.BeginInvoke(new Action(RefreshInputValidity), DispatcherPriority.DataBind);
        }
        private void InputValidation_Error(object sender, ValidationErrorEventArgs e)
        {
            RefreshInputValidity();
            // Binding validation may finish clearing its error after the routed event.
            Dispatcher.BeginInvoke(new Action(RefreshInputValidity), DispatcherPriority.DataBind);
        }
        private static IEnumerable<TextBox> TextInputs(DependencyObject parent)
        {
            foreach(object item in LogicalTreeHelper.GetChildren(parent))
                if(item is DependencyObject child)
                {
                    if(child is TextBox box) yield return box;
                    foreach(var descendant in TextInputs(child)) yield return descendant;
                }
        }
        private void RefreshInputValidity()
        {
            if(refreshingValidation || observedModel == null) return;
            refreshingValidation = true;
            try { observedModel.SetTextInputErrors(TextInputs(this).Any(t => t.IsEnabled && Validation.GetHasError(t))); }
            finally { refreshingValidation = false; }
        }
        // The import handler calls this again, independently of the button enabled state.
        internal bool TryGetValidCalculation(out BeamCalculatorViewModel vm)
        {
            vm = DataContext as BeamCalculatorViewModel;
            RefreshInputValidity();
            return vm != null && vm.HasSelectedSupportSystem && vm.IsCalculationValid && !TextInputs(this).Any(t => t.IsEnabled && Validation.GetHasError(t));
        }
        private void UpdateDiagram()
        {
            SupportDiagram.SetModel(observedModel);
            TowerPlanScroll.ScrollToLeftEnd();
            DiagramCaption.Text = observedModel == null || !observedModel.IsCalculationValid ? "Enter valid dimensions to show the support arrangement."
                : observedModel.UseTowers ? $"Plan view · {observedModel.BeamLengthMm:0.###} mm beam · {observedModel.TowerResult.LegCount} legs\nP labels: H20 stock pieces · dotted: free member breaks\nSolid: frames · dashed: cross sides · gaps measured between leg planes"
                : $"Beam {observedModel.Width:0.##} × {observedModel.Depth:0.##} mm · {observedModel.NumberOfProps} props per primary line\nPrimary lines {observedModel.PrimaryLineSpacingMm:0.##} mm apart";
        }
        private async void OptimizeTowers_Click(object sender, RoutedEventArgs e)
        {
            RefreshInputValidity();
            if (observedModel != null) await observedModel.OptimizeTowersAsync();
        }
        private const string FileInputs = "Depth,Width,FloorHeight,HasLeftSlab,HasRightSlab,LeftSlabThickness,RightSlabThickness,SlabStripWidthMm,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad,NumberOfProps";
        internal CalculationRecord CaptureCalculation()
        {
            if(!TryGetValidCalculation(out var vm)) throw new ArgumentException("Correct the inputs before saving or exporting.");
            var record = CalculationFiles.Capture("Beam", vm, FileInputs);
            record.Inputs.Add(CalculationFiles.Field("UseTowers", vm.UseTowers));
            record.Inputs.Add(CalculationFiles.Field("BeamLengthMm", vm.BeamLengthMm));
            record.Inputs.Add(CalculationFiles.Field("TowerLegCapacityKn", vm.TowerLegCapacityKn));
            record.Inputs.Add(CalculationFiles.Field("StockLengthsText", vm.StockLengthsText));
            record.Inputs.Add(CalculationFiles.Field("StockEndSetbackMm", vm.StockEndSetbackMm));
            record.Inputs.Add(CalculationFiles.Field("MaximumConcreteEndOverhangMm", vm.MaximumConcreteEndOverhangMm));
            record.Inputs.Add(CalculationFiles.Field("Exclude600Standalone", vm.Exclude600Standalone));
            record.Inputs.Add(CalculationFiles.Field("PropConfiguration", vm.SelectedProp?.CapacityKey ?? BeamPropOption.All[0].CapacityKey));
            record.Inputs.Add(CalculationFiles.Field("DeflectionLimitMm", vm.DeflectionLimit.Millimetres));
            return record;
        }
        internal void RestoreCalculation(CalculationRecord record)
        {
            var candidate = new BeamCalculatorViewModel();
            CalculationFiles.Apply(record,"Beam",candidate,FileInputs);
            candidate.SelectedProp = BeamPropOption.All.SingleOrDefault(p=>p.CapacityKey==record.Inputs.SingleOrDefault(f=>f.Name=="PropConfiguration")?.Value);
            string limit = record.Inputs.SingleOrDefault(f=>f.Name=="DeflectionLimitMm")?.Value;
            candidate.DeflectionLimit = candidate.DeflectionOptions.SingleOrDefault(d=>d.Millimetres.ToString(System.Globalization.CultureInfo.InvariantCulture)==limit);
            RestoreTowerFields(record, candidate);
            if(candidate.UseTowers) candidate.ValidateTowerInputs();
            else if(!candidate.IsCalculationValid) throw new ArgumentException("The loaded calculation is invalid: " + candidate.WarningMessage);
            // Validate a separate model first. Preserve the launcher's retained model instance.
            var target=(BeamCalculatorViewModel)DataContext;
            CalculationFiles.Apply(record,"Beam",target,FileInputs);
            target.SelectedProp=candidate.SelectedProp; target.DeflectionLimit=candidate.DeflectionLimit;
            target.UseTowers = candidate.UseTowers; target.BeamLengthMm = candidate.BeamLengthMm; target.TowerLegCapacityKn = candidate.TowerLegCapacityKn;
            target.Exclude600Standalone = candidate.Exclude600Standalone; target.MaximumConcreteEndOverhangMm = candidate.MaximumConcreteEndOverhangMm; target.StockLengthsText = candidate.StockLengthsText; target.StockEndSetbackMm = candidate.StockEndSetbackMm;
            foreach(var box in TextInputs(this)) box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            target.HasSelectedSupportSystem = true;
            RefreshInputValidity();
        }
        private static void RestoreTowerFields(CalculationRecord record, BeamCalculatorViewModel vm)
        {
            vm.Exclude600Standalone = false;
            if (record.Inputs.Any(f => f.Name == "Exclude600Standalone")) CalculationFiles.Apply(record, "Beam", vm, "Exclude600Standalone");
            vm.MaximumConcreteEndOverhangMm = 0;
            if (record.Inputs.Any(f => f.Name == "MaximumConcreteEndOverhangMm")) CalculationFiles.Apply(record, "Beam", vm, "MaximumConcreteEndOverhangMm");
            // Old version-1 Beam records have none of these fields and retain prop mode.
            var fields = new[] { "UseTowers", "BeamLengthMm", "TowerLegCapacityKn" };
            if (record.Inputs.Any(f => fields.Contains(f.Name))) CalculationFiles.Apply(record, "Beam", vm, string.Join(",", fields));
            var stockFields = new[] { "StockLengthsText", "StockEndSetbackMm" };
            if (record.Inputs.Any(f => stockFields.Contains(f.Name))) CalculationFiles.Apply(record, "Beam", vm, string.Join(",", stockFields));
        }
        private void SaveCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.Save(this,CaptureCalculation(),"Beam"));
        private void LoadCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=> { var path=CalculationFiles.ChooseLoad(this); if(path!=null) RestoreCalculation(CalculationFiles.Read<CalculationRecord>(path)); });
        private void ExportReport_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.ExportHtml(this,"Beam",BuildReport()));
        internal string BuildReport()
        {
            var record=CaptureCalculation(); var vm=(BeamCalculatorViewModel)DataContext;
            if (vm.UseTowers)
            {
                var tower = vm.TowerResult;
                var fields = new[] { CalculationFiles.Field("Layout", tower.Summary), CalculationFiles.Field("GoverningCheck", tower.Governing), CalculationFiles.Field("ConcreteEndOverhangMm", tower.ConcreteEndOverhangMm), CalculationFiles.Field("CurrentLegLoad", tower.LegLoadSummary), CalculationFiles.Field("OptimizationBasis", tower.CapacityBasis),
                    CalculationFiles.Field("LegUtilizationPercent", tower.MaxLegKn / vm.TowerLegCapacityKn * 100), CalculationFiles.Field("SupportSchedule", tower.Schedule),
                    CalculationFiles.Field("PrimaryStockSchedule", tower.StockSchedule), CalculationFiles.Field("TotalPrimaryH20Count", tower.StockPlan.TotalH20Count),
                    CalculationFiles.Field("SearchStatus", vm.TowerSearchNote), CalculationFiles.Field("TotalLineLoadKnPerM", vm.TotalLineLoad),
                    CalculationFiles.Field("LeftLineLoadKnPerM", vm.LeftLineLoad), CalculationFiles.Field("RightLineLoadKnPerM", vm.RightLineLoad) };
                return CalculationFiles.Report("Beam — shoring towers", record.Inputs, fields, BeamCalculatorViewModel.TowerAssumptions);
            }
            var results=CalculationFiles.Capture("Beam",vm,"MaximumPropSpacing,PrimaryBeamCantilever,MaximumPropLoad,PropCapacityKn,PropUtilizationPercent,PropExtensionMm,PropLookupExtensionMm,PrimaryLineSpacingMm,LeftLineLoad,RightLineLoad,TotalLineLoad,LimitingFactor,IsCalculationValid");
            return CalculationFiles.Report("Beam",record.Inputs,results.Inputs,
                "Geometry and spacing: mm. Area loads: kN/m². Line loads: kN/m. Prop loads/capacity: kN. Utilization: %. Deflection limit: mm.\n"+
                "Each checked side uses the entered slab strip width. Primary lines = (beam width + 1400)/2. Line reactions use force and moment equilibrium; the heavier line governs.\n"+
                "Concrete 25 kN/mÂ³; default heaping min(1.75,max(0.75,10% of concrete weight)), working 0.75 and formwork 0.40 kN/m². Custom live replaces placing + working load.\n"+
                "Equal spans and full uniform loading. H20 moment 5 kNm, shear 11 kN, EI 449.8756 kNm². Spacing rounds down to 25 mm and checks selected prop capacity; extension = floor height - beam depth - 418 mm.\n"+
                "2/3/4+ support coefficient model; primary span limits 2000/3000 mm. Sheeting, secondary beams, joints, restraint and local bearing require separate checks. Results recalculated from inputs; this is not a complete formwork verification.");
        }
    }
}
