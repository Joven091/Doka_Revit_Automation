using System;
using System.Linq;
using System.Text.RegularExpressions;
using EngineeringDashboard;
internal static class CrossSupportTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Cross supports: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message);
    static TowerInput Common()=>new TowerInput {LengthMm=2450,LegCapacityKn=70,DeflectionDenominator=350,MaximumConcreteEndOverhangMm=0,AllowSeparateSlabFallback=false,NetworkSearchWorkers=1};
    static TransferNetworkInput Input(CrossBeamSupportMode mode=CrossBeamSupportMode.ExistingSupportsOnly,double bottom=-400)=>new TransferNetworkInput {UpperSpacingMm=100,Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=600},new TransferBeamRow {WidthMm=300,DepthMm=600}},Bays=new[] {new TransferBayRow {WidthMm=3900,ThicknessMm=100}},CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=1225,WidthMm=400,DepthMm=150,BottomElevationMm=bottom,SupportMode=mode}}};
    static void Failed(TowerInput common,TransferNetworkInput input,string message)
    {
        bool failed=false;try {TransferNetworkOptimizer.Optimize(common,input);}catch(TowerOptimizationException e){failed=true;Check(!string.IsNullOrEmpty(e.Details),"failure retains a structural or geometry diagnosis");}Check(failed,message);
    }
    static void CheckPrimaryFootprints(TransferNetworkResult result)
    {
        bool incrementFound=false;
        for(int beam=0;beam<result.Beams.Length;beam++)
        {
            var layout=result.Beams[beam];var transfer=layout.Transfer;
            int stage=transfer.CaseNames.Length-1;
            string name=transfer.CaseNames[stage];int suffix=name.IndexOf(" + X",StringComparison.Ordinal);
            int baseline=Array.IndexOf(transfer.CaseNames,name.Substring(0,suffix));
            int runs=layout.StockPlan.Pieces.Length;
            var bearings=result.CrossSupports.SelectMany(s=>s.Bearings).Where(b=>b.Beam==beam).ToArray();
            for(int member=0;member<transfer.PrimaryCases[stage].Length;member++)
            {
                var loaded=transfer.PrimaryCases[stage][member];var original=transfer.PrimaryCases[baseline][member];
                int line=member/runs;
                var cuts=loaded.Uniform.Concat(original.Uniform).SelectMany(q=>new[] {q.StartMm,q.EndMm})
                    .Concat(bearings.SelectMany(b=>new[] {Math.Max(0,b.FootprintStartMm-loaded.StartMm),Math.Min(loaded.LengthMm,b.FootprintEndMm-loaded.StartMm)}))
                    .Where(x=>x>=0 && x<=loaded.LengthMm).Distinct().OrderBy(x=>x).ToArray();
                for(int i=1;i<cuts.Length;i++)
                {
                    double x=(cuts[i-1]+cuts[i])/2,expected=original.Uniform.Where(q=>x>q.StartMm && x<q.EndMm).Sum(q=>q.KnPerM);
                    foreach(var bearing in bearings.Where(b=>x+loaded.StartMm>b.FootprintStartMm && x+loaded.StartMm<b.FootprintEndMm))
                    {
                        var lower=transfer.LowerCases[baseline][bearing.Row];
                        double position=bearing.Ymm-result.BeamCentresMm[beam]-lower.StartMm;
                        double right=bearing.ReactionKn*(position-lower.SupportsMm[0])/(lower.SupportsMm[1]-lower.SupportsMm[0]);
                        expected+=(line==0 ? bearing.ReactionKn-right : right)*1000/(bearing.FootprintEndMm-bearing.FootprintStartMm);
                        incrementFound=true;
                    }
                    Near(loaded.Uniform.Where(q=>x>q.StartMm && x<q.EndMm).Sum(q=>q.KnPerM),expected,"primary baseline UDL and local cross increments agree with independent transverse statics");
                }
            }
        }
        Check(incrementFound,"cross reactions retain their actual longitudinal footprints");
    }
    public static void Run()
    {
        double gap;
        Check(CrossBeamSupportOptimizer.ValidFillerGap(-400,-600,out gap),"zero gap permits direct bearing");Near(gap,0,"zero filler");
        Check(CrossBeamSupportOptimizer.ValidFillerGap(-250,-600,out gap),"positive gap permits filler");Near(gap,150,"positive filler");
        Check(!CrossBeamSupportOptimizer.ValidFillerGap(-401,-600,out gap),"negative gap invalidates shared bearing");Near(gap,-1,"negative gap sign");
        var a=new FormworkSolid {X0=0,X1=100,Y0=0,Y1=80,Z0=0,Z1=200};
        var b=new FormworkSolid {X0=0,X1=100,Y0=0,Y1=80,Z0=200,Z1=400};
        Check(!a.Overlaps(b),"intended surface bearing contact permitted");b.Z0=199;Check(a.Overlaps(b),"1 mm overlap rejected");b.Z0=200;b.X0=99;Check(!a.Overlaps(b),"stock extensions with vertical clearance do not clash");b.Z0=100;Check(a.Overlaps(b),"tail overlap detected with actual solid coordinates");
        var boundCommon=Common();
        var baseMember=new TransferMember {Name="Subset fixture",H20Count=2,LengthMm=1000,SupportsMm=new[] {0.0,1000},Analysis=LoadedBeamAnalysis.Analyze(1000,new[] {0.0,1000},new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=1}},null,TowerOptimizer.PairEI,350)};
        var increments=Enumerable.Range(0,9).Select(i=>new TransferMember {Analysis=LoadedBeamAnalysis.Analyze(1000,new[] {0.0,1000},new[] {new BeamDistributedLoad {StartMm=0,EndMm=1000,KnPerM=.1}},null,TowerOptimizer.PairEI,350)}).ToArray();
        var reported=new TransferMember();
        Check(CrossBeamSupportOptimizer.Envelope(boundCommon,baseMember,increments,.95,reported),"nine-crossing subset bound includes the maximum all-loaded stage");
        Near(reported.SubsetMomentBoundKnM,1.9/8,"subset moment bound agrees with independent simply-supported UDL formula");Near(reported.SubsetShearBoundKn,.95,"subset shear and reaction bound agree with equilibrium");
        Check(!CrossBeamSupportOptimizer.Envelope(boundCommon,baseMember,increments,.949,reported),"subset leg bound rejects a capacity below the combined reaction");
        var common=Common();var input=Input();
        // At 200 mm only the centre row fits the 400 mm footprint including the
        // 80 mm flange. Its full-length H20 fails L/350 at the stock overhang;
        // additional parallel members, rather than a relaxed limit, are needed.
        var sparse=Input();sparse.UpperSpacingMm=200;
        bool sparseFailed=false;
        try {TransferNetworkOptimizer.Optimize(common,sparse);}
        catch(TowerOptimizationException e) {sparseFailed=true;Check(e.Details.Contains("X1 H20 1 · deflection"),"single full-width H20 rejection retains the actual cross-member deflection failure");}
        Check(sparseFailed,"one H20 at the original 200 mm row spacing cannot support the 400 mm cross beam");
        var widths=TransferNetworkOptimizer.EffectiveWidthsMm(common,input,0,2450,0);
        Near(widths.Sum(),2050,"slab footprint removed once");
        var result=TransferNetworkOptimizer.Optimize(common,input);var cross=result.CrossSupports.Single();
        Check(result.Bays.All(b=>Math.Abs(b.NearFootMm-b.FarFootMm)<1e-7),"shared cross support is evaluated with symmetric slab feet");
        Check(cross.Arrangement==CrossBeamSupportMode.ExistingSupportsOnly && cross.Towers==null,"shared arrangement accepted with no new towers");
        Check(cross.Members.Length==3 && cross.Members.All(m=>m.Utilization<=1+1e-8),"100 mm receiving rows provide three individually passing parallel H20s for the 400 mm cross beam");
        Check(cross.Bearings.All(bearing=>Math.Abs(bearing.FillerHeightMm)<1e-7),"both zero gaps are direct bearings");
        Near(cross.LoadKn,input.AreaLoad(150)*.4*3.9,"cross load has no slab tributary");
        Near(result.SlabLoadKn,input.AreaLoad(100)*2.05*3.9,"remaining slab load conserved");
        Near(result.Beams.Sum(l=>l.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn)),input.Beams.Sum(i=>input.AreaLoad(i.DepthMm)*i.WidthMm/1000*2.45)+result.SlabLoadKn+cross.LoadKn,"load transferred through secondaries and primary pairs to main legs");
        Check(result.Beams.All(l=>l.MaxLegKn<=70 && l.Transfer.PrimaryCases.Concat(l.Transfer.LowerCases).SelectMany(c=>c).All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.All(r=>r>=-1e-7))),"all receiving members, uplift and individual legs checked");
        CheckPrimaryFootprints(result);
        Near(result.H20Count,result.Beams.Sum(l=>l.StockPlan.TotalH20Count+l.Transfer.RowsMm.Length)+result.SlabRowCount(0)+cross.H20Count,"shared cross quantities added and excluded slab H20s removed");
        Check(result.SlabRowCount(0)<result.Beams[0].Transfer.RowsMm.Length && result.UpperFootCount==2*result.SlabRowCount(0),"slab H20s and feet removed physically within footprint");
        Check(result.Schedule.Contains("direct bearing") && result.Schedule.Contains("not verified") && result.Schedule.Contains("bottom -400"),"report contains elevations, bearing and filler limitations");
        var narrowInput=Input();narrowInput.CrossingBeams[0].WidthMm=100;
        var narrow=TransferNetworkOptimizer.Optimize(common,narrowInput);
        Check(narrow.CrossSupports[0].Members.Length==1,"one parallel H20 is accepted when its actual cross tributary passes");
        Near(narrow.CrossSupports[0].LoadKn,input.AreaLoad(150)*.1*3.9,"single H20 carries only its actual narrow cross-beam load");
        var shallowInput=Input();shallowInput.CrossingBeams[0].DepthMm=50;
        var shallow=TransferNetworkOptimizer.Optimize(common,shallowInput);
        Near(shallow.CrossSupports[0].LoadKn,input.AreaLoad(50)*.4*3.9,"modern cross depth is independent of slab thickness and retains its own load");
        var wideInput=Input();wideInput.CrossingBeams[0].WidthMm=800;
        var wide=TransferNetworkOptimizer.Optimize(common,wideInput);
        Check(wide.CrossSupports[0].Members.Length>=2 && wide.CrossSupports[0].Members.All(m=>m.H20Count==1),"multiple parallel cross H20s selected and individually checked");
        Near(wide.CrossSupports[0].LoadKn,input.AreaLoad(150)*.8*3.9,"multiple H20 tributaries conserve the cross concrete load");
        var unequal=Input(bottom: -250);unequal.Beams[1].BottomElevationMm=-1000;
        var unequalResult=TransferNetworkOptimizer.Optimize(common,unequal);
        Near(unequalResult.CrossSupports[0].Bearings.First(bearing=>bearing.Beam==0).FillerHeightMm,150,"left filler independently calculated");
        Near(unequalResult.CrossSupports[0].Bearings.First(bearing=>bearing.Beam==1).FillerHeightMm,550,"unequal right soffit gives different filler");
        Failed(common,Input(bottom: -401),"negative gap fails Existing supports only");
        Failed(common,Input(CrossBeamSupportMode.DedicatedTowers,-1000),"cross concrete intersecting existing main primaries is rejected");
        Failed(common,Input(CrossBeamSupportMode.DedicatedTowers,-1400),"zero end allowance cannot meet the shutter gap when receiving H20s are above the cross soffit");
        var dedicatedCommon=Common();dedicatedCommon.MaximumConcreteEndOverhangMm=300;
        var dedicated=TransferNetworkOptimizer.Optimize(dedicatedCommon,Input(CrossBeamSupportMode.DedicatedTowers,-1400));
        Check(dedicated.CrossSupports[0].Towers!=null,"requested dedicated tower arrangement calculated");
        Check(dedicated.LegCount==dedicated.Beams.Sum(l=>l.LegCount)+dedicated.CrossSupports[0].Towers.LegCount,"dedicated legs included in ranking and totals");
        Check(dedicated.CrossSupports[0].Members.All(m=>m.Utilization<=1+1e-8 && m.Analysis.Reactions.All(r=>r>=-1e-7)),"dedicated single secondaries and primary pairs checked");
        Near(dedicated.CrossSupports[0].LoadKn,input.AreaLoad(150)*.4*3.9,"dedicated clear-bay concrete load conserved");
        var fallbackInput=Input(CrossBeamSupportMode.Automatic,-1400);
        var fallback=TransferNetworkOptimizer.Optimize(dedicatedCommon,fallbackInput);
        Check(fallback.CrossSupports[0].Towers!=null && fallback.CrossSupports[0].SharedFailure.Contains("negative filler"),"Automatic falls back to towers for negative gap");
        var capacityCommon=Common();capacityCommon.Exclude600Standalone=true;
        // The two checked shutter strips leave an exact 3500 mm central span.
        capacityCommon.StockLengthsMm=capacityCommon.StockLengthsMm.Concat(new[] {3500.0}).Distinct().ToArray();
        var capacityInput=Input(CrossBeamSupportMode.Automatic,-600);foreach(var beam in capacityInput.Beams) {beam.WidthMm=1100;beam.DepthMm=1750;}
        capacityInput.CrossingBeams[0].WidthMm=600;capacityInput.CrossingBeams[0].DepthMm=600;
        var capacityFallback=TransferNetworkOptimizer.Optimize(capacityCommon,capacityInput);
        Check(capacityFallback.CrossSupports[0].Towers!=null && capacityFallback.CrossSupports[0].SharedFailure!=null && !capacityFallback.CrossSupports[0].SharedFailure.Contains("negative filler"),"Automatic tower fallback after shared structural failure with a positive gap");
        var multiple=Input();multiple.CrossingBeams=new[] {new TransferCrossingBeamRow {PositionMm=825,WidthMm=400,DepthMm=150,BottomElevationMm=-400,SupportMode=CrossBeamSupportMode.ExistingSupportsOnly},new TransferCrossingBeamRow {PositionMm=1625,WidthMm=400,DepthMm=150,BottomElevationMm=-400,SupportMode=CrossBeamSupportMode.ExistingSupportsOnly}};
        var combined=TransferNetworkOptimizer.Optimize(common,multiple);
        Check(combined.Bays.All(b=>Math.Abs(b.NearFootMm-b.FarFootMm)<1e-7),"multiple cross stages retain the repeated symmetric slab arrangement");
        Check(combined.CrossSupports.Length==2 && combined.Beams.All(l=>l.Transfer.CaseNames.Any(c=>c.Contains("X1 + X2"))),"multiple crossings checked alone and together");
        CheckPrimaryFootprints(combined);
        var overloaded=Input();overloaded.CrossingBeams[0].DepthMm=10000;Failed(common,overloaded,"cross H20 overload cannot be accepted");
        var weak=Common();weak.LegCapacityKn=.1;Failed(weak,Input(),"low leg capacity rejects layout");
        var clash=Input(CrossBeamSupportMode.DedicatedTowers,-750);Failed(common,clash,"cross stock / slab or concrete overlap rejects layout");
        var stock=Common();stock.StockLengthsMm=new[] {1250.0,1800,2450};Failed(stock,Input(),"missing cross H20 stock is a clear failure");
        stock.AllowCustomH20Lengths=true;var custom=TransferNetworkOptimizer.Optimize(stock,Input());Check(custom.CrossSupports[0].Members.All(m=>m.LengthMm%25==0),"permitted custom cross stock uses 25 mm increments");
        var vm=new BeamCalculatorViewModel {UseBeamNetwork=true};vm.AddCrossingBeam();Check(vm.CrossingBeams[0].SupportMode==CrossBeamSupportMode.Automatic,"new crossing defaults Automatic");
        vm.NetworkBeams[0].BottomElevationMm=-900;vm.CrossingBeams[0].BottomElevationMm=-100;vm.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        string xml=vm.NetworkConfigurationXml;var restored=new BeamCalculatorViewModel();restored.NetworkConfigurationXml=xml;
        Near(restored.NetworkBeams[0].BottomElevationMm,-900,"saved main soffit round trip");Near(restored.CrossingBeams[0].BottomElevationMm,-100,"saved cross soffit round trip");Check(restored.CrossingBeams[0].SupportMode==CrossBeamSupportMode.DedicatedTowers,"saved support mode round trip");
        string legacy=Regex.Replace(xml,@"<(SupportFormatVersion|SupportMode|BottomElevationMm)>.*?</\1>","");restored.NetworkConfigurationXml=legacy;Check(restored.CrossingBeams[0].SupportMode==CrossBeamSupportMode.LegacySlabRows,"old saved crossing explicitly migrated to legacy");
        restored.NetworkConfigurationXml=restored.NetworkConfigurationXml;Check(restored.CrossingBeams[0].SupportMode==CrossBeamSupportMode.LegacySlabRows,"legacy survives resave");restored.AddCrossingBeam();Check(restored.CrossingBeams.Last().SupportMode==CrossBeamSupportMode.Automatic,"new crossing in legacy document defaults Automatic");
        Console.WriteLine($"Cross supports: {checks} checks passed.");
    }
}
