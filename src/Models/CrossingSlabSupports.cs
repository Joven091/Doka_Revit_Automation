using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    internal sealed class SlabEdgeReaction
    {
        internal int Crossing;
        internal double PositionMm,WidthMm;
    }
    internal sealed class CrossingSlabSupportLayout
    {
        // Reserve the existing 200 mm shutter working zone, plus half an H20 flange.
        internal const double ShutterZoneMm=CrossBeamSupportOptimizer.SideShutterClearanceMm;
        internal double[] Rows=new double[0],Widths=new double[0];
        internal readonly List<SlabEdgeReaction> Edges=new List<SlabEdgeReaction>();
        internal readonly List<BeamDistributedLoad> Redistribution=new List<BeamDistributedLoad>();
        internal bool Valid=true;
        internal static CrossingSlabSupportLayout Build(TransferNetworkInput input,int bay,double start,double end,double[] adjacent=null)
        {
            var layout=new CrossingSlabSupportLayout();double maximum=input.BaySpacing(bay);
            var cuts=input.CrossingBeams.Select((c,i)=>new {C=c,I=i}).Where(x=>x.C.BayNumber==bay+1 && x.C.SupportMode!=CrossBeamSupportMode.LegacySlabRows).OrderBy(x=>x.C.PositionMm).ToArray();
            var strips=new List<Tuple<double,double,int,int>>();double a=start;int left=-1;
            var coverageAnchors=PlywoodSpacing.SheetSupportRows(start,end,50000);
            foreach(var cut in cuts)
            {
                double lo=cut.C.PositionMm-cut.C.WidthMm/2,hi=cut.C.PositionMm+cut.C.WidthMm/2;
                if(lo<start-1e-7 || hi>end+1e-7){layout.Valid=false;return layout;}
                if(lo>a+1e-7)strips.Add(Tuple.Create(a,lo,left,cut.I));a=hi;left=cut.I;
            }
            if(end>a+1e-7)strips.Add(Tuple.Create(a,end,left,-1));
            var rows=new SortedSet<double>();
            foreach(var strip in strips)
            {
                bool l=strip.Item3>=0,r=strip.Item4>=0;
                double lo=l ? strip.Item1 : coverageAnchors.First(),hi=r ? strip.Item2 : coverageAnchors.Last();
                if(hi<=lo){layout.Valid=false;return layout;}
                // Keep every required 2440 mm sheet joint outside the crossing footprint.
                // Crossing bearings and coverage ends are additional setting-out anchors.
                var anchors=new List<double> {lo};
                for(double x=(Math.Floor(lo/PlywoodSpacing.SheetLengthMm)+1)*PlywoodSpacing.SheetLengthMm;x<hi-80;x+=PlywoodSpacing.SheetLengthMm)
                    if(x-lo>=80)anchors.Add(x);
                anchors.Add(hi);anchors.Sort();var local=new List<double> {lo};
                for(int k=1;k<anchors.Count;k++)
                {
                    double p=anchors[k-1],q=anchors[k];int count=(int)Math.Ceiling((q-p)/maximum-1e-10);
                    bool shortClosing=q-p-(count-1)*maximum<80-1e-7;
                    for(int j=1;j<count;j++)local.Add(shortClosing ? p+(q-p)*j/count : r && !l ? q-(count-j)*maximum : p+j*maximum);
                    local.Add(q);
                }
                // A short closing interval is shared with its neighbour rather than placing timber in a shutter.
                double clearance=ShutterZoneMm+40;
                if(l && local.Count>2 && local[1]-lo<clearance)local[1]=lo+clearance;
                if(r && local.Count>2 && hi-local[local.Count-2]<clearance)local[local.Count-2]=hi-clearance;
                for(int k=0;k<local.Count;k++)
                {
                    bool virtualNode=k==0 && l || k==local.Count-1 && r;
                    if(k>0 && (local[k]-local[k-1]>maximum+1e-7 || local[k]-local[k-1]<80-1e-7)){layout.Valid=false;return layout;}
                    if(!virtualNode){if(l && local[k]-lo<clearance-1e-7 || r && hi-local[k]<clearance-1e-7){layout.Valid=false;return layout;}rows.Add(local[k]);}
                }
            }
            // A closing slab sheet can end at a shifted support beside a shutter, while
            // the receiving beam still needs its own sheet joint. Keep their H20 flanges clear.
            foreach(double row in rows.ToArray())
            {
                foreach(double joint in coverageAnchors.Where(x=>Math.Abs(x/PlywoodSpacing.SheetLengthMm-Math.Round(x/PlywoodSpacing.SheetLengthMm))<1e-7 && Math.Abs(x-row)>1e-7 && Math.Abs(x-row)<80-1e-7))
                {
                    var strip=strips.First(s=>row>=s.Item1 && row<=s.Item2);
                    foreach(double candidate in new[] {joint-80,joint+80}.OrderBy(x=>Math.Abs(x-row)))
                    {
                        if(candidate<coverageAnchors.First() || candidate>coverageAnchors.Last() || cuts.Any(c=>Math.Abs(candidate-c.C.PositionMm)<c.C.WidthMm/2+ShutterZoneMm+40-1e-7))continue;
                        var nodes=rows.Where(x=>x!=row && x>=strip.Item1 && x<=strip.Item2).Concat(new[] {candidate}).OrderBy(x=>x).ToList();
                        if(strip.Item3>=0)nodes.Insert(0,strip.Item1);if(strip.Item4>=0)nodes.Add(strip.Item2);
                        if(nodes.Zip(nodes.Skip(1),(x,y)=>y-x).Any(g=>g<80-1e-7 || g>maximum+1e-7))continue;
                        rows.Remove(row);rows.Add(candidate);break;
                    }
                }
            }
            if(adjacent!=null && adjacent.Length>0)
            {
                foreach(double row in rows.ToArray())
                {
                    bool mandatory=Math.Abs(row/PlywoodSpacing.SheetLengthMm-Math.Round(row/PlywoodSpacing.SheetLengthMm))<1e-7 || row==start+40 || row==end-40;
                    if(mandatory)continue;
                    var nearby=adjacent.OrderBy(x=>Math.Abs(x-row)).First();
                    if(Math.Abs(nearby-row)>=80 || Math.Abs(nearby-row)<1e-7)continue;
                    if(nearby<start+40 || nearby>end-40 || cuts.Any(c=>Math.Abs(nearby-c.C.PositionMm)<c.C.WidthMm/2+ShutterZoneMm+40-1e-7))continue;
                    rows.Remove(row);rows.Add(nearby);
                }
            }
            layout.Rows=rows.ToArray();layout.Widths=new double[layout.Rows.Length];
            foreach(var strip in strips)
            {
                var nodes=layout.Rows.Where(x=>x>=strip.Item1-1e-7 && x<=strip.Item2+1e-7).ToList();
                if(strip.Item3>=0)nodes.Insert(0,strip.Item1);if(strip.Item4>=0)nodes.Add(strip.Item2);
                if(nodes.Count<2){layout.Valid=false;return layout;}
                if(nodes.Zip(nodes.Skip(1),(x,y)=>y-x).Any(g=>g<80-1e-7 || g>maximum+1e-7)){layout.Valid=false;return layout;}
                var widths=CrossBeamSupportOptimizer.EndZoneWidths(strip.Item1,strip.Item2,nodes.ToArray());
                for(int k=0;k<nodes.Count;k++)
                {
                    int row=Array.IndexOf(layout.Rows,nodes[k]);
                    if(row>=0)layout.Widths[row]+=widths[k];
                    else layout.Edges.Add(new SlabEdgeReaction {Crossing=k==0 ? strip.Item3 : strip.Item4,PositionMm=nodes[k],WidthMm=widths[k]});
                }
                // Remove each edge span from the primary baseline and restore the physical secondary
                // reaction over its 80 mm flange. Ledger reactions go through the crossing instead.
                if(strip.Item3>=0)layout.Redistribute(nodes[0],nodes[1],strip.Item3>=0 && strip.Item4>=0 && nodes.Count==2 ? (double?)null : nodes[1]);
                if(strip.Item4>=0 && !(strip.Item3>=0 && nodes.Count==2))layout.Redistribute(nodes[nodes.Count-2],nodes.Last(),nodes[nodes.Count-2]);
            }
            return layout;
        }
        private void Redistribute(double a,double b,double? secondary)
        {
            Redistribution.Add(new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=-1});
            if(secondary.HasValue)Redistribution.Add(new BeamDistributedLoad {StartMm=secondary.Value-40,EndMm=secondary.Value+40,KnPerM=(b-a)/160});
        }
    }
    public static partial class CrossBeamSupportOptimizer
    {
        internal static BeamPointLoad[] SlabEdgeLoads(TransferNetworkResult r,int index)
        {
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;
            if(r.Bays[bay]==null || !r.Input.HasIndividualSpacings || !r.Input.UseCrossingSlabLedgers)return new BeamPointLoad[0];
            var map=r.Input.CrossSlabLayout(r.Beams[0].LengthMm,r.SlabCoveredLengthMm,bay);
            double area=r.Input.AreaLoad(r.Input.Bays[bay].ThicknessMm);
            return map.Edges.Where(e=>e.Crossing==index).Select(e=>new BeamPointLoad {PositionMm=e.PositionMm-c.PositionMm,Kn=area*e.WidthMm/1000}).ToArray();
        }
        internal static double[] LedgerRowLineLoads(TransferNetworkResult r,int index,double[] rows)
        {
            var values=new double[rows.Length];var c=r.Input.CrossingBeams[index];
            foreach(var edge in SlabEdgeLoads(r,index))
            {
                var weights=PointRowWeights(rows,c.PositionMm+edge.PositionMm);
                for(int k=0;k<values.Length;k++)values[k]+=weights[k]*edge.Kn;
            }
            return values;
        }
        internal static double[] PointRowWeights(double[] rows,double point)
        {
            var weights=new double[rows.Length];if(rows.Length<2)throw new ArgumentException("A shutter edge needs two receiving rows.");
            int last=Array.FindIndex(rows,x=>x>=point);last=Math.Max(1,last<0 ? rows.Length-1 : last);int first=last-1;
            double right=(point-rows[first])/(rows[last]-rows[first]);weights[first]=1-right;weights[last]=right;return weights;
        }
    }
}
