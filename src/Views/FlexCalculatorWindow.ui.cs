using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EngineeringDashboard
{
    public partial class FlexCalculatorWindow
    {
        public bool ChangeSystemRequested { get; private set; }
        private void ChangeSystem_Click(object sender, RoutedEventArgs e) { ChangeSystemRequested = true; Close(); }
        private FlexCalculatorViewModel observedModel;
        private bool refreshingValidation;
        private void InitializeUi()
        {
            AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(InputValidation_Error));
            DataContextChanged += (s,e) => {
                if(observedModel != null) observedModel.PropertyChanged -= ModelChanged;
                observedModel = DataContext as FlexCalculatorViewModel;
                if(observedModel != null) observedModel.PropertyChanged += ModelChanged;
                RefreshInputValidity(); UpdateSupportDiagram();
            };
            Closed += (s,e) => { if(observedModel != null) observedModel.PropertyChanged -= ModelChanged; };
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(FlexCalculatorViewModel.IsCalculationValid)) UpdateSupportDiagram();
            if(e.PropertyName == nameof(FlexCalculatorViewModel.UseCustomLoads))
                Dispatcher.BeginInvoke(new Action(RefreshInputValidity), DispatcherPriority.DataBind);
        }
        private void UpdateSupportDiagram()
        {
            SupportDiagram.SetModel(observedModel);
            if (observedModel == null || !observedModel.IsCalculationValid)
            {
                DiagramCaption.Text = "Enter valid inputs to show the slab and support arrangement.";
                return;
            }
            bool envelope = observedModel.PrimaryBeamCount == "5+" || observedModel.PropCount == "5+";
            DiagramCaption.Text = $"{observedModel.SlabThickness:0.##} mm slab · primary spacing {observedModel.SuggestedPrimaryBeamSpacing:0} mm\n" +
                $"Prop spacing {observedModel.PropSpacing:0.##} mm · secondary spacing {observedModel.SecondaryBeamSpacing:0.##} mm\n" +
                (envelope ? "5+ shown with five supports. " : "") + "Secondary members span up to three supports.\n" +
                "Side-by-side overlaps and quantities are illustrative; calculations retain the selected continuous support count.";
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
        internal bool TryGetValidCalculation(out FlexCalculatorViewModel vm)
        {
            vm = DataContext as FlexCalculatorViewModel;
            RefreshInputValidity();
            return vm != null && vm.IsCalculationValid && !TextInputs(this).Any(t => t.IsEnabled && Validation.GetHasError(t));
        }
        private const string FileInputs = "SlabThickness,FloorHeight,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad,PrimaryBeamCount,SecondaryBeamCantilever,DeflectionLimit,PropCount,PropSpacing,PrimaryBeamCantilever,PropModel,PropType,PropOrientation";
        internal CalculationRecord CaptureCalculation()
        {
            if(!TryGetValidCalculation(out var vm)) throw new ArgumentException("Correct the inputs before saving or exporting.");
            var record=CalculationFiles.Capture("Slab",vm,FileInputs);
            record.Inputs.Add(CalculationFiles.Field("PlywoodProfileId",vm.PlywoodProfileId));
            record.Inputs.Add(CalculationFiles.Field("PlywoodDeflectionLimit",vm.PlywoodDeflectionLimit));
            record.Inputs.Add(CalculationFiles.Field("SecondaryBeamSpacing",vm.SecondaryBeamSpacing));
            return record;
        }
        internal void RestoreCalculation(CalculationRecord record)
        {
            var candidate=new FlexCalculatorViewModel(); CalculationFiles.Apply(record,"Slab",candidate,FileInputs);
            candidate.PlywoodProfileId=record.Inputs.SingleOrDefault(f=>f.Name=="PlywoodProfileId")?.Value ?? PlywoodCatalog.Default.Id;
            candidate.PlywoodDeflectionLimit=PlywoodSpacing.RestoreDeflectionLimit(record.Inputs.SingleOrDefault(f=>f.Name=="PlywoodDeflectionLimit")?.Value,candidate.DeflectionLimit);
            if(!candidate.IsCalculationValid) throw new ArgumentException("The loaded calculation is invalid: "+candidate.WarningMessage);
            var target=(FlexCalculatorViewModel)DataContext;
            CalculationFiles.Apply(record,"Slab",target,FileInputs);
            target.SelectedPlywood=candidate.SelectedPlywood; target.PlywoodDeflectionLimit=candidate.PlywoodDeflectionLimit;
            foreach(var box in TextInputs(this)) box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            RefreshInputValidity();
        }
        private void SaveCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.Save(this,CaptureCalculation(),"Slab"));
        private void LoadCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=> { var path=CalculationFiles.ChooseLoad(this); if(path!=null) RestoreCalculation(CalculationFiles.Read<CalculationRecord>(path)); });
        private void ExportReport_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.ExportHtml(this,"Slab",BuildReport()));
        internal string BuildReport()
        {
            var record=CaptureCalculation(); var vm=(FlexCalculatorViewModel)DataContext;
            var results=CalculationFiles.Capture("Slab",vm,"SecondaryBeamSpacing,PlywoodProfileId,PlywoodSpacingSummary,SuggestedPrimaryBeamSpacing,ActualPropLoad,PropCapacityLimit,UtilizationRate,PropExtensionMm,PropLookupExtensionMm,PrimaryDeflectionMm,H20Shear,H20Utilization,IsCalculationValid");
            double area=vm.UseCustomLoads ? 25*vm.SlabThickness/1000+vm.CustomLiveLoad+vm.CustomFormworkLoad : SlabLoadHelper.CalculateTotalLoad(vm.SlabThickness);
            int secondaryCount=vm.PrimaryBeamCount=="5+"?5:int.Parse(vm.PrimaryBeamCount), primaryCount=vm.PropCount=="5+"?5:int.Parse(vm.PropCount);
            double secondarySpan=vm.SuggestedPrimaryBeamSpacing/1000, primarySpan=vm.PropSpacing/1000;
            double secondaryLoad=area*vm.SecondaryBeamSpacing/1000;
            double primaryLoad=area*SlabSupportEnvelope.ForSelection(secondaryCount,secondarySpan,vm.SecondaryBeamCantilever/1000,1,449.8756).Reaction;
            double divisor=double.Parse(vm.DeflectionLimit.Substring(2),System.Globalization.CultureInfo.InvariantCulture);
            results.Inputs.Add(CalculationFiles.Field("Total formwork load (kN/m²)",area));
            foreach(var layer in new[]{new {Name="Secondary",Count=secondaryCount,Span=secondarySpan,End=vm.SecondaryBeamCantilever/1000,Load=secondaryLoad},new {Name="Primary",Count=primaryCount,Span=primarySpan,End=vm.PrimaryBeamCantilever/1000,Load=primaryLoad}})
            {
                var response=SlabSupportEnvelope.ForSelection(layer.Count,layer.Span,layer.End,layer.Load,449.8756);
                results.Inputs.Add(CalculationFiles.Field(layer.Name+" H20 checks at recommended spacing",
                    $"Line load {layer.Load:0.000} kN/m; moment {response.Moment:0.000}/5 kNm; shear {response.Shear:0.000}/11 kN; span deflection {response.SpanDeflection*1000:0.000}/{layer.Span*1000/divisor:0.000} mm; end deflection {response.CantileverDeflection*1000:0.000}/{layer.End*1000/divisor:0.000} mm; minimum reaction {response.MinimumReaction:0.000} kN; uplift check {(response.HasUplift?"Fail":"Pass")}"));
            }
            return CalculationFiles.Report("Slab",record.Inputs,results.Inputs,
                "Geometry and spacing: mm. Area loads: kN/m²; prop loads/capacity and H20 shear: kN. Utilizations: %. Deflection: mm.\n"+
                "Concrete 25 kN/m³; default heaping min(1.75,max(0.75,10% of concrete weight)), working 0.75 and formwork 0.40 kN/m². Custom live replaces placing + working load.\n"+
                "Equal spans, full uniform load, equal end overhangs and constant EI. H20 moment 5 kNm, shear 11 kN, EI 449.8756 kNm². 5+ uses the component-wise five/six-support envelope. Continuous analysis applies across illustrated overlaps.\n"+
                "The spacing search checks both H20 layers for moment, shear, span deflection, end deflection including support rotation, and uplift; selected prop capacity is checked. Member length is not a calculator input. End deflection uses overhang length / selected divisor. Spacing rounds down to 25 mm.\n"+
                "Prop extension = floor height - slab thickness - (400 mm H20 layers + selected plywood thickness). Results are recalculated from inputs; this is not a complete formwork verification.\n"+vm.PlywoodCalculationBasis);
        }
    }
}
