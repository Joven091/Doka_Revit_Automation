#if DASHBOARD_DESKTOP
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorWindow
    {
        internal void ShowRestartNotice(string notice)
        {if(FormworkWorkspace!=null)FormworkWorkspace.ShowRestartNotice(notice);else DiagramCaption.Text=notice+"\n"+DiagramCaption.Text;}
        internal bool RestartBusy=>desktopApplying || updatingWorkspace || (FormworkWorkspace?.RestartBusy??false);
        internal void ValidateRestartEditors()
        {
            if(RestartBusy)throw new InvalidOperationException("Finish the active beam operation before updating.");
            if(FormworkWorkspace!=null){FormworkWorkspace.ValidateRestartEditors();return;}
            foreach(var box in TextInputs(this).Where(b=>b.IsEnabled))box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            RefreshInputValidity();
            if(TextInputs(this).Any(b=>b.IsEnabled&&Validation.GetHasError(b)))throw new InvalidOperationException("Correct the beam input highlighted in red before updating.");
        }
        internal static BeamCalculatorViewModel RestoreRestartInputs(CalculationRecord record,bool selected)
        {
            var vm=new BeamCalculatorViewModel();CalculationFiles.Apply(record,"Beam",vm,FileInputs);
            vm.PlywoodProfileId=record.Inputs.Single(f=>f.Name=="PlywoodProfileId").Value;
            vm.PlywoodDeflectionLimit=PlywoodSpacing.RestoreDeflectionLimit(record.Inputs.Single(f=>f.Name=="PlywoodDeflectionLimit").Value,"L/350");
            vm.SelectedProp=BeamPropOption.All.Single(p=>p.CapacityKey==record.Inputs.Single(f=>f.Name=="PropConfiguration").Value);
            vm.DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(record.Inputs.Single(f=>f.Name=="DeflectionCriterion").Value,record.Inputs.SingleOrDefault(f=>f.Name=="DeflectionLimitMm")?.Value);
            RestoreTowerFields(record,vm);vm.HasSelectedSupportSystem=selected;return vm;
        }
    }
    public partial class FlexCalculatorWindow
    {
        internal void ShowRestartNotice(string notice){DiagramCaption.Text=notice+"\n"+DiagramCaption.Text;}
        internal bool RestartBusy=>desktopApplying;
        internal void ValidateRestartEditors()
        {
            foreach(var box in TextInputs(this).Where(b=>b.IsEnabled))box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            RefreshInputValidity();
            if(TextInputs(this).Any(b=>b.IsEnabled&&Validation.GetHasError(b)))throw new InvalidOperationException("Correct the slab input highlighted in red before updating.");
        }
        internal static CalculationRecord CaptureRestartInputs(FlexCalculatorViewModel vm)
        {
            var record=CalculationFiles.Capture("Slab",vm,FileInputs);
            foreach(var field in CalculationFiles.Capture("Slab",vm,"PlywoodProfileId,PlywoodDeflectionLimit,SecondaryBeamSpacing").Inputs)record.Inputs.Add(field);
            return record;
        }
        internal static FlexCalculatorViewModel RestoreRestartInputs(CalculationRecord record)
        {
            var vm=new FlexCalculatorViewModel();CalculationFiles.Apply(record,"Slab",vm,FileInputs);
            vm.PlywoodProfileId=record.Inputs.Single(f=>f.Name=="PlywoodProfileId").Value;
            vm.PlywoodDeflectionLimit=PlywoodSpacing.RestoreDeflectionLimit(record.Inputs.Single(f=>f.Name=="PlywoodDeflectionLimit").Value,vm.DeflectionLimit);
            return vm;
        }
    }
    internal sealed partial class BeamFormworkWorkspace
    {
        internal void ShowRestartNotice(string notice){status.Text=notice;}
        internal bool RestartBusy=>readingRevit || Model.IsOptimizing;
        internal void ValidateRestartEditors()
        {
            if(draftCommand!=null || DimensionPopup.IsOpen)throw new InvalidOperationException("Finish or cancel the active drawing command before updating.");
            if(!CommitFields())throw new InvalidOperationException("Correct the drawing input highlighted in red before updating.");
        }
    }
}
#endif
