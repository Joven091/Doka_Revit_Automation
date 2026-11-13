using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EngineeringDashboard;

internal static class ResolvedPlanKeyTests
{
    sealed class Fixture
    {
        internal TowerInput Common=new TowerInput {LegCapacityKn=70};
        internal TransferNetworkInput Network=new TransferNetworkInput {CrossingBeams=new[] {
            new TransferCrossingBeamRow {BayNumber=1,PositionMm=2300.125,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150},
            new TransferCrossingBeamRow {BayNumber=2,PositionMm=4400.375,WidthMm=800,DepthMm=1250,BottomElevationMm=-1250}}};
        internal TransferNetworkResult Result=new TransferNetworkResult {BeamCentresMm=new[] {125.375,3600.5},Beams=new[] {
            new TowerLayout {PositionsMm=new[] {200.0,1724},Transfer=new TransferredSlabResult {LowerCases=new[] {new[] {
                new TransferMember {StartMm=125.125},new TransferMember {StartMm=235.25}}}}}}};
        internal CrossBeamSupportResult[] Supports;
        internal bool Pending;
        internal Fixture()
        {
            Supports=Network.CrossingBeams.Select((c,i)=>new CrossBeamSupportResult {Input=c,UsesSpreader=i==0,SlabEdgeLoadKn=2.125+i,Bearings=new[] {
                new CrossBeamBearing {Beam=0,Row=0,Xmm=2200.125+i,Ymm=350.25,ReactionKn=12.375,Label="Secondary",EndZoneStartMm=100,EndZoneEndMm=200,PrimaryLine=-1},
                new CrossBeamBearing {Beam=1,Row=0,Xmm=2250,Ymm=3600,ReactionKn=13.25,Label="Other beam"},
                new CrossBeamBearing {Beam=0,Row=1,Xmm=2400.25+i,Ymm=1500.375,ReactionKn=9.125,Label="Slab edge ledger",IsConcreteEndZone=true,IsShutterEndZone=true,EndZoneStartMm=200,EndZoneEndMm=300,PrimaryLine=1}}}).ToArray();
        }
        internal EngineerPrimaryPlan.ResolvedPlanKey Key()=>new EngineerPrimaryPlan.ResolvedPlanKey(Common,Result,0,Supports,Network,Pending);
        internal string Original()
        {
            var t=Result.Beams[0].Transfer;
            return FormattableString.Invariant($"{0}|{Result.BeamCentresMm[0]:R}|{Pending}|{Common.LegCapacityKn:R}|{Common.PrimaryMaterial}|")
                +string.Join(";",Supports.Select(s=>FormattableString.Invariant($"{s.Input.BayNumber},{s.Input.PositionMm:R},{s.Input.WidthMm:R},{s.UsesSpreader},{s.SlabEdgeLoadKn:R}")+":"
                    +string.Join(",",s.Bearings.Where(b=>b.Beam==0).Select(b=>FormattableString.Invariant($"{b.Xmm:R}/{b.Ymm:R}/{b.ReactionKn:R}/{b.Label}/{b.IsConcreteEndZone}/{b.IsShutterEndZone}/{b.EndZoneStartMm:R}/{b.EndZoneEndMm:R}/{b.PrimaryLine}/{(b.IsPrimaryBearing ? 0 : t.LowerCases[0][b.Row].StartMm):R}")))))
                +"|"+string.Join(";",Network.CrossingBeams.Select(c=>FormattableString.Invariant($"{c.BayNumber}/{c.PositionMm:R}/{c.WidthMm:R}/{c.DepthMm:R}/{c.BottomElevationMm:R}/{c.SupportMode}")));
        }
    }
    static int checks;
    static void Check(bool condition,string text){checks++;if(!condition)throw new Exception("Resolved plan key: "+text);}
    internal static void Run()
    {
        var mutations=new Action<Fixture>[] {
            f=>f.Pending=true,f=>f.Common.LegCapacityKn=PortableTestRuntime.NextUp(70),f=>f.Common.PrimaryMaterial=PrimaryBeamMaterial.WS10,
            f=>f.Result.BeamCentresMm[0]=PortableTestRuntime.NextUp(f.Result.BeamCentresMm[0]),f=>f.Supports[0].UsesSpreader=false,
            f=>f.Supports[0].Input.BayNumber++,f=>f.Supports[0].Input.PositionMm=PortableTestRuntime.NextUp(f.Supports[0].Input.PositionMm),
            f=>f.Supports[0].Input.WidthMm=PortableTestRuntime.NextUp(f.Supports[0].Input.WidthMm),f=>f.Supports[0].SlabEdgeLoadKn=PortableTestRuntime.NextUp(f.Supports[0].SlabEdgeLoadKn),
            f=>f.Supports[0].Bearings[0].Xmm=PortableTestRuntime.NextUp(f.Supports[0].Bearings[0].Xmm),f=>f.Supports[0].Bearings[0].Ymm=PortableTestRuntime.NextUp(f.Supports[0].Bearings[0].Ymm),
            f=>f.Supports[0].Bearings[0].ReactionKn=PortableTestRuntime.NextUp(f.Supports[0].Bearings[0].ReactionKn),f=>f.Supports[0].Bearings[0].Label="Secondary / ledger",
            f=>f.Supports[0].Bearings[0].IsConcreteEndZone=true,f=>f.Supports[0].Bearings[0].IsShutterEndZone=true,
            f=>f.Supports[0].Bearings[0].EndZoneStartMm=PortableTestRuntime.NextUp(f.Supports[0].Bearings[0].EndZoneStartMm),
            f=>f.Supports[0].Bearings[0].EndZoneEndMm=PortableTestRuntime.NextUp(f.Supports[0].Bearings[0].EndZoneEndMm),
            f=>f.Supports[0].Bearings[0].PrimaryLine=1,f=>f.Supports[0].Bearings[0].PrimaryLine=0,f=>f.Supports[0].Bearings[0].Row=1,
            f=>f.Result.Beams[0].Transfer.LowerCases[0][0].StartMm=PortableTestRuntime.NextUp(f.Result.Beams[0].Transfer.LowerCases[0][0].StartMm),
            f=>f.Network.CrossingBeams[1].BayNumber++,f=>f.Network.CrossingBeams[1].PositionMm=PortableTestRuntime.NextUp(f.Network.CrossingBeams[1].PositionMm),
            f=>f.Network.CrossingBeams[1].WidthMm=PortableTestRuntime.NextUp(f.Network.CrossingBeams[1].WidthMm),
            f=>f.Network.CrossingBeams[1].DepthMm=PortableTestRuntime.NextUp(f.Network.CrossingBeams[1].DepthMm),
            f=>f.Network.CrossingBeams[1].BottomElevationMm=PortableTestRuntime.NextUp(f.Network.CrossingBeams[1].BottomElevationMm),
            f=>f.Network.CrossingBeams[1].SupportMode=CrossBeamSupportMode.DedicatedTowers,
            f=>Array.Reverse(f.Supports),f=>Array.Reverse(f.Supports[0].Bearings),f=>Array.Reverse(f.Network.CrossingBeams),
            f=>f.Supports[0].Bearings=f.Supports[0].Bearings.Take(2).ToArray(),f=>f.Supports=f.Supports.Take(1).ToArray(),
            f=>f.Supports[0].Bearings[1].ReactionKn=1200,f=>f.Result.Beams[0].PositionsMm[0]+=50,
            f=>f.Supports[0].Bearings[0].Label=null,f=>f.Supports[0].Bearings[0].Label="",
            f=>f.Supports[0].Bearings[0].EndZoneStartMm=-0.0,f=>f.Supports[0].Bearings[0].EndZoneStartMm=0.0};
        var fixtures=new List<Fixture> {new Fixture()};
        foreach(var mutation in mutations){var f=new Fixture();mutation(f);fixtures.Add(f);}
        var original=fixtures.Select(f=>f.Original()).ToArray();var keys=fixtures.Select(f=>f.Key()).ToArray();
        for(int i=0;i<keys.Length;i++)for(int j=0;j<keys.Length;j++)
        {
            Check(keys[i].Equals(keys[j])==(original[i]==original[j]),"every old source distinction, precise number, label, ordering and excluded nonlocal geometry is retained");
            if(keys[i].Equals(keys[j]))Check(keys[i].GetHashCode()==keys[j].GetHashCode(),"equal immutable keys have equal hashes");
        }
        var source=new Fixture();var snapshot=source.Key();source.Supports[0].Bearings[0].ReactionKn+=2;
        Check(snapshot.Equals(new Fixture().Key()) && !snapshot.Equals(source.Key()),"later source edits cannot modify a retained snapshot");
        var culture=CultureInfo.CurrentCulture;
        try
        {
            foreach(string name in new[] {"fr-FR","de-DE","ar-AE"})
            {CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(name);Check(new Fixture().Key().Equals(keys[0]),"numeric identities are culture independent");}
        }
        finally{CultureInfo.CurrentCulture=culture;}
        for(int i=0;i<100;i++){new Fixture().Key();new Fixture().Original();}
        var sample=new Fixture();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)sample.Original();long textBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)sample.Key();long typedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(typedBytes<textBytes/2,"typed lookup identity removes at least half the old per-key allocation");
        Console.WriteLine("PASS "+checks+" resolved-plan key checks: original identity differential, all source fields/order/ULPs, snapshot/culture and allocation "+textBytes+" -> "+typedBytes+" bytes/1000 keys");
    }
}
