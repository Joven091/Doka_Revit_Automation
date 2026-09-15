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
    }
}
