using System;
using System.Linq;
using EngineeringDashboard;

internal static class PlywoodTests
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static void Near(double actual, double expected, string name) => Check(Math.Abs(actual-expected)<1e-8, name);
    private static void Reject(Action action, string name)
    {
        bool rejected=false; try { action(); } catch (ArgumentException) { rejected=true; } Check(rejected,name);
    }
    internal static void Run()
    {
        VerifyProfiles();
        VerifySheetLayouts();
        // Independent image readings: L/500 crosses p=15 near 39 cm, p=25 near 33 cm.
        // Conservative fit + downward rounding must remain close and below those readings.
        Near(PlywoodSpacing.Calculate(15,500).MaximumSpacingMm,385,"15 kPa L/500 chart reading");
        Near(PlywoodSpacing.Calculate(25,500).MaximumSpacingMm,325,"25 kPa L/500 chart reading");
        Near(PlywoodSpacing.Calculate(25,270).MaximumSpacingMm,400,"25 kPa L/270 intersection");
        var high=PlywoodSpacing.Calculate(60,270);
        Near(high.MaximumSpacingMm,265,"60 kPa shear envelope");
        Check(high.Governing=="Shear Q","relaxed deflection cannot override shear");
        Near(PlywoodSpacing.Calculate(2.5,270).MaximumSpacingMm,600,"chart span ceiling");
        Near(PlywoodSpacing.Calculate(1,270).MaximumSpacingMm,600,"below-range pressure uses chart minimum");
        Near(PlywoodSpacing.Calculate(1,270).ChartPressureKnM2,2.5,"minimum chart pressure is explicit");
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,0,-1,60.0001})
            Reject(()=>PlywoodSpacing.Calculate(bad,350),"invalid/out-of-range pressure rejected");
        Reject(()=>PlywoodSpacing.Calculate(15,0),"unsupported criterion rejected");
        double[] previous={600,600,600};
        for(int i=0;i<=230;i++)
        {
            double pressure=2.5+i*.25;
            int index=0;
            foreach(int denominator in new[]{500,350,270})
            {
                var result=PlywoodSpacing.Calculate(pressure,denominator);
                double span=result.MaximumSpacingMm;
                Check(span>=200 && span<=600 && span%5==0,"bounded rounded spacing");
                Check(span<=previous[index],"increasing pressure never increases spacing");previous[index++]=span;
                Check(result.DeflectionMm<=result.AllowableDeflectionMm+1e-10,"rounded spacing satisfies deflection");
                Check(result.MomentUtilization<=1 && result.ShearUtilization<=1,"rounded spacing satisfies both strength envelopes");
                Check(span<=result.MomentSpacingMm && span<=result.ShearSpacingMm && span<=result.DeflectionSpacingMm,"smallest limit governs");
                Check(span+5>Math.Min(600,Math.Min(result.MomentSpacingMm,Math.Min(result.ShearSpacingMm,result.DeflectionSpacingMm))),"next increment fails a limit");
            }
            Check(PlywoodSpacing.Calculate(pressure,500).MaximumSpacingMm<=PlywoodSpacing.Calculate(pressure,350).MaximumSpacingMm
                && PlywoodSpacing.Calculate(pressure,350).MaximumSpacingMm<=PlywoodSpacing.Calculate(pressure,270).MaximumSpacingMm,"criterion ordering");
        }
        var vm=new BeamCalculatorViewModel();
        Near(vm.PlywoodPressureKnM2,14.9,"beam area pressure includes standard allowances");
        Near(vm.MaximumSecondarySpacingMm,407,"default L/350 plywood spacing");
        double propSpacing=vm.MaximumPropSpacing;
        vm.Width=1000;Near(vm.MaximumSecondarySpacingMm,407,"beam width does not change plywood pressure");
        Check(vm.MaximumPropSpacing<=propSpacing,"primary calculation remains separate");
        vm.DeflectionLimit=BeamCalculatorViewModel.AvailableDeflectionOptions.Single(x=>x.Denominator==270);
        Near(vm.MaximumSecondarySpacingMm,407,"selected L/270 applies to plywood");
        double flexSpacing=vm.MaximumSecondarySpacingMm;vm.UseTowers=true;
        Near(vm.MaximumSecondarySpacingMm,flexSpacing,"D3 and Dokaflex share plywood limits before optimization");
        vm.Depth=1750;Near(vm.PlywoodPressureKnM2,46.65,"deep beam pressure is not an averaged line load");
        vm.HasLeftSlab=true;vm.LeftSlabThickness=300;vm.TransferSlabLoads=true;
        Near(vm.UpperSupportSpacingMm,vm.MaximumSecondarySpacingMm,"D3 row spacing automatically uses plywood maximum");
        Check(!typeof(BeamCalculatorViewModel).GetProperty(nameof(vm.UpperSupportSpacingMm)).CanWrite,"calculated spacing has no writable setter");
        int spacingNotifications=0;vm.PropertyChanged+=(s,e)=> {if(e.PropertyName==nameof(vm.UpperSupportSpacingMm))spacingNotifications++;};
        vm.UseBeamNetwork=true;vm.NetworkBeams[1].DepthMm=2000;
        Near(vm.PlywoodPressureKnM2,52.9,"deepest network beam governs common spacing");
        Near(vm.UpperSupportSpacingMm,vm.MaximumSecondarySpacingMm,"network recomputes automatic spacing without stale dependency");
        Check(spacingNotifications>0,"spacing changes notify locked fields");
        vm.CrossingBeams.Add(new TransferCrossingBeamRow {DepthMm=2200,WidthMm=400,PositionMm=3000});
        vm.CustomLiveLoad=2.5; // Trigger recalculation; inactive custom load has no pressure effect.
        Near(vm.PlywoodPressureKnM2,57.9,"crossing-beam pressure participates in plywood envelope");
        vm.NetworkBeams[1].DepthMm=2400;
        Check(vm.MaximumSecondarySpacingMm==0 && vm.PlywoodResult==null && vm.WarningMessage.Contains("60 kN/m²"),"out-of-chart network clears stale result");
        vm=new BeamCalculatorViewModel {UseCustomLoads=true,CustomLiveLoad=0,CustomFormworkLoad=0};
        Near(vm.PlywoodPressureKnM2,12.5,"custom allowances replace defaults");
        vm.DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(null,"10");
        Check(vm.PlywoodCriterion=="L/500" && vm.SecondarySpacingSummary.Contains("Legacy"),"legacy H20 criterion uses explicit plywood L/500");
        vm.SetTextInputErrors(true);Check(vm.PlywoodResult==null && vm.MaximumSecondarySpacingMm==0 && vm.UpperSupportSpacingMm==0,"text errors clear secondary result");
        vm.SetTextInputErrors(false);Check(vm.MaximumSecondarySpacingMm>0,"secondary result recovers");
        vm=new BeamCalculatorViewModel {UseTowers=true,Depth=900,Width=800,BeamLengthMm=10600,TowerLegCapacityKn=70,AllowCustomH20Lengths=true,
            MaximumConcreteEndOverhangMm=75,HasLeftSlab=true,LeftSlabThickness=300,LeftSlabBayMm=2575,TransferSlabLoads=true};
        vm.OptimizeTowersAsync().GetAwaiter().GetResult();
        Check(vm.IsCalculationValid && vm.TowerResult?.Transfer!=null,"single-beam transfer calculates with automatic maximum: "+vm.WarningMessage);
        Near(vm.TowerResult.Transfer.EnteredUpperSpacingMm,vm.MaximumSecondarySpacingMm,"single-beam optimizer uses locked spacing");
        var t=vm.TowerResult.Transfer;double stockEdge=(vm.BeamLengthMm-vm.TowerResult.StockPlan.Pieces.Sum(p=>p.StockLengthMm))/2;
        for(double joint=2440;joint<vm.BeamLengthMm-stockEdge;joint+=2440)
            Check(t.RowsMm.Any(x=>Math.Abs(x-joint)<1e-7),"calculated D3 transfer has real sheet-joint supports");
        Console.WriteLine($"Passed {checks} plywood spacing checks.");
    }

    private static void VerifyProfiles()
    {
        Check(PlywoodCatalog.All.Select(x=>x.ThicknessMm).SequenceEqual(new[] {9.0,18,21}),"three supplied plywood thicknesses in selector");
        Check(PlywoodCatalog.Default.ThicknessMm==18,"existing calculations retain 18 mm default");
        var nine=PlywoodCatalog.All.Single(x=>x.ThicknessMm==9);
        var twentyOne=PlywoodCatalog.All.Single(x=>x.ThicknessMm==21);
        Near(PlywoodSpacing.Calculate(nine,14.9,350).MaximumSpacingMm,210,"9 mm L/350 chart spacing");
        Near(PlywoodSpacing.Calculate(nine,80,270).MaximumSpacingMm,100,"9 mm high-pressure shear envelope");
        Near(PlywoodSpacing.Calculate(twentyOne,14.9,350).MaximumSpacingMm,515,"21 mm full chart extends beyond detailed view");
        Near(PlywoodSpacing.Calculate(twentyOne,100,270).MaximumSpacingMm,190,"21 mm detailed high-pressure shear envelope");
        var detailed=PlywoodSpacing.Calculate(twentyOne,90,350);
        Check(detailed.Chart.Reference=="9732-120" && detailed.MaximumSpacingMm<=300,"above 80 kPa uses detailed chart only");
        Check(PlywoodSpacing.Calculate(twentyOne,5,350).Chart.Reference=="9732-104","long spans use full 21 mm chart");
        Near(PlywoodSpacing.Calculate(twentyOne,1,270).MaximumSpacingMm,600,"21 mm full-range chart ceiling");
        Reject(()=>PlywoodCatalog.FromId("unknown"),"unknown saved plywood rejected");
        Reject(()=>PlywoodSpacing.Calculate(null,10,350),"missing plywood rejected");
        foreach(var profile in PlywoodCatalog.All)
        {
            Reject(()=>PlywoodSpacing.Calculate(profile,profile.MaximumPressureKnM2+.001,350),"profile-specific pressure ceiling");
            for(double pressure=profile.MinimumPressureKnM2;pressure<=profile.MaximumPressureKnM2;pressure+=.5)
            foreach(int denominator in new[] {500,350,270})
            {
                var r=PlywoodSpacing.Calculate(profile,pressure,denominator);
                Check(r.Profile==profile && r.MaximumSpacingMm>=r.Chart.MinimumSpanMm && r.MaximumSpacingMm<=r.Chart.MaximumSpanMm,"result belongs to its selected product/chart span domain");
                Check(r.ChartPressureKnM2>=r.Chart.MinimumPressureKnM2 && r.ChartPressureKnM2<=r.Chart.MaximumPressureKnM2,"result belongs to chart pressure domain");
                Check(r.MaximumSpacingMm%5==0 && r.MomentUtilization<=1+1e-10 && r.ShearUtilization<=1+1e-10 && r.DeflectionMm<=r.AllowableDeflectionMm+1e-10,"all plywoods satisfy rounded strength and deflection checks");
            }
        }
        // Synthetic chart domains test the union logic; this fixture is never a catalog product.
        var gap=new PlywoodProfile("test-domains","Synthetic domain fixture",18,"test-A",2.3,5.6,16,100,200,2.5,60,"",
            new PlywoodChartDomain("test-B",300,400,2.5,60));
        Near(PlywoodSpacing.Calculate(gap,60,350).MaximumSpacingMm,200,"chart domain gaps are not extrapolated");

        var vm=new BeamCalculatorViewModel();
        vm.SelectedPlywood=nine;Check(vm.MaximumSecondarySpacingMm==0 && vm.WarningMessage.Contains("minimum 250 mm"),"9 mm cannot satisfy a listed spacing at default beam pressure");
        Near(vm.FormworkDepthMm,409,"9 mm changes formwork buildup");Near(vm.PropExtensionMm,2091,"9 mm changes actual prop extension");
        vm.SelectedPlywood=twentyOne;Near(vm.MaximumSecondarySpacingMm,488,"selector applies full 21 mm chart");
        Near(vm.FormworkDepthMm,421,"21 mm changes formwork buildup");Near(vm.PropExtensionMm,2079,"21 mm changes actual prop extension");
        vm.UseTowers=true;Near(vm.UpperSupportSpacingMm,488,"D3 locked field follows selected plywood");
        vm.PlywoodProfileId=nine.Id;Check(vm.SelectedPlywood==nine && vm.PlywoodCalculationBasis.Contains("9732-121"),"stable saved key selects correct profile/basis");
        vm.SelectedPlywood=null;Check(vm.PlywoodResult==null && vm.MaximumSecondarySpacingMm==0 && !vm.IsCalculationValid,"missing selection clears stale results");
        vm.SelectedPlywood=twentyOne;vm.Depth=3200;vm.HasRightSlab=true;vm.RightSlabThickness=300;vm.TransferSlabLoads=true;vm.UseBeamNetwork=true;
        Check(vm.PlywoodResult==null && vm.WarningMessage.Contains("minimum 250 mm"),"detail chart cannot justify an unsafe sheet spacing");
        vm.SelectedPlywood=nine;Check(vm.PlywoodResult==null && vm.WarningMessage.Contains("80 kN/m²"),"changing to insufficient chart clears network result");
        vm.StartNewSession();Check(vm.SelectedPlywood==PlywoodCatalog.Default && vm.MaximumSecondarySpacingMm==407,"new session restores default profile and derived spacing");
    }
    private static void VerifySheetLayouts()
    {
        Check(PlywoodSpacing.StandardSpacingsMm.SequenceEqual(new[] {488.0,407,350,305,275,250}),"requested nominal spacing list");
        foreach(var profile in PlywoodCatalog.All)
        foreach(int denominator in new[] {500,350,270})
        for(double p=profile.MinimumPressureKnM2;p<=profile.MaximumPressureKnM2;p+=.5)
        {
            var chart=PlywoodSpacing.Calculate(profile,p,denominator);
            var allowed=PlywoodSpacing.StandardSpacingsMm.Where(x=>x<=chart.MaximumSpacingMm).ToArray();
            if(allowed.Length==0) {Reject(()=>PlywoodSpacing.CalculateForSheet(profile,p,denominator),"below-250 chart cannot be rounded up");continue;}
            var result=PlywoodSpacing.CalculateForSheet(profile,p,denominator);
            Near(result.MaximumSpacingMm,allowed[0],"largest safe listed spacing");
            Check(result.DeflectionMm<=result.AllowableDeflectionMm+1e-10 && result.MomentUtilization<=1+1e-10 && result.ShearUtilization<=1+1e-10,"snapped spacing passes all plywood checks");
        }
        foreach(double spacing in PlywoodSpacing.StandardSpacingsMm)
        foreach(double edge in new[] {0.0,25,75,150})
        foreach(double length in new[] {2450.0,6100,10600,10800})
        {
            var rows=PlywoodSpacing.SheetSupportRows(edge,length-edge,spacing);
            Check(rows.First()>edge && rows.Last()<length-edge,"sheet supports lie within covered stock");
            Check(rows.Skip(1).Select((x,i)=>x-rows[i]<=spacing+1e-7 && x-rows[i]>=80-1e-7).All(x=>x),"joint rows never exceed safe spacing or overlap H20 flanges");
            for(double joint=2440;joint<length-edge;joint+=2440)
                if(joint>edge)Check(rows.Contains(joint),"H20 row at every internal sheet joint");
        }
    }

}
