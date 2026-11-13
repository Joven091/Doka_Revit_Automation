using System;
using System.Linq;
using EngineeringDashboard;

// Small exported-result fixtures exercise conservation boundaries without a search.
internal static class CrossingLoadPathTests
{
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    static TransferMember Primary(double force,PrimaryBeamMaterial material)
    {
        const double length=2000;
        var profile=PrimaryBeamProfile.For(material);double weight=profile.SelfWeightKnM(length);
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=force*1000/length+weight}};
        return new TransferMember {H20Count=2,PrimaryMaterial=material,PrimarySelfWeightKnM=weight,LengthMm=length,
            Uniform=uniform,Points=Array.Empty<BeamPointLoad>(),SupportsMm=new[] {200.0,1800},
            Analysis=LoadedBeamAnalysis.Analyze(length,new[] {200.0,1800},uniform,null,profile.EI,270)};
    }
    static TowerLayout Tower(double force,PrimaryBeamMaterial material)
    {
        return new TowerLayout {PrimaryMaterial=material,
            // Historical reaction envelopes do not represent one applied load case.
            LeftReactions=new[] {91.0,92},RightReactions=new[] {93.0,94},
            Transfer=new TransferredSlabResult {PrimaryCases=new[] {
                new[] {Primary(force/8,material),Primary(force/8,material)},
                new[] {Primary(force/2,material),Primary(force/2,material)}}}};
    }
    static void Rejected(CrossBeamSupportResult cross,string name)
    {
        try {ReportedScenarioTests.ValidateCrossingLoadPaths(cross);}
        catch(Exception error) when(error.Message.Contains("Exported crossing receiver/leg load-path conservation")){return;}
        throw new Exception("Expected load-path rejection: "+name);
    }
    static FormworkSolid Solid(string name,double x0,double x1,double z0=0,double z1=1)
        =>new FormworkSolid {Name=name,X0=x0,X1=x1,Y0=0,Y1=1,Z0=z0,Z1=z1};
    static void PhysicalClashes()
    {
        void Reject(FormworkSolid[] solids,FormworkSolid[] concrete,string name)
        {
            try {ReportedScenarioTests.ValidateSolidClashes(solids,concrete);}
            catch(Exception error) when(error.Message.StartsWith("Final concrete clash:")||error.Message.StartsWith("Final formwork clash:")){return;}
            throw new Exception("Expected final physical-clash rejection: "+name);
        }
        var first=Solid("X1 stock",0,2);var second=Solid("X2 stock",1,3);
        Reject(new[] {first,second},Array.Empty<FormworkSolid>(),"different crossings");
        Reject(new[] {first,Solid("X1 another member",1,2)},Array.Empty<FormworkSolid>(),"members in one crossing");
        Reject(new[] {first},new[] {Solid("beam concrete",1,3)},"formwork inside concrete");
        ReportedScenarioTests.ValidateSolidClashes(new[] {first,Solid("bearing member",0,2,1,2)},new[] {Solid("touching concrete",2,3)});
        var cage=Solid("X1 cage",0,2);cage.IsTowerEnvelope=true;cage.TowerOwner="X1";
        var leg=Solid("X1 leg",.5,1);leg.IsLeg=true;leg.TowerOwner="X1";
        ReportedScenarioTests.ValidateSolidClashes(new[] {cage,leg},Array.Empty<FormworkSolid>());
        leg.TowerOwner="X2";Reject(new[] {cage,leg},Array.Empty<FormworkSolid>(),"another tower leg inside reserved cage");
    }
    internal static void Run()
    {
        PhysicalClashes();
        var shared=new CrossBeamSupportResult {LoadKn=24,Bearings=new[] {
            new CrossBeamBearing {Beam=0,PrimaryLine=1,ReactionKn=9},new CrossBeamBearing {Beam=1,PrimaryLine=0,ReactionKn=9},
            new CrossBeamBearing {Beam=0,IsConcreteEndZone=true,IsShutterEndZone=true,ReactionKn=2.5},
            new CrossBeamBearing {Beam=1,IsConcreteEndZone=true,ReactionKn=2.5},
            new CrossBeamBearing {Beam=0,Label="Slab edge ledger",ReactionKn=.5},new CrossBeamBearing {Beam=1,Label="Slab edge ledger",ReactionKn=.5}}};
        ReportedScenarioTests.ValidateCrossingLoadPaths(shared);
        shared.Bearings=shared.Bearings.Skip(1).ToArray();Rejected(shared,"missing shared primary bearing");
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var dedicated=new CrossBeamSupportResult {LoadKn=24,Towers=Tower(24,material),Bearings=new[] {
                new CrossBeamBearing {Beam=-1,IsDedicated=true,Label="Dedicated T1 / primary L",ReactionKn=24},
                new CrossBeamBearing {Beam=-1,IsDedicated=true,Label="Dedicated P1 L / leg 1",ReactionKn=24}}};
            ReportedScenarioTests.ValidateCrossingLoadPaths(dedicated);
            // Trimmed central load 18 plus signed external end-zone/ledger load 6.
            var mixed=new CrossBeamSupportResult {LoadKn=24,Towers=Tower(18,material),Bearings=dedicated.Bearings.Concat(new[] {
                new CrossBeamBearing {Beam=0,IsConcreteEndZone=true,IsShutterEndZone=true,ReactionKn=-1},
                new CrossBeamBearing {Beam=1,IsConcreteEndZone=true,Label="Slab edge ledger",ReactionKn=7}}).ToArray()};
            ReportedScenarioTests.ValidateCrossingLoadPaths(mixed);
            Check(mixed.Towers.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn)>18 || material==PrimaryBeamMaterial.DoubleH20,"WS10 fixture must include nonzero timber/steel self-weight");
            mixed.Bearings=mixed.Bearings.Where(b=>b.Beam!=1).ToArray();Rejected(mixed,"missing mixed external end zone");
            dedicated.Towers.Transfer.PrimaryCases=dedicated.Towers.Transfer.PrimaryCases.Take(1).ToArray();Rejected(dedicated,"missing full dedicated primary case");
        }
        Console.WriteLine("PASS exported crossing load paths: shared primary/end-zone/ledger, dedicated and mixed supports, signed forces, final case and self-weight; missing transfers rejected");
        Console.WriteLine("PASS final exported geometry: all crossing/main-member pairs and concrete; intended bearing contact and own tower envelope remain allowed");
    }
}
