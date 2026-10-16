using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard
{
    // Adopted primary tributary model: own beam shared equally; each adjacent
    // bay loads its inner primary. Secondary bearing checks remain independent.
    internal sealed class EngineerPrimaryPlan
    {
        internal BeamDistributedLoad[][] Lines;
        internal double[] Forces;
        internal int InitialLegs;
        internal bool UseDesignZones;
        internal TransferNetworkInput Network;
        internal int Beam;
        internal double LengthMm;
        internal bool LeftSlab,RightSlab;
        internal bool IsResolved;
        internal readonly Dictionary<int,double[]> CrossingTransfers=new Dictionary<int,double[]>();
        internal readonly Dictionary<int,double> SlabLedgerForces=new Dictionary<int,double>();
        internal bool CarriesBay(int bay)=>(bay==Beam-1 && LeftSlab) || (bay==Beam && RightSlab);
        internal bool HasCrossings=>Adjacent(Network,Beam).Any();
        internal static EngineerPrimaryPlan Create(TowerInput common,TransferNetworkInput network,int beam,bool leftSlab=true,bool rightSlab=true)
        {
            var plan=new EngineerPrimaryPlan {Network=network,Beam=beam,LengthMm=common.LengthMm,LeftSlab=leftSlab,RightSlab=rightSlab,
                UseDesignZones=network.CrossingBeams.Where(c=>c.BayNumber==beam || c.BayNumber==beam+1).All(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows)};
            plan.Lines=plan.Build(null);
            plan.Forces=plan.Lines.Select(l=>l.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000)).ToArray();
            plan.InitialLegs=EngineerTowerSearch.InitialLegs(common,plan.Forces.Max());return plan;
        }
        private BeamDistributedLoad[][] Build(HashSet<int> active)
        {
            var network=Network;int beam=Beam;
            double own=network.AreaLoad(network.Beams[beam].DepthMm)*network.Beams[beam].WidthMm/2000;
            var lines=new[] {new List<BeamDistributedLoad>(),new List<BeamDistributedLoad>()};
            for(int line=0;line<2;line++)
            {
                lines[line].Add(new BeamDistributedLoad {StartMm=0,EndMm=LengthMm,KnPerM=own});
                int bay=line==0 ? beam-1 : beam;
                if(bay<0 || bay>=network.Bays.Length)continue;
                double slab=CarriesBay(bay) ? network.AreaLoad(network.Bays[bay].ThicknessMm)*network.Bays[bay].WidthMm/2000 : 0;
                var cuts=network.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).OrderBy(c=>c.PositionMm).ToArray();
                // Slab edge loads carried by independent crossing formwork are taken
                // once from the normal slab zone, rather than creating flange-width
                // peaks or loading the main primaries and crossing towers twice.
                double normalLength=LengthMm-cuts.Sum(c=>c.WidthMm);
                if(SlabLedgerForces.TryGetValue(bay,out double ledger) && normalLength>1e-7)
                    slab-=ledger*1000/normalLength;
                if(slab< -1e-7)throw new ArgumentException("Crossing slab transfer exceeds the available slab load.");
                slab=Math.Max(0,slab);
                double start=0;
                foreach(var cut in cuts)
                {
                    double a=cut.PositionMm-cut.WidthMm/2,z=cut.PositionMm+cut.WidthMm/2;
                    if(a>start)lines[line].Add(new BeamDistributedLoad {StartMm=start,EndMm=a,KnPerM=slab});
                    int index=Array.IndexOf(network.CrossingBeams,cut);
                    if((active==null || active.Contains(index)) && !CrossingTransfers.ContainsKey(index))
                        lines[line].Add(new BeamDistributedLoad {StartMm=a,EndMm=z,KnPerM=network.AreaLoad(cut.DepthMm)*network.Bays[bay].WidthMm/2000});
                    start=z;
                }
                if(start<LengthMm)lines[line].Add(new BeamDistributedLoad {StartMm=start,EndMm=LengthMm,KnPerM=slab});
            }
            foreach(var assigned in CrossingTransfers)
            {
                if(active!=null && !active.Contains(assigned.Key))continue;
                var cut=network.CrossingBeams[assigned.Key];
                for(int line=0;line<2;line++)lines[line].Add(new BeamDistributedLoad {StartMm=cut.PositionMm-cut.WidthMm/2,EndMm=cut.PositionMm+cut.WidthMm/2,KnPerM=assigned.Value[line]});
            }
            return lines.Select(Merge).ToArray();
        }
        internal bool Applies(CrossBeamSupportResult[] supports)=>UseDesignZones;
        internal EngineerPrimaryPlan Resolve(TowerInput common,TransferNetworkResult result,int receivingBeam,CrossBeamSupportResult[] supports)
        {
            var plan=Create(common,Network,Beam,LeftSlab,RightSlab);plan.IsResolved=true;
            var t=result.Beams[receivingBeam].Transfer;
            foreach(var support in supports.Where(s=>s.Input.BayNumber==Beam || s.Input.BayNumber==Beam+1))
            {
                if(support.UsesSpreader)continue;
                var force=new double[2];
                foreach(var bearing in support.Bearings.Where(b=>b.Beam==receivingBeam))
                {
                    var lower=t.LowerCases[0][bearing.Row];
                    double position=(bearing.IsConcreteEndZone && !bearing.IsShutterEndZone ? (bearing.EndZoneStartMm+bearing.EndZoneEndMm)/2 : bearing.Ymm)-result.BeamCentresMm[receivingBeam]-lower.StartMm;
                    double right=bearing.ReactionKn*(position-lower.SupportsMm[0])/(lower.SupportsMm[1]-lower.SupportsMm[0]);
                    force[0]+=bearing.ReactionKn-right;force[1]+=right;
                }
                plan.CrossingTransfers[support.CrossingIndex]=force.Select(f=>f*1000/support.Input.WidthMm).ToArray();
                int bay=support.Input.BayNumber-1;
                if(plan.CarriesBay(bay) && support.SlabEdgeLoadKn>0)
                    plan.SlabLedgerForces[bay]=(plan.SlabLedgerForces.TryGetValue(bay,out double prior) ? prior : 0)+support.SlabEdgeLoadKn/2;
            }
            plan.Lines=plan.Build(null);plan.Forces=plan.Lines.Select(l=>l.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000)).ToArray();
            plan.InitialLegs=EngineerTowerSearch.InitialLegs(common,plan.Forces.Max());return plan;
        }
        internal double ValueAt(double position,int line)=>Lines[line].Where(q=>q.StartMm<=position+1e-7 && q.EndMm>position+1e-7).Sum(q=>q.KnPerM);
        internal BeamDistributedLoad[] Loads(double start,double length,int line,IEnumerable<int> active=null,double selfWeight=0)
        {
            var zones=active==null ? Lines : Build(new HashSet<int>(active));
            var loads=zones[line].Select(q=>new BeamDistributedLoad {StartMm=Math.Max(start,q.StartMm)-start,EndMm=Math.Min(start+length,q.EndMm)-start,KnPerM=q.KnPerM})
                .Where(q=>q.EndMm>q.StartMm+1e-7).ToList();
            if(selfWeight>0)loads.Add(new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=selfWeight});
            return Merge(loads);
        }
        internal ColumnEndStripLoad[] Columns(double first,double last,string name,IEnumerable<int> active=null)
        {
            var result=new List<ColumnEndStripLoad>();
            foreach(int end in new[] {0,1})
            {
                double a=end==0 ? 0 : last,z=end==0 ? first : LengthMm;if(z<=a+1e-7)continue;
                var force=new double[2];var moment=new double[2];
                for(int line=0;line<2;line++)foreach(var q in Loads(a,z-a,line,active))
                {double f=q.KnPerM*(q.EndMm-q.StartMm)/1000;force[line]+=f;moment[line]+=f*(a+(q.StartMm+q.EndMm)/2)/1000;}
                result.Add(new ColumnEndStripLoad {LoadCase=name,End=end==0 ? "Near" : "Far",StartMm=a,EndMm=z,LeftLineKn=force[0],RightLineKn=force[1],LeftLineMomentKnM=moment[0],RightLineMomentKnM=moment[1]});
            }
            return result.ToArray();
        }
        internal static bool SameLoads(BeamDistributedLoad[] a,BeamDistributedLoad[] b)=>a.Length==b.Length && a.Zip(b,(x,y)=>Math.Abs(x.StartMm-y.StartMm)<1e-7 && Math.Abs(x.EndMm-y.EndMm)<1e-7 && Math.Abs(x.KnPerM-y.KnPerM)<1e-10).All(x=>x);
        private static BeamDistributedLoad[] Merge(List<BeamDistributedLoad> loads)
        {
            var cuts=loads.SelectMany(q=>new[] {q.StartMm,q.EndMm}).Distinct().OrderBy(x=>x).ToArray();var result=new List<BeamDistributedLoad>();
            for(int i=1;i<cuts.Length;i++)
            {
                double middle=(cuts[i-1]+cuts[i])/2,q=loads.Where(b=>b.StartMm<middle && b.EndMm>middle).Sum(b=>b.KnPerM);
                if(result.Count>0 && Math.Abs(result.Last().KnPerM-q)<1e-10)result.Last().EndMm=cuts[i];
                else result.Add(new BeamDistributedLoad {StartMm=cuts[i-1],EndMm=cuts[i],KnPerM=q});
            }
            return result.ToArray();
        }
        internal static bool Eligible(TransferNetworkInput n,TransferCrossingBeamRow c)
        {
            int bay=c.BayNumber-1;
            return c.SupportMode!=CrossBeamSupportMode.LegacySlabRows && c.SupportMode!=CrossBeamSupportMode.DedicatedTowers
                && c.BottomElevationMm-200>=Math.Max(n.Beams[bay].BottomElevationMm,n.Beams[bay+1].BottomElevationMm)-1e-7 && c.WidthMm>=160;
        }
        internal static IEnumerable<TransferCrossingBeamRow> Adjacent(TransferNetworkInput n,int beam)
            =>n.CrossingBeams.Where(c=>(c.BayNumber==beam || c.BayNumber==beam+1) && Eligible(n,c));
        internal static double[] Rows(TransferredSlabInput t,double[] required)
        {
            var result=new List<double>();var n=t.EngineerNetwork;
            if(n==null)return result.ToArray();
            foreach(var c in Adjacent(n,t.EngineerBeam))
            {
                double a=c.PositionMm-c.WidthMm/2+40,z=c.PositionMm+c.WidthMm/2-40;
                int count=Math.Min(t.EngineerReceiverCount,1+(int)Math.Floor((z-a)/80));
                if(count<2)continue;
                for(int i=0;i<count;i++)
                {
                    double x=a+(z-a)*i/(count-1);
                    // Real plywood joints remain supported. Nearby optional rows are
                    // represented by that joint instead of overlapping its flange.
                    double joint=Math.Round(x/PlywoodSpacing.SheetLengthMm)*PlywoodSpacing.SheetLengthMm;
                    if(joint>=a && joint<=z && Math.Abs(x-joint)<80-1e-7)x=joint;
                    if(required.Any(r=>Math.Abs(r-x)<80-1e-7))continue;
                    if(!result.Any(r=>Math.Abs(r-x)<80-1e-7))result.Add(x);
                }
            }
            return result.OrderBy(x=>x).ToArray();
        }
        internal static bool Local(TransferredSlabInput t,double x)=>t.EngineerNetwork!=null && Adjacent(t.EngineerNetwork,t.EngineerBeam).Any(c=>Math.Abs(x-c.PositionMm)<=c.WidthMm/2-40+1e-7);
        internal string Summary=>string.Format(CultureInfo.InvariantCulture,"{3} force L/R {0:0.###}/{1:0.###} kN; {4} {2} legs per primary line. Beam + slab outside crossings; assigned beam + crossing transfer over each full crossing width. Dedicated tower loads and slab edge transfers are counted once. Secondary bearings are checked separately.",Forces[0],Forces[1],InitialLegs,IsResolved ? "Selected primary-zone" : "Provisional full-tributary",IsResolved ? "selected-force estimate" : "provisional estimate")
            +" "+string.Join("; ",Lines.Select((line,i)=>(i==0 ? "L" : "R")+": "+string.Join(", ",line.Select(q=>string.Format(CultureInfo.InvariantCulture,"{0:0.###}–{1:0.###} mm @ {2:0.#####} kN/m",q.StartMm,q.EndMm,q.KnPerM)))));
    }

    public static partial class CrossBeamSupportOptimizer
    {
        private static bool SpreadShared(TowerInput common,TransferNetworkResult result,CrossBeamSupportResult support,out string failure)
        {
            failure=null;var c=support.Input;double a=c.PositionMm-c.WidthMm/2,z=c.PositionMm+c.WidthMm/2;
            var redistributed=new List<CrossBeamBearing>();
            foreach(var side in support.Bearings.GroupBy(b=>b.Beam))
            {
                int beam=side.Key;var rows=result.Beams[beam].Transfer.RowsMm;
                var receivers=Enumerable.Range(0,rows.Length).Where(i=>rows[i]>=a+40-1e-7 && rows[i]<=z-40+1e-7).ToArray();
                if(receivers.Length<2){failure=$"X{support.CrossingIndex+1}: spreader needs at least two receiving H20s inside its {c.WidthMm:0.###} mm footprint.";return false;}
                double mean=receivers.Average(i=>rows[i]),sumSquare=receivers.Sum(i=>(rows[i]-mean)*(rows[i]-mean));
                if(sumSquare<1e-7)return false;
                // Rigid, engineer-specified spreader with equal bearing compliance.
                // A centred force shares equally on symmetric rows; eccentric forces
                // retain their first moment. No spreader strength is claimed here.
                foreach(var group in side.GroupBy(b=>Tuple.Create(b.Ymm,b.IsConcreteEndZone,b.IsShutterEndZone,b.Label??"",b.EndZoneStartMm,b.EndZoneEndMm)))
                {
                    double force=group.Sum(b=>b.ReactionKn),moment=group.Sum(b=>b.ReactionKn*(b.Xmm-mean));
                    foreach(int row in receivers)
                    {
                        var bearing=group.First().Copy();bearing.Row=row;bearing.Xmm=rows[row];
                        bearing.ReactionKn=force/receivers.Length+moment*(rows[row]-mean)/sumSquare;
                        if(!TowerBeamAnalysis.Finite(bearing.ReactionKn)){failure="Non-finite spreader reaction.";return false;}
                        bearing.FootprintStartMm=rows[row]-40;bearing.FootprintEndMm=rows[row]+40;
                        if(bearing.IsConcreteEndZone && Math.Abs(force)>1e-10)bearing.EndZoneTributaryWidthMm=group.Sum(b=>b.EndZoneTributaryWidthMm)*bearing.ReactionKn/force;
                        redistributed.Add(bearing);
                    }
                }
            }
            support.Bearings=redistributed.ToArray();support.UsesSpreader=true;
            support.JunctionNotice=$"X{support.CrossingIndex+1} reaction shared within {c.WidthMm:0.###} mm over "+string.Join("; ",support.Bearings.GroupBy(b=>b.Beam).Select(g=>$"B{g.Key+1}: {g.Select(b=>b.Row).Distinct().Count()} receiving H20s"))+". Spreader load sharing is an engineer-specified rigid distribution; spreader member design is outside the calculator scope. Force and first moment are preserved.";
            return true;
        }

        internal static bool CheckEngineerReceiver(TowerInput common,TransferNetworkInput network,int beam,TowerLayout layout,bool lowerOnly=false)
        {
            var eligible=EngineerPrimaryPlan.Adjacent(network,beam).ToArray();
            if(eligible.Length==0)return true;
            double cover=common.TransferredSlab.FixedCoveredLengthMm??common.LengthMm;
            var centres=new double[network.Beams.Length];
            for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+network.Beams[i-1].WidthMm/2+network.Bays[i-1].WidthMm+network.Beams[i].WidthMm/2;
            // The local planning check uses a mirrored opposite receiver. The actual
            // complete network is checked again with both selected receiver geometries.
            var mirror=new TransferNetworkResult {Input=network,Beams=Enumerable.Repeat(layout,network.Beams.Length).ToArray(),Bays=network.Bays.Select((b,i)=>(layout.Transfer.PrimaryPlan??common.EngineerPlan)?.CarriesBay(i)==false ? null : new UpperTransferPlan()).ToArray(),BeamCentresMm=centres,SlabCoveredLengthMm=cover};
            var supports=new List<CrossBeamSupportResult>();
            foreach(var c in eligible)
            {
                int index=Array.IndexOf(network.CrossingBeams,c);
                var shared=Shared(common,mirror,index,out string failure);
                if(shared==null)
                {
                    if(c.SupportMode!=CrossBeamSupportMode.Automatic || layout.Transfer.EngineerReceiverCount<8)return false;
                    var dedicated=common.CopyForSearch();dedicated.EngineerDedicatedFallback=true;
                    return lowerOnly ? CheckJunctionLowerReceiver(dedicated,network,beam,layout) : CheckJunctionReceiver(dedicated,network,beam,layout);
                }
                var own=shared.Bearings.Where(b=>b.Beam==beam).Select(b=>
                {
                    var copy=b.Copy();copy.Beam=0;copy.Ymm-=centres[beam];copy.EndZoneStartMm-=centres[beam];copy.EndZoneEndMm-=centres[beam];return copy;
                }).ToArray();
                supports.Add(new CrossBeamSupportResult {CrossingIndex=index,Input=c,Bearings=own,UsesSpreader=true});
            }
            var local=new TransferNetworkResult {Input=new TransferNetworkInput {Beams=new[] {network.Beams[beam]}},Beams=new[] {layout},BeamCentresMm=new[] {0.0}};
            if(Recheck(common,local,new[] {layout},supports.ToArray(),lowerOnly))return true;
            // Automatic support can try the separately supported detail only after
            // exhausting local spreader refinement. The final network validates the
            // actual crossing detail and all receiver/primary/leg demands again.
            if(layout.Transfer.EngineerReceiverCount<8 || eligible.Any(c=>c.SupportMode!=CrossBeamSupportMode.Automatic))return false;
            var fallback=common.CopyForSearch();fallback.EngineerDedicatedFallback=true;
            return lowerOnly ? CheckJunctionLowerReceiver(fallback,network,beam,layout) : CheckJunctionReceiver(fallback,network,beam,layout);
        }
    }
}
