using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class PrimaryBearingTests
{
    static void Check(bool ok,string name){if(!ok)throw new Exception("Primary bearing: "+name);}
    static void Near(double a,double b,string name)=>Check(Math.Abs(a-b)<1e-7*Math.Max(1,Math.Abs(b)),name+": "+a+" / "+b);
    internal static TransferNetworkResult Fixture()
    {
        var beam=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150,BottomElevationMm=-1150},new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}};
        var crossing=new TransferCrossingBeamRow {BayNumber=1,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150};
        var network=new TransferNetworkInput {PlywoodSheetLengthMm=2440,Beams=beam,Bays=new[] {new TransferBayRow {WidthMm=1875,ThicknessMm=300}},CrossingBeams=new[] {crossing},UpperSpacingMm=305};
        var layouts=Enumerable.Range(0,2).Select(i=>new TowerLayout {LengthMm=6900,WidthMm=1500,PositionsMm=new[] {750.0,2250,3750,5250},Joined=new[] {true,true,true},
            StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=6900,FirstSupport=0,LastSupport=3}}},
            Transfer=new TransferredSlabResult {RowsMm=new[] {150.0,1400,2450,3180,3485,3790,4900,6100,6750},LowerStockStartMm=i==0 ? -900 : -1225,LowerStockLengthMm=i==0 ? 1800 : 2450,EnteredUpperSpacingMm=305,UpperMembers=Array.Empty<TransferMember>()}}).ToArray();
        return new TransferNetworkResult {Input=network,Beams=layouts,Bays=new UpperTransferPlan[] {null},BeamCentresMm=new[] {0.0,2600}};
    }
    internal static void Run()
    {
        var r=Fixture();var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,StockEndSetbackMm=150,Exclude600Standalone=true,DeflectionDenominator=270,LegCapacityKn=70};
        Check(CrossBeamSupportOptimizer.PrimaryBearingPossible(input,r.Input,r.Input.CrossingBeams[0]),"new path survives dedicated-stock screen");
        var support=CrossBeamSupportOptimizer.PrimaryBearing(input,r,0,out string failure);Check(support!=null,"1800 stock supported: "+failure);
        Check(support.Towers==null && support.UsesPrimaryBearings,"no new tower");Check(support.Members.All(m=>m.LengthMm==1800),"uses 1800 catalogue secondary");
        Check(support.Bearings.Where(b=>b.IsPrimaryBearing && b.Beam==0).All(b=>b.PrimaryLine==1 && b.FillerHeightMm==0),"direct B2-equivalent inner primary");
        Check(support.Bearings.Where(b=>b.IsPrimaryBearing && b.Beam==1).All(b=>b.PrimaryLine==0 && b.FillerHeightMm==600),"600 high transfer to deeper primary");
        Near(support.Bearings.Sum(b=>b.ReactionKn),support.LoadKn,"all central and end-strip load conserved");
        Near(support.LoadKn,r.Input.AreaLoad(1150)*.65*1.875,"concrete footprint conserved");
        foreach(var m in support.Members)
        {
            Check(m.Utilization<=1 && m.Analysis.DeflectionEvaluated,"secondary strength and deflection checked");
            Near(m.Analysis.Reactions.Sum(),m.Analysis.TotalLoadKn,"secondary equilibrium");
            Near(m.StartMm+m.SupportsMm[0],750,"near primary axis");Near(m.StartMm+m.SupportsMm[1],1850,"far primary axis");
            Check(m.SupportsMm[0]>=150 && m.LengthMm-m.SupportsMm[1]>=150,"both stock end setbacks checked");
        }
        Check(support.Solids.All(s=>s.Member!=null),"box-out has no drawn solid");
        var prepared=Fixture();foreach(var l in prepared.Beams)l.StockPlan.Pieces=Array.Empty<TowerStockPiece>();prepared.Bays[0]=new UpperTransferPlan();
        Check(CrossBeamSupportOptimizer.PrimaryBearing(input,prepared,0,out failure)!=null,"transverse-only receiver probe does not read unassembled slab scales");
        var jointFixture=Fixture();
        foreach(var l in jointFixture.Beams)l.StockPlan.Pieces=new[] {new TowerStockPiece {StockLengthMm=3450},new TowerStockPiece {StartMm=3450,StockLengthMm=3450}};
        var atJoint=CrossBeamSupportOptimizer.PrimaryBearing(input,jointFixture,0,out failure);
        Check(atJoint!=null && atJoint.SecondaryStationsMm.All(x=>Math.Abs(x-3450)>=40),"secondary flange avoids a primary stock joint");
        var tooShort=Fixture();foreach(var l in tooShort.Beams)l.WidthMm=1000;
        Check(CrossBeamSupportOptimizer.PrimaryBearing(input,tooShort,0,out failure)==null,"stock cannot omit either actual end setback");
        var overloaded=Fixture();overloaded.Input.CrossingBeams[0].DepthMm=20000;
        Check(CrossBeamSupportOptimizer.PrimaryBearing(input,overloaded,0,out failure)==null,"additional H20s cannot bypass capacity limits");
        r.CrossSupports=new[] {support};
        var protectedRow=typeof(CrossBeamSupportOptimizer).GetMethod("ProtectedRow",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Check(!(bool)protectedRow.Invoke(null,new object[] {r,0,3,3180.0,true}),"primary-bearing layout can adjust a local receiving row");
        r.CrossSupports=new[] {support,new CrossBeamSupportResult {Bearings=new[] {new CrossBeamBearing {Beam=0,Row=3}}}};
        Check((bool)protectedRow.Invoke(null,new object[] {r,0,3,3180.0,true}),"another crossing's required receiving row remains protected");
        r.CrossSupports=new[] {support};
        Populate(r,input);input.Diagnostics=new TowerSearchDiagnostics(270);
        double beforePrimary=r.Input.Beams.Sum(b=>r.Input.AreaLoad(b.DepthMm)*b.WidthMm/1000)*6.9;
        double beforeSecondary=r.Beams.Sum(l=>l.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn));
        var originals=r.Beams;
        var recheck=typeof(CrossBeamSupportOptimizer).GetMethod("Recheck",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        bool passed=(bool)recheck.Invoke(null,new object[] {input,r,originals,new[] {support},false,false});if(!passed)Console.WriteLine(input.Diagnostics.Exception("Primary-bearing fixture").Details);Check(passed,"resolved primary/member/leg checks pass");
        Near(r.Beams.Sum(l=>l.Transfer.PrimaryCases[0].Sum(m=>m.Analysis.TotalLoadKn))-beforePrimary,support.LoadKn,"primary receives the full crossing exactly once");
        Near(r.Beams.Sum(l=>l.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn))-beforeSecondary,support.Bearings.Where(b=>!b.IsPrimaryBearing).Sum(b=>b.ReactionKn),"secondary receives only end strips");
        r.Input.CrossingBeams[0].SupportMode=CrossBeamSupportMode.DedicatedTowers;
        Check(!CrossBeamSupportOptimizer.PrimaryBearingPossible(input,r.Input,r.Input.CrossingBeams[0]),"explicit dedicated preference preserved");
        r.Input.CrossingBeams[0].SupportMode=CrossBeamSupportMode.Automatic;r.Input.Beams[0].BottomElevationMm=-1000;
        Check(!CrossBeamSupportOptimizer.PrimaryBearingPossible(input,r.Input,r.Input.CrossingBeams[0]),"negative primary bearing gap rejected");
        Console.WriteLine("PASS primary-bearing stock, elevations, secondary checks and force conservation");
    }
    static void Populate(TransferNetworkResult r,TowerInput common)
    {
        common.LengthMm=6900;
        for(int i=0;i<2;i++)
        {
            var l=r.Beams[i];var t=l.Transfer;var b=r.Input.Beams[i];
            l.PositionsMm=new[] {450.0,1450,2450,3450,4450,5450,6450};l.Joined=new[] {true,true,true,true,true,true};l.LegCapacityKn=70;l.DeflectionDenominator=270;
            l.StockPlan.Pieces[0].LastSupport=6;
            var unit=new TransferMember {H20Count=1,Name="Unit",StartMm=t.LowerStockStartMm,LengthMm=t.LowerStockLengthMm,SupportsMm=new[] {-750-t.LowerStockStartMm,750-t.LowerStockStartMm},
                Uniform=new[] {new BeamDistributedLoad {StartMm=-b.WidthMm/2-t.LowerStockStartMm,EndMm=b.WidthMm/2-t.LowerStockStartMm,KnPerM=r.Input.AreaLoad(b.DepthMm)}},Points=Array.Empty<BeamPointLoad>()};
            t.PrimaryPlan=EngineerPrimaryPlan.Create(common,r.Input,i,false,false).WithGeometry(common,unit,Array.Empty<string>());
            t.CaseNames=new[] {"B"+(i+1)};t.FullLoadingOnly=true;
            t.LowerCases=new[] {t.RowsMm.Select((x,k)=>
            {
                var m=new TransferMember {H20Count=1,Name="T"+(k+1),StartMm=unit.StartMm,LengthMm=unit.LengthMm,SupportsMm=unit.SupportsMm,Uniform=unit.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*.305}).ToArray(),Points=unit.Points};
                m.Analysis=LoadedBeamAnalysis.AnalyzeSigned(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,m.EI,270);return m;
            }).ToArray()};
            t.PrimaryCases=new[] {Enumerable.Range(0,2).Select(line=>
            {
                var m=new TransferMember {Name="P1 "+(line==0 ? "L" : "R"),H20Count=2,LengthMm=6900,SupportsMm=l.PositionsMm,Points=Array.Empty<BeamPointLoad>(),Uniform=t.PrimaryPlan.Loads(0,6900,line)};
                m.Analysis=LoadedBeamAnalysis.AnalyzeSigned(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,m.EI,270);return m;
            }).ToArray()};
        }
    }
    internal static void Benchmark(string[] args)
    {
        var vm=ControlledPerformanceTests.Model(args.Contains("--two") ? "eight-x2" : "eight-crossings");vm.AllowCustomH20Lengths=false;vm.PlywoodDeflectionLimit="L/350";
        var common=(TowerInput)typeof(BeamCalculatorViewModel).GetMethod("CreateTowerInput",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(vm,new object[] {true});
        int seconds=args.Contains("--prefix") ? 90 : 0;
        using(var cancel=new CancellationTokenSource())
        {
            if(seconds>0)cancel.CancelAfter(TimeSpan.FromSeconds(seconds));common.Trace=new BeamSearchTrace("Uploaded 2026-10-22; primary bearings",cancel.Token);
            if(args.Contains("--first"))typeof(TowerInput).GetField("FirstCompleteNetworkOnly",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(common,true);
            var watch=Stopwatch.StartNew();TransferNetworkResult result=null;
            var task=System.Threading.Tasks.Task.Run(()=>TransferNetworkOptimizer.Optimize(common,common.Network));
            try
            {
                while(!task.IsCompleted){Thread.Sleep(100);if((int)watch.Elapsed.TotalSeconds/10>secondsReported){secondsReported=(int)watch.Elapsed.TotalSeconds/10;Console.WriteLine(common.Trace.Snapshot());}}
                result=task.GetAwaiter().GetResult();
            }
            catch(Exception ex){Console.WriteLine(ex);if(ex is TowerOptimizationException rejected)Console.WriteLine(rejected.Details);throw;}
            finally{common.Trace.Complete(result==null ? "Failed or cancelled" : "Succeeded");Console.WriteLine("BENCHMARK wall="+watch.Elapsed.TotalSeconds.ToString("0.000")+" runtime="+Environment.Version+" result="+(result!=null));Console.WriteLine(common.Trace.Snapshot());}
            ControlledPerformanceTests.Validate(common,result);
            Check(result.CrossSupports.Length==common.Network.CrossingBeams.Length,"every crossing resolved");int selected=args.Contains("--two") ? 0 : 1;Check(result.CrossSupports[selected].UsesPrimaryBearings,"X2 uses the new path");
            Check(result.CrossSupports[selected].Members.All(m=>common.StockLengthsMm.Contains(m.LengthMm)),"crossing uses permitted catalogue secondaries");
            Console.WriteLine(result.Summary);Console.WriteLine(result.Schedule);Console.WriteLine(ControlledPerformanceTests.Fingerprint(result));
        }
    }
    static int secondsReported;
}
