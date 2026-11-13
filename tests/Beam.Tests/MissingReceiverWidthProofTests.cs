using System;
using System.Linq;
using System.Threading;
using EngineeringDashboard;

internal static class MissingReceiverWidthProofTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Missing receiver width proof: "+message);}
    static TransferNetworkInput Network(double length=2450)=>new TransferNetworkInput {
        PlywoodSheetLengthMm=2440,UpperSpacingMm=305,
        Beams=Enumerable.Range(0,2).Select(_=>new TransferBeamRow {WidthMm=400,DepthMm=800,BottomElevationMm=-800}).ToArray(),
        Bays=new[] {new TransferBayRow {WidthMm=500,ThicknessMm=100}},
        CrossingBeams=new[] {new TransferCrossingBeamRow {BayNumber=1,PositionMm=length/2,WidthMm=200,DepthMm=300,BottomElevationMm=-300,
            SupportMode=CrossBeamSupportMode.Automatic}}};
    static TowerInput Input(double length=2450,int ceiling=8)
    {
        var input=new TowerInput {LengthMm=length,EngineerSearch=true,FullLoadingOnly=true,LegCapacityKn=70,IgnoreUplift=true,
            StockLengthsMm=new[] {length},StockEndSetbackMm=150,DeflectionDenominator=270,MaximumConcreteEndOverhangMm=0,
            NetworkLegCeiling=ceiling,Trace=new BeamSearchTrace("missing width proof"),CoordinateCrossingLayouts=false};
        input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,BeamWidthMm=400,BeamLineLoadKnM=3,UpperSpacingMm=305,PlywoodSheetLengthMm=2440,
            FixedCoveredLengthMm=length,StockCovers=new[] {new[] {length}},EngineerNetwork=Network(length),EngineerBeam=0,
            SharedGeometryCache=new TransferredSlabOptimizer.GeometryCache()};
        input.EngineerPlan=EngineerPrimaryPlan.Create(input,input.TransferredSlab.EngineerNetwork,0,false,false);
        input.EngineerPlan.InitialLegs=4;return input;
    }
    static TowerLayout Winner(int count=8,int stations=4)=>new TowerLayout {WidthMm=1524,PositionsMm=new double[stations],
        Transfer=new TransferredSlabResult {EngineerReceiverCount=count}};
    static TransferNetworkOptimizer.ReceiverWidthAlternatives Alternatives(TowerInput input,TowerLayout winner)
    {
        var alternatives=new TransferNetworkOptimizer.ReceiverWidthAlternatives();alternatives.Add(winner.WidthMm,winner);
        input.ReceiverAlternatives=alternatives;return alternatives;
    }
    static void Scope()
    {
        var input=Input();var selected=Winner();var alternatives=Alternatives(input,selected);
        Check(alternatives.Complete(input,input,selected,true,false),"a fresh complete final-count search at its entire allowed ceiling certifies missing centred widths");
        Check(alternatives.ThroughLegs==8,"proof records the actually covered leg budget");
        foreach(double width in new[] {1000.0,1500,2000})
        {
            Check(alternatives.Rejects(width,8,input.Trace),"missing exact width is rejected at the exhausted ceiling");
            Check(alternatives.Rejects(width,6,input.Trace),"a tighter subset can consume the completed proof");
            Check(!alternatives.Rejects(width,10,input.Trace),"a higher ceiling cannot consume the proof");
        }
        Check(!alternatives.Rejects(1524,8,input.Trace),"the selected width is never a missing-width failure");
        Check(!alternatives.Rejects(PortableTestRuntime.NextUp(1000.0),8,input.Trace),"unrecognized widths do not inherit a rounded failure");
        alternatives.Add(1000,Winner());Check(!alternatives.Rejects(1000,8,input.Trace),"a retained passing alternative always wins over failure evidence");
        alternatives.Clear();Check(!alternatives.Rejects(1524,8,input.Trace),"later dictionary clearing cannot invent a failure for the original winner");
        Check(!alternatives.Complete(input,input,selected,false,false) && !alternatives.Rejects(1500,8,input.Trace),"an incomplete replacement clears the previous proof");

        Action<TowerInput>[] excluded={
            t=>t.EngineerSearch=false,t=>t.LocalNetworkSearch=false,t=>t.ReuseSearchWork=false,t=>t.ReuseReceiverSearches=false,
            t=>t.OptimizeCrossingSearch=false,t=>t.FirstCompleteNetworkOnly=true,t=>t.EngineerPlan=null,
            t=>t.Trace=new BeamSearchTrace("unexpired explicit deadline",searchTimeLimit:TimeSpan.FromHours(1)),
            t=>t.NetworkSeedCompletion=new NetworkSeedCompletion(),t=>t.ExternalObstacles=new[] {new FormworkSolid()},
            t=>t.TransferredSlab.FixedTowerWidthMm=1524,t=>t.ReceiverTransverseOffsetMm=25
        };
        for(int i=0;i<excluded.Length;i++)
        {
            input=Input();selected=Winner();alternatives=Alternatives(input,selected);excluded[i](input);
            if(input.NetworkSeedCompletion!=null)input.NetworkSeedCompletion.Complete();
            Check(!alternatives.Complete(input,input,selected,true,false),"excluded scope "+i+" retains the original fixed-width search");
        }
        foreach(int count in Enumerable.Range(3,5))
        {
            input=Input();selected=Winner(count);alternatives=Alternatives(input,selected);
            Check(!alternatives.Complete(input,input,selected,true,false),"a count "+count+" success does not certify later receiver refinements");
        }
        input=Input();selected=Winner();alternatives=Alternatives(input,selected);
        Check(!alternatives.Complete(input,input,selected,true,true),"fixed geometry, stock or layout cannot publish unrestricted failure evidence");
        selected.TransverseOffsetMm=25;Check(!alternatives.Complete(input,input,selected,true,false),"an offset result cannot certify centred widths");selected.TransverseOffsetMm=0;
        selected.SearchTimeLimitReached=true;Check(!alternatives.Complete(input,input,selected,true,false),"a retained deadline result has no exhaustive proof");selected.SearchTimeLimitReached=false;
        input.ReceiverAlternatives=new System.Collections.Generic.Dictionary<double,TowerLayout>();
        Check(!alternatives.Complete(input,input,selected,true,false),"a collector from another receiving call cannot publish evidence");
        input=Input(2450,int.MaxValue);selected=Winner(stations:EngineerTowerSearch.MaximumStations(input));alternatives=Alternatives(input,selected);
        Check(alternatives.Complete(input,input,selected,true,false) && alternatives.Rejects(1000,int.MaxValue,input.Trace),"the unchanged physical station cap may establish full coverage even for an unlimited raw ceiling");
        using(var source=new CancellationTokenSource())
        {
            input=Input();input.Trace=new BeamSearchTrace("cancelled width proof",source.Token);selected=Winner();alternatives=Alternatives(input,selected);
            source.Cancel();bool cancelled=false;try{alternatives.Complete(input,input,selected,true,false);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled && alternatives.ThroughLegs==0,"cancelled completion publishes no proof");
            cancelled=false;try{alternatives.Rejects(1000,8,input.Trace);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"a cached rejection still observes cancellation");
        }
    }
    static TowerLayout Optimize(TowerInput input)
    {try{return TransferredSlabOptimizer.Optimize(input,true);}catch(TowerOptimizationException){return null;}}
    static void FullWidthFailure()
    {
        var input=Input();
        // Deterministic structural acceptance isolates the count/width traversal:
        // no count before eight can pass and only width 1524 may pass at eight.
        input.TransferLayoutAcceptance=layout=>layout.Transfer.EngineerReceiverCount==8 && layout.WidthMm==1524 && layout.PositionsMm.Length>=4;
        var alternatives=new TransferNetworkOptimizer.ReceiverWidthAlternatives();input.ReceiverAlternatives=alternatives;
        var selected=Optimize(input);
        Check(selected!=null && selected.Transfer.EngineerReceiverCount==8 && selected.LegCount==8,"the real optimizer completes every refinement at its final permitted count");
        Check(alternatives.Count==1 && alternatives.ContainsKey(1524),"the real exhaustive result contains exactly its passing width");
        Check(alternatives.Complete(input,input,selected,true,false),"the complete actual optimizer result certifies its missing width subsets");
        foreach(double width in new[] {1000.0,1500,2000})
        {
            var fixedInput=Input();fixedInput.TransferredSlab.FixedTowerWidthMm=width;fixedInput.TransferLayoutAcceptance=input.TransferLayoutAcceptance;
            Check(Optimize(fixedInput)==null && alternatives.Rejects(width,8,input.Trace),"the unchanged fixed-width optimizer independently rejects each certified subset");
        }
    }
    static void LaterWidthFallback()
    {
        foreach(bool earlierCount in new[] {false,true})
        {
            int ceiling=earlierCount ? 8 : 12,missingWidthLegs=earlierCount ? 8 : 12;
            var input=Input(6000,ceiling);
            input.TransferLayoutAcceptance=layout=>layout.WidthMm==1524
                ? layout.Transfer.EngineerReceiverCount==(earlierCount ? 3 : 8) && layout.PositionsMm.Length>=4
                : layout.WidthMm==1000 && layout.Transfer.EngineerReceiverCount==8 && layout.PositionsMm.Length>=missingWidthLegs/2;
            var alternatives=new TransferNetworkOptimizer.ReceiverWidthAlternatives();input.ReceiverAlternatives=alternatives;
            var selected=Optimize(input);
            Check(selected!=null && selected.WidthMm==1524 && selected.LegCount==8,"unrestricted traversal can return an earlier width before a larger station ceiling");
            Check(!alternatives.ContainsKey(1000),"the later-refinement or wider-budget width is absent from the unrestricted winner's alternatives");
            bool certified=alternatives.Complete(input,input,selected,true,false);
            Check(certified==!earlierCount,"only count-eight completion certifies its exhausted smaller-width subset");
            Check(!alternatives.Rejects(1000,ceiling,input.Trace),"higher permitted station counts remain searchable after the smaller subset fails");
            Check(alternatives.FailedThroughStations(1000,ceiling,input.Trace)==(earlierCount ? 0 : 4),"the floor records only the actual completed count-eight winner stations");
            var fixedInput=Input(6000,ceiling);fixedInput.TransferredSlab.FixedTowerWidthMm=1000;fixedInput.TransferLayoutAcceptance=input.TransferLayoutAcceptance;
            var later=Optimize(fixedInput);
            Check(later!=null && later.WidthMm==1000 && later.LegCount==missingWidthLegs && later.Transfer.EngineerReceiverCount==8,
                "the independent fixed-width optimizer retains its genuine later-count/larger-ceiling passing layout");
            var filteredInput=Input(6000,ceiling);filteredInput.TransferredSlab.FixedTowerWidthMm=1000;filteredInput.TransferLayoutAcceptance=input.TransferLayoutAcceptance;
            filteredInput.ReceiverFailedThroughStations=alternatives.FailedThroughStations(1000,ceiling,input.Trace);
            var filtered=Optimize(filteredInput);
            Check(filtered!=null && filtered.WidthMm==later.WidthMm && filtered.LegCount==later.LegCount
                && filtered.Transfer.EngineerReceiverCount==later.Transfer.EngineerReceiverCount && filtered.PositionsMm.SequenceEqual(later.PositionsMm)
                && filtered.StockPlan.Pieces.Select(p=>p.StockLengthMm).SequenceEqual(later.StockPlan.Pieces.Select(p=>p.StockLengthMm))
                && filtered.MomentKnM==later.MomentKnM && filtered.ShearKn==later.ShearKn && filtered.Utilization==later.Utilization,
                "certified subset skipping preserves the later fixed-width winner and exact numerical response");
        }
    }
    static void RefinementPriority()
    {
        var broad=Input(6000,12);
        broad.TransferLayoutAcceptance=layout=>layout.WidthMm==1524
            ? layout.Transfer.EngineerReceiverCount==8 && layout.LegCount==8
            : layout.WidthMm==1000 && ((layout.Transfer.EngineerReceiverCount==3 && layout.LegCount==12)
                || (layout.Transfer.EngineerReceiverCount==8 && layout.LegCount==10));
        var alternatives=new TransferNetworkOptimizer.ReceiverWidthAlternatives();broad.ReceiverAlternatives=alternatives;
        var selected=Optimize(broad);
        Check(selected!=null && selected.WidthMm==1524 && selected.LegCount==8 && alternatives.Complete(broad,broad,selected,true,false),"priority fixture establishes an actual partial count-eight missing-width certificate");
        var plain=Input(6000,12);plain.TransferredSlab.FixedTowerWidthMm=1000;plain.TransferLayoutAcceptance=broad.TransferLayoutAcceptance;
        var baseline=Optimize(plain);
        var filtered=Input(6000,12);filtered.TransferredSlab.FixedTowerWidthMm=1000;filtered.TransferLayoutAcceptance=broad.TransferLayoutAcceptance;
        filtered.ReceiverFailedThroughStations=alternatives.FailedThroughStations(1000,12,broad.Trace);
        var actual=Optimize(filtered);
        Check(baseline!=null && actual!=null && baseline.LegCount==12 && actual.LegCount==12 && actual.Transfer.EngineerReceiverCount==3
            && actual.PositionsMm.SequenceEqual(baseline.PositionsMm),"failed subset filtering keeps the original ceiling sequence: count-three twelve-leg winner precedes count-eight ten-leg alternative");
        Check(filtered.EngineerMinimumStations==plain.EngineerMinimumStations && filtered.EngineerPlan.InitialLegs==plain.EngineerPlan.InitialLegs,"certificate does not alter the original minimum or initial leg tier");
        var network=Network(6000);
        Check(!new ReceiverSolveKey(plain,network,0,false).Equals(new ReceiverSolveKey(filtered,network,0,false)),"receiver search keys retain transient proof provenance");
        Check(new ReceiverSolveKey(plain,network,0,false,layoutIdentity:true).Equals(new ReceiverSolveKey(filtered,network,0,false,layoutIdentity:true)),"passing physical-layout identity is independent of the transient failed subset");
    }
    static void TransportAndEntryPoints()
    {
        var input=Input();input.ReceiverFailedThroughStations=4;
        string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".bin");
        try
        {
            BeamWire.Write(path,new BeamSolverRequest {Main=input});
            var read=BeamWire.Read<BeamSolverRequest>(path).Main;
            Check(read.ReceiverFailedThroughStations==0,"per-call failed-width proof is excluded from worker requests");
        }
        finally{System.IO.File.Delete(path);}
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        Check(typeof(TransferNetworkOptimizer).GetMethod("Solve",flags).GetParameters().Length==14,"existing Solve reflection entry retains its original arity");
        Check(typeof(TransferNetworkOptimizer).GetMethod("SolveReceiver",flags).GetParameters().Length==15,"existing SolveReceiver reflection entry retains its original arity");
    }
    internal static void Run(){Scope();FullWidthFailure();LaterWidthFallback();RefinementPriority();TransportAndEntryPoints();Console.WriteLine("PASS "+checks+" missing centred receiver-width subset proof checks, exact scopes, cancellation and full optimizer width-late-fallback counterexamples");}
}
