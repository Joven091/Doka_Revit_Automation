using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class CrossingLocalReuseTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Crossing local reuse: "+message);}
    static string Signature(CrossBeamSupportResult s)=>s==null ? "none" : string.Join(";",s.SecondaryStationsMm)
        +"|"+string.Join(";",s.Members.Select(m=>m.LengthMm+":"+string.Join(",",m.SupportsMm)+":"+string.Join(",",m.Analysis.Reactions)))
        +"|"+string.Join(";",s.Bearings.Select(b=>b.Beam+","+b.Row+","+b.Ymm+","+b.ReactionKn+","+b.IsPrimaryBearing+","+b.IsShutterEndZone));
    internal static void Run()
    {
        Rows();Keys();Reuse();ProjectStockModes();
        Console.WriteLine("Passed "+checks+" local crossing reuse, row placement and project stock checks.");
    }
    static void Rows()
    {
        var method=typeof(CrossBeamSupportOptimizer).GetMethod("PrimaryBearingRows",BindingFlags.NonPublic|BindingFlags.Static);
        var random=new Random(105022);
        for(int sample=0;sample<400;sample++)
        {
            var r=PrimaryBearingTests.Fixture();var c=r.Input.CrossingBeams[0];
            if(sample%3==0)foreach(var b in r.Beams)b.StockPlan.Pieces=new[] {new TowerStockPiece {StockLengthMm=3450},new TowerStockPiece {StartMm=3450,StockLengthMm=3450}};
            var obstacles=Enumerable.Range(0,25).Select(i=>
            {
                double x=c.PositionMm-450+random.NextDouble()*900,y=300+random.NextDouble()*2800,z=-1800+random.NextDouble()*1200;
                return new FormworkSolid {X0=x,X1=x+20+random.NextDouble()*80,Y0=y,Y1=y+50+random.NextDouble()*400,Z0=z,Z1=z+40+random.NextDouble()*300,
                    IsTowerEnvelope=i%7==0,IsLeg=i%5==0,TowerOwner="fixture"};
            }).Concat(new[] {new FormworkSolid {X0=c.PositionMm+c.WidthMm/2-1e-7,X1=c.PositionMm+c.WidthMm/2+80,Y0=400,Y1=2200,Z0=c.BottomElevationMm-200,Z1=c.BottomElevationMm}}).ToArray();
            int count=2+sample%7;double start=400,stock=1800;
            var expected=ReferenceRows(r,count,start,stock,obstacles);
            var actual=(double[])method.Invoke(null,new object[] {r,0,count,start,stock,obstacles});
            Check(expected==null ? actual==null : actual!=null && expected.SequenceEqual(actual),"swept obstacle filtering preserves exact rows, flange/joint clearances and contact ordering");
        }
    }
    // Original exhaustive row placement is the reference for the broad phase.
    static double[] ReferenceRows(TransferNetworkResult r,int count,double start,double stock,FormworkSolid[] obstacles)
    {
        var c=r.Input.CrossingBeams[0];double max=c.WidthMm/2-40;
        Func<double,bool> clear=x=>new[] {c.BayNumber-1,c.BayNumber}.All(b=>r.Beams[b].StockPlan.Pieces.Length==0
            || r.Beams[b].StockPlan.Pieces.Any(p=>x>=p.StartMm+40-1e-7 && x<=p.EndMm-40+1e-7))
            && !obstacles.Any(s=>new FormworkSolid {X0=x-40,X1=x+40,Y0=start,Y1=start+stock,Z0=c.BottomElevationMm-200,Z1=c.BottomElevationMm}.Conflicts(s));
        var rows=new System.Collections.Generic.List<double>();if(count%2==1){if(!clear(c.PositionMm))return null;rows.Add(c.PositionMm);}
        var offsets=Enumerable.Range(0,1+(int)Math.Ceiling(max/25)).Select(k=>Math.Min(max,k*25.0)).Concat(new[] {max})
            .Concat(obstacles.SelectMany(s=>new[] {Math.Abs(s.X0-40-c.PositionMm),Math.Abs(s.X1+40-c.PositionMm)})).Where(d=>d<=max+1e-7).Distinct().ToArray();
        for(int pair=0;pair<count/2;pair++)
        {
            double desired=max*(count%2==1 ? (pair+1.0)/(count/2) : (2*pair+1.0)/(count-1));
            var choices=offsets.Where(d=>d>=40 && rows.All(x=>Math.Abs(x-(c.PositionMm-d))>=80-1e-7 && Math.Abs(x-(c.PositionMm+d))>=80-1e-7)).OrderBy(d=>Math.Abs(d-desired)).ThenBy(d=>d);
            double selected=choices.FirstOrDefault(d=>clear(c.PositionMm-d) && clear(c.PositionMm+d));
            if(selected<=0)return null;rows.Add(c.PositionMm-selected);rows.Add(c.PositionMm+selected);
        }
        var result=rows.OrderBy(x=>x).ToArray();double limit=r.Input.CrossSpacing(0);
        return result[0]-(c.PositionMm-c.WidthMm/2)<=limit+1e-7 && c.PositionMm+c.WidthMm/2-result.Last()<=limit+1e-7
            && result.Zip(result.Skip(1),(a,b)=>b-a).All(g=>g<=limit+1e-7) ? result : null;
    }
    static void Keys()
    {
        var r=PrimaryBearingTests.Fixture();var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true};
        var local=new FormworkSolid {X0=3400,X1=3480,Y0=1000,Y1=1500,Z0=-1300,Z1=-1200};
        var remote=new FormworkSolid {X0=3350,X1=3430,Y0=10000,Y1=10100,Z0=-1300,Z1=-1200};
        Func<PrimaryBearingKey> key=()=>new PrimaryBearingKey(common,r,0,new[] {local,remote});var original=key();
        remote.Y0+=50;remote.Y1+=50;Check(original.Equals(key()),"remote geometry with identical row contacts reuses local work");
        remote.X0+=.000000001;Check(!original.Equals(key()),"even a remote exact contact-offset change invalidates reuse");remote.X0-=.000000001;
        local.Y0+=.000000001;Check(!original.Equals(key()),"precise local obstacle coordinate invalidates reuse");local.Y0-=.000000001;
        common.AllowCustomH20Lengths=true;Check(!original.Equals(key()),"project stock permission is part of the key");common.AllowCustomH20Lengths=false;
        r.Beams[0].WidthMm+=25;Check(!original.Equals(key()),"primary bearing axis invalidates reuse");r.Beams[0].WidthMm-=25;
        r.Beams[0].Transfer.RowsMm[0]+=25;Check(!original.Equals(key()),"receiving-row mapping invalidates reuse");r.Beams[0].Transfer.RowsMm[0]-=25;
        r.Input.CrossingBeams[0].DepthMm+=25;Check(!original.Equals(key()),"crossing load invalidates reuse");r.Input.CrossingBeams[0].DepthMm-=25;
        r.Input.UpperSpacingMm-=25;Check(!original.Equals(key()),"crossing spacing invalidates reuse");r.Input.UpperSpacingMm+=25;
        r.Beams[0].StockPlan.Pieces[0].StockLengthMm-=25;Check(!original.Equals(key()),"stock joint/end invalidates reuse");
        var ledger=PrimaryBearingTests.Fixture();ledger.Bays[0]=new UpperTransferPlan();ledger.SlabCoveredLengthMm=6900;
        ledger.Input.BeamSpacingsMm=new[] {305.0,305};ledger.Input.SlabSpacingsMm=new[] {488.0};ledger.Input.CrossingSpacingsMm=new[] {305.0};
        var ledgerKey=new PrimaryBearingKey(common,ledger,0,Array.Empty<FormworkSolid>());
        Check(CrossBeamSupportOptimizer.SlabEdgeLoads(ledger,0).Length>0,"ledger fixture includes an actual slab-edge transfer");
        ledger.Input.Bays[0].ThicknessMm+=25;
        Check(!ledgerKey.Equals(new PrimaryBearingKey(common,ledger,0,Array.Empty<FormworkSolid>())),"changed slab-edge ledger loading invalidates reuse");
        var cache=new CrossGeometryCache();
        for(int i=0;i<600;i++)
        {r.Input.CrossingBeams[0].PositionMm=1000+i;cache.PrimaryResults.Store(new PrimaryBearingKey(common,r,0,Array.Empty<FormworkSolid>()),new PrimaryBearingSearch());}
        Check(cache.PrimaryResults.Count==512,"local crossing reuse retains a fixed memory bound");
    }
    static void Reuse()
    {
        foreach(bool overloaded in new[] {false,true})
        {
            var r=PrimaryBearingTests.Fixture();if(overloaded)r.Input.CrossingBeams[0].DepthMm=20000;
            var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,DeflectionDenominator=270,StockEndSetbackMm=150,
                CrossingGeometryWork=new CrossGeometryCache(),Diagnostics=new TowerSearchDiagnostics(270),Trace=new BeamSearchTrace("local crossing reuse")};
            common.ReuseCrossingLocalWork=false;var reference=CrossBeamSupportOptimizer.PrimaryBearing(common,r,0,out string expectedFailure);
            common.ReuseCrossingLocalWork=true;var first=CrossBeamSupportOptimizer.PrimaryBearing(common,r,0,out string firstFailure);
            common.Diagnostics=new TowerSearchDiagnostics(270);var second=CrossBeamSupportOptimizer.PrimaryBearing(common,r,0,out string secondFailure);
            Check(Signature(reference)==Signature(first) && Signature(first)==Signature(second) && expectedFailure==firstFailure && firstFailure==secondFailure,"cached success/failure preserves checked rows, reactions, source mapping and failure details");
            Check(common.Trace.Count(BeamSearchCounter.PrimaryBearingCacheHits)==1,"identical local calculation reuses its checked result");
            if(second!=null){Check(!ReferenceEquals(first,second),"callers receive distinct support result objects");second.MovementMm=123;Check(first.MovementMm==0,"movement updates do not mutate another result");}
        }
    }
    static void ProjectStockModes()
    {
        var reported=ControlledPerformanceTests.Input("eight-catalogue");
        Check(!reported.AllowCustomH20Lengths && reported.Network.Beams.Length==8 && reported.Network.CrossingBeams.Length==7 && reported.Network.PlywoodDeflectionDenominator==350,"exact uploaded catalogue-only model is retained");
        foreach(bool custom in new[] {false,true})
        {
            var input=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,CoordinateCrossingLayouts=true,LengthMm=2450,
                StockLengthsMm=new[] {1250.0,1800,2450,2900},LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=175,
                DeflectionDenominator=270,IgnoreUplift=true,NetworkSearchWorkers=1,AllowSeparateSlabFallback=false,AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.SlabSupports};
            var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
                BeamSpacingsMm=new[] {305.0,305,305},SlabSpacingsMm=new[] {488.0,488},CrossingSpacingsMm=new[] {350.0,350},
                Beams=Enumerable.Range(0,3).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=800,BottomElevationMm=-800}).ToArray(),
                Bays=Enumerable.Range(0,2).Select(i=>new TransferBayRow {WidthMm=2200,ThicknessMm=200}).ToArray(),
                CrossingBeams=Enumerable.Range(1,2).Select(i=>new TransferCrossingBeamRow {BayNumber=i,PositionMm=1225,WidthMm=650,DepthMm=600,BottomElevationMm=-600}).ToArray()};
            var result=TransferNetworkOptimizer.Optimize(input,network);ControlledPerformanceTests.Validate(input,result);
            Check(input.AllowCustomH20Lengths==custom && input.CustomH20Scope==CustomH20LengthScope.SlabSupports,"fast search preserves the project's custom-stock setting and scope");
            Check(result.Beams.All(b=>b.StockPlan.Pieces.All(p=>input.StockLengthsMm.Contains(p.StockLengthMm)) && input.StockLengthsMm.Contains(b.Transfer.LowerStockLengthMm)),"slab-only scope keeps all main primary and beam secondary lengths on the catalogue");
        }
    }
}
