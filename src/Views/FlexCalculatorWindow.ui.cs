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
                "Joints and quantities are illustrative; calculations retain the selected continuous support count.";
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
    }
}
