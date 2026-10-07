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
        private int diagramBeam=-1;
        private void AddBeamBay_Click(object sender,RoutedEventArgs e) {if(observedModel==null)return;observedModel.UseBeamNetwork=true;observedModel.AddNetworkBay();}
        private void AddCrossingBeam_Click(object sender,RoutedEventArgs e)=>observedModel?.AddCrossingBeam();
        private void RemoveCrossingBeam_Click(object sender,RoutedEventArgs e)=>observedModel?.RemoveCrossingBeam((sender as Button)?.Tag as TransferCrossingBeamRow);
        private void RemoveBeamBay_Click(object sender,RoutedEventArgs e)=>observedModel?.RemoveNetworkBay();
        internal TowerDiagramWindow TowerDiagrams { get; private set; }
        internal BeamFormworkWorkspace FormworkWorkspace {get;private set;}
        internal BeamDiagnosticWindow Diagnostics {get;private set;}
        private void DiagnoseBeam_Click(object sender,RoutedEventArgs e)
        {
            if(observedModel==null)return;
            if(Diagnostics!=null){Diagnostics.RefreshReport();Diagnostics.Activate();return;}
            Diagnostics=new BeamDiagnosticWindow(remarks=>observedModel.BuildDiagnosticReport(remarks,"Source: Beam calculator\n"+string.Join("\n",TextInputs(this).Select(box=>(box.Name.Length>0 ? box.Name : box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path??"Field")+" = "+box.Text+"; enabled="+box.IsEnabled+"; visible="+box.IsVisible))));
            Diagnostics.Closed+=(s,args)=>Diagnostics=null;if(IsVisible)Diagnostics.Owner=this;Diagnostics.Show();
        }
        private void DrawingWorkspace_Click(object sender,RoutedEventArgs e)
        {
            if(observedModel==null || !observedModel.UseTowers)return;
            if(FormworkWorkspace!=null){FormworkWorkspace.Activate();return;}
            CloseTowerDiagrams();
            FormworkWorkspace=new BeamFormworkWorkspace(observedModel,
                ()=>CalculationFiles.Run(FormworkWorkspace,()=>CalculationFiles.Save(FormworkWorkspace,CaptureDrawingInputs(),"Beam")),
                ()=>CalculationFiles.Run(FormworkWorkspace,()=> {var path=CalculationFiles.ChooseLoad(FormworkWorkspace);if(path!=null)RestoreCalculation(CalculationFiles.Read<CalculationRecord>(path));}),
                ()=>CalculationFiles.Run(FormworkWorkspace,()=>CalculationFiles.ExportHtml(FormworkWorkspace,"Beam",BuildReport())),
                ()=> {CloseTowerDiagrams();Diagnostics?.Close();foreach(var box in TextInputs(this))box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();RefreshInputValidity();});
            FormworkWorkspace.Closed+=(s,args)=>FormworkWorkspace=null;
            if(IsVisible)FormworkWorkspace.Owner=this;
            FormworkWorkspace.Show();
        }
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
            NetworkPlan.IsSelectionEnabled=false;diagramBeam=-1;
            TowerDiagrams?.Close(); TowerDiagrams = null;
            SupportDiagram.SelectMember(-1, 0, -1);
        }
        private void TowerDiagrams_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetValidCalculation(out var vm) || !vm.UseTowers || vm.TowerResult == null) return;
            SupportDiagram.IsSelectionEnabled = true;
            NetworkPlan.IsSelectionEnabled=vm.IsNetworkMode;
            DiagramCaption.Text = "Click a primary pair in plan, or a single secondary H20 in section.";
            if(vm.IsNetworkMode)NetworkPlanScroll.BringIntoView();else TowerPlanScroll.BringIntoView();
        }
        private void InitializeUi()
        {
            NetworkPlan.SeparateSlabSelected+=bay=> {SeparateSlabNotice.ShowBay(bay);SeparateSlabNotice.BringIntoView();};
            NetworkPlan.MemberSelected+=(beam,run,line)=>
            {
                if(!NetworkPlan.IsSelectionEnabled || !TryGetValidCalculation(out var vm) || vm.NetworkResult==null)return;
                if(diagramBeam!=beam) {TowerDiagrams?.Close();TowerDiagrams=null;}
                vm.SelectNetworkBeam(beam);diagramBeam=beam;NetworkPlan.SelectMember(beam,run,line);
                bool opening=TowerDiagrams==null;
                if(opening) {TowerDiagrams=beam<0 ? new TowerDiagramWindow(vm,vm.NetworkResult.CrossSupports.Single(c=>c.CrossingIndex==-beam-1)) : new TowerDiagramWindow(vm);TowerDiagrams.Closed+=(s,e)=> {TowerDiagrams=null;diagramBeam=-1;NetworkPlan.SelectMember(-1,-1,0);};if(IsVisible)TowerDiagrams.Owner=this;}
                TowerDiagrams.SelectMember(run,line,-1);if(opening)TowerDiagrams.Show();
            };
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
                FormworkWorkspace?.Close();
                CloseTowerDiagrams(); if(observedModel != null) observedModel.PropertyChanged -= ModelChanged;
                observedModel = DataContext as BeamCalculatorViewModel;
                if(observedModel != null) observedModel.PropertyChanged += ModelChanged;
                RefreshInputValidity(); UpdateDiagram();
            };
            Closed += (s,e) => { FormworkWorkspace?.Close();CloseTowerDiagrams(); if(observedModel != null) observedModel.PropertyChanged -= ModelChanged; };
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName==nameof(BeamCalculatorViewModel.HasTowerFailure) && observedModel?.HasTowerFailure==true)
                Dispatcher.BeginInvoke(new Action(()=> {if(observedModel?.HasTowerFailure==true)ReviewScroll.ScrollToTop();}),DispatcherPriority.Loaded);
            if(e.PropertyName == nameof(BeamCalculatorViewModel.IsCalculationValid)) { UpdateDiagram(); if (!observedModel.IsCalculationValid) CloseTowerDiagrams(); }
            if(e.PropertyName == nameof(BeamCalculatorViewModel.HasLeftSlab) || e.PropertyName == nameof(BeamCalculatorViewModel.HasRightSlab) || e.PropertyName == nameof(BeamCalculatorViewModel.UseCustomLoads) || e.PropertyName == nameof(BeamCalculatorViewModel.UseTowers) || e.PropertyName == nameof(BeamCalculatorViewModel.TransferSlabLoads) || e.PropertyName == nameof(BeamCalculatorViewModel.UseBeamNetwork))
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
            var visited=new HashSet<DependencyObject>();var found=new List<TextBox>();Action<DependencyObject> visit=null;
            visit=node=> {if(node==null || !visited.Add(node))return;if(node is TextBox box)found.Add(box);
                foreach(object item in LogicalTreeHelper.GetChildren(node))if(item is DependencyObject child)visit(child);
                if(node is System.Windows.Media.Visual || node is System.Windows.Media.Media3D.Visual3D)for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)visit(System.Windows.Media.VisualTreeHelper.GetChild(node,i));};
            visit(parent);return found;
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
            SeparateSlabNotice.SetResult(observedModel?.IsCalculationValid==true ? observedModel.NetworkResult : null);
            SupportDiagram.SetModel(observedModel);
            NetworkPlan.SetResult(observedModel?.NetworkResult);
            TowerPlanScroll.ScrollToLeftEnd();
            DiagramCaption.Text = observedModel == null || !observedModel.IsCalculationValid ? "Enter valid dimensions to show the support arrangement."
                : observedModel.UseTowers ? $"Plan view · {observedModel.BeamLengthMm:0.###} mm beam · {observedModel.TowerResult.LegCount} legs\nP labels: H20 stock pieces · dotted: free member breaks\nSolid: frames · dashed: cross sides · gaps measured between leg planes"
                : $"Beam {observedModel.Width:0.##} × {observedModel.Depth:0.##} mm · {observedModel.NumberOfProps} props per primary line\nPrimary lines {observedModel.PrimaryLineSpacingMm:0.##} mm apart";
        }
        private void CancelBeamAnalysis_Click(object sender,RoutedEventArgs e)=>observedModel?.CancelOptimization();
        private async void OptimizeTowers_Click(object sender, RoutedEventArgs e)
        {
            RefreshInputValidity();
            if (observedModel != null) await observedModel.OptimizeTowersAsync();
        }
        private const string FileInputs = "Depth,Width,FloorHeight,HasLeftSlab,HasRightSlab,LeftSlabThickness,RightSlabThickness,SlabStripWidthMm,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad,NumberOfProps";
        internal CalculationRecord CaptureCalculation()=>CaptureInputs(true);
        internal CalculationRecord CaptureDrawingInputs()=>CaptureInputs(false);
        private CalculationRecord CaptureInputs(bool requireResult)
        {
            var vm=DataContext as BeamCalculatorViewModel;
            if(requireResult){if(!TryGetValidCalculation(out vm))throw new ArgumentException("Correct the inputs before saving or exporting.");}
            else {RefreshInputValidity();if(vm==null || !vm.HasSelectedSupportSystem || !vm.UseTowers)throw new ArgumentException("Select D3 before saving drawing inputs.");vm.ValidateTowerInputs();}
            var record = CalculationFiles.Capture("Beam", vm, FileInputs);
            foreach (var field in CalculationFiles.Capture("Beam", vm, "TransferSlabLoads,UpperSupportSpacingMm,LeftSlabBayMm,RightSlabBayMm").Inputs) record.Inputs.Add(field);
            record.Inputs.Add(CalculationFiles.Field("UseTowers", vm.UseTowers));
            record.Inputs.Add(CalculationFiles.Field("UseBeamNetwork",vm.UseBeamNetwork));
            if(vm.NetworkBeams.Count>=2)record.Inputs.Add(CalculationFiles.Field("NetworkConfigurationXml",vm.NetworkConfigurationXml));
            record.Inputs.Add(CalculationFiles.Field("BeamLengthMm", vm.BeamLengthMm));
            record.Inputs.Add(CalculationFiles.Field("TowerLegCapacityKn", vm.TowerLegCapacityKn));
            record.Inputs.Add(CalculationFiles.Field("StockLengthsText", vm.StockLengthsText));
            record.Inputs.Add(CalculationFiles.Field("StockEndSetbackMm", vm.StockEndSetbackMm));
            record.Inputs.Add(CalculationFiles.Field("MaximumConcreteEndOverhangMm", vm.MaximumConcreteEndOverhangMm));
            record.Inputs.Add(CalculationFiles.Field("Exclude600Standalone", vm.Exclude600Standalone));
            record.Inputs.Add(CalculationFiles.Field("AllowCustomH20Lengths", vm.AllowCustomH20Lengths));
            record.Inputs.Add(CalculationFiles.Field("IgnoreUplift", vm.IgnoreUplift));
            record.Inputs.Add(CalculationFiles.Field("PropConfiguration", vm.SelectedProp?.CapacityKey ?? BeamPropOption.All[0].CapacityKey));
            record.Inputs.Add(CalculationFiles.Field("DeflectionCriterion", vm.DeflectionLimit.Label));
            if (vm.DeflectionLimit.Denominator == 0) record.Inputs.Add(CalculationFiles.Field("DeflectionLimitMm", vm.DeflectionLimit.Millimetres));
            return record;
        }
        internal void RestoreCalculation(CalculationRecord record)
        {
            var candidate = new BeamCalculatorViewModel();
            CalculationFiles.Apply(record,"Beam",candidate,FileInputs);
            candidate.SelectedProp = BeamPropOption.All.SingleOrDefault(p=>p.CapacityKey==record.Inputs.SingleOrDefault(f=>f.Name=="PropConfiguration")?.Value);
            string limit = record.Inputs.SingleOrDefault(f=>f.Name=="DeflectionLimitMm")?.Value;
            candidate.DeflectionLimit = BeamCalculatorViewModel.RestoreDeflectionOption(record.Inputs.SingleOrDefault(f=>f.Name=="DeflectionCriterion")?.Value, limit);
            RestoreTowerFields(record, candidate);
            if(candidate.UseTowers) candidate.ValidateTowerInputs();
            else if(!candidate.IsCalculationValid) throw new ArgumentException("The loaded calculation is invalid: " + candidate.WarningMessage);
            // Validate a separate model first. Preserve the launcher's retained model instance.
            var target=(BeamCalculatorViewModel)DataContext;
            CalculationFiles.Apply(record,"Beam",target,FileInputs);
            target.SelectedProp=candidate.SelectedProp; target.DeflectionLimit=candidate.DeflectionLimit;
            target.TransferSlabLoads=candidate.TransferSlabLoads; target.UpperSupportSpacingMm=candidate.UpperSupportSpacingMm; target.LeftSlabBayMm=candidate.LeftSlabBayMm; target.RightSlabBayMm=candidate.RightSlabBayMm;
            target.UseBeamNetwork=candidate.UseBeamNetwork;if(candidate.NetworkBeams.Count>=2)target.NetworkConfigurationXml=candidate.NetworkConfigurationXml;
            target.UseTowers = candidate.UseTowers; target.BeamLengthMm = candidate.BeamLengthMm; target.TowerLegCapacityKn = candidate.TowerLegCapacityKn;
            target.AllowCustomH20Lengths = candidate.AllowCustomH20Lengths;
            target.IgnoreUplift = candidate.IgnoreUplift;
            target.Exclude600Standalone = candidate.Exclude600Standalone; target.MaximumConcreteEndOverhangMm = candidate.MaximumConcreteEndOverhangMm; target.StockLengthsText = candidate.StockLengthsText; target.StockEndSetbackMm = candidate.StockEndSetbackMm;
            foreach(var box in TextInputs(this)) box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            target.HasSelectedSupportSystem = true;
            RefreshInputValidity();
        }
        private static void RestoreTowerFields(CalculationRecord record, BeamCalculatorViewModel vm)
        {
            foreach (string field in new[] { "TransferSlabLoads", "UpperSupportSpacingMm", "LeftSlabBayMm", "RightSlabBayMm" })
                if (record.Inputs.Any(f=>f.Name==field)) CalculationFiles.Apply(record,"Beam",vm,field);
            foreach(string field in new[] {"UseBeamNetwork","NetworkConfigurationXml"})if(record.Inputs.Any(f=>f.Name==field))CalculationFiles.Apply(record,"Beam",vm,field);
            vm.IgnoreUplift = false;
            if (record.Inputs.Any(f => f.Name == "IgnoreUplift")) CalculationFiles.Apply(record, "Beam", vm, "IgnoreUplift");
            vm.AllowCustomH20Lengths = false;
            if (record.Inputs.Any(f => f.Name == "AllowCustomH20Lengths")) CalculationFiles.Apply(record, "Beam", vm, "AllowCustomH20Lengths");
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
                var fields = new List<CalculationField> { CalculationFiles.Field("Layout", tower.Summary), CalculationFiles.Field("GoverningCheck", tower.Governing), CalculationFiles.Field("ConcreteEndOverhangMm", tower.ConcreteEndOverhangMm), CalculationFiles.Field("CurrentLegLoad", tower.LegLoadSummary), CalculationFiles.Field("OptimizationBasis", tower.CapacityBasis), CalculationFiles.Field("UpliftAcceptance", tower.UpliftSummary),
                    CalculationFiles.Field("LegUtilizationPercent", tower.MaxLegKn / vm.TowerLegCapacityKn * 100), CalculationFiles.Field("SupportSchedule", tower.Schedule),
                    CalculationFiles.Field("PrimaryStockSchedule", tower.StockSchedule), CalculationFiles.Field("TotalPrimaryH20Count", tower.StockPlan.TotalH20Count),
                    CalculationFiles.Field("SearchStatus", vm.TowerSearchNote), CalculationFiles.Field("TotalLineLoadKnPerM", vm.TotalLineLoad),
                    CalculationFiles.Field("LeftLineLoadKnPerM", vm.LeftLineLoad), CalculationFiles.Field("RightLineLoadKnPerM", vm.RightLineLoad) };
                if(tower.Transfer != null) {fields.Add(CalculationFiles.Field("TransferredSlabSummary",tower.Transfer.Summary));fields.Add(CalculationFiles.Field("EstimatedSlabSoffitMovementMm",tower.Transfer.SlabMovementMm));fields.Add(CalculationFiles.Field("ShutterLoadKn",tower.Transfer.ShutterLoadKn));}
                if(vm.NetworkResult!=null) {fields.Add(CalculationFiles.Field("NetworkSummary",vm.NetworkResult.Summary));fields.Add(CalculationFiles.Field("NetworkSignedReactions",string.Join("\n",vm.NetworkResult.Beams.Select((b,i)=>$"B{i+1}: {b.UpliftSummary}"))));fields.Add(CalculationFiles.Field("AllBeamSchedules",vm.NetworkResult.Schedule));fields.Add(CalculationFiles.Field("TotalNetworkH20LengthMm",vm.NetworkResult.TimberMm));fields.Add(CalculationFiles.Field("TotalNetworkSlabLoadKn",vm.NetworkResult.SlabLoadKn));if(vm.NetworkResult.RequiresSeparateSlabFormwork)fields.Add(CalculationFiles.Field("FullTransferFailureDetails",vm.NetworkResult.FullTransferFailureDetails));}
                return CalculationFiles.Report("Beam — shoring towers", record.Inputs, fields, vm.TowerCalculationBasis);
            }
            var results=CalculationFiles.Capture("Beam",vm,"MaximumPropSpacing,PrimaryBeamCantilever,MaximumPropLoad,PropCapacityKn,PrimaryDeflectionMm,PrimaryDeflectionUtilization,PropUtilizationPercent,PropExtensionMm,PropLookupExtensionMm,PrimaryLineSpacingMm,LeftLineLoad,RightLineLoad,TotalLineLoad,LimitingFactor,IsCalculationValid");
            return CalculationFiles.Report("Beam",record.Inputs,results.Inputs,
                "Geometry and spacing: mm. Area loads: kN/m². Line loads: kN/m. Prop loads/capacity: kN. Utilization: %. Deflection criterion: each H20 support span or overhang length divided by the selected denominator; legacy limits use mm.\n"+
                "Each checked side uses the entered slab strip width. Primary lines = (beam width + 1400)/2. Line reactions use force and moment equilibrium; the heavier line governs.\n"+
                "Concrete 25 kN/mÂ³; default heaping min(1.75,max(0.75,10% of concrete weight)), working 0.75 and formwork 0.40 kN/m². Custom live replaces placing + working load.\n"+
                "Equal spans and full uniform loading. Deflection includes support rotation at overhangs. The 4+ prop selection checks four through twelve supports for deflection.  H20 moment 5 kNm, shear 11 kN, EI 449.8756 kNm². Spacing rounds down to 25 mm and checks selected prop capacity; extension = floor height - beam depth - 418 mm.\n"+
                "2/3/4+ support coefficient model; primary span limits 2000/3000 mm. Sheeting, secondary beams, joints, restraint and local bearing require separate checks. Results recalculated from inputs; this is not a complete formwork verification.");
        }
    }
}


