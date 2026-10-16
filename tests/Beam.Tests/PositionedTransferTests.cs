using System;
using System.Linq;
using EngineeringDashboard;

internal static class PositionedTransferTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Positioned transfer: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-8*Math.Max(1,Math.Abs(b)),message+$" ({a:R} / {b:R})");
    internal static TowerLayout Fixture(bool mirror=false,double pitch=305,double slabPitch=488)
    {
        var common=new TowerInput {LengthMm=7000,EngineerSearch=true,FullLoadingOnly=true,IgnoreUplift=true,LegCapacityKn=70,DeflectionDenominator=270};
        var network=new TransferNetworkInput {Beams=Enumerable.Range(0,2).Select(i=>new TransferBeamRow {WidthMm=800,DepthMm=1750,BottomElevationMm=-1750}).ToArray(),Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=300}}};
        var unit=new TransferMember {Name="Positioned unit",H20Count=1,StartMm=-1225,LengthMm=2450,SupportsMm=new[] {463.0,1987},
            Uniform=new[] {new BeamDistributedLoad {StartMm=825,EndMm=1625,KnPerM=46.65}},
            Points=new[] {new BeamPointLoad {PositionMm=mirror ? 325 : 2125,Kn=6.6975},new BeamPointLoad {PositionMm=mirror ? 725 : 1725,Kn=3.29}}};
        unit.Analysis=LoadedBeamAnalysis.AnalyzeDenseReference(unit.LengthMm,unit.SupportsMm,unit.Uniform,unit.Points,unit.EI,270);
        string side=mirror ? "Left " : "Right ";
        var plan=EngineerPrimaryPlan.Create(common,network,mirror ? 1 : 0).WithGeometry(common,unit,new[] {side+"slab foot",side+"shutter"});plan.IsResolved=true;
        var actual=new TransferMember {Name="T12",H20Count=1,StartMm=unit.StartMm,LengthMm=unit.LengthMm,SupportsMm=unit.SupportsMm,LoadCase="Beam + Slab",
            Uniform=new[] {new BeamDistributedLoad {StartMm=825,EndMm=1625,KnPerM=46.65*pitch/1000}},
            Points=unit.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*slabPitch/1000}).ToArray()};
        actual.Analysis=LoadedBeamAnalysis.AnalyzeDenseReference(actual.LengthMm,actual.SupportsMm,actual.Uniform,actual.Points,actual.EI,270);
        var primaries=Enumerable.Range(0,2).Select(line=>
        {
            var m=new TransferMember {Name=line==0 ? "P1 L" : "P1 R",H20Count=2,StartMm=0,LengthMm=7000,SupportsMm=new[] {400.0,1400,2700,4000,5300,6600},Uniform=plan.Loads(0,7000,line),Points=new BeamPointLoad[0]};
            m.Analysis=LoadedBeamAnalysis.AnalyzeDenseReference(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,m.EI,270);return m;
        }).ToArray();
        return new TowerLayout {EngineerSearch=true,LengthMm=7000,WidthMm=1524,DeflectionDenominator=270,LegCapacityKn=70,PositionsMm=new[] {400.0,1400,2700,4000,5300,6600},Joined=new[] {true,false,true,false,true},
            StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StartMm=0,StockLengthMm=7000,FirstSupport=0,LastSupport=5}}},
            Transfer=new TransferredSlabResult {PrimaryPlan=plan,FullLoadingOnly=true,EnteredUpperSpacingMm=pitch,LeftSlabSpacingMm=mirror ? slabPitch : 0,RightSlabSpacingMm=mirror ? 0 : slabPitch,HasIndividualSpacings=true,
                RowsMm=new[] {2400.0,2600,2905},SlabRowIndices=new[] {0,2},TributaryWidthsMm=new[] {305.0,200,305},LowerStockStartMm=-1225,LowerStockLengthMm=2450,
                LeftUpperScales=mirror ? new[] {slabPitch/1000,0,slabPitch/1000} : new double[3],RightUpperScales=mirror ? new double[3] : new[] {slabPitch/1000,0,slabPitch/1000},
                CaseNames=new[] {"Beam + Slab"},UnitLowerCases=new[] {unit},UnitPointLabels=new[] {new[] {side+"slab foot",side+"shutter"}},
                LowerCases=new[] {new[] {actual,actual,actual}},PrimaryCases=new[] {primaries},UpperMembers=new TransferMember[0]}};
    }
    internal static void Run()
    {
        foreach(bool mirror in new[] {false,true})foreach(double pitch in new[] {250.0,275,305,350})foreach(double slabPitch in new[] {407.0,488})
        {
            var layout=Fixture(mirror,pitch,slabPitch);var t=layout.Transfer;var actual=t.LowerCases[0][0];var values=actual.Points.Select(p=>p.Kn).ToArray();
            for(int row=0;row<t.RowsMm.Length;row++)
            {
                var v=EquivalentTransferredLoading.Create(layout,row,0);
                Check(v.IsAvailable && v.HasPositionedLoads,"positioned reference exists on regular and closing rows");
                Check(v.Member.Points.Length==2,"slab foot and shutter remain separate loads");
                var foot=v.Components.Single(c=>c.Name.Contains("slab H20"));var edge=v.Components.Single(c=>c.Name.Contains("side-shutter"));
                Near(foot.PositionMm,mirror ? 325 : 2125,"foot stays at its physical coordinate");Near(edge.PositionMm,mirror ? 725 : 1725,"shutter stays at its physical coordinate");
                Near(foot.SourceValue,6.6975*slabPitch/1000,"physical slab source is at its own pitch");Near(edge.SourceValue,3.29*slabPitch/1000,"physical edge source is at its own pitch");
                Near(foot.EquivalentValue,6.6975*pitch/1000,"only the load magnitude is converted");Near(edge.EquivalentValue,3.29*pitch/1000,"separate edge conversion");
                Check(foot.SourceCalculation.Contains(" / 2") && edge.SourceCalculation.Contains("0.35"),"source arithmetic explains the real reaction and 350 mm strip");
                int inner=mirror ? 0 : 1,outer=1-inner;
                Near(v.PrimaryTransferKnM[outer],18.61913713910761,"outer line includes signed slab influence");Near(v.PrimaryTransferKnM[inner],28.688362860892386,"inner line includes proper positioned transfer");
                for(int line=0;line<2;line++)
                {
                    Near(v.Member.Analysis.Reactions[line]/(pitch/1000),v.PrimaryTransferKnM[line],"reaction / beam pitch recovers primary UDL");
                    Near(v.Components.Sum(c=>c.ReactionsKn[line]),v.Member.Analysis.Reactions[line],"all component influences sum to the reaction");
                    Near(t.PrimaryCases[0][line].Uniform.Single().KnPerM,v.PrimaryTransferKnM[line],"no edge-spacing UDL step");
                }
                var independent=LoadedBeamAnalysis.AnalyzeDenseReference(v.Member.LengthMm,v.Member.SupportsMm,v.Member.Uniform,v.Member.Points,v.Member.EI,270);
                Near(v.Member.Analysis.MomentKnM,independent.MomentKnM,"independent positioned moment");Near(v.Member.Analysis.ShearKn,independent.ShearKn,"independent positioned shear");
                Check(ReferenceEquals(actual,t.LowerCases[0][0]) && actual.Points.Select(p=>p.Kn).SequenceEqual(values),"reference conversion never reduces the physical capacity-check loads");
            }
        }
        var sample=EquivalentTransferredLoading.Create(Fixture(),0,0);
        Near(sample.Member.Analysis.Reactions[0],5.678836827427821,"reported outer reference reaction");Near(sample.Member.Analysis.Reactions[1],8.749950672572178,"reported inner reference reaction");
        Near(sample.Member.Points.Sum(p=>p.Kn),3.0461875,"combined 3.046 remains the sum of two positioned loads");
        Crossing();Console.WriteLine($"Passed {checks} positioned transfer checks.");
    }
    static void Crossing()
    {
        var layout=Fixture();var original=layout.Transfer.PrimaryPlan;var network=original.Network;
        var c=new TransferCrossingBeamRow {BayNumber=1,PositionMm=3350,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150};network.CrossingBeams=new[] {c};
        var common=new TowerInput {LengthMm=7000,EngineerSearch=true,FullLoadingOnly=true,IgnoreUplift=true,LegCapacityKn=70};
        var plan=EngineerPrimaryPlan.Create(common,network,0).WithGeometry(common,original.SecondaryBasis,layout.Transfer.UnitPointLabels[0]);layout.Transfer.PrimaryPlan=plan;
        var bearings=new[] {3250.0,3450}.SelectMany((x,row)=>new[] {
            new CrossBeamBearing {Beam=0,Row=row,Xmm=x,Ymm=750,ReactionKn=28.485*.65/2},
            new CrossBeamBearing {Beam=0,Row=row,Xmm=x,Ymm=481.25,ReactionKn=5.143125*.65/2,IsConcreteEndZone=true,IsShutterEndZone=true}}).ToArray();
        var support=new CrossBeamSupportResult {Input=c,UsesSpreader=true,Bearings=bearings};
        var result=new TransferNetworkResult {Input=network,Beams=new[] {layout},BeamCentresMm=new[] {0.0}};
        var resolved=plan.Resolve(common,result,0,new[] {support});
        Check(resolved.HasPositionedLoads && resolved.IsResolved,"shared crossing resolves from actual bearing forces");
        for(int line=0;line<2;line++)
        {
            var zones=resolved.Lines[line];Check(zones.Length==3,"only normal/crossing/normal zones remain");
            Near(zones[0].KnPerM,zones[2].KnPerM,"equal normal UDL on both sides of crossing");
            Near(zones[0].EndMm,3025,"crossing near physical face");Near(zones[1].EndMm,3675,"crossing far physical face");
            Near(EngineerPrimaryPlan.Reactions(resolved.SecondaryBasis,resolved.LoadsAt(3350))[line],resolved.ValueAt(3350,line),"positioned crossing source recovers its full-width UDL");
        }
        Near(resolved.Forces.Sum(),37.32*7+9.9875*6.35+33.628125*.65,"beam/slab/crossing force accounted once");
        double transverse=bearings.Sum(b=>b.ReactionKn*b.Ymm),allocated=resolved.CrossingTransfers[0][0]*.65*(-762)+resolved.CrossingTransfers[0][1]*.65*762;
        Near(allocated,transverse,"crossing transfer preserves transverse first moment");
        Near(bearings.Sum(b=>b.ReactionKn*(b.Xmm-3350)),0,"centred full-width crossing retains longitudinal resultant");
        // The same receiving end forces can accompany a different amount of slab
        // ledger load carried by dedicated towers. That separate allocation input
        // must invalidate reuse, even when this receiver's bearing list is identical.
        support.UsesSpreader=false;
        var withoutLedger=plan.Resolve(common,result,0,new[] {support});
        support.SlabEdgeLoadKn=2;
        var withLedger=plan.Resolve(common,result,0,new[] {support});
        Near(withoutLedger.Forces.Sum()-withLedger.Forces.Sum(),1,"changed dedicated ledger allocation cannot reuse stale normal slab zones");
        Check(ReferenceEquals(withLedger,plan.Resolve(common,result,0,new[] {support})),"identical complete transfer input can reuse the resolved allocation");
    }
}
