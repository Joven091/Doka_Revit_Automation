using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public static partial class CrossBeamSupportOptimizer
    {
        // Optimistic eligibility only. Actual primary axes, stock ends, loads and
        // all timber intersections are checked on the selected receiver pair.
        internal static bool PrimaryBearingPossible(TowerInput common,TransferNetworkInput network,TransferCrossingBeamRow c)
        {
            if(!common.EngineerWorkflow || c.SupportMode==CrossBeamSupportMode.DedicatedTowers || c.SupportMode==CrossBeamSupportMode.LegacySlabRows)return false;
            int bay=c.BayNumber-1;
            if(ValidFillerGap(c.BottomElevationMm,network.Beams[bay].BottomElevationMm,out var a)
                && ValidFillerGap(c.BottomElevationMm,network.Beams[bay+1].BottomElevationMm,out var b))return false;
            if(c.BottomElevationMm<Math.Max(network.Beams[bay].BottomElevationMm,network.Beams[bay+1].BottomElevationMm)-1e-7)return false;
            double span=network.Bays[bay].WidthMm,limit=network.CrossSpacing(Array.IndexOf(network.CrossingBeams,c));
            return common.StockLengthsMm.Any(s=>s<=span+1e-7 && span-s<=2*limit+1e-7) || common.AllowCustomBeamLengths;
        }
        private static Func<TransferredSlabOptimizer.Geometry,double,bool> PreparePrimaryReceiver(TowerInput common,TransferNetworkInput network,int beam,TowerLayout probe)
        {
            var adjacent=network.CrossingBeams.Where(c=>(c.BayNumber==beam || c.BayNumber==beam+1) && PrimaryBearingPossible(common,network,c)).ToArray();
            if(adjacent.Length==0)return null;
            var centres=new double[network.Beams.Length];
            for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+network.Beams[i-1].WidthMm/2+network.Bays[i-1].WidthMm+network.Beams[i].WidthMm/2;
            // Opposite receivers and slab allocations are not assembled yet.
            // Keep this preliminary screen optimistic: reserve crossing concrete
            // here, then include actual slab-edge ledgers in Complete/Recheck.
            // A mirrored ledger split can otherwise discard a viable receiver
            // before its real adjacent primary axes are known.
            var mirror=new TransferNetworkResult {Input=network,Beams=Enumerable.Repeat(probe,network.Beams.Length).ToArray(),
                Bays=new UpperTransferPlan[network.Bays.Length],BeamCentresMm=centres,
                SlabCoveredLengthMm=common.TransferredSlab.FixedCoveredLengthMm??common.LengthMm};
            var supports=new List<CrossBeamSupportResult>();
            foreach(var c in adjacent)
            {
                int index=Array.IndexOf(network.CrossingBeams,c);var shared=PrimaryBearing(common,mirror,index,out string failure);
                if(shared==null)return (g,e)=>false;
                var own=shared.Bearings.Where(b=>b.Beam==beam).Select(b=>
                {var copy=b.Copy();copy.Beam=0;copy.Ymm-=centres[beam];copy.EndZoneStartMm-=centres[beam];copy.EndZoneEndMm-=centres[beam];return copy;}).ToArray();
                supports.Add(new CrossBeamSupportResult {CrossingIndex=index,Input=c,Bearings=own,SlabEdgeLoadKn=shared.SlabEdgeLoadKn});
            }
            return PrepareRecheck(common,network,beam,probe,supports.ToArray());
        }

        internal static CrossBeamSupportResult PrimaryBearing(TowerInput common,TransferNetworkResult r,int index,out string failure)
        {
            failure=null;var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;
            if(!PrimaryBearingPossible(common,r.Input,c)){failure="Primary bearings are above the crossing H20 underside or no secondary stock can cover its span.";return null;}
            double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2,span=r.Input.Bays[bay].WidthMm;
            double near=r.BeamCentresMm[bay]+r.Beams[bay].TransverseOffsetMm+r.Beams[bay].WidthMm/2;
            double far=r.BeamCentresMm[bay+1]+r.Beams[bay+1].TransverseOffsetMm-r.Beams[bay+1].WidthMm/2;
            if(far<=near+1e-7){failure="The adjacent inner primary bearings are reversed or coincide.";return null;}
            double limit=r.Input.CrossSpacing(index),end=common.StockEndSetbackMm;
            var stocks=common.StockLengthsMm.Where(s=>s<=span+1e-7);
            if(common.AllowCustomBeamLengths)
                stocks=stocks.Concat(new[] {25*Math.Ceiling((far-near+2*end)/25)}.Where(s=>s<=span+1e-7));
            // Prepared receiver probes contain transverse data but no assembled
            // primaries/slab scales. Reserve concrete there; the complete network
            // checks every actual timber solid before accepting this option.
            var existing=r.Beams.All(l=>l.StockPlan.Pieces.Length==0) ? Concrete(r).ToArray()
                : NetworkSolids(r).Where(s=>!s.Name.StartsWith("B") || !s.Name.Contains("/T")).Concat(Concrete(r)).ToArray();
            foreach(double stock in stocks.Distinct().OrderBy(s=>s))
            {
                double earliest=Math.Max(0,far+end-stock-face),latest=Math.Min(span-stock,near-end-face);
                if(earliest>latest+1e-7)continue;
                // Symmetry first; unequal tails are used only inside the same checked interval.
                double preferred=Math.Max(earliest,Math.Min(latest,(span-stock)/2));
                foreach(double edge in new[] {preferred,earliest,latest}.Distinct())
                {
                    double farEdge=span-edge-stock;
                    if(edge>limit+1e-7 || farEdge>limit+1e-7)continue;
                    // Each end strip retains the existing shutter-to-secondary path.
                    // The box-out carries only the checked H20 bearing reaction to its primary.
                    if(!EndStripFits(r,bay,face,edge) || !EndStripFits(r,bay+1,face+span-farEdge,farEdge))continue;
                    int firstCount=Math.Max(2,1+(int)Math.Ceiling((c.WidthMm-80)/limit));
                    int maximum=1+(int)Math.Floor((c.WidthMm-80)/80);
                    for(int count=firstCount;count<=maximum;count++)
                    {
                        common.Trace?.CheckCancellation();
                        var rows=PrimaryBearingRows(r,index,count,face+edge,stock,existing);
                        if(rows==null)continue;
                        var members=new List<TransferMember>();var bearings=new List<CrossBeamBearing>();var ledgers=new List<CrossBeamBearing>();var strips=new List<CrossBeamBearing>();
                        var ledgerLoads=LedgerRowLineLoads(r,index,rows);bool passing=true;
                        for(int row=0;row<rows.Length;row++)
                        {
                            double lo=row==0 ? c.PositionMm-c.WidthMm/2 : (rows[row-1]+rows[row])/2;
                            double hi=row==rows.Length-1 ? c.PositionMm+c.WidthMm/2 : (rows[row]+rows[row+1])/2;
                            double q=r.Input.AreaLoad(c.DepthMm)*(hi-lo)/1000+ledgerLoads[row];
                            var m=new TransferMember {Name=$"X{index+1} H20 {row+1}",LoadCase="Cross concrete + construction/formwork",H20Count=1,StartMm=face+edge,LengthMm=stock,
                                SupportsMm=new[] {near-face-edge,far-face-edge},Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=q}},Points=Array.Empty<BeamPointLoad>()};
                            m.Analysis=TransferredSlabOptimizer.AnalyzeTransverse(common,BeamSearchCounter.LowerAnalyses,stock,m.SupportsMm,m.Uniform,m.Points,m.EI,common.DeflectionDenominator,signed:true);
                            if(!Pass(m,common,$"X{index+1} secondary on main primaries")){passing=false;break;}
                            members.Add(m);
                            for(int side=0;side<2;side++)
                            {
                                int beam=bay+side,line=side==0 ? 1 : 0;
                                var bearing=new CrossBeamBearing {Beam=beam,Row=-1,PrimaryLine=line,Xmm=rows[row],Ymm=side==0 ? near : far,
                                    ReceivingTopMm=r.Input.Beams[beam].BottomElevationMm-200,H20UndersideMm=c.BottomElevationMm-200,
                                    ReactionKn=m.Analysis.Reactions[side],FootprintStartMm=lo,FootprintEndMm=hi,Label=$"B{beam+1} primary {(line==0 ? "L" : "R")}"};
                                if(Math.Abs(ledgerLoads[row])>1e-10)
                                {var extra=bearing.Copy();extra.Label="Slab edge ledger";extra.ReactionKn=bearing.ReactionKn*ledgerLoads[row]/q;bearing.ReactionKn-=extra.ReactionKn;extra.FootprintStartMm=rows[row]-40;extra.FootprintEndMm=rows[row]+40;ledgers.Add(extra);}
                                bearings.Add(bearing);
                                double strip=side==0 ? edge : farEdge;if(strip<=1e-7)continue;
                                double a=side==0 ? face : face+span-strip,z=a+strip;
                                var receiving=r.Beams[beam].Transfer;
                                int receivingRow=Enumerable.Range(0,receiving.RowsMm.Length).Where(k=>Math.Abs(receiving.RowsMm[k]-c.PositionMm)<=c.WidthMm/2+1e-7).OrderBy(k=>Math.Abs(receiving.RowsMm[k]-rows[row])).DefaultIfEmpty(-1).First();
                                if(receivingRow<0){passing=false;break;}
                                var stripBearing=new CrossBeamBearing {Beam=beam,Row=receivingRow,Label="Crossing end strip",IsConcreteEndZone=true,IsShutterEndZone=true,
                                    EndZoneStartMm=a,EndZoneEndMm=z,EndZoneTributaryWidthMm=hi-lo,Xmm=rows[row],Ymm=(a+z)/2,
                                    ReceivingTopMm=r.Input.Beams[beam].BottomElevationMm,H20UndersideMm=c.BottomElevationMm,FootprintStartMm=lo,FootprintEndMm=hi,
                                    ReactionKn=r.Input.AreaLoad(c.DepthMm)*(hi-lo)*strip/1e6};
                                strips.Add(stripBearing);
                                if(Math.Abs(ledgerLoads[row])>1e-10)
                                {var extra=stripBearing.Copy();extra.Label="Slab edge ledger";extra.ReactionKn=ledgerLoads[row]*strip/1000;strips.Add(extra);}
                            }
                            if(!passing)break;
                        }
                        if(!passing)continue;
                        return new CrossBeamSupportResult {CrossingIndex=index,Input=c,Arrangement=CrossBeamSupportMode.ExistingSupportsOnly,Members=members.ToArray(),
                            Bearings=bearings.Concat(ledgers).Concat(strips).ToArray(),SecondaryStationsMm=rows,Solids=members.Select((m,k)=>Timber(m.Name,rows[k],m.StartMm,m.LengthMm,false,c.BottomElevationMm,m)).ToArray(),
                            LoadKn=members.Sum(m=>m.Analysis.TotalLoadKn)+strips.Sum(b=>b.ReactionKn),SlabEdgeLoadKn=SlabEdgeLoads(r,index).Sum(p=>p.Kn)*span/1000,
                            JunctionNotice="Crossing secondary H20s bear on the adjacent main primaries. Raised bearings transfer vertically at the primary axes; box-out member, bearing and connection design is outside scope. Concrete end strips retain the checked side-shutter transfer to beam secondaries."};
                    }
                }
            }
            failure=$"X{index+1}: no permitted secondary stock and row arrangement passes on the adjacent primary bearings (axes {near:0.###}/{far:0.###} mm).";return null;
        }
        static bool EndStripFits(TransferNetworkResult r,int beam,double start,double width)
        {
            if(width<=1e-7)return true;
            var t=r.Beams[beam].Transfer;double position=start+width/2-r.BeamCentresMm[beam];
            return position>=t.LowerStockStartMm+40-1e-7 && position<=t.LowerStockStartMm+t.LowerStockLengthMm-40+1e-7;
        }
        // Mirror pairs avoid arbitrary longitudinal eccentricity. Exact obstacle
        // contacts supplement the 25 mm placement grid; higher row counts repair
        // a blocked centre without changing any main-beam support geometry.
        static double[] PrimaryBearingRows(TransferNetworkResult r,int index,int count,double start,double stock,FormworkSolid[] obstacles)
        {
            var c=r.Input.CrossingBeams[index];double max=c.WidthMm/2-40;
            Func<double,bool> clear=x=>new[] {c.BayNumber-1,c.BayNumber}.All(b=>r.Beams[b].StockPlan.Pieces.Length==0
                    || r.Beams[b].StockPlan.Pieces.Any(p=>x>=p.StartMm+40-1e-7 && x<=p.EndMm-40+1e-7))
                && !obstacles.Any(s=>Timber("Cross secondary",x,start,stock,false,c.BottomElevationMm).Conflicts(s));
            var rows=new List<double>();if(count%2==1){if(!clear(c.PositionMm))return null;rows.Add(c.PositionMm);}
            var offsets=Enumerable.Range(0,1+(int)Math.Ceiling(max/25)).Select(k=>Math.Min(max,k*25.0)).Concat(new[] {max})
                .Concat(obstacles.SelectMany(s=>new[] {Math.Abs(s.X0-40-c.PositionMm),Math.Abs(s.X1+40-c.PositionMm)})).Where(d=>d<=max+1e-7).Distinct().ToArray();
            for(int pair=0;pair<count/2;pair++)
            {
                double desired=max*(count%2==1 ? (pair+1.0)/(count/2) : (2*pair+1.0)/(count-1));
                var choices=offsets.Where(d=>d>=40 && rows.All(x=>Math.Abs(x-(c.PositionMm-d))>=80-1e-7 && Math.Abs(x-(c.PositionMm+d))>=80-1e-7))
                    .OrderBy(d=>Math.Abs(d-desired)).ThenBy(d=>d);
                double selected=choices.FirstOrDefault(d=>clear(c.PositionMm-d) && clear(c.PositionMm+d));
                if(selected<=0)return null;rows.Add(c.PositionMm-selected);rows.Add(c.PositionMm+selected);
            }
            var result=rows.OrderBy(x=>x).ToArray();double limit=r.Input.CrossSpacing(index);
            return result[0]-(c.PositionMm-c.WidthMm/2)<=limit+1e-7 && c.PositionMm+c.WidthMm/2-result.Last()<=limit+1e-7
                && result.Zip(result.Skip(1),(a,b)=>b-a).All(g=>g<=limit+1e-7) ? result : null;
        }
    }
}
