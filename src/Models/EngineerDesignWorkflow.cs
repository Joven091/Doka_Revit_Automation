using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard
{
    // A planning model, not a substitute for the final bearing/member analyses.
    // Own-beam load is shared equally; each adjacent bay initially loads its inner line.
    internal sealed class EngineerPrimaryPlan
    {
        internal BeamDistributedLoad[][] Lines;
        internal double[] Forces;
        internal int InitialLegs;
        internal static EngineerPrimaryPlan Create(TowerInput common,TransferNetworkInput network,int beam)
        {
            double own=network.AreaLoad(network.Beams[beam].DepthMm)*network.Beams[beam].WidthMm/2000;
            var lines=new[] {new List<BeamDistributedLoad>(),new List<BeamDistributedLoad>()};
            for(int line=0;line<2;line++)
            {
                lines[line].Add(new BeamDistributedLoad {StartMm=0,EndMm=common.LengthMm,KnPerM=own});
                int bay=line==0 ? beam-1 : beam;
                if(bay<0 || bay>=network.Bays.Length)continue;
                double slab=network.AreaLoad(network.Bays[bay].ThicknessMm)*network.Bays[bay].WidthMm/2000;
                var cuts=network.CrossingBeams.Where(c=>c.BayNumber==bay+1 && Eligible(network,c)).OrderBy(c=>c.PositionMm).ToArray();
                double start=0;
                foreach(var cut in cuts)
                {
                    double a=cut.PositionMm-cut.WidthMm/2,z=cut.PositionMm+cut.WidthMm/2;
                    if(a>start)lines[line].Add(new BeamDistributedLoad {StartMm=start,EndMm=a,KnPerM=slab});
                    lines[line].Add(new BeamDistributedLoad {StartMm=a,EndMm=z,KnPerM=network.AreaLoad(cut.DepthMm)*network.Bays[bay].WidthMm/2000});
                    start=z;
                }
                if(start<common.LengthMm)lines[line].Add(new BeamDistributedLoad {StartMm=start,EndMm=common.LengthMm,KnPerM=slab});
            }
            var forces=lines.Select(l=>l.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000)).ToArray();
            return new EngineerPrimaryPlan {Lines=lines.Select(l=>l.ToArray()).ToArray(),Forces=forces,InitialLegs=EngineerTowerSearch.InitialLegs(common,forces.Max())};
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
        internal string Summary=>string.Format(CultureInfo.InvariantCulture,"Planning force L/R {0:0.###}/{1:0.###} kN; initial {2} legs per primary line. Full beam/slab/crossing zones; final bearing reactions and capacities independently checked.",Forces[0],Forces[1],InitialLegs);
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
            var mirror=new TransferNetworkResult {Input=network,Beams=Enumerable.Repeat(layout,network.Beams.Length).ToArray(),Bays=network.Bays.Select(b=>new UpperTransferPlan()).ToArray(),BeamCentresMm=centres,SlabCoveredLengthMm=cover};
            var supports=new List<CrossBeamSupportResult>();
            foreach(var c in eligible)
            {
                int index=Array.IndexOf(network.CrossingBeams,c);
                var shared=Shared(common,mirror,index,out string failure);
                if(shared==null)return false;
                var own=shared.Bearings.Where(b=>b.Beam==beam).Select(b=>
                {
                    var copy=b.Copy();copy.Beam=0;copy.Ymm-=centres[beam];copy.EndZoneStartMm-=centres[beam];copy.EndZoneEndMm-=centres[beam];return copy;
                }).ToArray();
                supports.Add(new CrossBeamSupportResult {CrossingIndex=index,Input=c,Bearings=own,UsesSpreader=true});
            }
            var local=new TransferNetworkResult {Input=new TransferNetworkInput {Beams=new[] {network.Beams[beam]}},Beams=new[] {layout},BeamCentresMm=new[] {0.0}};
            return Recheck(common,local,new[] {layout},supports.ToArray(),lowerOnly);
        }
    }
}
