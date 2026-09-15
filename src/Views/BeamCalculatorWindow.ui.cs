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
        private void InitializeUi()
        {
            AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(InputValidation_Error));
            DataContextChanged += (s,e) => {
                if(observedModel != null) observedModel.PropertyChanged -= ModelChanged;
                observedModel = DataContext as BeamCalculatorViewModel;
                if(observedModel != null) observedModel.PropertyChanged += ModelChanged;
                RefreshInputValidity(); UpdateDiagram();
            };
            Closed += (s,e) => { if(observedModel != null) observedModel.PropertyChanged -= ModelChanged; };
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(BeamCalculatorViewModel.IsCalculationValid)) UpdateDiagram();
            if(e.PropertyName == nameof(BeamCalculatorViewModel.HasLeftSlab) || e.PropertyName == nameof(BeamCalculatorViewModel.HasRightSlab) || e.PropertyName == nameof(BeamCalculatorViewModel.UseCustomLoads))
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
            return vm != null && vm.IsCalculationValid && !TextInputs(this).Any(t => t.IsEnabled && Validation.GetHasError(t));
        }
        private void UpdateDiagram()
        {
            SupportDiagram.SetModel(observedModel);
            DiagramCaption.Text = observedModel == null || !observedModel.IsCalculationValid ? "Enter valid dimensions to show the support arrangement."
                : $"Beam {observedModel.Width:0.##} × {observedModel.Depth:0.##} mm · {observedModel.NumberOfProps} props per primary line\nPrimary lines {observedModel.PrimaryLineSpacingMm:0.##} mm apart";
        }
        private const string FileInputs = "Depth,Width,FloorHeight,HasLeftSlab,HasRightSlab,LeftSlabThickness,RightSlabThickness,SlabStripWidthMm,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad,NumberOfProps";
        internal CalculationRecord CaptureCalculation()
        {
            if(!TryGetValidCalculation(out var vm)) throw new ArgumentException("Correct the inputs before saving or exporting.");
            var record = CalculationFiles.Capture("Beam", vm, FileInputs);
            record.Inputs.Add(CalculationFiles.Field("PropConfiguration", vm.SelectedProp.CapacityKey));
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
            if(!candidate.IsCalculationValid) throw new ArgumentException("The loaded calculation is invalid: " + candidate.WarningMessage);
            // Validate a separate model first. Preserve the launcher's retained model instance.
            var target=(BeamCalculatorViewModel)DataContext;
            CalculationFiles.Apply(record,"Beam",target,FileInputs);
            target.SelectedProp=candidate.SelectedProp; target.DeflectionLimit=candidate.DeflectionLimit;
            foreach(var box in TextInputs(this)) box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            RefreshInputValidity();
        }
        private void SaveCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.Save(this,CaptureCalculation(),"Beam"));
        private void LoadCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=> { var path=CalculationFiles.ChooseLoad(this); if(path!=null) RestoreCalculation(CalculationFiles.Read<CalculationRecord>(path)); });
        private void ExportReport_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.ExportHtml(this,"Beam",BuildReport()));
        internal string BuildReport()
        {
            var record=CaptureCalculation(); var vm=(BeamCalculatorViewModel)DataContext;
            var results=CalculationFiles.Capture("Beam",vm,"MaximumPropSpacing,PrimaryBeamCantilever,MaximumPropLoad,PropCapacityKn,PropUtilizationPercent,PropExtensionMm,PropLookupExtensionMm,PrimaryLineSpacingMm,LeftLineLoad,RightLineLoad,TotalLineLoad,LimitingFactor,IsCalculationValid");
            return CalculationFiles.Report("Beam",record.Inputs,results.Inputs,
                "Geometry and spacing: mm. Area loads: kN/m². Line loads: kN/m. Prop loads/capacity: kN. Utilization: %. Deflection limit: mm.\n"+
                "Each checked side uses the entered slab strip width. Primary lines = (beam width + 1400)/2. Line reactions use force and moment equilibrium; the heavier line governs.\n"+
                "Concrete 25 kN/m³; default heaping min(1.75,max(0.75,10% of concrete weight)), working 0.75 and formwork 0.40 kN/m². Custom live replaces placing + working load.\n"+
                "Equal spans and full uniform loading. H20 moment 5 kNm, shear 11 kN, EI 449.8756 kNm². Spacing rounds down to 25 mm and checks selected prop capacity; extension = floor height - beam depth - 418 mm.\n"+
                "2/3/4+ support coefficient model; primary span limits 2000/3000 mm. Sheeting, secondary beams, joints, restraint and local bearing require separate checks. Results recalculated from inputs; this is not a complete formwork verification.");
        }
    }
}
