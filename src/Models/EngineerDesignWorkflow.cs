using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard
{
    // Loads per metre along the main beam, at their actual transverse positions.
    // A reference-spacing diagram scales these inputs before solving reactions.
    internal sealed class PositionedSecondaryLoad
    {
        internal string Name;
        internal int Bay=-1;
        internal bool IsUniform,IsSlabFoot,IsSlabEdge;
        internal double PositionMm,EndMm,ValuePerMetre,AreaLoadKnM2,SourceLengthMm,AllocationFactor=1;
        internal PositionedSecondaryLoad Scaled(double factor)
        {var copy=(PositionedSecondaryLoad)MemberwiseClone();copy.AllocationFactor*=factor;return copy;}
    }
    internal sealed class EngineerPrimaryPlan
    {
        internal BeamDistributedLoad[][] Lines;
        internal double[] Forces;
        internal int InitialLegs;
        internal bool UseDesignZones;
        internal TransferNetworkInput Network;
        internal int Beam;
        internal double LengthMm,StartMm,ReferenceSpacingMm;
        internal bool LeftSlab,RightSlab,IsResolved;
        internal TransferMember SecondaryBasis;
        internal PositionedSecondaryLoad[] OwnLoads=new PositionedSecondaryLoad[0];
        internal readonly Dictionary<int,PositionedSecondaryLoad[]> SlabLoads=new Dictionary<int,PositionedSecondaryLoad[]>();
        internal readonly Dictionary<int,PositionedSecondaryLoad[]> CrossingLoads=new Dictionary<int,PositionedSecondaryLoad[]>();
        internal readonly Dictionary<int,double[]> CrossingTransfers=new Dictionary<int,double[]>();
        internal readonly Dictionary<int,double[]> PrimaryBearingTransfers=new Dictionary<int,double[]>();
        internal readonly Dictionary<int,double[]> CrossingResultantOffsetsMm=new Dictionary<int,double[]>();
        internal readonly Dictionary<int,double> SlabLedgerForces=new Dictionary<int,double>();
        private readonly ConcurrentDictionary<string,EngineerPrimaryPlan> resolvedCache=new ConcurrentDictionary<string,EngineerPrimaryPlan>();
        internal bool HasPositionedLoads=>SecondaryBasis!=null;
        internal bool CarriesBay(int bay)=>(bay==Beam-1 && LeftSlab) || (bay==Beam && RightSlab);
        internal bool HasCrossings=>Adjacent(Network,Beam).Any();
        internal static EngineerPrimaryPlan Create(TowerInput common,TransferNetworkInput network,int beam,bool leftSlab=true,bool rightSlab=true)
        {
            var plan=new EngineerPrimaryPlan {Network=network,Beam=beam,LengthMm=common.LengthMm,StartMm=common.LongitudinalStartMm,LeftSlab=leftSlab,RightSlab=rightSlab,
                UseDesignZones=network.CrossingBeams.Where(c=>c.BayNumber==beam || c.BayNumber==beam+1).All(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows)};
            plan.Refresh(common);return plan;
        }
        private EngineerPrimaryPlan CopyBasis()
        {
            var copy=new EngineerPrimaryPlan {Network=Network,Beam=Beam,LengthMm=LengthMm,StartMm=StartMm,LeftSlab=LeftSlab,RightSlab=RightSlab,UseDesignZones=UseDesignZones,ReferenceSpacingMm=ReferenceSpacingMm,SecondaryBasis=SecondaryBasis,OwnLoads=OwnLoads};
            foreach(var source in SlabLoads)copy.SlabLoads[source.Key]=source.Value;
            return copy;
        }
        private void Refresh(TowerInput common)
        {
            Lines=Build(null);Forces=Lines.Select(l=>l.Sum(q=>q.KnPerM*(q.EndMm-q.StartMm)/1000)).ToArray();
            InitialLegs=EngineerTowerSearch.InitialLegs(common,Forces.Max());
        }
        internal EngineerPrimaryPlan WithGeometry(TowerInput common,TransferMember unit,string[] labels)
        {
            var plan=CopyBasis();plan.SecondaryBasis=unit;plan.ReferenceSpacingMm=common.TransferredSlab?.UpperSpacingMm??Network.BeamSpacing(Beam);
            plan.OwnLoads=unit.Uniform.Select(q=>new PositionedSecondaryLoad {Name="Beam concrete",IsUniform=true,PositionMm=q.StartMm,EndMm=q.EndMm,ValuePerMetre=q.KnPerM}).ToArray();
            for(int side=0;side<2;side++)
            {
                int bay=side==0 ? Beam-1 : Beam;
                if(bay<0 || bay>=Network.Bays.Length || !CarriesBay(bay))continue;
                double area=Network.AreaLoad(Network.Bays[bay].ThicknessMm);string prefix=side==0 ? "Left " : "Right ";
                plan.SlabLoads[bay]=unit.Points.Select((p,i)=>new {P=p,Label=i<labels.Length ? labels[i] : ""}).Where(p=>p.Label.StartsWith(prefix,StringComparison.Ordinal)).Select(p=>
                {
                    bool shutter=p.Label.IndexOf("shutter",StringComparison.OrdinalIgnoreCase)>=0;
                    return new PositionedSecondaryLoad {Name=$"S{bay+1} "+(shutter ? "side-shutter edge transfer" : "slab H20 reaction"),Bay=bay,IsSlabEdge=shutter,IsSlabFoot=!shutter,
                        PositionMm=p.P.PositionMm,ValuePerMetre=p.P.Kn,AreaLoadKnM2=area,SourceLengthMm=p.P.Kn/area*1000*(shutter ? 1 : 2)};
                }).ToArray();
            }
            plan.Refresh(common);return plan;
        }
        internal static double[] Reactions(TransferMember basis,IEnumerable<PositionedSecondaryLoad> loads)
        {
            double left=0,right=0,a=basis.SupportsMm[0],b=basis.SupportsMm[1];
            foreach(var q in loads)
            {
                double force=q.ValuePerMetre*q.AllocationFactor*(q.IsUniform ? (q.EndMm-q.PositionMm)/1000 : 1);
                double x=q.IsUniform ? (q.PositionMm+q.EndMm)/2 : q.PositionMm;
                double r=force*(x-a)/(b-a);left+=force-r;right+=r;
            }
            return new[] {left,right};
        }
        private PositionedSecondaryLoad[] NormalLoads(int bay)
        {
            if(!SlabLoads.TryGetValue(bay,out var loads))return new PositionedSecondaryLoad[0];
            double reference=Network.ReferenceLengthMm>0 ? Network.ReferenceLengthMm : StartMm+LengthMm;
            double force=loads.Sum(q=>q.ValuePerMetre),normalLength=Network.BayEnd(bay,reference)-Network.BayStart(bay,reference)-Network.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).Sum(c=>c.WidthMm);
            double fraction=SlabLedgerForces.TryGetValue(bay,out var ledger) && normalLength>1e-7 && force>1e-10 ? 1-ledger*1000/normalLength/force : 1;
            if(fraction< -1e-7)throw new ArgumentException("Crossing slab transfer exceeds the available slab load.");
            return loads.Select(q=>q.Scaled(Math.Max(0,fraction))).ToArray();
        }
        internal PositionedSecondaryLoad[] LoadsAt(double x)
        {
            var loads=new List<PositionedSecondaryLoad>(OwnLoads);
            foreach(int bay in new[] {Beam-1,Beam}.Where(b=>b>=0 && b<Network.Bays.Length))
            {
                double reference=Network.ReferenceLengthMm>0 ? Network.ReferenceLengthMm : StartMm+LengthMm;
                if(x<Network.BayStart(bay,reference)-1e-7 || x>Network.BayEnd(bay,reference)+1e-7)continue;
                var crossing=Network.CrossingBeams.Select((c,i)=>new {C=c,I=i}).FirstOrDefault(c=>c.C.BayNumber==bay+1 && c.C.SupportMode!=CrossBeamSupportMode.LegacySlabRows && x>=c.C.PositionMm-c.C.WidthMm/2-1e-7 && x<c.C.PositionMm+c.C.WidthMm/2-1e-7);
                if(crossing==null)loads.AddRange(NormalLoads(bay));
                else if(CrossingLoads.TryGetValue(crossing.I,out var assigned))loads.AddRange(assigned);
            }
            return loads.ToArray();
        }
        private BeamDistributedLoad[][] Build(HashSet<int> active)
        {
            var network=Network;double own=network.AreaLoad(network.Beams[Beam].DepthMm)*network.Beams[Beam].WidthMm/2000;
            var ownReaction=HasPositionedLoads ? Reactions(SecondaryBasis,OwnLoads) : new[] {own,own};
            var lines=new[] {new List<BeamDistributedLoad>(),new List<BeamDistributedLoad>()};
            for(int line=0;line<2;line++)lines[line].Add(new BeamDistributedLoad {StartMm=StartMm,EndMm=StartMm+LengthMm,KnPerM=ownReaction[line]});
            foreach(int bay in new[] {Beam-1,Beam}.Where(b=>b>=0 && b<network.Bays.Length))
            {
                double slab=CarriesBay(bay) ? network.AreaLoad(network.Bays[bay].ThicknessMm)*network.Bays[bay].WidthMm/2000 : 0;
                var cuts=network.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).OrderBy(c=>c.PositionMm).ToArray();
                double bayStart=network.BayStart(bay,network.ReferenceLengthMm>0 ? network.ReferenceLengthMm : StartMm+LengthMm),bayEnd=network.BayEnd(bay,network.ReferenceLengthMm>0 ? network.ReferenceLengthMm : StartMm+LengthMm);
                double normalLength=bayEnd-bayStart-cuts.Sum(c=>c.WidthMm);
                if(!HasPositionedLoads && SlabLedgerForces.TryGetValue(bay,out double ledger) && normalLength>1e-7)slab-=ledger*1000/normalLength;
                if(slab< -1e-7)throw new ArgumentException("Crossing slab transfer exceeds the available slab load.");
                var normal=HasPositionedLoads ? Reactions(SecondaryBasis,NormalLoads(bay)) : new[] {bay==Beam-1 ? Math.Max(0,slab) : 0,bay==Beam ? Math.Max(0,slab) : 0};
                double start=bayStart;
                foreach(var cut in cuts)
                {
                    double a=cut.PositionMm-cut.WidthMm/2,z=cut.PositionMm+cut.WidthMm/2;int index=Array.IndexOf(network.CrossingBeams,cut);
                    for(int line=0;line<2;line++)
                    {
                        if(a>start)lines[line].Add(new BeamDistributedLoad {StartMm=start,EndMm=a,KnPerM=normal[line]});
                        if((active==null || active.Contains(index)) && !CrossingTransfers.ContainsKey(index))
                        {
                            double q=network.AreaLoad(cut.DepthMm)*network.Bays[bay].WidthMm/2000;
                            // Before support allocation this is only a planning estimate.
                            lines[line].Add(new BeamDistributedLoad {StartMm=a,EndMm=z,KnPerM=line==(bay==Beam ? 1 : 0) ? q : 0});
                        }
                    }
                    start=z;
                }
                if(start<bayEnd)for(int line=0;line<2;line++)lines[line].Add(new BeamDistributedLoad {StartMm=start,EndMm=bayEnd,KnPerM=normal[line]});
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
        internal EngineerPrimaryPlan Resolve(TowerInput common,TransferNetworkResult result,int receivingBeam,CrossBeamSupportResult[] supports,bool pendingAssignments=false)
        {
            var t=result.Beams[receivingBeam].Transfer;string cacheKey=null;
            if(common.ReuseSearchWork)
            {
                // Only identical transverse sources are reused. Longitudinal tower
                // stations do not alter this statically determinate reaction split.
                cacheKey=FormattableString.Invariant($"{receivingBeam}|{result.BeamCentresMm[receivingBeam]:R}|{pendingAssignments}|{common.LegCapacityKn:R}|{common.PrimaryMaterial}|")
                    +string.Join(";",supports.Select(s=>FormattableString.Invariant($"{s.Input.BayNumber},{s.Input.PositionMm:R},{s.Input.WidthMm:R},{s.UsesSpreader},{s.SlabEdgeLoadKn:R}")+":"
                        +string.Join(",",s.Bearings.Where(b=>b.Beam==receivingBeam).Select(b=>FormattableString.Invariant($"{b.Xmm:R}/{b.Ymm:R}/{b.ReactionKn:R}/{b.Label}/{b.IsConcreteEndZone}/{b.IsShutterEndZone}/{b.EndZoneStartMm:R}/{b.EndZoneEndMm:R}/{b.PrimaryLine}/{(b.IsPrimaryBearing ? 0 : t.LowerCases[0][b.Row].StartMm):R}")))))
                    +"|"+string.Join(";",Network.CrossingBeams.Select(c=>FormattableString.Invariant($"{c.BayNumber}/{c.PositionMm:R}/{c.WidthMm:R}/{c.DepthMm:R}/{c.BottomElevationMm:R}/{c.SupportMode}")));
                if(resolvedCache.TryGetValue(cacheKey,out var cached))return cached;
            }
            var plan=CopyBasis();plan.IsResolved=true;
            for(int i=0;i<Network.CrossingBeams.Length;i++)
                if((Network.CrossingBeams[i].BayNumber==Beam || Network.CrossingBeams[i].BayNumber==Beam+1) && (pendingAssignments || !Eligible(Network,Network.CrossingBeams[i])))plan.CrossingTransfers[i]=new double[2];
            foreach(var support in supports.Where(s=>s.Input.BayNumber==Beam || s.Input.BayNumber==Beam+1))
            {
                int index=Array.IndexOf(Network.CrossingBeams,support.Input);
                if(index<0)index=Array.FindIndex(Network.CrossingBeams,c=>c.BayNumber==support.Input.BayNumber && Math.Abs(c.PositionMm-support.Input.PositionMm)<1e-7 && Math.Abs(c.WidthMm-support.Input.WidthMm)<1e-7);
                if(index<0)return null;
                if(!plan.HasPositionedLoads && support.UsesSpreader){plan.CrossingTransfers.Remove(index);continue;}
                var sources=new List<PositionedSecondaryLoad>();var force=new double[2];var directForce=new double[2];var longitudinalMoment=new double[2];
                // Shared ledger loads stay in the normal slab design allocation, once.
                // The physical secondary checks still include every ledger reaction.
                foreach(var bearing in support.Bearings.Where(b=>b.Beam==receivingBeam && (!support.UsesSpreader || b.Label!="Slab edge ledger")))
                {
                    if(bearing.IsPrimaryBearing)
                    {
                        force[bearing.PrimaryLine]+=bearing.ReactionKn;
                        directForce[bearing.PrimaryLine]+=bearing.ReactionKn;
                        longitudinalMoment[bearing.PrimaryLine]+=bearing.ReactionKn*(bearing.Xmm-support.Input.PositionMm);
                        continue;
                    }
                    var lower=t.LowerCases[0][bearing.Row];double origin=result.BeamCentresMm[receivingBeam]+lower.StartMm;
                    bool uniform=bearing.IsConcreteEndZone && !bearing.IsShutterEndZone;
                    var load=new PositionedSecondaryLoad {Name=$"X{index+1} "+(uniform ? "junction strip" : bearing.IsShutterEndZone ? "side-shutter transfer" : "bearing reaction"),Bay=support.Input.BayNumber-1,IsUniform=uniform,
                        PositionMm=(uniform ? bearing.EndZoneStartMm : bearing.Ymm)-origin,EndMm=bearing.EndZoneEndMm-origin,
                        ValuePerMetre=bearing.ReactionKn*1000/support.Input.WidthMm*(uniform ? 1000/(bearing.EndZoneEndMm-bearing.EndZoneStartMm) : 1)};
                    sources.Add(load);
                    double position=uniform ? (load.PositionMm+load.EndMm)/2 : load.PositionMm;
                    double right=bearing.ReactionKn*(position-lower.SupportsMm[0])/(lower.SupportsMm[1]-lower.SupportsMm[0]);force[0]+=bearing.ReactionKn-right;force[1]+=right;
                    longitudinalMoment[0]+=(bearing.ReactionKn-right)*(bearing.Xmm-support.Input.PositionMm);longitudinalMoment[1]+=right*(bearing.Xmm-support.Input.PositionMm);
                }
                // Equal-position reactions from spreader receiving rows form a per-metre source.
                var grouped=sources.GroupBy(q=>Tuple.Create(q.Name,q.PositionMm,q.EndMm,q.IsUniform)).Select(g=>new PositionedSecondaryLoad {Name=g.Key.Item1,Bay=g.First().Bay,PositionMm=g.Key.Item2,EndMm=g.Key.Item3,IsUniform=g.Key.Item4,ValuePerMetre=g.Sum(q=>q.ValuePerMetre)}).ToArray();
                plan.CrossingLoads[index]=grouped;
                plan.CrossingTransfers[index]=force.Select(f=>f*1000/support.Input.WidthMm).ToArray();
                if(directForce.Any(f=>Math.Abs(f)>1e-10))plan.PrimaryBearingTransfers[index]=directForce.Select(f=>f*1000/support.Input.WidthMm).ToArray();
                plan.CrossingResultantOffsetsMm[index]=Enumerable.Range(0,2).Select(line=>Math.Abs(force[line])<1e-9 ? 0 : longitudinalMoment[line]/force[line]).ToArray();
                int bay=support.Input.BayNumber-1;
                if(!support.UsesSpreader && plan.CarriesBay(bay) && support.SlabEdgeLoadKn>0)
                    plan.SlabLedgerForces[bay]=(plan.SlabLedgerForces.TryGetValue(bay,out double prior) ? prior : 0)+support.SlabEdgeLoadKn/2;
            }
            plan.Refresh(common);
            if(cacheKey!=null && resolvedCache.Count<256)resolvedCache.TryAdd(cacheKey,plan);
            return plan;
        }
        internal double ValueAt(double position,int line)=>Lines[line].Where(q=>q.StartMm<=position+1e-7 && q.EndMm>position+1e-7).Sum(q=>q.KnPerM);
        internal BeamDistributedLoad[] Loads(double start,double length,int line,IEnumerable<int> active=null,double selfWeight=0)
        {
            var zones=active==null ? Lines : Build(new HashSet<int>(active));
            // Clip again in member coordinates: subtracting an absolute origin can
            // round an exactly clipped endpoint a fraction above the member length.
            var loads=zones[line].Select(q=>new BeamDistributedLoad {StartMm=Math.Max(0,Math.Max(start,q.StartMm)-start),EndMm=Math.Min(length,Math.Min(start+length,q.EndMm)-start),KnPerM=q.KnPerM})
                .Where(q=>q.EndMm>q.StartMm+1e-7).ToList();
            if(selfWeight>0)loads.Add(new BeamDistributedLoad {StartMm=0,EndMm=length,KnPerM=selfWeight});
            return Merge(loads);
        }
        internal ColumnEndStripLoad[] Columns(double first,double last,string name,IEnumerable<int> active=null)
        {
            var result=new List<ColumnEndStripLoad>();
            foreach(int end in new[] {0,1})
            {
                double a=end==0 ? Math.Min(StartMm,Lines.SelectMany(l=>l).Min(q=>q.StartMm)) : last,z=end==0 ? first : Math.Max(StartMm+LengthMm,Lines.SelectMany(l=>l).Max(q=>q.EndMm));if(z<=a+1e-7)continue;
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
        internal string Summary=>Describe(IsResolved);
        internal string SelectedSummary=>Describe(true);
        private string Describe(bool selected)=>string.Format(CultureInfo.InvariantCulture,"{3} force L/R {0:0.###}/{1:0.###} kN; {4} {2} legs per primary line. Beam + slab outside crossings; assigned beam + crossing transfer over each full crossing width. Dedicated tower loads and slab edge transfers are counted once. Positioned secondary loads determine the primary reaction split; local row spacing does not change the per-metre UDL.",Forces[0],Forces[1],InitialLegs,selected ? "Selected primary-zone" : "Provisional full-tributary",selected ? "selected-force estimate" : "provisional estimate")
            +" "+string.Join("; ",Lines.Select((line,i)=>(i==0 ? "L" : "R")+": "+string.Join(", ",line.Select(q=>string.Format(CultureInfo.InvariantCulture,"{0:0.###}–{1:0.###} mm @ {2:0.#####} kN/m",q.StartMm,q.EndMm,q.KnPerM)))))
            +string.Join("",CrossingResultantOffsetsMm.Where(c=>c.Value.Any(x=>Math.Abs(x)>.001)).Select(c=>string.Format(CultureInfo.InvariantCulture,"; X{0} physical transfer resultant offset L/R {1:0.###}/{2:0.###} mm; full-width primary UDL remains the longitudinal design idealization",c.Key+1,c.Value[0],c.Value[1])));
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
