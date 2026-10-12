using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EngineeringDashboard;

internal static class SchematicTests
{
    private static void Draw(SlabSupportDiagram diagram, FlexCalculatorViewModel model)
    {
        diagram.SetModel(model); diagram.Measure(new Size(440,300)); diagram.Arrange(new Rect(0,0,440,300)); diagram.UpdateLayout();
        new RenderTargetBitmap(440,300,96,96,PixelFormats.Pbgra32).Render(diagram);
    }
    internal static void Run(Action<bool,string> check)
    {
        var diagram = new SlabSupportDiagram();
        foreach (string primary in new[] { "2", "3", "4", "5+" })
        foreach (string props in new[] { "2", "3", "4", "5+" })
        {
            var model = new FlexCalculatorViewModel { PropSpacing=750, PrimaryBeamCount=primary, PropCount=props };
            check(model.IsCalculationValid,"schematic fixture valid"); Draw(diagram,model);
            int lines=primary=="5+"?5:int.Parse(primary), perLine=props=="5+"?5:int.Parse(props);
            check(diagram.DrawnPrimaryCount==lines && diagram.DrawnPropCount==lines*perLine,"schematic support counts follow both selectors");
            check(diagram.DrawnSecondaryCount>0 && diagram.DrawnSecondaryCount<=18,"bounded secondary detail");
            var ranges = SlabSupportDiagram.SecondarySupportRanges(lines);
            check(ranges[0][0] == 0 && ranges[ranges.Length-1][1] == lines-1,"secondary pieces cover all supports");
            for(int i=0;i<ranges.Length;i++){
                check(ranges[i][1]-ranges[i][0]+1<=3,"secondary member has at most three supports");
                if(i>0)check(ranges[i][0]==ranges[i-1][1],"secondary joint shares primary support");
            }
            check(diagram.DrawnSecondaryMemberCount==diagram.DrawnSecondaryCount*ranges.Length,"secondary members rendered separately");
            var plans=SlabSupportDiagram.SecondaryMemberPlans(lines,2100,500,125,8);
            for(int i=1;i<plans.Length;i++){
                double bearing=-120+(500+ranges[i][0]*2100)*240.0/((lines-1)*2100+1000);
                check(plans[i].Left<bearing && plans[i-1].Right>bearing,"both secondary ends extend beyond shared support");
                check(plans[i].Left<plans[i-1].Right,"secondary lengths overlap");
                check(plans[i].Bottom<=plans[i-1].Top || plans[i-1].Bottom<=plans[i].Top,"lapped H20s sit side by side without intersecting");
            }
        }
        var valid = new FlexCalculatorViewModel(); Draw(diagram,valid); int before=diagram.DrawnSecondaryCount;
        valid.SlabThickness=150; valid.SelectedPlywood=PlywoodCatalog.All[0]; Draw(diagram,valid); check(diagram.DrawnSecondaryCount>before,"secondary pattern responds to spacing");
        valid.PropCount="5+"; Draw(diagram,valid); check(valid.IsCalculationValid && diagram.DrawnSecondaryCount==18,"sheet rows have finite bounded schematic");
        valid.FloorHeight=10000; diagram.SetModel(valid); check(diagram.DrawnPropCount==0 && diagram.DrawnPrimaryCount==0,"invalid state clears schematic immediately");
        valid.FloorHeight=3000; Draw(diagram,valid); check(diagram.DrawnPropCount==15,"valid input restores schematic with the selected five-prop layout");
        var old=Thread.CurrentThread.CurrentCulture;
        try { Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("de-DE"); Draw(diagram,new FlexCalculatorViewModel()); check(diagram.DrawnPropCount==9,"schematic culture independent"); }
        finally { Thread.CurrentThread.CurrentCulture=old; }
    }
}