#if DASHBOARD_DESKTOP
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EngineeringDashboard.DesktopHost;

namespace EngineeringDashboard
{
    public partial class LauncherWindow
    {
        internal bool RestartBusy=>(_beamViewModel?.IsOptimizing??false)||(_top50ViewModel?.IsBusy??false)
            ||(currentTool is BeamCalculatorWindow beam&&beam.RestartBusy)||(currentTool is FlexCalculatorWindow slab&&slab.RestartBusy)
            ||(currentTool is Top50Window top50&&top50.RestartBusy)||(currentTool is SteelShape.SteelShapeWindow steel&&steel.RestartBusy);
        internal void ValidateRestartEditors()
        {
            (currentTool as BeamCalculatorWindow)?.ValidateRestartEditors();(currentTool as FlexCalculatorWindow)?.ValidateRestartEditors();
            if(currentTool is Top50Window && Descendants(currentTool).OfType<TextBox>().Any(box=>box.IsKeyboardFocusWithin))throw new InvalidOperationException("Finish editing the Top50 field before updating.");
        }
        internal DashboardSessionSnapshot CaptureRestartState()
        {
            if(RestartBusy)throw new InvalidOperationException("Finish the current operation before updating.");
            if(_d3SlabViewModel!=null||_domeViewModel!=null)throw new InvalidOperationException("Close this session before updating: it contains a legacy calculator state that cannot be restored by this version.");
            string tool=currentTool==null?"launcher":currentTool is BeamCalculatorWindow?"beam":currentTool is FlexCalculatorWindow?"slab":currentTool is SlabSystemWindow?"slab-choice":currentTool is Top50Window?"top50":currentTool is BackproppingWindow?"backpropping":currentTool is SteelShape.SteelShapeWindow?"steel":null;
            if(tool==null)throw new InvalidOperationException("Return to the Dashboard before updating this tool.");
            // Do not close modal draft/review dialogs, or copy a half-completed edit.
            foreach(Window window in Application.Current.Windows)
                if(window!=this&&window!=currentTool&&window.IsVisible&&window.Owner==currentTool&&window.GetType().Namespace!="EngineeringDashboard.DesktopHost")
                    throw new InvalidOperationException("Close the open tool dialog before updating so its working inputs are preserved.");
            var active=currentTool??this;
            return new DashboardSessionSnapshot{Version=Assembly.GetExecutingAssembly().GetName().Version.ToString(),Tool=tool,
                Beam=_beamViewModel==null?null:BeamCalculatorWindow.CaptureRestartInputs(_beamViewModel),BeamSupportSelected=_beamViewModel?.HasSelectedSupportSystem??false,
                Slab=_flexViewModel==null?null:FlexCalculatorWindow.CaptureRestartInputs(_flexViewModel),Top50=_top50ViewModel?.CaptureRestartState(),
                Backpropping=BackproppingWindow.CaptureRestartState(currentTool as BackproppingWindow),SteelShape=(currentTool as SteelShape.SteelShapeWindow)?.CaptureRestartState(),
                Width=active.RestoreBounds.IsEmpty?active.Width:active.RestoreBounds.Width,Height=active.RestoreBounds.IsEmpty?active.Height:active.RestoreBounds.Height,Maximized=active.WindowState==WindowState.Maximized};
        }
        internal static void ValidateRestartState(DashboardSessionSnapshot state)
        {
            if(state.Beam!=null)BeamCalculatorWindow.RestoreRestartInputs(state.Beam,state.BeamSupportSelected);
            if(state.Slab!=null)FlexCalculatorWindow.RestoreRestartInputs(state.Slab);
            if(state.Top50!=null)Top50ViewModel.RestoreRestartState(state.Top50);
            BackproppingWindow.ValidateRestartState(state.Backpropping);
            if(state.Tool=="beam"&&state.Beam==null||state.Tool=="slab"&&state.Slab==null||state.Tool=="top50"&&state.Top50==null||state.Tool=="backpropping"&&state.Backpropping==null)throw new InvalidDataException("The active tool's recovery inputs are missing.");
        }
        internal void RestoreRestartState(DashboardSessionSnapshot state)
        {
            _beamViewModel=state.Beam==null?null:BeamCalculatorWindow.RestoreRestartInputs(state.Beam,state.BeamSupportSelected);
            _flexViewModel=state.Slab==null?null:FlexCalculatorWindow.RestoreRestartInputs(state.Slab);
            _top50ViewModel=state.Top50==null?null:Top50ViewModel.RestoreRestartState(state.Top50);
            BackproppingWindow.RestoreRestartState(state.Backpropping);
            switch(state.Tool)
            {
                case "beam":BtnBeamCalculator_Click(null,null);break;
                case "slab":ShowTool(new FlexCalculatorWindow{ViewModel=_flexViewModel});break;
                case "slab-choice":OpenSlabChoice();break;
                case "top50":BtnTop50_Click(null,null);break;
                case "backpropping":ShowTool(new BackproppingWindow());BackproppingWindow.RestoreRestartState(state.Backpropping,(BackproppingWindow)currentTool);break;
                case "steel":ShowTool(new SteelShape.SteelShapeWindow());((SteelShape.SteelShapeWindow)currentTool).RestoreRestartState(state.SteelShape);break;
            }
            var active=currentTool??this;
            active.Width=Math.Min(state.Width,SystemParameters.WorkArea.Width);active.Height=Math.Min(state.Height,SystemParameters.WorkArea.Height);
            if(state.Maximized)active.WindowState=WindowState.Maximized;
            string notice=state.Tool=="beam"&&_beamViewModel.UseTowers?"Drawing inputs restored. Optimize towers to calculate updated results.":"Inputs restored. Automatic calculations have been recalculated with the updated Dashboard.";
            if(currentTool is BeamCalculatorWindow beam)beam.ShowRestartNotice(notice);
            if(currentTool is FlexCalculatorWindow slab)slab.ShowRestartNotice(notice);
        }
        static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
        }
    }
}
#endif
