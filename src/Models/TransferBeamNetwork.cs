using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class UpperTransferPlan
    {
        public double StockLengthMm, EdgeMm, NearFootMm, FarFootMm;
        public UpperTransferPlan Reverse() => new UpperTransferPlan {StockLengthMm=StockLengthMm,EdgeMm=EdgeMm,NearFootMm=FarFootMm,FarFootMm=NearFootMm};
        public void Validate(double bay,double[] stocks)
        {
            if(new[] {StockLengthMm,EdgeMm,NearFootMm,FarFootMm}.Any(x=>!TowerBeamAnalysis.Finite(x)) || !stocks.Contains(StockLengthMm)
                || Math.Abs(bay-StockLengthMm-2*EdgeMm)>1e-7 || EdgeMm<200 || EdgeMm>350 || NearFootMm<Math.Max(250,EdgeMm+150)
                || FarFootMm<Math.Max(250,EdgeMm+150) || bay-NearFootMm-FarFootMm<300) throw new ArgumentException("Invalid shared upper H20 stock or foot positions.");
        }
    }
    public sealed class TransferBeamRow : INotifyPropertyChanged
    {
        public string Label {get;set;}
        private double depth=1750,width=1100;
        public double DepthMm {get=>depth;set {depth=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(DepthMm)));}}
        public double WidthMm {get=>width;set {width=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(WidthMm)));}}
        public event PropertyChangedEventHandler PropertyChanged;
    }
    public sealed class TransferBayRow : INotifyPropertyChanged
    {
        public string Label {get;set;}
        private double width=3900,thickness=300;
        public double WidthMm {get=>width;set {width=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(WidthMm)));}}
        public double ThicknessMm {get=>thickness;set {thickness=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(ThicknessMm)));}}
        public event PropertyChangedEventHandler PropertyChanged;
    }
    public sealed class TransferNetworkInput
    {
        public TransferBeamRow[] Beams;
        public TransferBayRow[] Bays;
        public double UpperSpacingMm, LiveKnM2, FormworkKnM2;
        public bool CustomLoads;
        public double AreaLoad(double thickness) => 25*thickness/1000+(CustomLoads ? LiveKnM2+FormworkKnM2 : BeamCalculatorViewModel.HeapingLoad(25*thickness/1000)+.75+.4);
        public void Validate(TowerInput common)
        {
            if(Beams==null || Bays==null || Beams.Length<2 || Beams.Length>8 || Bays.Length!=Beams.Length-1) throw new ArgumentException("Use 2–8 beams with one slab bay between each adjacent pair.");
            if(Beams.Any(b=>b==null || !TowerBeamAnalysis.Finite(b.WidthMm) || !TowerBeamAnalysis.Finite(b.DepthMm) || b.WidthMm<=0 || b.WidthMm>10000 || b.DepthMm<=0 || b.DepthMm>10000)) throw new ArgumentException("Enter a positive width and depth for every beam (maximum 10,000 mm).");
            if(Bays.Any(b=>b==null || !TowerBeamAnalysis.Finite(b.WidthMm) || !TowerBeamAnalysis.Finite(b.ThicknessMm) || b.WidthMm<=0 || b.WidthMm>30000 || b.ThicknessMm<=0)) throw new ArgumentException("Enter a positive width and thickness for every slab bay.");
            for(int i=0;i<Bays.Length;i++) if(Bays[i].ThicknessMm>Math.Min(Beams[i].DepthMm,Beams[i+1].DepthMm)) throw new ArgumentException($"Slab S{i+1} is thicker than an adjacent beam.");
            if(CustomLoads && new[] {LiveKnM2,FormworkKnM2}.Any(x=>!TowerBeamAnalysis.Finite(x) || x<0)) throw new ArgumentException("Custom loads must be finite and nonnegative.");
            new TransferredSlabInput {BeamWidthMm=Beams[0].WidthMm,BeamLineLoadKnM=AreaLoad(Beams[0].DepthMm)*Beams[0].WidthMm/1000,UpperSpacingMm=UpperSpacingMm,RightBayMm=Bays[0].WidthMm,RightAreaLoadKnM2=AreaLoad(Bays[0].ThicknessMm)}.Validate(common);
        }
    }
    public sealed class TransferNetworkResult
    {
        public TransferNetworkInput Input;
        public TowerLayout[] Beams;
        public UpperTransferPlan[] Bays;
        public double[] BeamCentresMm;
        public double SlabMovementMm, SlabLoadKn;
        public int Tested;
        public int LegCount=>Beams.Sum(b=>b.LegCount);
        public int UpperFootCount=>Bays.Length*2*Beams[0].Transfer.RowsMm.Length;
        public double TimberMm=>Beams.Sum(b=>b.StockPlan.TotalStockLengthMm+b.Transfer.RowsMm.Length*b.Transfer.LowerStockLengthMm)+Bays.Sum(b=>b.StockLengthMm)*Beams[0].Transfer.RowsMm.Length;
        public int H20Count=>Beams.Sum(b=>b.StockPlan.TotalH20Count+b.Transfer.RowsMm.Length)+Bays.Length*Beams[0].Transfer.RowsMm.Length;
        public double PeakLegKn=>Beams.Max(b=>b.MaxLegKn);
        public string Summary=>$"{Beams.Length} beams · {Bays.Length} slab bays · {LegCount} tower legs\n{H20Count} H20s · {TimberMm/1000:0.00} m timber · peak leg {PeakLegKn:0.00} kN\nSlab movement {SlabMovementMm:0.00} mm · best found in bounded search";
        public string Schedule=>string.Join("\n\n",Beams.Select((b,i)=>$"B{i+1}: {Input.Beams[i].WidthMm:0} × {Input.Beams[i].DepthMm:0} mm · {b.LegCount} legs\n{b.Schedule}\n{b.StockSchedule}"));
    }
    public static class TransferNetworkOptimizer
    {
        private sealed class State { public TowerLayout[] Beams; public UpperTransferPlan[] Bays; public int Legs;public double Timber; }
        private static bool Better(int legs,double timber,int oldLegs,double oldTimber)=>legs<oldLegs || legs==oldLegs && timber<oldTimber-.01;
        public static TransferNetworkResult Optimize(TowerInput common,TransferNetworkInput input)
        {
            input.Validate(common); TransferNetworkResult best=null;int tested=0;
            var stockCovers=TransferredSlabOptimizer.StockCovers(common);
            foreach(double cover in stockCovers.Select(c=>c.Sum()).Distinct()) foreach(double phase in new[] {0.0,-50,50})
            {
                int rows=(int)Math.Ceiling(cover/input.UpperSpacingMm);double first=(cover-(rows-1)*input.UpperSpacingMm)/2+phase;
                if(first<=0 || first+(rows-1)*input.UpperSpacingMm>=cover || (rows>1 && (first+input.UpperSpacingMm/2>input.UpperSpacingMm+1e-7 || cover-(first+(rows-1)*input.UpperSpacingMm)+input.UpperSpacingMm/2>input.UpperSpacingMm+1e-7))) continue;
                double tributary=rows>2 ? input.UpperSpacingMm : rows==1 ? cover : cover/2+Math.Abs(phase);
                var options=input.Bays.Select((b,i)=>Options(common,input,i,tributary).ToArray()).ToArray();if(options.Any(o=>o.Length==0))continue;
                var states=new Dictionary<int,State>();
                for(int option=0;option<options[0].Length;option++)
                {
                    tested++;var beam=Solve(common,input,0,null,options[0][option],cover,phase,stockCovers);if(beam==null)continue;
                    states[option]=new State {Beams=new[] {beam},Bays=new[] {options[0][option]},Legs=beam.LegCount,Timber=LowerTimber(beam)+options[0][option].StockLengthMm*rows};
                }
                for(int i=1;i<input.Bays.Length && states.Count>0;i++)
                {
                    var next=new Dictionary<int,State>();
                    foreach(var previous in states.Values) for(int option=0;option<options[i].Length;option++)
                    {
                        tested++;var beam=Solve(common,input,i,previous.Bays.Last(),options[i][option],cover,phase,stockCovers);if(beam==null)continue;
                        int legs=previous.Legs+beam.LegCount;double timber=previous.Timber+LowerTimber(beam)+options[i][option].StockLengthMm*rows;
                        if(!next.TryGetValue(option,out var old) || Better(legs,timber,old.Legs,old.Timber))next[option]=new State {Beams=previous.Beams.Concat(new[] {beam}).ToArray(),Bays=previous.Bays.Concat(new[] {options[i][option]}).ToArray(),Legs=legs,Timber=timber};
                    }
                    states=next;
                }
                foreach(var state in states.Values)
                {
                    tested++;var last=Solve(common,input,input.Beams.Length-1,state.Bays.Last(),null,cover,phase,stockCovers);if(last==null)continue;
                    var beams=state.Beams.Concat(new[] {last}).ToArray();int legs=state.Legs+last.LegCount;double timber=state.Timber+LowerTimber(last);
                    if(best!=null && !Better(legs+input.Bays.Length*2*rows,timber,best.LegCount+best.UpperFootCount,best.TimberMm))continue;
                    var centres=new double[beams.Length];for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+input.Beams[i-1].WidthMm/2+input.Bays[i-1].WidthMm+input.Beams[i].WidthMm/2;
                    best=new TransferNetworkResult {Input=input,Beams=beams,Bays=state.Bays,BeamCentresMm=centres,SlabLoadKn=beams.Sum(b=>b.Transfer.SlabLoadKn)};
                    CalculateMovement(best);
                }
            }
            if(best==null)throw new ArgumentException("No valid beam/slab network found. Check slab stocks, beam depths, upper spacing, loads and leg capacity.");
            best.Tested=tested;return best;
        }
        private static double LowerTimber(TowerLayout beam)=>beam.StockPlan.TotalStockLengthMm+beam.Transfer.RowsMm.Length*beam.Transfer.LowerStockLengthMm;
        private static IEnumerable<UpperTransferPlan> Options(TowerInput common,TransferNetworkInput input,int bay,double tributary)
        {
            var row=input.Bays[bay];var result=new List<Tuple<UpperTransferPlan,double>>();
            foreach(double stock in common.StockLengthsMm)
            {
                double edge=(row.WidthMm-stock)/2;if(edge<200 || edge>350)continue;
                double min=Math.Max(250,edge+150),max=row.WidthMm/2-150;
                Func<double,IEnumerable<double>> feet=width=>new[] {min,max}.Concat(new[] {600.0,1000,1500,1524,2000}.Select(w=>(w-width)/2)).Concat(Enumerable.Range(0,Math.Max(0,(int)((max-min)/250)+1)).Select(i=>min+i*250)).Where(x=>x>=min && x<=max).Distinct();
                foreach(double a in feet(input.Beams[bay].WidthMm))foreach(double b in feet(input.Beams[bay+1].WidthMm))
                {
                    var analysis=LoadedBeamAnalysis.Analyze(stock,new[] {a-edge,row.WidthMm-b-edge},new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=input.AreaLoad(row.ThicknessMm)*tributary/1000}},null,TowerOptimizer.PairEI/2,common.DeflectionDenominator);
                    if(analysis.MomentKnM>TowerOptimizer.PairMoment/2+1e-8 || analysis.ShearKn>TowerOptimizer.PairShear/2+1e-8 || analysis.DeflectionUtilization>1+1e-8 || analysis.Reactions.Any(r=>r< -1e-7))continue;
                    result.Add(Tuple.Create(new UpperTransferPlan {StockLengthMm=stock,EdgeMm=edge,NearFootMm=a,FarFootMm=b},analysis.DeflectionUtilization));
                }
            }
            // Keep varied near/far positions, both symmetric and asymmetric. The chain DP is exact over this bounded candidate set.
            return result.OrderBy(r=>r.Item1.StockLengthMm).ThenBy(r=>r.Item1.NearFootMm+r.Item1.FarFootMm).ThenBy(r=>r.Item2)
                .GroupBy(r=>Math.Round(r.Item1.NearFootMm/250)+":"+Math.Round(r.Item1.FarFootMm/250)).Select(g=>g.First().Item1).Take(10).ToArray();
        }
        private static TowerLayout Solve(TowerInput common,TransferNetworkInput network,int beam,UpperTransferPlan left,UpperTransferPlan right,double cover,double phase,double[][] stockCovers)
        {
            var b=network.Beams[beam];var t=new TransferredSlabInput {IsNetwork=true,StockCovers=stockCovers,BeamWidthMm=b.WidthMm,BeamLineLoadKnM=network.AreaLoad(b.DepthMm)*b.WidthMm/1000,UpperSpacingMm=network.UpperSpacingMm,FixedCoveredLengthMm=cover,FixedRowPhaseMm=phase,
                LeftBayMm=left==null ? 0 : network.Bays[beam-1].WidthMm,RightBayMm=right==null ? 0 : network.Bays[beam].WidthMm,LeftAreaLoadKnM2=left==null ? 0 : network.AreaLoad(network.Bays[beam-1].ThicknessMm),RightAreaLoadKnM2=right==null ? 0 : network.AreaLoad(network.Bays[beam].ThicknessMm),LeftPlan=left?.Reverse(),RightPlan=right};
            var input=new TowerInput {LengthMm=common.LengthMm,LegCapacityKn=common.LegCapacityKn,DeflectionDenominator=common.DeflectionDenominator,DeflectionLimitMm=common.DeflectionLimitMm,StockLengthsMm=common.StockLengthsMm,StockEndSetbackMm=common.StockEndSetbackMm,MaximumConcreteEndOverhangMm=common.MaximumConcreteEndOverhangMm,Exclude600Standalone=common.Exclude600Standalone,TransferredSlab=t};
            try{return TransferredSlabOptimizer.Optimize(input);}catch(ArgumentException){return null;}
        }
        public static double FootDisplacement(TowerLayout beam,double beamWidth,int loadCase,int row,bool left)
        {
            var t=beam.Transfer;double station=t.RowsMm[row];int run=Array.FindIndex(beam.StockPlan.Pieces,p=>station>=p.StartMm && station<p.EndMm);var stock=beam.StockPlan.Pieces[run];
            double dl=t.PrimaryCases[loadCase][run].Analysis.Response(station-stock.StartMm)[2],dr=t.PrimaryCases[loadCase][run+beam.StockPlan.Pieces.Length].Analysis.Response(station-stock.StartMm)[2];
            double x=(left ? -1 : 1)*(beamWidth/2+(left ? t.LeftFootMm : t.RightFootMm));
            return t.LowerCases[loadCase][row].Analysis.Response(x-t.LowerStockStartMm)[2]+dl+(dr-dl)*(x+beam.WidthMm/2)/beam.WidthMm;
        }
        private static int Case(TowerLayout b,bool left,bool right)=>Array.IndexOf(b.Transfer.CaseNames,left && right ? "Beam + both slabs" : left ? "Beam + left slab" : right ? "Beam + right slab" : "Beam");
        private static void CalculateMovement(TransferNetworkResult result)
        {
            for(int bay=0;bay<result.Bays.Length;bay++)for(int mask=0;mask<4;mask++)
            {
                if((mask&1)!=0 && bay==0 || (mask&2)!=0 && bay==result.Bays.Length-1)continue;
                var left=result.Beams[bay];var right=result.Beams[bay+1];var upper=left.Transfer.UpperMembers.Single(m=>m.Name=="Upper right");
                int lc=Case(left,(mask&1)!=0,true),rc=Case(right,true,(mask&2)!=0);
                for(int row=0;row<left.Transfer.RowsMm.Length;row++)
                {
                    double dl=FootDisplacement(left,result.Input.Beams[bay].WidthMm,lc,row,false),dr=FootDisplacement(right,result.Input.Beams[bay+1].WidthMm,rc,row,true);
                    var analysis=upper.Analysis.Scale(left.Transfer.TributaryWidthsMm[row]/left.Transfer.TributaryWidthsMm.Max());
                    double movement=analysis.DeflectionWithAffineOffsetMm(dl,dr,upper.SupportsMm[0],upper.SupportsMm[1]);
                    result.SlabMovementMm=Math.Max(result.SlabMovementMm,movement);
                    left.Transfer.SlabMovementMm=Math.Max(left.Transfer.SlabMovementMm,movement);right.Transfer.SlabMovementMm=Math.Max(right.Transfer.SlabMovementMm,movement);
                }
            }
        }
    }
}
