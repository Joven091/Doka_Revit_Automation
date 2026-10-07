using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    /// <summary>Actual stock solids, in the network's x (main beam), y (bay), z (up) coordinates.</summary>
    public sealed class FormworkSolid
    {
        public string Name;
        public double X0,X1,Y0,Y1,Z0,Z1;
        public TransferMember Member;
        public bool IsLeg;
        // Strict interior intersection permits intended bearing contact on either surface.
        public bool Overlaps(FormworkSolid b)=>X0<b.X1-1e-7 && X1>b.X0+1e-7 && Y0<b.Y1-1e-7 && Y1>b.Y0+1e-7 && Z0<b.Z1-1e-7 && Z1>b.Z0+1e-7;
    }
    public sealed class CrossBeamBearing
    {
        public int Beam,Row;
        public bool IsDedicated;
        public string Label;
        public double Xmm,Ymm,ReceivingTopMm,H20UndersideMm,ReactionKn,FootprintStartMm,FootprintEndMm;
        public double FillerHeightMm=>H20UndersideMm-ReceivingTopMm;
    }
    public sealed class CrossBeamSupportResult
    {
        public int CrossingIndex;
        public CrossBeamSupportMode Arrangement;
        public TransferCrossingBeamRow Input;
        public TowerLayout Towers;
        public TransferMember[] Members=new TransferMember[0];
        public FormworkSolid[] Solids=new FormworkSolid[0];
        public CrossBeamBearing[] Bearings=new CrossBeamBearing[0];
        public double LoadKn,MovementMm;
        public string SharedFailure;
        public int H20Count=>Towers==null ? Members.Sum(m=>m.H20Count) : Towers.StockPlan.TotalH20Count+Towers.Transfer.AdditionalH20Count;
        public double TimberMm=>Towers==null ? Members.Sum(m=>m.LengthMm*m.H20Count) : Towers.StockPlan.TotalStockLengthMm+Towers.Transfer.AdditionalTimberMm;
        public const string Limitations="Filler heights are geometric gaps only: filler timber bearing strength, restraint and stability are not verified. H20 bearing/connection design, sheathing, tower stability, bracing and foundations require separate verification. H20 depth is 200 mm and flange width is 80 mm. Clash checks include full stock tails, H20 pairs and tower leg columns; tower braces and hardware are not modelled. Cross-beam load includes concrete and applicable construction/formwork allowances, with no slab tributary. Shared reactions are added over their actual longitudinal footprints to the main primary UDL design model. Construction stages include every subset of up to eight shared crossings; larger sets use conservative demand and reaction bounds covering every subset. Search is bounded and does not prove a global optimum.";
        public string Schedule=>$"X{CrossingIndex+1}: S{Input.BayNumber} · {(Towers==null ? "Existing supports" : "Dedicated towers")} · {Input.WidthMm:0.###} × {Input.DepthMm:0.###} mm · bottom {Input.BottomElevationMm:0.###} mm\nCross-beam load {LoadKn:0.###} kN · {H20Count} H20s · {TimberMm/1000:0.###} m timber · {Towers?.LegCount??0} dedicated legs · movement {MovementMm:0.###} mm"
            +(Towers==null || Towers.ConcreteEndOverhangMm<=1e-7 ? "" : $"\nConcrete end strips {Towers.ConcreteEndOverhangMm:0.###} mm per end transfer through the outer two secondary rows at each end; primary UDL plus localized end-strip reactions retains the full cross-beam load.")
            +(SharedFailure==null ? "" : "\nAutomatic fallback: "+SharedFailure)
            +"\n"+string.Join("\n",Bearings.Select(b=>$"Bearing {b.Label??($"B{b.Beam+1}/T{b.Row+1}")} @ ({b.Xmm:0.###}, {b.Ymm:0.###}) mm: receiving top {b.ReceivingTopMm:0.###}, cross H20 underside {b.H20UndersideMm:0.###} mm; "+(b.FillerHeightMm<=1e-7 ? "direct bearing" : $"filler timber {b.FillerHeightMm:0.###} mm")+$"; reaction {b.ReactionKn:0.###} kN"))
            +"\n"+string.Join("\n",Members.Select(m=>$"{m.Name} · {m.LoadCase} · {m.H20Count} × {m.LengthMm:0.###} mm: M {m.Analysis.MomentKnM:0.###}/{m.MomentLimitKnM:0.###} kNm; V {m.Analysis.ShearKn:0.###}/{m.ShearLimitKn:0.###} kN; d {m.Analysis.DeflectionMm:0.###} mm; minimum signed reaction {m.MinimumReactionKn:0.######} kN; utilization {m.Utilization*100:0.0}%"))
            +(Towers==null ? "" : "\n"+Towers.Schedule+"\n"+Towers.StockSchedule);
    }
    internal sealed class DedicatedCrossSearch
    {
        internal CrossBeamSupportResult Result;
        internal string Failure;
    }
    /// <summary>Cross formwork is evaluated on complete network candidates, before ranking them.</summary>
    public static class CrossBeamSupportOptimizer
    {
        public const double H20DepthMm=200, H20WidthMm=80, BearingInsetMm=150;
        internal static bool Modern(TransferNetworkInput n)=>n.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows);
        internal static double[] SlabWidths(TransferNetworkInput n,int bay,double edge,double cover,double[] rows,double[] original)
        {
            var cuts=n.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).OrderBy(c=>c.PositionMm).ToArray();
            if(cuts.Length==0)return original;
            var active=Enumerable.Range(0,rows.Length).Where(i=>!cuts.Any(c=>Math.Abs(rows[i]-c.PositionMm)<c.WidthMm/2+40-1e-7)).ToArray();
            var widths=new double[rows.Length];
            if(active.Length==0)return widths; // A fully excluded slab is explicitly checked below.
            for(int k=0;k<active.Length;k++)
            {
                double a=k==0 ? edge : (rows[active[k-1]]+rows[active[k]])/2,b=k==active.Length-1 ? edge+cover : (rows[active[k]]+rows[active[k+1]])/2;
                widths[active[k]]=b-a;
                double segment=a;
                foreach(var c in cuts)
                {
                    double lo=Math.Max(a,c.PositionMm-c.WidthMm/2),hi=Math.Min(b,c.PositionMm+c.WidthMm/2);
                    if(hi<=lo)continue;
                    if(lo>segment && Math.Max(Math.Abs(segment-rows[active[k]]),Math.Abs(lo-rows[active[k]]))>n.UpperSpacingMm+1e-7)return null;
                    widths[active[k]]-=hi-lo;segment=Math.Max(segment,hi);
                }
                if(b>segment && Math.Max(Math.Abs(segment-rows[active[k]]),Math.Abs(b-rows[active[k]]))>n.UpperSpacingMm+1e-7)return null;
            }
            return widths;
        }
        public static bool ValidFillerGap(double crossBottom,double receivingBottom,out double filler)
        {filler=crossBottom-H20DepthMm-receivingBottom;return TowerBeamAnalysis.Finite(filler) && filler>=-1e-7;}
        internal static bool Pass(TransferMember m,TowerInput common,string location)
        {
            common.Diagnostics?.Analysis(m.Analysis,1,m.H20Count,m.Name,2,location);
            return m.Utilization<=1+1e-8 && (common.IgnoreUplift || m.Analysis.Reactions.All(r=>r>=-1e-7));
        }
        private static FormworkSolid Timber(string name,double x,double y,double length,bool alongX,double top,TransferMember member=null)
            =>new FormworkSolid {Name=name,X0=alongX ? x : x-40,X1=alongX ? x+length : x+40,Y0=alongX ? y-40 : y,Y1=alongX ? y+40 : y+length,Z0=top-200,Z1=top,Member=member};
        internal static IEnumerable<FormworkSolid> NetworkSolids(TransferNetworkResult r)
        {
            for(int b=0;b<r.Beams.Length;b++)
            {
                var l=r.Beams[b];var t=l.Transfer;double y=r.BeamCentresMm[b],bottom=r.Input.Beams[b].BottomElevationMm;
                foreach(var p in l.StockPlan.Pieces)foreach(int side in new[] {-1,1})foreach(int pair in new[] {-1,1})
                    yield return Timber($"B{b+1} primary",p.StartMm,y+side*l.WidthMm/2+pair*40,p.StockLengthMm,true,bottom-200);
                for(int row=0;row<t.RowsMm.Length;row++)yield return Timber($"B{b+1}/T{row+1}",t.RowsMm[row],y+t.LowerStockStartMm,t.LowerStockLengthMm,false,bottom);
                foreach(double x in l.PositionsMm)foreach(int side in new[] {-1,1})yield return new FormworkSolid {Name=$"B{b+1} tower leg",X0=x-40,X1=x+40,Y0=y+side*l.WidthMm/2-40,Y1=y+side*l.WidthMm/2+40,Z0=-1e6,Z1=bottom-400,IsLeg=true};
            }
            for(int bay=0;bay<r.Bays.Length;bay++)
            {
                var p=r.Bays[bay];if(p==null)continue;
                double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2;
                double top=Math.Max(r.Input.Beams[bay].BottomElevationMm+r.Input.Beams[bay].DepthMm,r.Input.Beams[bay+1].BottomElevationMm+r.Input.Beams[bay+1].DepthMm)-r.Input.Bays[bay].ThicknessMm;
                for(int row=0;row<r.Beams[0].Transfer.RowsMm.Length;row++)if(r.SlabRowActive(bay,row))yield return Timber($"S{bay+1} slab H20",r.Beams[0].Transfer.RowsMm[row],face+p.EdgeMm,p.StockLengthMm,false,top);
            }
        }
        private static IEnumerable<FormworkSolid> Concrete(TransferNetworkResult r)
        {
            for(int b=0;b<r.Beams.Length;b++)
            {
                var i=r.Input.Beams[b];double y=r.BeamCentresMm[b];yield return new FormworkSolid {Name=$"B{b+1} concrete",X0=0,X1=r.Beams[b].LengthMm,Y0=y-i.WidthMm/2,Y1=y+i.WidthMm/2,Z0=i.BottomElevationMm,Z1=i.BottomElevationMm+i.DepthMm};
            }
            for(int bay=0;bay<r.Input.Bays.Length;bay++)
            {
                double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2,top=Math.Max(r.Input.Beams[bay].BottomElevationMm+r.Input.Beams[bay].DepthMm,r.Input.Beams[bay+1].BottomElevationMm+r.Input.Beams[bay+1].DepthMm),start=0;
                var cuts=r.Input.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).OrderBy(c=>c.PositionMm).ToArray();
                foreach(var cut in cuts)
                {
                    double end=cut.PositionMm-cut.WidthMm/2;
                    if(end>start)yield return new FormworkSolid {Name=$"S{bay+1} concrete",X0=start,X1=end,Y0=face,Y1=face+r.Input.Bays[bay].WidthMm,Z0=top-r.Input.Bays[bay].ThicknessMm,Z1=top};
                    start=cut.PositionMm+cut.WidthMm/2;
                }
                if(start<r.Beams[0].LengthMm)yield return new FormworkSolid {Name=$"S{bay+1} concrete",X0=start,X1=r.Beams[0].LengthMm,Y0=face,Y1=face+r.Input.Bays[bay].WidthMm,Z0=top-r.Input.Bays[bay].ThicknessMm,Z1=top};
            }
            foreach(var c in r.Input.CrossingBeams.Where(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows))
            {
                int bay=c.BayNumber-1;double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2;
                yield return new FormworkSolid {Name="cross concrete",X0=c.PositionMm-c.WidthMm/2,X1=c.PositionMm+c.WidthMm/2,Y0=face,Y1=face+r.Input.Bays[bay].WidthMm,Z0=c.BottomElevationMm,Z1=c.BottomElevationMm+c.DepthMm};
            }
        }
        private static string Clash(IEnumerable<FormworkSolid> added,IEnumerable<FormworkSolid> other)
        {
            var a=added.ToArray();var b=other.ToArray();
            for(int i=0;i<a.Length;i++)
            {
                foreach(var s in b)if(a[i].Overlaps(s))return $"Physical overlap: {a[i].Name} / {s.Name}.";
                for(int j=0;j<i;j++)if(a[i].Overlaps(a[j]))return $"Physical overlap: {a[i].Name} / {a[j].Name}.";
            }
            return null;
        }
        private static CrossBeamSupportResult Shared(TowerInput common,TransferNetworkResult r,int index,out string failure)
        {
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;double span=r.Input.Bays[bay].WidthMm;
            var left=r.Beams[bay].Transfer;var right=r.Beams[bay+1].Transfer;double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2;
            failure=null;
            double lf,rf;
            if(!ValidFillerGap(c.BottomElevationMm,r.Input.Beams[bay].BottomElevationMm,out lf) || !ValidFillerGap(c.BottomElevationMm,r.Input.Beams[bay+1].BottomElevationMm,out rf))
            {failure=$"X{index+1}: cross H20 underside {c.BottomElevationMm-200:0.###} mm is below a receiving secondary top (negative filler gap).";return null;}
            var rows=Enumerable.Range(0,left.RowsMm.Length).Where(i=>Math.Abs(left.RowsMm[i]-c.PositionMm)<=c.WidthMm/2-40+1e-7).ToArray();
            if(rows.Length==0) {failure=$"X{index+1}: no receiving secondary within the cross-beam width including the 80 mm H20 flange.";return null;}
            double near=left.LowerStockStartMm+left.LowerStockLengthMm-r.Input.Beams[bay].WidthMm/2-BearingInsetMm;
            double far=-right.LowerStockStartMm-r.Input.Beams[bay+1].WidthMm/2-BearingInsetMm;
            near=Math.Min(r.Beams[bay].WidthMm/2-r.Input.Beams[bay].WidthMm/2,Math.Min(350,Math.Min(near,span/2-150)));far=Math.Min(r.Beams[bay+1].WidthMm/2-r.Input.Beams[bay+1].WidthMm/2,Math.Min(350,Math.Min(far,span/2-150)));
            if(near<150 || far<150) {failure=$"X{index+1}: receiving stock tails cannot provide 150 mm bearing/end allowances.";return null;}
            var lengths=common.StockLengthsMm.Where(s=>s<=span+1e-7 && s>=span-2*common.MaximumConcreteEndOverhangMm-1e-7);
            if(common.AllowCustomH20Lengths)lengths=lengths.Concat(new[] {Math.Floor(span/25)*25}).Where(s=>s>=span-2*common.MaximumConcreteEndOverhangMm-1e-7).Distinct();
            foreach(double stock in lengths.OrderBy(x=>x))
            {
                double edge=(span-stock)/2;
                if(near-edge<150 || far-edge<150)continue;
                var members=new List<TransferMember>();var bearings=new List<CrossBeamBearing>();var solids=new List<FormworkSolid>();bool valid=true;
                for(int k=0;k<rows.Length;k++)
                {
                    double lo=k==0 ? c.PositionMm-c.WidthMm/2 : (left.RowsMm[rows[k-1]]+left.RowsMm[rows[k]])/2;
                    double hi=k==rows.Length-1 ? c.PositionMm+c.WidthMm/2 : (left.RowsMm[rows[k]]+left.RowsMm[rows[k+1]])/2;
                    // Concrete end allowance is carried at the stock end, retaining full concrete load.
                    var q=new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=r.Input.AreaLoad(c.DepthMm)*(hi-lo)/1000}};
                    var points=edge<=1e-7 ? new BeamPointLoad[0] : new[] {new BeamPointLoad {PositionMm=0,Kn=q[0].KnPerM*edge/1000},new BeamPointLoad {PositionMm=stock,Kn=q[0].KnPerM*edge/1000}};
                    var m=new TransferMember {Name=$"X{index+1} H20 {k+1}",LoadCase="Cross concrete + construction/formwork",H20Count=1,StartMm=face+edge,LengthMm=stock,SupportsMm=new[] {near-edge,span-far-edge},Uniform=q,Points=points};
                    m.Analysis=LoadedBeamAnalysis.Analyze(stock,m.SupportsMm,q,points,TowerOptimizer.PairEI/2,common.DeflectionDenominator);
                    if(!Pass(m,common,$"Cross X{index+1}; receiving T{rows[k]+1}")) {valid=false;break;}
                    members.Add(m);double x=left.RowsMm[rows[k]];
                    solids.Add(Timber(m.Name,x,face+edge,stock,false,c.BottomElevationMm,m));
                    for(int side=0;side<2;side++)
                    {
                        int beam=bay+side;double y=face+(side==0 ? near : span-far);
                        var bearing=new CrossBeamBearing {Beam=beam,Row=rows[k],Xmm=x,Ymm=y,ReceivingTopMm=r.Input.Beams[beam].BottomElevationMm,H20UndersideMm=c.BottomElevationMm-200,ReactionKn=m.Analysis.Reactions[side],FootprintStartMm=lo,FootprintEndMm=hi};bearings.Add(bearing);
                        if(bearing.FillerHeightMm>1e-7)solids.Add(new FormworkSolid {Name=$"X{index+1} filler B{beam+1}/T{rows[k]+1}",X0=x-40,X1=x+40,Y0=y-40,Y1=y+40,Z0=bearing.ReceivingTopMm,Z1=bearing.H20UndersideMm});
                    }
                }
                if(valid)return new CrossBeamSupportResult {CrossingIndex=index,Input=c,Arrangement=CrossBeamSupportMode.ExistingSupportsOnly,Members=members.ToArray(),Bearings=bearings.ToArray(),Solids=solids.ToArray(),LoadKn=members.Sum(m=>m.Analysis.TotalLoadKn)};
            }
            failure=$"X{index+1}: no permitted shared H20 stock passes moment, shear, deflection{(common.IgnoreUplift ? "" : ", uplift")} and bearing/end geometry.";return null;
        }
        private static CrossBeamSupportResult Dedicated(TowerInput common,TransferNetworkResult r,int index,IEnumerable<FormworkSolid> obstacles,out string failure)
        {
            failure=null;
            foreach(double spacing in new[] {Math.Min(r.Input.UpperSpacingMm,250),225.0,250,175,150}.Distinct())
            {
                var result=DedicatedCandidate(common,r,index,spacing,out string reason);
                if(result!=null)reason=Clash(result.Solids,obstacles);
                if(result!=null && reason==null)return result;
                failure=reason;
            }
            return null;
        }
        private static CrossBeamSupportResult DedicatedCandidate(TowerInput common,TransferNetworkResult r,int index,double spacing,out string failure)
        {
            if(common.DedicatedCrossCache!=null && common.DedicatedCrossCache.TryGetValue(Tuple.Create(index,spacing),out var cached)) {failure=cached.Failure;return cached.Result;}
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2,span=r.Input.Bays[bay].WidthMm;
            failure=null;
            // Dedicated transverse secondaries and primary pairs use the same checked tower geometry as main beams.
            var input=common.CopyForSearch();input.LengthMm=span;input.Network=null;
            input.DiagnosticScope=$"X{index+1} dedicated · ";
            input.Diagnostics=new TowerSearchDiagnostics(input.DeflectionDenominator,trace:input.Trace,ignoreUplift:input.IgnoreUplift) {LazyLocations=input.LazySearchDiagnostics};
            input.NetworkLegCeiling=int.MaxValue;
            input.TransferredSlab=new TransferredSlabInput {IsNetwork=true,IncludeConcreteEndStrips=true,BeamWidthMm=c.WidthMm,BeamLineLoadKnM=r.Input.AreaLoad(c.DepthMm)*c.WidthMm/1000,UpperSpacingMm=spacing};
            TowerLayout l;
            try {l=TransferredSlabOptimizer.Optimize(input,true);}catch(TowerOptimizationException ex) {failure=$"X{index+1} dedicated towers: {ex.Message}";if(common.DedicatedCrossCache!=null)common.DedicatedCrossCache[Tuple.Create(index,spacing)]=new DedicatedCrossSearch {Failure=failure};return null;}
            var primary=new TransferMember[l.Transfer.PrimaryCases[0].Length];
            var left=new double[l.PositionsMm.Length];var right=new double[left.Length];
            int primaryRuns=l.StockPlan.Pieces.Length;
            for(int line=0;line<2;line++)for(int run=0;run<primaryRuns;run++)
            {
                var original=l.Transfer.PrimaryCases[0][run+line*primaryRuns];
                var uniform=original.Uniform;
                var m=Analyze(original,original.Points,uniform,input,"Cross concrete + construction/formwork + end strips");
                if(!Pass(m,input,$"X{index+1} dedicated primary UDL + end strips") || m.Analysis.Reactions.Max()>common.LegCapacityKn+1e-8) {failure="Dedicated primary UDL or leg check failed.";return null;}
                primary[run+line*primaryRuns]=m;
                for(int leg=0;leg<m.Analysis.Reactions.Length;leg++)(line==0 ? left : right)[l.StockPlan.Pieces[run].FirstSupport+leg]=m.Analysis.Reactions[leg];
            }
            l.Transfer.PrimaryCases=new[] {primary};l.LeftReactions=left;l.RightReactions=right;l.MinimumLeftReactions=(double[])left.Clone();l.MinimumRightReactions=(double[])right.Clone();l.MaxLegKn=Math.Max(left.Max(),right.Max());
            RefreshStockDemands(l);
            // End-strip reactions are included in the selected stock layout and every member check.
            var solids=new List<FormworkSolid>();var members=new List<TransferMember>();var bearings=new List<CrossBeamBearing>();double bottom=c.BottomElevationMm;
            int runs=l.StockPlan.Pieces.Length;
            for(int run=0;run<runs;run++)for(int side=0;side<2;side++)
            {
                var m=l.Transfer.PrimaryCases[0][run+side*runs];members.Add(m);
                for(int j=0;j<m.SupportsMm.Length;j++)bearings.Add(new CrossBeamBearing {IsDedicated=true,Label=$"Dedicated P{run+1} {(side==0 ? "L" : "R")} / leg {j+1}",Beam=-1,Row=j,Xmm=c.PositionMm+(side==0 ? -1 : 1)*l.WidthMm/2,Ymm=face+m.StartMm+m.SupportsMm[j],ReceivingTopMm=bottom-400,H20UndersideMm=bottom-400,ReactionKn=m.Analysis.Reactions[j]});
                foreach(int pair in new[] {-1,1})solids.Add(Timber($"X{index+1} {m.Name}",c.PositionMm+(side==0 ? -1 : 1)*l.WidthMm/2+pair*40,face+m.StartMm,m.LengthMm,false,bottom-200,m));
            }
            for(int row=0;row<l.Transfer.RowsMm.Length;row++)
            {
                var m=l.Transfer.LowerCases[0][row];members.Add(m);
                for(int side=0;side<2;side++)bearings.Add(new CrossBeamBearing {IsDedicated=true,Label=$"Dedicated T{row+1} / primary {(side==0 ? "L" : "R")}",Beam=-1,Row=row,Xmm=c.PositionMm+m.StartMm+m.SupportsMm[side],Ymm=face+l.Transfer.RowsMm[row],ReceivingTopMm=bottom-200,H20UndersideMm=bottom-200,ReactionKn=m.Analysis.Reactions[side]});
                solids.Add(Timber($"X{index+1} {m.Name}",c.PositionMm+m.StartMm,face+l.Transfer.RowsMm[row],m.LengthMm,true,bottom,m));
            }
            foreach(double station in l.PositionsMm)foreach(int side in new[] {-1,1})solids.Add(new FormworkSolid {Name=$"X{index+1} tower leg",X0=c.PositionMm+side*l.WidthMm/2-40,X1=c.PositionMm+side*l.WidthMm/2+40,Y0=face+station-40,Y1=face+station+40,Z0=-1e6,Z1=bottom-400,IsLeg=true});
            var result=new CrossBeamSupportResult {CrossingIndex=index,Input=c,Arrangement=CrossBeamSupportMode.DedicatedTowers,Towers=l,Members=members.ToArray(),Solids=solids.ToArray(),Bearings=bearings.ToArray(),LoadKn=l.Transfer.BeamLoadKn};
            if(common.DedicatedCrossCache!=null)common.DedicatedCrossCache[Tuple.Create(index,spacing)]=new DedicatedCrossSearch {Result=result};return result;
        }
        // Do not mutate the DP's shared tower layouts while evaluating a cross arrangement.
        private static TowerLayout Copy(TowerLayout l)
        {
            return new TowerLayout {IgnoreUplift=l.IgnoreUplift,MinimumLeftReactions=l.MinimumLeftReactions,MinimumRightReactions=l.MinimumRightReactions,Transfer=l.Transfer,PositionsMm=l.PositionsMm,Joined=l.Joined,WidthMm=l.WidthMm,Rotated=l.Rotated,LengthMm=l.LengthMm,ConcreteEndOverhangMm=l.ConcreteEndOverhangMm,LeftReactions=l.LeftReactions,RightReactions=l.RightReactions,LeftLineLoad=l.LeftLineLoad,RightLineLoad=l.RightLineLoad,LegCapacityKn=l.LegCapacityKn,MaxLegKn=l.MaxLegKn,StockPlan=l.StockPlan,DeflectionDenominator=l.DeflectionDenominator,DeflectionLimitMm=l.DeflectionLimitMm,MomentKnM=l.MomentKnM,ShearKn=l.ShearKn,DeflectionMm=l.DeflectionMm,Utilization=l.Utilization,Governing=l.Governing};
        }
        internal static bool Complete(TowerInput common,TransferNetworkResult r)
        {
            if(!Modern(r.Input))return true;
            var original=r.Beams;var existing=NetworkSolids(r).ToArray();var concrete=Concrete(r).ToArray();
            string baselineClash=Clash(existing,concrete);
            if(baselineClash!=null) {Reject(common,baselineClash);return false;}
            var accepted=new List<CrossBeamSupportResult>();
            Func<int,bool> arrange=null;
            arrange=index=>
            {
                common.Trace?.CheckCancellation();
                if(index==r.Input.CrossingBeams.Length)return Recheck(common,r,original,accepted.ToArray());
                common.Trace?.Work("Cross-beam support arrangements");
                var c=r.Input.CrossingBeams[index];if(c.SupportMode==CrossBeamSupportMode.LegacySlabRows)return arrange(index+1);
                string sharedFailure=null,towerFailure=null;
                if(c.SupportMode!=CrossBeamSupportMode.DedicatedTowers)
                {
                    var support=Shared(common,r,index,out sharedFailure);
                    if(support!=null)
                    {
                        sharedFailure=Clash(support.Solids,existing.Concat(concrete).Concat(accepted.SelectMany(a=>a.Solids)));
                        if(sharedFailure==null && !Recheck(common,r,original,accepted.Concat(new[] {support}).ToArray()))sharedFailure=$"X{index+1}: combined receiving-secondary, primary or leg construction-stage checks fail.";
                        if(sharedFailure==null)
                        {
                            accepted.Add(support);if(arrange(index+1))return true;accepted.RemoveAt(accepted.Count-1);
                            sharedFailure=$"X{index+1}: this shared arrangement does not permit a complete passing cross-beam combination.";
                        }
                    }
                }
                if(c.SupportMode!=CrossBeamSupportMode.ExistingSupportsOnly)
                {
                    var support=Dedicated(common,r,index,existing.Concat(concrete).Concat(accepted.SelectMany(a=>a.Solids)),out towerFailure);
                    if(support!=null)
                    {
                        support=new CrossBeamSupportResult {CrossingIndex=support.CrossingIndex,Input=support.Input,Arrangement=support.Arrangement,Towers=support.Towers,Members=support.Members,Solids=support.Solids,Bearings=support.Bearings,LoadKn=support.LoadKn,SharedFailure=sharedFailure};
                        accepted.Add(support);if(arrange(index+1))return true;accepted.RemoveAt(accepted.Count-1);
                        towerFailure=$"X{index+1}: dedicated towers do not permit a complete passing cross-beam combination.";
                    }
                }
                Reject(common,(sharedFailure??"")+" "+(towerFailure??"")+" No valid requested cross-beam support arrangement.");return false;
            };
            if(!arrange(0))return false;
            r.CrossSupports=accepted.ToArray();
            if(!Recheck(common,r,original,r.CrossSupports))return false;
            // Cross load is distinct from the reduced slab load and conserved in its full clear-bay footprint.
            foreach(var s in accepted)
            {
                double expected=r.Input.AreaLoad(s.Input.DepthMm)*s.Input.WidthMm*r.Input.Bays[s.Input.BayNumber-1].WidthMm/1e6;
                if(Math.Abs(expected-s.LoadKn)>1e-6*Math.Max(1,expected)) {Reject(common,$"X{s.CrossingIndex+1}: cross load conservation failed.");return false;}
            }
            double expectedSlab=0;
            for(int bay=0;bay<r.Bays.Length;bay++)if(r.Bays[bay]!=null)
            {
                double removed=r.Input.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).Sum(c=>c.WidthMm);
                expectedSlab+=r.Input.AreaLoad(r.Input.Bays[bay].ThicknessMm)*(r.SlabCoveredLengthMm-removed)*r.Input.Bays[bay].WidthMm/1e6;
                expectedSlab+=r.Input.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode==CrossBeamSupportMode.LegacySlabRows).Sum(c=>(r.Input.AreaLoad(c.DepthMm)-r.Input.AreaLoad(r.Input.Bays[bay].ThicknessMm))*c.WidthMm*r.Input.Bays[bay].WidthMm/1e6);
            }
            if(Math.Abs(expectedSlab-r.SlabLoadKn)>1e-6*Math.Max(1,expectedSlab)) {Reject(common,"No receiving slab H20 arrangement conserves the slab load outside cross footprints.");return false;}
            for(int beam=0;beam<r.Beams.Length;beam++)
            {
                var transfer=r.Beams[beam].Transfer;
                transfer.CoveredSlabAreaM2-=r.Input.CrossingBeams.Where(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).Sum(c=>c.WidthMm*(c.BayNumber==beam ? transfer.LeftUpperStockMm : c.BayNumber==beam+1 ? transfer.RightUpperStockMm : 0))/1e6;
            }
            foreach(var support in r.CrossSupports)
            {
                if(support.Towers!=null) {support.MovementMm=DedicatedMovement(support.Towers);continue;}
                for(int member=0;member<support.Members.Length;member++)
                {
                    var bearings=support.Bearings.Skip(member*2).Take(2).ToArray();
                    double[] offsets=bearings.Select(b=>Enumerable.Range(0,r.Beams[b.Beam].Transfer.CaseNames.Length).Select(stage=>BearingDisplacement(r,b,stage)).Min()).ToArray();
                    var m=support.Members[member];support.MovementMm=Math.Max(support.MovementMm,m.Analysis.DeflectionWithAffineOffsetMm(offsets[0],offsets[1],m.SupportsMm[0],m.SupportsMm[1]));
                }
            }
            return true;
        }
        private static double DedicatedMovement(TowerLayout layout)
        {
            var t=layout.Transfer;double movement=0;int runs=layout.StockPlan.Pieces.Length;
            for(int row=0;row<t.RowsMm.Length;row++)
            {
                double x=t.RowsMm[row];int run=Array.FindIndex(layout.StockPlan.Pieces,p=>x>=p.StartMm && x<p.EndMm);
                var member=t.LowerCases[0][row];double local=x-layout.StockPlan.Pieces[run].StartMm;
                double left=t.PrimaryCases[0][run].Analysis.Response(local)[2],right=t.PrimaryCases[0][run+runs].Analysis.Response(local)[2];
                movement=Math.Max(movement,member.Analysis.DeflectionWithAffineOffsetMm(left,right,member.SupportsMm[0],member.SupportsMm[1]));
            }
            return movement;
        }
        private static double BearingDisplacement(TransferNetworkResult r,CrossBeamBearing b,int stage)
        {
            var l=r.Beams[b.Beam];var t=l.Transfer;int run=Array.FindIndex(l.StockPlan.Pieces,p=>b.Xmm>=p.StartMm && b.Xmm<p.EndMm);
            if(run<0)return 0;
            double local=b.Xmm-l.StockPlan.Pieces[run].StartMm,y=b.Ymm-r.BeamCentresMm[b.Beam];
            double dl=t.PrimaryCases[stage][run].Analysis.Response(local)[2],dr=t.PrimaryCases[stage][run+l.StockPlan.Pieces.Length].Analysis.Response(local)[2];
            var secondary=t.LowerCases[stage][b.Row];return secondary.Analysis.Response(y-secondary.StartMm)[2]+dl+(dr-dl)*(y+l.WidthMm/2)/l.WidthMm;
        }
        private static void Reject(TowerInput common,string message)=>common.Diagnostics?.Geometry("cross support",message,"Review elevations, cross position, stock availability, bearing/end allowances and tower geometry.",blocker:true);
        private static TransferMember Analyze(TransferMember source,BeamPointLoad[] points,BeamDistributedLoad[] uniform,TowerInput common,string stage)
        {
            return new TransferMember {Name=source.Name,LoadCase=stage,StartMm=source.StartMm,LengthMm=source.LengthMm,H20Count=source.H20Count,SupportsMm=source.SupportsMm,Points=points,Uniform=uniform,Analysis=LoadedBeamAnalysis.AnalyzeSigned(source.LengthMm,source.SupportsMm,uniform,points,TowerOptimizer.PairEI*source.H20Count/2,common.DeflectionDenominator)};
        }
        private static BeamDistributedLoad[] PrimaryIncrement(TransferNetworkResult r,int beam,TransferMember p,int line,IEnumerable<CrossBeamSupportResult> supports,TowerInput common)
        {
            var loads=new List<BeamDistributedLoad>();var t=r.Beams[beam].Transfer;
            foreach(var s in supports)foreach(var b in s.Bearings.Where(b=>b.Beam==beam))
            {
                var lower=t.LowerCases[0][b.Row];
                double point=b.Ymm-r.BeamCentresMm[beam]-lower.StartMm;
                var increment=Analyze(lower,new[] {new BeamPointLoad {PositionMm=point,Kn=b.ReactionKn}},new BeamDistributedLoad[0],common,"Cross increment");
                double a=Math.Max(p.StartMm,b.FootprintStartMm),z=Math.Min(p.StartMm+p.LengthMm,b.FootprintEndMm);
                if(z>a+1e-7)loads.Add(new BeamDistributedLoad {StartMm=a-p.StartMm,EndMm=z-p.StartMm,KnPerM=increment.Analysis.Reactions[line]*1000/(b.FootprintEndMm-b.FootprintStartMm)});
            }
            return loads.ToArray();
        }
        private static bool Recheck(TowerInput common,TransferNetworkResult r,TowerLayout[] originals,CrossBeamSupportResult[] supports)
        {
            var shared=supports.Where(s=>s.Towers==null).ToArray();
            r.Beams=originals.Select(Copy).ToArray();
            for(int beam=0;beam<r.Beams.Length;beam++)
            {
                var l=r.Beams[beam];var t=originals[beam].Transfer;int runs=l.StockPlan.Pieces.Length;
                var relevant=shared.Where(s=>s.Bearings.Any(b=>b.Beam==beam)).ToArray();
                var cases=new List<TransferMember[]>();var lowerCases=new List<TransferMember[]>();var names=new List<string>();var left=Enumerable.Repeat(double.NegativeInfinity,l.PositionsMm.Length).ToArray();var right=Enumerable.Repeat(double.NegativeInfinity,left.Length).ToArray();
                var minLeft=Enumerable.Repeat(double.PositiveInfinity,left.Length).ToArray();var minRight=Enumerable.Repeat(double.PositiveInfinity,left.Length).ToArray();
                bool conservative=relevant.Length>8;
                int combinations=conservative ? 2 : 1<<relevant.Length;
                for(int stage=0;stage<t.CaseNames.Length;stage++)for(int mask=0;mask<combinations;mask++)
                {
                    common.Trace?.CheckCancellation();
                    var active=relevant.Where((s,i)=>conservative ? mask==1 : (mask&(1<<i))!=0).ToArray();
                    string name=t.CaseNames[stage]+(mask==0 ? "" : " + "+string.Join(" + ",active.Select(s=>"X"+(s.CrossingIndex+1))));
                    var lower=new TransferMember[t.RowsMm.Length];
                    for(int row=0;row<lower.Length;row++)
                    {
                        var original=t.LowerCases[stage][row];
                        var extra=active.SelectMany(s=>s.Bearings).Where(b=>b.Beam==beam && b.Row==row).Select(b=>new BeamPointLoad {PositionMm=b.Ymm-r.BeamCentresMm[beam]-original.StartMm,Kn=b.ReactionKn}).ToArray();
                        lower[row]=Analyze(original,original.Points.Concat(extra).ToArray(),original.Uniform,common,name);
                        if(!Pass(lower[row],common,name))return false;
                        if(conservative && mask==1 && !Envelope(common,original,active.Select(s=>Analyze(original,s.Bearings.Where(b=>b.Beam==beam && b.Row==row).Select(b=>new BeamPointLoad {PositionMm=b.Ymm-r.BeamCentresMm[beam]-original.StartMm,Kn=b.ReactionKn}).ToArray(),new BeamDistributedLoad[0],common,"increment")).ToArray(),null,lower[row]))return false;
                    }
                    var primary=new TransferMember[runs*2];
                    for(int line=0;line<2;line++)for(int run=0;run<runs;run++)
                    {
                        var original=t.PrimaryCases[stage][run+line*runs];
                        // Retain the primary UDL baseline; increments remain local to their concrete tributaries.
                        var baseline=original.Uniform;
                        var uniform=baseline.Concat(PrimaryIncrement(r,beam,original,line,active,common)).ToArray();
                        var m=Analyze(original,new BeamPointLoad[0],uniform,common,name);primary[run+line*runs]=m;
                        if(!Pass(m,common,name))return false;
                        common.Diagnostics?.Legs(m.Analysis.Reactions,common.LegCapacityKn,$"B{beam+1} cross stage legs",name);
                        if(m.Analysis.Reactions.Max()>common.LegCapacityKn+1e-8)return false;
                        if(conservative && mask==1)
                        {
                            var baseMember=Analyze(original,new BeamPointLoad[0],baseline,common,name);
                            var increments=active.Select(s=>Analyze(original,new BeamPointLoad[0],PrimaryIncrement(r,beam,original,line,new[] {s},common),common,"increment")).ToArray();
                            if(!Envelope(common,baseMember,increments,common.LegCapacityKn,m))return false;
                            for(int j=0;j<m.Analysis.Reactions.Length;j++)
                            {
                                int leg=l.StockPlan.Pieces[run].FirstSupport+j;var reactions=line==0 ? left : right;
                                reactions[leg]=Math.Max(reactions[leg],baseMember.Analysis.Reactions[j]+increments.Sum(i=>Math.Max(0,i.Analysis.Reactions[j])));
                                var minimum=line==0 ? minLeft : minRight;minimum[leg]=Math.Min(minimum[leg],baseMember.Analysis.Reactions[j]+increments.Sum(i=>Math.Min(0,i.Analysis.Reactions[j])));
                            }
                        }
                        for(int j=0;j<m.Analysis.Reactions.Length;j++) {int leg=l.StockPlan.Pieces[run].FirstSupport+j;var reactions=line==0 ? left : right;reactions[leg]=Math.Max(reactions[leg],m.Analysis.Reactions[j]);var minimum=line==0 ? minLeft : minRight;minimum[leg]=Math.Min(minimum[leg],m.Analysis.Reactions[j]);}
                    }
                    names.Add(name);cases.Add(primary);lowerCases.Add(lower);
                }
                l.Transfer=new TransferredSlabResult {RowsMm=t.RowsMm,TributaryWidthsMm=t.TributaryWidthsMm,LeftUpperScales=t.LeftUpperScales,RightUpperScales=t.RightUpperScales,EnteredUpperSpacingMm=t.EnteredUpperSpacingMm,CrossFormwork=t.CrossFormwork,BeamLoadKn=t.BeamLoadKn,LowerStockStartMm=t.LowerStockStartMm,LowerStockLengthMm=t.LowerStockLengthMm,LeftFootMm=t.LeftFootMm,RightFootMm=t.RightFootMm,LeftUpperStockMm=t.LeftUpperStockMm,RightUpperStockMm=t.RightUpperStockMm,LeftUpperEdgeMm=t.LeftUpperEdgeMm,RightUpperEdgeMm=t.RightUpperEdgeMm,LeftUpperCustom=t.LeftUpperCustom,RightUpperCustom=t.RightUpperCustom,LowerCustom=t.LowerCustom,LeftFootReactionKn=t.LeftFootReactionKn,RightFootReactionKn=t.RightFootReactionKn,SlabMovementMm=t.SlabMovementMm,CoveredSlabAreaM2=t.CoveredSlabAreaM2,SlabLoadKn=t.SlabLoadKn,ShutterLoadKn=t.ShutterLoadKn,PrimaryCases=cases.ToArray(),LowerCases=lowerCases.ToArray(),UpperMembers=t.UpperMembers,CaseNames=names.ToArray()};
                l.LeftReactions=left;l.RightReactions=right;l.MinimumLeftReactions=minLeft;l.MinimumRightReactions=minRight;l.MaxLegKn=Math.Max(left.Max(),right.Max());
                var all=cases.Concat(lowerCases).SelectMany(c=>c).Concat(t.UpperMembers).ToArray();
                l.MomentKnM=all.Max(m=>m.Analysis.MomentKnM);l.ShearKn=all.Max(m=>m.Analysis.ShearKn);l.DeflectionMm=all.Max(m=>m.Analysis.DeflectionMm);l.Utilization=Math.Max(l.MaxLegKn/l.LegCapacityKn,all.Max(m=>m.Utilization));l.Governing=l.MaxLegKn/l.LegCapacityKn>=all.Max(m=>m.Utilization) ? "Leg capacity" : all.OrderByDescending(m=>m.Utilization).First().Name;
                RefreshStockDemands(l);
            }
            return true;
        }
        private static void RefreshStockDemands(TowerLayout l)
        {
            int runs=l.StockPlan.Pieces.Length;
            var pieces=l.StockPlan.Pieces.Select((p,i)=>
            {
                var members=l.Transfer.PrimaryCases.SelectMany(c=>new[] {c[i],c[i+runs]}).ToArray();
                var d=members.OrderByDescending(m=>m.Analysis.DeflectionUtilization).First().Analysis;
                return new TowerStockPiece {StartMm=p.StartMm,StockLengthMm=p.StockLengthMm,FirstSupport=p.FirstSupport,LastSupport=p.LastSupport,IsCustomLength=p.IsCustomLength,MomentKnM=members.Max(m=>m.Analysis.MomentKnM),ShearKn=members.Max(m=>m.Analysis.ShearKn),DeflectionMm=members.Max(m=>m.Analysis.DeflectionMm),DeflectionUtilization=d.DeflectionUtilization,GoverningDeflectionMm=d.DeflectionUtilization*d.GoverningLengthMm/l.DeflectionDenominator,GoverningDeflectionLimitMm=d.GoverningLengthMm/l.DeflectionDenominator,GoverningDeflectionRegion="Envelope of primary UDL + local cross reaction stages"};
            }).ToArray();
            l.StockPlan=new TowerStockPlan {Pieces=pieces,Reactions=l.LeftReactions.Zip(l.RightReactions,Math.Max).ToArray()};
        }
        // Triangle inequalities for absolute response peaks and signed reaction influence bounds
        // conservatively cover all 2^N subsets, without silently skipping a multiple-crossing stage.
        internal static bool Envelope(TowerInput common,TransferMember baseline,TransferMember[] increments,double? legCapacity,TransferMember reported)
        {
            double m=baseline.Analysis.MomentKnM+increments.Sum(i=>i.Analysis.MomentKnM),v=baseline.Analysis.ShearKn+increments.Sum(i=>i.Analysis.ShearKn),d=baseline.Analysis.DeflectionUtilization+increments.Sum(i=>i.Analysis.DeflectionUtilization);
            reported.SubsetMomentBoundKnM=m;reported.SubsetShearBoundKn=v;reported.SubsetDeflectionBound=d;
            common.Diagnostics?.Limit(baseline.Name,"subset moment bound",3,m,baseline.MomentLimitKnM,"kNm",baseline.LoadCase,"Use dedicated towers to avoid combined shared reactions.");
            common.Diagnostics?.Limit(baseline.Name,"subset shear bound",3,v,baseline.ShearLimitKn,"kN",baseline.LoadCase,"Use dedicated towers.");
            common.Diagnostics?.Limit(baseline.Name,"subset deflection bound",3,d,1,"utilization",baseline.LoadCase,"Use dedicated towers.");
            if(m>baseline.MomentLimitKnM+1e-8 || v>baseline.ShearLimitKn+1e-8 || d>1+1e-8)return false;
            reported.SubsetMinimumReactionsKn=new double[baseline.Analysis.Reactions.Length];
            for(int j=0;j<baseline.Analysis.Reactions.Length;j++)
            {
                double low=baseline.Analysis.Reactions[j]+increments.Sum(i=>Math.Min(0,i.Analysis.Reactions[j])),high=baseline.Analysis.Reactions[j]+increments.Sum(i=>Math.Max(0,i.Analysis.Reactions[j]));
                reported.SubsetMinimumReactionsKn[j]=low;
                common.Diagnostics?.Bearing(baseline.Name,3,low,baseline.LoadCase);
                if(!common.IgnoreUplift && low< -1e-7 || legCapacity.HasValue && high>legCapacity.Value+1e-8)return false;
            }
            return true;
        }
    }
}
