using System;
using System.Linq;
using EngineeringDashboard;
internal static class Program
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception(message);}
    static void Equal(double expected,double actual,string message) => Check(Math.Abs(expected-actual)<1e-8,message+": "+actual);
    static int Main()
    {
        try
        {
            var vm=new FlexCalculatorViewModel();
            Check(vm.IsCalculationValid && vm.SecondaryBeamSpacing==488,"default slab uses passing sheet spacing");
            Check(!typeof(FlexCalculatorViewModel).GetProperty(nameof(vm.SecondaryBeamSpacing)).CanWrite,"slab spacing cannot be assigned");
            foreach(var plywood in PlywoodCatalog.All)
            foreach(int h in new[] {100,150,300,600})
            foreach(string criterion in new[] {"L/500","L/350","L/270"})
            {
                vm=new FlexCalculatorViewModel {SlabThickness=h,FloorHeight=h+2400+plywood.ThicknessMm,SelectedPlywood=plywood,DeflectionLimit=criterion};
                if(vm.PlywoodResult==null) {Check(!vm.IsCalculationValid && vm.SecondaryBeamSpacing==0,"unavailable sheet spacing invalidates slab");continue;}
                Check(PlywoodSpacing.StandardSpacingsMm.Contains(vm.SecondaryBeamSpacing),"slab uses only requested spacings");
                Check(vm.PlywoodResult.DeflectionMm<=vm.PlywoodResult.AllowableDeflectionMm+1e-10 && vm.PlywoodResult.MomentUtilization<=1 && vm.PlywoodResult.ShearUtilization<=1,"slab sheet satisfies all chart limits");
                Equal(2000,vm.PropExtensionMm,"selected plywood thickness affects prop extension");
                if(vm.IsCalculationValid)
                {
                    double q=SlabLoadHelper.CalculateTotalLoad(h)*vm.SecondaryBeamSpacing/1000;
                    var response=ContinuousBeamResponse.Analyze(3,vm.SuggestedPrimaryBeamSpacing/1000,.5,q,449.8756);
                    Check(response.Moment<=5+1e-7 && response.Shear<=11+1e-7,"secondary response checks actual sheet spacing");
                }
            }
            vm=new FlexCalculatorViewModel {SelectedPlywood=PlywoodCatalog.All[0]};
            Check(!vm.IsCalculationValid && vm.WarningMessage.Contains("minimum 250 mm"),"9 mm slab cannot silently round up");
            vm.SlabThickness=150;Check(vm.IsCalculationValid && vm.SecondaryBeamSpacing==275,"9 mm slab recovers at lower loading");
            vm.SetTextInputErrors(true);Check(vm.SecondaryBeamSpacing==0,"invalid text clears derived spacing");vm.SetTextInputErrors(false);Check(vm.SecondaryBeamSpacing==275,"spacing recovers after input repair");
            vm.SelectedPlywood=null;Check(!vm.IsCalculationValid && vm.SecondaryBeamSpacing==0,"missing plywood invalidates slab");
            UpliftTests.Run(Check,Equal);CantileverTests.Run(Check,Equal);FivePlusTests.Run(Check,Equal);
            Console.WriteLine($"Passed {checks} slab core and plywood checks.");return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
