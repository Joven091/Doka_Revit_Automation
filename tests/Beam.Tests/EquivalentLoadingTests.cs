using System;
using System.Linq;
using EngineeringDashboard;

internal static class EquivalentLoadingTests
{
    static int checks;
    static void Check(bool ok,string message) {checks++;if(!ok)throw new Exception("Equivalent loading: "+message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-10*Math.Max(1,Math.Abs(b)),message+$" ({a:R} vs {b:R})");
    internal static TowerLayout Fixture(double reference=305)
    {
        var source=new TransferMember {Name="Equivalent source",LoadCase="Beam + left slab",H20Count=1,LengthMm=1800,
            SupportsMm=new[] {425.0,1425},Uniform=new[] {new BeamDistributedLoad {StartMm=575,EndMm=1225,KnPerM=31.65}},
            Points=new[] {new BeamPointLoad {PositionMm=199,Kn=8.46},new BeamPointLoad {PositionMm=475,Kn=2.1244}}};
        var t=new TransferredSlabResult {EnteredUpperSpacingMm=reference,LeftSlabSpacingMm=407,RowsMm=new[] {3000.0},
            SlabSupportStartMm=100,SlabSupportEndMm=6800,CaseNames=new[] {source.LoadCase},UnitLowerCases=new[] {source},
            UnitPointLabels=new[] {new[] {"Left slab foot","Left shutter"}},PrimaryCases=new[] {new[] {
                new TransferMember {Name="P1 L",StartMm=0,LengthMm=6900,Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=6900,KnPerM=23.1907025}}},
                new TransferMember {Name="P1 R",StartMm=0,LengthMm=6900,Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=6900,KnPerM=7.9661975}}}}}};
        return new TowerLayout {DeflectionDenominator=270,Transfer=t};
    }
    internal static void Run()
    {
        var fixture=Fixture();var view=EquivalentTransferredLoading.Create(fixture,0,0);
        Check(view.IsAvailable,"illustrative nominal case matches verified primary source");
        var foot=view.Components.Single(c=>c.Name.Contains("foot"));var shutter=view.Components.Single(c=>c.Name.Contains("shutter"));var beam=view.Components.Single(c=>c.IsUniform);
        Near(foot.SourceValue,3.44322,"407 mm nominal slab foot");Near(shutter.SourceValue,.8646308,"407 mm nominal shutter");
        Near(foot.EquivalentValue,2.58030,"foot converted to selected 305 mm reference");Near(shutter.EquivalentValue,.647942,"shutter converted to common reference");
        Near(beam.EquivalentValue,9.65325,"beam design pitch already equals reference");
        Near(foot.ReactionsKn[0],3.1634478,"overhanging foot signed influence");Near(shutter.ReactionsKn[0],.6155449,"shutter signed influence");Near(beam.ReactionsKn[0],3.2941715625,"beam resultant influence");
        Near(view.Member.Analysis.Reactions[0],7.0731642625,"equivalent left reaction");Near(view.Member.Analysis.Reactions[1],2.4296902375,"equivalent right reaction");
        Near(view.PrimaryTransferKnM[0],23.1907025,"primary left equivalent UDL");
        Near(view.Member.Analysis.Reactions.Sum(),view.Member.Analysis.TotalLoadKn,"equivalent force equilibrium");
        foreach(double reference in new[] {250.0,275,305,350,407,488})
        {
            var dynamic=EquivalentTransferredLoading.Create(Fixture(reference),0,0);
            Check(dynamic.IsAvailable,"all permitted reference pitches use model values");Near(dynamic.ReferenceSpacingMm,reference,"reference never hardcoded to 305");
            for(int line=0;line<2;line++)
            {
                Near(dynamic.Member.Analysis.Reactions[line]/(reference/1000),dynamic.PrimaryTransferKnM[line],"single division reproduces primary UDL without another reaction factor");
                Near(dynamic.Components.Sum(c=>c.ReactionsKn[line]),dynamic.Member.Analysis.Reactions[line],"component reaction sum retains full precision");
            }
            Near(dynamic.Components.Single(c=>c.Name.Contains("foot")).SourceValue,3.44322,"source nominal spacing is independent of reference pitch");
        }
        var weight=Fixture();foreach(var primary in weight.Transfer.PrimaryCases[0]) {primary.PrimarySelfWeightKnM=.1;primary.Uniform[0].KnPerM+=.1;}
        var weighted=EquivalentTransferredLoading.Create(weight,0,0);
        Check(weighted.IsAvailable,"primary-only self-weight is kept separate from secondary transfer");Near(weighted.PrimarySelfWeightKnM[0],.1,"self-weight retained explicitly");
        var local=Fixture();local.Transfer.PrimaryCases[0][0].Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2500,KnPerM=23.1907025},new BeamDistributedLoad {StartMm=2500,EndMm=3500,KnPerM=25.1907025},new BeamDistributedLoad {StartMm=3500,EndMm=6900,KnPerM=23.1907025}};
        Check(!EquivalentTransferredLoading.Create(local,0,0).IsAvailable,"localized crossing UDL cannot be represented as uniform baseline equivalent loads");
        local.Transfer.RowsMm[0]=2000;Check(EquivalentTransferredLoading.Create(local,0,0).IsAvailable,"outside crossing footprint baseline remains valid");
        var stage=Fixture();stage.Transfer.CaseNames[0]+=" + X1";
        Check(!EquivalentTransferredLoading.Create(stage,0,0).IsAvailable,"unmatched crossing stage cannot force a baseline identity");
        var end=Fixture();end.Transfer.RowsMm[0]=50;
        end.Transfer.PrimaryCases[0][0].Uniform[0].KnPerM=10.8005625;end.Transfer.PrimaryCases[0][1].Uniform[0].KnPerM=9.7719375;
        var columns=EquivalentTransferredLoading.Create(end,0,0);
        Check(columns.IsAvailable && columns.Member.Points.Length==0,"column-supported slab strips add no equivalent slab load to the primary");
        var both=BothFixture();var two=EquivalentTransferredLoading.Create(both,0,0);
        Check(two.IsAvailable && two.Components.Length==5,"both slab sides retain distinct nominal sources");
        for(int line=0;line<2;line++)Near(two.Components.Sum(c=>c.ReactionsKn[line])/.305,two.PrimaryTransferKnM[line],"both-sided component sum reproduces primary UDL");
        Near(two.Components.Single(c=>c.Name=="Right slab foot").NominalSpacingMm,488,"right slab pitch independent of left slab pitch");
        ProductionReference();
        Console.WriteLine($"Passed {checks} equivalent loading invariant checks.");
    }
    static void ProductionReference()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})
        {
            var layout=BothFixture();var t=layout.Transfer;var unit=t.UnitLowerCases[0];
            layout.PrimaryMaterial=material;t.FullLoadingOnly=true;t.TributaryWidthsMm=new[] {305.0};
            var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=6900,PrimaryMaterial=material};
            var network=new TransferNetworkInput {Beams=Enumerable.Range(0,3).Select(i=>new TransferBeamRow {WidthMm=650,DepthMm=1150}).ToArray(),Bays=Enumerable.Range(0,2).Select(i=>new TransferBayRow {WidthMm=2125,ThicknessMm=200}).ToArray()};
            t.PrimaryPlan=EngineerPrimaryPlan.Create(common,network,1).WithGeometry(common,unit,t.UnitPointLabels[0]);
            var actual=new TransferMember {H20Count=1,LengthMm=unit.LengthMm,StartMm=-900,SupportsMm=unit.SupportsMm,
                Uniform=unit.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*.305}).ToArray(),
                Points=unit.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*.488}).ToArray()};
            actual.Analysis=LoadedBeamAnalysis.AnalyzeSigned(actual.LengthMm,actual.SupportsMm,actual.Uniform,actual.Points,actual.EI,270);
            t.LowerCases=new[] {new[] {actual}};var physical=actual.Analysis;
            for(int line=0;line<2;line++)
            {
                var primary=t.PrimaryCases[0][line];primary.LengthMm=4500;primary.PrimaryMaterial=material;primary.PrimarySelfWeightKnM=common.Primary.SelfWeightKnM(4500);
                primary.Uniform=t.PrimaryPlan.Loads(0,4500,line,selfWeight:primary.PrimarySelfWeightKnM);
            }
            var view=EquivalentTransferredLoading.Create(layout,0,0);
            Check(view.IsAvailable && view.IsDesignSlice,"production primary plan always has a reference transfer diagram");
            var transfers=view.Components.Where(c=>!c.IsUniform).ToArray();
            Near(transfers[0].NominalSpacingMm,407,"production left slab retains its own source pitch");Near(transfers.Last().NominalSpacingMm,488,"production right slab retains its own source pitch");
            for(int line=0;line<2;line++)
            {
                Near(view.Member.Analysis.Reactions[line]/.305,LoadedBeamAnalysis.AnalyzeDenseReference(unit.LengthMm,unit.SupportsMm,unit.Uniform,unit.Points,unit.EI,270).Reactions[line],"production reaction recovers physically positioned source UDL");
                Near(view.PrimaryTransferKnM[line]+view.PrimarySelfWeightKnM[line],t.PrimaryCases[0][line].Uniform[0].KnPerM,"primary self-weight is added exactly once after secondary transfer");
            }
            foreach(var part in view.Components)Near(part.SourceValue*part.ReferenceSpacingMm/part.NominalSpacingMm,part.EquivalentValue,"production card conversion including independent slab pitches");
            Check(ReferenceEquals(actual.Analysis,physical),"production reference diagram leaves physical secondary response intact");
        }
    }
    internal static TowerLayout BothFixture()
    {
        var layout=Fixture();var t=layout.Transfer;var source=t.UnitLowerCases[0];
        source.LoadCase=t.CaseNames[0]="Beam + both slabs";t.RightSlabSpacingMm=488;
        source.Points=source.Points.Concat(new[] {new BeamPointLoad {PositionMm=1601,Kn=5.6},new BeamPointLoad {PositionMm=1325,Kn=1.1}}).ToArray();
        t.UnitPointLabels[0]=t.UnitPointLabels[0].Concat(new[] {"Right slab foot","Right shutter"}).ToArray();
        var analysis=LoadedBeamAnalysis.AnalyzeSigned(source.LengthMm,source.SupportsMm,source.Uniform,source.Points,TowerOptimizer.PairEI/2,270);
        for(int line=0;line<2;line++)t.PrimaryCases[0][line].Uniform[0].KnPerM=analysis.Reactions[line];
        return layout;
    }
}
