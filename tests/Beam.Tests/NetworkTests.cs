using System;
using System.Linq;
using EngineeringDashboard;
internal static class NetworkTests
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Network: "+message);}
    static void Near(double actual,double expected,string message)=>Check(Math.Abs(actual-expected)<1e-6*Math.Max(1,Math.Abs(expected)),message);
    public static void Run()
    {
        var common=new TowerInput {LengthMm=6750,LegCapacityKn=70,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=75,DeflectionDenominator=350};
        var input=new TransferNetworkInput {UpperSpacingMm=275,Beams=new[] {new TransferBeamRow {WidthMm=1100,DepthMm=1750},new TransferBeamRow {WidthMm=1100,DepthMm=1200},new TransferBeamRow {WidthMm=900,DepthMm=1500}},Bays=new[] {new TransferBayRow {WidthMm=3900,ThicknessMm=300},new TransferBayRow {WidthMm=3500,ThicknessMm=250}}};
        var timer=System.Diagnostics.Stopwatch.StartNew();var result=TransferNetworkOptimizer.Optimize(common,input);
        Check(result.Beams.Length==3 && result.Bays.Length==2,"extended slab/beam chain");
        double loadedLength=common.LengthMm-2*result.Beams[0].ConcreteEndOverhangMm;
        Near(result.SlabLoadKn,input.Bays.Sum(b=>input.AreaLoad(b.ThicknessMm)*b.WidthMm/1000)*loadedLength/1000,"full slab area conserved including both shutter edges");
        for(int b=0;b<result.Beams.Length;b++)
        {
            var beam=result.Beams[b];Check(beam.Transfer.RowsMm.SequenceEqual(result.Beams[0].Transfer.RowsMm),"shared upper rows align across all lower beams");
            double beamLoad=input.AreaLoad(input.Beams[b].DepthMm)*input.Beams[b].WidthMm/1000*loadedLength/1000;
            Near(beam.Transfer.LowerCases[0].Sum(m=>m.Analysis.TotalLoadKn),beamLoad,"each beam depth produces its own beam concrete load");
            Check(beam.LeftReactions.Concat(beam.RightReactions).All(r=>r>=-1e-7 && r<=common.LegCapacityKn+1e-8),"each beam's legs checked");
            Near(beam.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn),beamLoad+beam.Transfer.SlabLoadKn,"shutter and upper feet reach longitudinal pairs");
            foreach(var m in beam.Transfer.LowerCases.Last())
            {
                var shutters=m.Points.Where(p=>Math.Abs(Math.Abs(p.PositionMm+m.StartMm)-input.Beams[b].WidthMm/2-100)<1e-6).ToArray();
                Check(shutters.Length==(b==0 || b==result.Beams.Length-1 ? 1 : 2),"one point reaction per shutter, 100 mm outward");
            }
        }
        for(int bay=0;bay<result.Bays.Length;bay++)
        {
            var left=result.Beams[bay].Transfer.UpperMembers.Single(m=>m.Name=="Upper right");var right=result.Beams[bay+1].Transfer.UpperMembers.Single(m=>m.Name=="Upper left");
            Near(left.LengthMm,right.LengthMm,"upper stock shared across adjacent beams");
            Near(left.Analysis.Reactions[0],right.Analysis.Reactions[1],"same physical upper-left reaction at both analyses");
            Near(left.Analysis.Reactions[1],right.Analysis.Reactions[0],"same physical upper-right reaction at both analyses");
        }
        double expectedTimber=result.Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.RowsMm.Length*2*b.Transfer.LowerStockLengthMm)+result.Bays.Sum(b=>b.StockLengthMm)*2*result.Beams[0].Transfer.RowsMm.Length;
        Near(result.TimberMm,expectedTimber,"shared upper timber counted once");
        Check(result.SlabMovementMm>0 && TowerBeamAnalysis.Finite(result.SlabMovementMm),"different lower-beam movements propagated into slab");
        var free=LoadedBeamAnalysis.Analyze(2000,new[] {0.0,2000},null,new BeamPointLoad[0],900,350);
        Near(free.DeflectionWithAffineOffsetMm(-2,-5,0,2000),5,"different prescribed foot movements");
        var vm=new BeamCalculatorViewModel {UseTowers=true,HasLeftSlab=true,TransferSlabLoads=true,UseBeamNetwork=true};
        Check(vm.NetworkBeams.Count==2,"network initializes two actual beams");vm.AddNetworkBay();vm.NetworkBeams[1].DepthMm=1200;Check(vm.NetworkBeams.Count==3 && vm.NetworkBays.Count==2,"add slab and beam");
        var xml=vm.NetworkConfigurationXml;var other=new BeamCalculatorViewModel();other.NetworkConfigurationXml=xml;Near(other.NetworkBeams[1].DepthMm,1200,"per-beam depths saved");
        vm.RemoveNetworkBay();Check(vm.NetworkBeams.Count==2 && !vm.CanRemoveNetworkBay,"remove preserves connected sequence");
        Console.WriteLine($"Network: {checks} checks passed · {result.LegCount} tower legs · {result.TimberMm/1000:0.0} m timber · {timer.Elapsed.TotalSeconds:0.00} s");
    }
}
