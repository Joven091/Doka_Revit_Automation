#if DASHBOARD_DESKTOP
using System;
using System.Threading.Tasks;
using System.Windows;
using EngineeringDashboard.DesktopHost;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorWindow
    {
        private bool desktopApplying;
        private async Task ApplyBeamFromDesktopAsync()
        {
            if(desktopApplying)return;
            if(!TryGetValidCalculation(out var model)||!model.CanImport){MessageBox.Show(this,"Correct the inputs before importing values.","Invalid calculation",MessageBoxButton.OK,MessageBoxImage.Information);return;}
            desktopApplying=true;
            try{await DesktopRuntime.ApplyHelpersAsync(this,new HelperApplyRequest{Kind="beam",WidthMm=model.Width,DepthMm=model.Depth,PropSpacingMm=model.MaximumPropSpacing,PrimaryCantileverMm=model.PrimaryBeamCantilever});}
            catch(Exception error){if(IsLoaded)MessageBox.Show(this,error.Message,"Revit request",MessageBoxButton.OK,MessageBoxImage.Information);}
            finally{desktopApplying=false;}
        }
    }
    public partial class FlexCalculatorWindow
    {
        private bool desktopApplying;
        private async Task ApplySlabFromDesktopAsync()
        {
            if(desktopApplying)return;
            if(!TryGetValidCalculation(out var model)){MessageBox.Show(this,"Correct the inputs before importing values.","Invalid calculation",MessageBoxButton.OK,MessageBoxImage.Information);return;}
            desktopApplying=true;
            try{await DesktopRuntime.ApplyHelpersAsync(this,new HelperApplyRequest{Kind="slab",SlabThicknessMm=model.SlabThickness,PropSpacingMm=model.PropSpacing,PrimaryCantileverMm=model.PrimaryBeamCantilever,PrimarySpacingMm=model.SuggestedPrimaryBeamSpacing});}
            catch(Exception error){if(IsLoaded)MessageBox.Show(this,error.Message,"Revit request",MessageBoxButton.OK,MessageBoxImage.Information);}
            finally{desktopApplying=false;}
        }
    }
}
#endif
