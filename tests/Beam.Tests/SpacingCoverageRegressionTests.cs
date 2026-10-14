using System;
using System.Linq;
using EngineeringDashboard;

internal static class SpacingCoverageRegressionTests
{
    static int checks;
    static void Check(bool ok,string message) {checks++;if(!ok)throw new Exception("Spacing/coverage regression: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-7*Math.Max(1,Math.Abs(expected)),message+$" ({actual} vs {expected})");
    static void Reject(Action action,string message) {bool rejected=false;try {action();}catch(ArgumentException){rejected=true;}Check(rejected,message);}
    static TowerInput Common(double length=2450)=>new TowerInput {LengthMm=length,LegCapacityKn=70,DeflectionDenominator=270,
        StockLengthsMm=new[] {1250.0,1800,2450,3300,3400,3500,3600},MaximumConcreteEndOverhangMm=75};
    static TransferNetworkInput Network(double maximum)=>new TransferNetworkInput {UpperSpacingMm=maximum,CustomLoads=true,
        Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=200},new TransferBeamRow {WidthMm=300,DepthMm=200}},
        Bays=new[] {new TransferBayRow {WidthMm=1800,ThicknessMm=100}}};
    static void Members(TowerLayout layout)
    {
        foreach(var member in layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(x=>x).Concat(layout.Transfer.UpperMembers))
        {
            Check(member.Utilization<=1+1e-8 && member.Analysis.DeflectionEvaluated,"moment, shear and deflection remain checked");
            Near(member.Analysis.Reactions.Sum(),member.Analysis.TotalLoadKn,"member force equilibrium");
            Check(member.Analysis.Reactions.Min()>=-1e-7,"bearing uplift check remains active");
        }
        Check(layout.MaxLegKn<=layout.LegCapacityKn+1e-8,"individual leg capacity remains checked");
    }
    static void Diagrams(TransferredSlabResult t)
    {
        foreach(bool left in new[] {true,false})
        foreach(int row in Enumerable.Range(0,t.RowsMm.Length).Where(i=>t.UpperScale(left,i)>0))
        {
            var member=t.UpperMemberAtRow(left,row);
            var reference=LoadedBeamAnalysis.Analyze(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,449.8756,270);
            Near(member.Analysis.TotalLoadKn,reference.TotalLoadKn,"display load agrees with solver");
            Near(member.Analysis.MomentKnM,reference.MomentKnM,"display moment agrees with solver");
            Near(member.Analysis.ShearKn,reference.ShearKn,"display shear agrees with solver");
            Near(member.Analysis.DeflectionMm,reference.DeflectionMm,"display deflection agrees with solver");
            Near(member.Uniform[0].KnPerM,2.5*t.UpperDesignWidthMm(left,row)/1000,"display scales by this slab's effective width");
            Check(t.UpperSpacingDescription(left,row).Contains($"Nominal slab design spacing {t.SlabSpacing(left):0.###} mm"),"label uses selected slab spacing");
        }
    }
    internal static void Run()
    {
        foreach(double maximum in new[] {1000.0,488,487.99,407,406.99,350,349.99,305,304.99,275,274.99,250})
        {
            double selected=PlywoodSpacing.ListedSpacingAtOrBelow(maximum);
            Check(PlywoodSpacing.StandardSpacingsMm.Contains(selected) && selected<=maximum,"only listed spacings, never rounded upward");
            Check(!PlywoodSpacing.StandardSpacingsMm.Any(x=>x>selected && x<=maximum),"largest permitted value is selected");
        }
        foreach(double invalid in new[] {249.99,225,0,double.NaN,double.PositiveInfinity})
            Reject(()=>PlywoodSpacing.ListedSpacingAtOrBelow(invalid),"no upward/minimum-spacing substitution");
        foreach(var profile in PlywoodCatalog.All)
        foreach(int denominator in new[] {270,350,500})
        foreach(double pressure in new[] {5.0,10,25,50})
        {
            try
            {
                var chart=PlywoodSpacing.Calculate(profile,pressure,denominator);
                var sheet=PlywoodSpacing.CalculateForSheet(profile,pressure,denominator);
                Near(sheet.MaximumSpacingMm,PlywoodSpacing.StandardSpacingsMm.First(x=>x<=chart.MaximumSpacingMm && x>=chart.Chart.MinimumSpanMm),"largest plywood-passing listed spacing");
                Check(sheet.MomentUtilization<=1 && sheet.ShearUtilization<=1 && sheet.DeflectionMm<=sheet.AllowableDeflectionMm+1e-8,"all plywood checks retained");
            }
            catch(ArgumentException) {Reject(()=>PlywoodSpacing.CalculateForSheet(profile,pressure,denominator),"no listed spacing passes this plywood");}
        }
        foreach(bool custom in new[] {false,true})
        {
            var common=Common();common.AllowCustomH20Lengths=custom;
            var network=TransferNetworkOptimizer.Optimize(common,Network(480));
            Near(network.Input.UpperSpacingMm,407,"network/custom legacy path chooses largest passing listed spacing");
            foreach(var beam in network.Beams)Members(beam);
            common=Common();common.AllowCustomH20Lengths=custom;
            common.TransferredSlab=new TransferredSlabInput {BeamWidthMm=300,BeamLineLoadKnM=1.5,UpperSpacingMm=480,RightBayMm=1800,RightAreaLoadKnM2=2.5};
            var single=TransferredSlabOptimizer.Optimize(common);
            Near(single.Transfer.SlabSpacing(false),407,"normal/custom path chooses largest passing listed spacing");Members(single);
        }
        Reject(()=>TransferNetworkOptimizer.Optimize(Common(),Network(249)),"network cannot round an entered limit upward");
        var local=Network(250);local.LocalRowGapMm=100;
        var localResult=TransferNetworkOptimizer.Optimize(Common(),local);
        Near(localResult.Input.BaySpacing(0),250,"shorter local gaps never redefine nominal design spacing");
        foreach(var beam in localResult.Beams)
        {
            Check(beam.Transfer.RowsMm.Zip(beam.Transfer.RowsMm.Skip(1),(a,b)=>b-a).Any(x=>Math.Abs(x-100)<1e-8),"physical local gaps are retained separately");
            foreach(int row in Enumerable.Range(0,beam.Transfer.RowsMm.Length).Where(i=>beam.Transfer.UpperScale(false,i)>0))
                Check(beam.Transfer.UpperDesignWidthMm(false,row)>=250,"effective slab width retains the full permitted nominal spacing");
            Members(beam);
        }
        // Independently replay fixed candidate pitches through every structural check.
        // The unrestricted selector must return the first (largest) passing pitch.
        var h20Input=Common();h20Input.TransferredSlab=new TransferredSlabInput {BeamWidthMm=800,BeamLineLoadKnM=37.32,
            UpperSpacingMm=488,RightBayMm=2575,RightAreaLoadKnM2=9.4};
        double largestH20=0;
        foreach(double spacing in PlywoodSpacing.StandardSpacingsMm)
        {
            var fixedInput=h20Input.CopyForSearch();fixedInput.TransferredSlab=h20Input.TransferredSlab.Copy();fixedInput.TransferredSlab.UpperSpacingMm=spacing;
            try {TransferredSlabOptimizer.Optimize(fixedInput,true);largestH20=spacing;break;}
            catch(TowerOptimizationException) { }
        }
        Check(largestH20>0 && largestH20<488,"H20 fixture requires reduction from a passing plywood maximum");
        var h20Selected=TransferredSlabOptimizer.Optimize(h20Input);
        Near(h20Selected.Transfer.SlabSpacing(false),largestH20,"largest candidate passing H20 and all remaining checks is selected");Members(h20Selected);
        // Different beam/slab spacings, with shorter sheet-joint and end intervals.
        var differing=Network(250);differing.PlywoodSheetLengthMm=2440;differing.PlywoodProfileId=PlywoodCatalog.Default.Id;
        differing.PlywoodDeflectionDenominator=350;differing.BeamSpacingsMm=new[] {250.0,275};differing.SlabSpacingsMm=new[] {488.0};differing.CrossingSpacingsMm=new double[0];
        var result=TransferNetworkOptimizer.Optimize(Common(),differing);
        Near(result.Input.BaySpacing(0),488,"slab spacing is independent of beam spacing");
        Check(result.LayoutDiagnostics.Contains("nominal slab") && result.LayoutDiagnostics.Contains("maximum row spacing=488"),"bay diagnostics use bay spacing");
        foreach(var beam in result.Beams) {Members(beam);Diagrams(beam.Transfer);}
        // Exercise the reverse mismatch too: a lower nominal slab width must not be
        // divided by the wider beam spacing when displaying its response.
        var reverse=Common();reverse.TransferredSlab=new TransferredSlabInput {PlywoodSheetLengthMm=2440,BeamWidthMm=300,BeamLineLoadKnM=1.5,
            UpperSpacingMm=488,RightSlabSpacingMm=250,RightBayMm=1800,RightAreaLoadKnM2=2.5};
        var reversed=TransferredSlabOptimizer.Optimize(reverse);Diagrams(reversed.Transfer);
        foreach(bool deferred in new[] {false,true})
        foreach(double coverage in new[] {6800.0,6900})
        {
            var common=Common(6900);common.DeferTransferLayout=deferred;
            common.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeMainBeamEndLoads=true,BeamWidthMm=300,BeamLineLoadKnM=1.5,
                UpperSpacingMm=350,RightBayMm=1800,RightAreaLoadKnM2=2.5,
                StockCovers=new[] {coverage==6800 ? new[] {3400.0,3400} : new[] {3300.0,3600}}};
            var beam=TransferredSlabOptimizer.Optimize(common);var t=beam.Transfer;Members(beam);
            for(int c=0;c<t.CaseNames.Length;c++)
            {
                double q=c==0 ? 1.5 : 3.75;
                Near(t.PrimaryCases[c].Sum(m=>m.Analysis.TotalLoadKn),q*coverage/1000,"coverage changes loaded length, never baseline UDL intensity");
                double external=t.ColumnSupportLoads.Length==0 ? 0 : t.ColumnSupportLoads[c].Sum(x=>x.TotalKn);
                Near(external,q*(6900-coverage)/1000,"uncovered loads assigned to column supports exactly once");
                Near(t.PrimaryCases[c].Sum(m=>m.Analysis.TotalLoadKn)+external,q*6.9,"covered plus external load conserves physical force");
                if(coverage<6900)
                {
                    Near(t.ColumnSupportLoads[c][0].TotalKn,t.ColumnSupportLoads[c][1].TotalKn,"centred end strips have equal loads");
                    Near(t.ColumnSupportLoads[c].Sum(x=>x.TotalKn*(x.StartMm+x.EndMm)/2000),external*3.45,"external end-strip longitudinal resultant");
                    Check(t.ColumnSupportNotice.Contains("capacity requires separate verification"),"external support capacity scope is explicit");
                }
            }
        }
        var fallbackInput=NetworkFallbackTests.Network();fallbackInput.UpperSpacingMm=350;
        var fallback=TransferNetworkOptimizer.Optimize(NetworkFallbackTests.Common(),fallbackInput);
        Check(fallback.RequiresSeparateSlabFormwork,"catalogue fallback remains available");
        Check(PlywoodSpacing.StandardSpacingsMm.Contains(fallback.Input.UpperSpacingMm),"fallback retains listed nominal spacing");
        foreach(var beam in fallback.Beams)Members(beam);
        Console.WriteLine($"Spacing/coverage regressions: {checks} checks passed.");
    }
}
