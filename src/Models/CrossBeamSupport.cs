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
        public bool IsTowerEnvelope;
        public bool IsSideShutter;
        public string TowerOwner;
        // Strict interior intersection permits intended bearing contact on either surface.
        public bool Overlaps(FormworkSolid b)=>X0<b.X1-1e-7 && X1>b.X0+1e-7 && Y0<b.Y1-1e-7 && Y1>b.Y0+1e-7 && Z0<b.Z1-1e-7 && Z1>b.Z0+1e-7;
        // The envelope reserves tower-to-tower clearance, rather than treating the
        // open cage as solid timber. Legs belong inside their own frame reservation.
        // H20/column intersections still use their actual full-height stock solids.
        public bool Conflicts(FormworkSolid b)=>Overlaps(b) && (!(IsTowerEnvelope || b.IsTowerEnvelope) || IsTowerEnvelope && b.IsTowerEnvelope || (IsTowerEnvelope ? b.IsLeg : IsLeg) && TowerOwner!=b.TowerOwner);
    }
    public sealed class CrossBeamBearing
    {
        internal CrossBeamBearing Copy()=>(CrossBeamBearing)MemberwiseClone();
        public int Beam,Row;
        public int PrimaryLine=-1;
        public bool IsPrimaryBearing=>PrimaryLine>=0;
        public bool IsDedicated;
        public string Label;
        public double Xmm,Ymm,ReceivingTopMm,H20UndersideMm,ReactionKn,FootprintStartMm,FootprintEndMm;
        // Concrete junction load received directly by existing H20s, or through
        // the existing side shutter when IsShutterEndZone is set.
        public bool IsConcreteEndZone;
        public bool IsShutterEndZone;
        public double EndZoneStartMm,EndZoneEndMm;
        public double EndZoneTributaryWidthMm;
        public double FillerHeightMm=>H20UndersideMm-ReceivingTopMm;
    }
    public sealed class CrossBeamSupportResult
    {
        internal CrossBeamSupportResult Copy()=>(CrossBeamSupportResult)MemberwiseClone();
        public int CrossingIndex;
        public CrossBeamSupportMode Arrangement;
        public TransferCrossingBeamRow Input;
        public TowerLayout Towers;
        public TransferMember[] Members=new TransferMember[0];
        public FormworkSolid[] Solids=new FormworkSolid[0];
        public CrossBeamBearing[] Bearings=new CrossBeamBearing[0];
        public double LoadKn,MovementMm;
        public double SlabEdgeLoadKn;
        public string SharedFailure;
        public string SymmetryNotice;
        public bool UsesSpreader;
        public bool UsesPrimaryBearings=>Bearings.Any(b=>b.IsPrimaryBearing);
        public string SupportDescription=>$"X{CrossingIndex+1} · "+(UsesPrimaryBearings ? "secondary H20s on main primaries" : UsesSpreader ? "shared spreader support on receiving beams" : Towers!=null ? "dedicated tower support" : "receiving-beam junction support");
        public string JunctionNotice;
        public double NearShutterClearanceMm=200,FarShutterClearanceMm=200;
        public bool HasReducedShutterClearance=>NearShutterClearanceMm<200 || FarShutterClearanceMm<200;
        public string ShutterClearanceNotice=>!HasReducedShutterClearance ? "" : $"X{CrossingIndex+1}: local side-shutter exception · B{Input.BayNumber} allowance {NearShutterClearanceMm:0.###} mm · B{Input.BayNumber+1} allowance {FarShutterClearanceMm:0.###} mm. Standard 200 mm detail was tried first; reduced junction loads, bearing positions and full stock clearances were rechecked.";
        public double TowerSpanOffsetMm;
        public double PrimaryNearExtensionMm,PrimaryFarExtensionMm;
        public double[] SecondaryStationsMm= new double[0];
        public double NominalSecondarySpacingMm=>Towers?.Transfer?.EnteredUpperSpacingMm??(SecondaryStationsMm.Length<2 ? 0 : SecondaryStationsMm.Zip(SecondaryStationsMm.Skip(1),(a,b)=>b-a).Max());
        public string SecondarySpacingDescription
        {
            get
            {
                if(Towers!=null)return $"{NominalSecondarySpacingMm:0.###} mm";
                var gaps=SecondaryStationsMm.Zip(SecondaryStationsMm.Skip(1),(a,b)=>b-a).ToArray();
                if(gaps.Length==0)return "Not applicable";
                return gaps.Max()-gaps.Min()<1e-7 ? $"{gaps.Max():0.###} mm" : $"varies, {gaps.Min():0.###}–{gaps.Max():0.###} mm";
            }
        }
        public int H20Count=>Towers==null ? Members.Sum(m=>m.H20Quantity) : Towers.StockPlan.TotalH20Count+Towers.Transfer.AdditionalH20Count;
        public int WS10Count=>Towers?.StockPlan.WS10Count??Members.Sum(m=>m.WS10Quantity);
        public double WS10LengthMm=>Towers==null ? Members.Sum(m=>m.LengthMm*m.WS10Quantity) : Towers.Primary.IsWS10 ? Towers.StockPlan.PrimaryLengthMm : 0;
        public double TimberMm=>Towers==null ? Members.Sum(m=>m.LengthMm*m.H20Quantity) : Towers.StockPlan.TotalStockLengthMm+Towers.Transfer.AdditionalTimberMm;
        public const string Limitations="Filler heights are geometric gaps only: filler timber bearing strength, restraint and stability are not verified. Crossing junction strips on deeper parallel beams use the existing slab-edge shutter model: the selected strip width (normally 200 mm; locally 175 mm only after the standard detail fails) reacts at its midpoint outside the concrete face on the receiving secondary. Side-shutter bearing and connection strength are not calculated. H20 bearing/connection design, sheathing, tower stability, bracing strength and foundations require separate verification. H20 depth is 200 mm and flange width is 80 mm. H20 clearance checks include full stock tails, H20 pairs and tower leg columns. Tower-to-tower clearance reserves each complete braced footprint below its head, with 40 mm around leg centres, so independent frames and columns cannot intersect. Frame-to-timber clearance needs the actual jack extensions, brace levels and hardware; these details are not modelled. Cross-beam load includes concrete and applicable construction/formwork allowances, with no slab tributary. Production main primaries use Beam + Slab UDL outside crossings and assigned Beam + Cross Beam UDL across each full physical crossing width for shared, dedicated and mixed supports. Dedicated tower loads are counted once; their slab edge transfers are deducted from the normal slab allocation. Physical secondary bearing loads remain checked. Historical replay alone retains the former bearing-derived primary model. The selected support arrangement and primary model are reported separately. Every member and leg capacity check remains in force. Shared H20 end strips transfer through the existing side shutters to receiving secondaries, rather than point loads at stock ends. Production checks all applicable structural loads together; partial construction stages are not covered. Historical staged test inputs retain their original subset checks. Search is bounded and does not prove a global optimum.";
        public string Schedule=>$"X{CrossingIndex+1}: S{Input.BayNumber} · {(Towers==null ? "Existing supports" : "Dedicated towers")} · {Input.WidthMm:0.###} × {Input.DepthMm:0.###} mm · bottom {Input.BottomElevationMm:0.###} mm\nCross-beam load {LoadKn:0.###} kN · {H20Count} H20s{(WS10Count>0 ? $" · {WS10Count} WS10s" : "")} · {TimberMm/1000:0.###} m timber · {Towers?.LegCount??0} dedicated legs · movement {MovementMm:0.###} mm"
            +(Towers==null || Towers.ConcreteEndOverhangMm<=1e-7 ? "" : $"\nConcrete end strips up to {Towers.ConcreteEndOverhangMm:0.###} mm per end transfer through the outer two secondary rows at each end; primary UDL includes the full concrete-span load over centred covered concrete; no secondary-row point loads are applied to primaries.")
            +(SlabEdgeLoadKn<=1e-10 ? "" : $"\nSlab plywood edge ledgers: {SlabEdgeLoadKn:0.###} kN transferred through this crossing formwork; counted once.")
            +(NominalSecondarySpacingMm<=0 ? "" : $"\nNominal crossing secondary spacing {NominalSecondarySpacingMm:0.###} mm; local receiving gaps can be shorter.")
            +(SharedFailure==null ? "" : "\nAutomatic fallback: "+SharedFailure)
            +(string.IsNullOrEmpty(SymmetryNotice) ? "" : "\n"+SymmetryNotice)
            +(JunctionNotice==null ? "" : "\n"+JunctionNotice)
            +(HasReducedShutterClearance ? "\n"+ShutterClearanceNotice : "")
            +(PrimaryNearExtensionMm+PrimaryFarExtensionMm<=0 ? "" : $"\nPrimary stock projections into end zones: near {PrimaryNearExtensionMm:0.###} mm, far {PrimaryFarExtensionMm:0.###} mm. Secondary H20s still stop clear of the receiving secondaries. Projecting tails carry WS10 self-weight when selected; the central-span UDL and its resultant stay in their original positions.")
            +"\n"+string.Join("\n",Bearings.Select(b=>b.IsConcreteEndZone ? $"Concrete end zone on B{b.Beam+1}/T{b.Row+1}: y={b.EndZoneStartMm:0.###}–{b.EndZoneEndMm:0.###} mm; effective row tributary {b.EndZoneTributaryWidthMm:0.###} mm; primary load footprint x={b.FootprintStartMm:0.###}–{b.FootprintEndMm:0.###} mm; load {b.ReactionKn:0.###} kN; "+(b.IsShutterEndZone ? $"via existing side shutter, height {b.FillerHeightMm:0.###} mm, reaction y={b.Ymm:0.###} mm; " : "directly on receiving H20s; ")+"included in receiving H20, primary and leg construction stages." : $"Bearing {b.Label??($"B{b.Beam+1}/T{b.Row+1}")} @ ({b.Xmm:0.###}, {b.Ymm:0.###}) mm: receiving top {b.ReceivingTopMm:0.###}, cross H20 underside {b.H20UndersideMm:0.###} mm; "+(b.FillerHeightMm<=1e-7 ? "direct bearing" : $"filler timber {b.FillerHeightMm:0.###} mm")+$"; reaction {b.ReactionKn:0.###} kN"))
            +"\n"+string.Join("\n",Members.Select(m=>$"{m.Name} · {m.LoadCase} · {m.MaterialDescription} · {m.LengthMm:0.###} mm: M {m.Analysis.MomentKnM:0.###}/{m.MomentLimitKnM:0.###} kNm; V {m.Analysis.ShearKn:0.###}/{m.ShearLimitKn:0.###} kN; d {m.Analysis.DeflectionMm:0.###} mm; minimum signed reaction {m.MinimumReactionKn:0.######} kN; utilization {m.Utilization*100:0.0}%"))
            +(Towers==null ? "" : "\n"+Towers.Schedule+"\n"+Towers.StockSchedule);
    }
    internal sealed class CrossShutterAllowance
    {
        internal readonly double Near,Far;
        internal CrossShutterAllowance(double near,double far){Near=near;Far=far;}
        internal static readonly CrossShutterAllowance Standard=new CrossShutterAllowance(200,200);
        internal static readonly CrossShutterAllowance[] StandardOnly={Standard};
        internal static readonly CrossShutterAllowance[] Candidates={Standard,new CrossShutterAllowance(175,200),new CrossShutterAllowance(200,175),new CrossShutterAllowance(175,175)};
    }
    internal sealed class CrossLowerCaseCheck
    {
        internal TransferMember[] Members;
        internal bool Passed;
        internal TowerSearchDiagnostics Diagnostics;
    }
    internal sealed class DedicatedCrossSearch
    {
        internal CrossBeamSupportResult Result;
        internal string Failure;
    }
    // One scenario owns this bounded cache. Loads/settings are immutable inside
    // that scenario; keys additionally retain exact receivers, edge loads and
    // obstacle geometry. Different receiver loads are still checked by Recheck.
    internal sealed class CrossGeometryCache
    {
        internal readonly TransferredSlabOptimizer.ResponseCache<CrossGeometryKey,DedicatedCrossSearch> Results
            =new TransferredSlabOptimizer.ResponseCache<CrossGeometryKey,DedicatedCrossSearch>(128);
        internal readonly TransferredSlabOptimizer.ResponseCache<PrimaryBearingGeometryKey,DedicatedCrossSearch> PrimaryBearings
            =new TransferredSlabOptimizer.ResponseCache<PrimaryBearingGeometryKey,DedicatedCrossSearch>(256);
    }
    internal sealed class CrossGeometryKey : IEquatable<CrossGeometryKey>
    {
        readonly double[] values;readonly string[] labels;readonly int hash;
        internal CrossGeometryKey(TransferNetworkResult r,int index,CrossShutterAllowance allowance,FormworkSolid[] obstacles)
        {
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;
            var data=new List<double> {index,c.BayNumber,c.PositionMm,c.WidthMm,c.DepthMm,c.BottomElevationMm,
                r.Input.AreaLoad(c.DepthMm),r.Input.CrossSpacing(index),r.Input.PlywoodSheetLengthMm,
                r.Input.Bays[bay].WidthMm,r.BeamCentresMm[bay],allowance.Near,allowance.Far};
            for(int beam=bay;beam<=bay+1;beam++)
            {
                var t=r.Beams[beam].Transfer;var b=r.Input.Beams[beam];
                data.AddRange(new[] {b.WidthMm,b.BottomElevationMm,t.LowerStockStartMm,t.LowerStockLengthMm,(double)t.RowsMm.Length});
                data.AddRange(t.RowsMm);
            }
            var loads=CrossBeamSupportOptimizer.SlabEdgeLoads(r,index);data.Add(loads.Length);
            foreach(var load in loads)data.AddRange(new[] {load.PositionMm,load.Kn});
            // Every candidate component is at or below the crossing soffit.
            // Higher slab timber cannot intersect it and is independent of the
            // geometry search, even when another slab-foot option is selected.
            var relevant=obstacles.Where(s=>s.Z0<c.BottomElevationMm-1e-7 || s.Name.Contains("primary")).ToArray();
            labels=new string[relevant.Length*2];int at=0;
            foreach(var s in relevant)
            {
                labels[at++]=s.Name;labels[at++]=s.TowerOwner;
                data.AddRange(new[] {s.X0,s.X1,s.Y0,s.Y1,s.Z0,s.Z1,s.IsLeg ? 1.0 : 0,s.IsTowerEnvelope ? 1.0 : 0,s.IsSideShutter ? 1.0 : 0});
            }
            values=data.ToArray();unchecked {int h=17;foreach(double v in values)h=h*31+v.GetHashCode();foreach(string label in labels)h=h*31+(label?.GetHashCode()??0);hash=h;}
        }
        public override int GetHashCode()=>hash;
        public override bool Equals(object obj)=>Equals(obj as CrossGeometryKey);
        public bool Equals(CrossGeometryKey other)=>other!=null && hash==other.hash && values.SequenceEqual(other.values) && labels.SequenceEqual(other.labels);
    }
    /// <summary>Cross formwork is evaluated on complete network candidates, before ranking them.</summary>
    public static partial class CrossBeamSupportOptimizer
    {
        public const double H20DepthMm=200, H20WidthMm=80, BearingInsetMm=150, SideShutterClearanceMm=200;
        internal static bool Modern(TransferNetworkInput n)=>n.CrossingBeams.Any(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows);
        internal static double[] SlabWidths(TransferNetworkInput n,int bay,double edge,double cover,double[] rows,double[] original)
        {
            var cuts=n.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).OrderBy(c=>c.PositionMm).ToArray();
            if(cuts.Length==0)return original;
            if(n.HasIndividualSpacings && n.UseCrossingSlabLedgers)
            {
                var map=n.CrossSlabLayout(cover+2*edge,cover,bay);
                return !map.Valid ? null : rows.Select(x=>Array.IndexOf(map.Rows,x)).Select(i=>i<0 ? 0 : map.Widths[i]).ToArray();
            }
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
                    if(lo>segment && Math.Max(Math.Abs(segment-rows[active[k]]),Math.Abs(lo-rows[active[k]]))>n.BaySpacing(bay)+1e-7)return null;
                    widths[active[k]]-=hi-lo;segment=Math.Max(segment,hi);
                }
                if(b>segment && Math.Max(Math.Abs(segment-rows[active[k]]),Math.Abs(b-rows[active[k]]))>n.BaySpacing(bay)+1e-7)return null;
            }
            return widths;
        }
        public static bool ValidFillerGap(double crossBottom,double receivingBottom,out double filler)
        {filler=crossBottom-H20DepthMm-receivingBottom;return TowerBeamAnalysis.Finite(filler) && filler>=-1e-7;}
        internal static bool MandatoryStocksFit(TowerInput common,TransferNetworkInput network)
        {
            if(!common.EngineerWorkflow || !common.OptimizeCrossingSearch)return true;
            double bay=TowerOptimizer.BaysMm.Where(x=>!common.Exclude600Standalone || x!=600).Min();
            double minimum=bay+2*common.StockEndSetbackMm;
            // This is an optimistic bound. Extra legs, additional stocks, clashes
            // and capacities can only need more room. Retain the local exception
            // until it has actually been ruled out by its own search pass.
            double clearance=common.AllowLocalShutterExceptions ? 175 : 200;
            for(int index=0;index<network.CrossingBeams.Length;index++)
            {
                var c=network.CrossingBeams[index];int side=c.BayNumber-1;
                if(c.SupportMode==CrossBeamSupportMode.LegacySlabRows || c.SupportMode==CrossBeamSupportMode.ExistingSupportsOnly)continue;
                if(!common.EngineerDedicatedFallback && PrimaryBearingPossible(common,network,c))continue;
                if(c.SupportMode==CrossBeamSupportMode.Automatic && ValidFillerGap(c.BottomElevationMm,network.Beams[side].BottomElevationMm,out var near)
                    && ValidFillerGap(c.BottomElevationMm,network.Beams[side+1].BottomElevationMm,out var far))continue;
                double maximum=network.Bays[side].WidthMm-2*clearance;
                bool fits=common.PrimaryStockLengthsMm.Any(s=>s>=minimum-1e-7 && s<=maximum+1e-7)
                    || common.AllowCustomPrimaryLengths && Math.Ceiling(minimum/25)*25<=Math.Min(50000,maximum)+1e-7;
                if(fits)continue;
                common.Diagnostics?.Geometry($"X{index+1} dedicated stock feasibility",
                    $"X{index+1}: shared stacked support is unavailable or dedicated support is required. A primary needs at least {minimum:0.###} mm for the smallest permitted braced bay and two {common.StockEndSetbackMm:0.###} mm stock setbacks, but the {network.Bays[side].WidthMm:0.###} mm clear bay leaves at most {maximum:0.###} mm after {clearance:0.###} mm shutter clearance at each end. No permitted primary stock fits this interval.",
                    "Review permitted primary stocks, custom-length scope or the crossing support detail.",blocker:true);
                return false;
            }
            return true;
        }
        // Necessary geometry only: test a crossing frame against this receiver's
        // actual braced footprints, allowing continuous translations and ignoring
        // the other receiver, timber and loads. This optimistic screen cannot
        // approve a layout; complete crossing/member checks still follow.
        internal static bool ReceiverFramesFit(TowerInput common,TransferNetworkInput network,int beam,TransferredSlabOptimizer.Geometry geometry,double edge,double width,double offset)
        {
            var main=network.Beams[beam];double clearance=common.AllowLocalShutterExceptions ? 175 : 200;
            var frames=new System.Collections.Generic.List<double[]>();
            for(int first=0;first<geometry.Positions.Length;)
            {
                int last=first;while(last<geometry.Joined.Length && geometry.Joined[last])last++;
                frames.Add(new[] {geometry.Positions[first]+edge-40,geometry.Positions[last]+edge+40,offset-width/2-40,offset+width/2+40});first=last+1;
            }
            foreach(var c in network.CrossingBeams.Where(c=>c.BayNumber==beam || c.BayNumber==beam+1))
            {
                int bay=c.BayNumber-1;
                if(c.SupportMode==CrossBeamSupportMode.LegacySlabRows || c.SupportMode==CrossBeamSupportMode.ExistingSupportsOnly)continue;
                if(PrimaryBearingPossible(common,network,c))continue;
                if(c.SupportMode==CrossBeamSupportMode.Automatic && ValidFillerGap(c.BottomElevationMm,network.Beams[bay].BottomElevationMm,out var a)
                    && ValidFillerGap(c.BottomElevationMm,network.Beams[bay+1].BottomElevationMm,out var b))continue;
                double span=network.Bays[bay].WidthMm,face=c.BayNumber==beam+1 ? main.WidthMm/2 : -main.WidthMm/2-span;
                bool fits=false;
                // Each possible braced group is at least this large. A connected
                // three-leg group of 600 mm bays is larger than the optimistic
                // 1000 mm group retained when standalone 600 mm is excluded.
                double smallest=TowerOptimizer.BaysMm.Where(x=>!common.Exclude600Standalone || x!=600).Min();
                foreach(var size in new[] {new[] {1524.0,smallest},new[] {600.0,1524}})
                {
                    double low=face+clearance+common.StockEndSetbackMm+size[1]/2;
                    double high=face+span-clearance-common.StockEndSetbackMm-size[1]/2;
                    if(low>high+1e-7)continue;
                    if(FrameCentreExists(frames,c.PositionMm-150,c.PositionMm+150,low,Math.Max(low,high),size[0]/2+40,size[1]/2+40)){fits=true;break;}
                }
                if(fits)continue;
                common.Diagnostics?.Geometry($"B{beam+1}/X{Array.IndexOf(network.CrossingBeams,c)+1} braced footprint",
                    $"B{beam+1}: this braced support pattern leaves no possible independent tower footprint for X{Array.IndexOf(network.CrossingBeams,c)+1} inside its shutter and stock-end clearances.",
                    "Try another main-beam frame width or support pattern; loads and member capacities remain checked.",blocker:true);
                return false;
            }
            return true;
        }
        private static bool FrameCentreExists(System.Collections.Generic.List<double[]> frames,double x0,double x1,double y0,double y1,double hx,double hy)
        {
            var forbidden=frames.Select(f=>new[] {f[0]-hx+1e-7,f[1]+hx-1e-7,f[2]-hy+1e-7,f[3]+hy-1e-7}).ToArray();
            var xs=new[] {x0,x1}.Concat(forbidden.SelectMany(f=>new[] {f[0],f[1]}).Where(x=>x>=x0 && x<=x1));
            foreach(double x in xs)
            {
                double y=y0;
                foreach(var f in forbidden.Where(f=>x>f[0] && x<f[1]).OrderBy(f=>f[2]))
                {
                    if(y<=f[2])return true;
                    if(y<f[3])y=f[3];
                    if(y>y1)break;
                }
                if(y<=y1)return true;
            }
            return false;
        }
        internal static bool Pass(TransferMember m,TowerInput common,string location)
        {
            if(m.IsWS10)
            {
                common.Diagnostics?.Limit(m.Name,"WS10 M/V interaction (end zones included)",3,m.StrengthUtilization,1,"utilization",location,"Review primary stock and tower spacing.");
                common.Diagnostics?.Limit(m.Name,"deflection",3,m.Analysis.DeflectionUtilization,1,"utilization",location,"Review support spans and overhangs.");
                common.Diagnostics?.Bearing(m.Name,3,m.Analysis.Reactions.Min(),location);
            }
            else common.Diagnostics?.Analysis(m.Analysis,1,m.H20Count,m.Name,2,location);
            return m.Utilization<=1+1e-8 && (common.IgnoreUplift || m.Analysis.Reactions.All(r=>r>=-1e-7));
        }
        private static FormworkSolid Timber(string name,double x,double y,double length,bool alongX,double top,TransferMember member=null)
            =>new FormworkSolid {Name=name,X0=alongX ? x : x-40,X1=alongX ? x+length : x+40,Y0=alongX ? y-40 : y,Y1=alongX ? y+40 : y+length,Z0=top-200,Z1=top,Member=member};
        private static IEnumerable<FormworkSolid> PrimarySolids(string name,double x,double y,double length,bool alongX,double top,PrimaryBeamProfile profile,TransferMember member=null)
        {
            if(!profile.IsWS10) {foreach(int pair in new[] {-1,1})yield return Timber(name,x+(alongX ? 0 : pair*40),y+(alongX ? pair*40 : 0),length,alongX,top,member);yield break;}
            double half=profile.WidthMm/2;
            yield return new FormworkSolid {Name=name,X0=alongX ? x : x-half,X1=alongX ? x+length : x+half,Y0=alongX ? y-half : y,Y1=alongX ? y+half : y+length,Z0=top-profile.DepthMm,Z1=top,Member=member};
        }
        internal static IEnumerable<FormworkSolid> TowerEnvelopes(TowerLayout layout,string owner,double originX,double originY,bool alongX,double head)
        {
            int group=0;
            for(int first=0;first<layout.PositionsMm.Length;)
            {
                int last=first;while(last<layout.Joined.Length && layout.Joined[last])last++;
                double a=layout.PositionsMm[first]-40,b=layout.PositionsMm[last]+40,c=-layout.WidthMm/2-40,d=layout.WidthMm/2+40;
                yield return new FormworkSolid {Name=$"{owner} tower G{++group} frame/brace envelope",TowerOwner=owner,IsTowerEnvelope=true,X0=originX+(alongX ? a : c),X1=originX+(alongX ? b : d),Y0=originY+(alongX ? c : a),Y1=originY+(alongX ? d : b),Z0=-1e6,Z1=head};
                first=last+1;
            }
        }
        internal static IEnumerable<FormworkSolid> ProjectedTowerLegs(TowerLayout layout,string owner,double originX,double originY,double head)
        {
            // Identical to the dedicated result's actual leg solids. Unlike the
            // frame reservation, these also conflict with timber and concrete.
            foreach(double station in layout.PositionsMm)foreach(int side in new[] {-1,1})
                yield return new FormworkSolid {Name=owner+" tower leg",TowerOwner=owner,X0=originX+side*layout.WidthMm/2-40,X1=originX+side*layout.WidthMm/2+40,Y0=originY+station-40,Y1=originY+station+40,Z0=-1e6,Z1=head,IsLeg=true};
        }
        internal static IEnumerable<FormworkSolid> NetworkSolids(TransferNetworkResult r)
        {
            for(int b=0;b<r.Beams.Length;b++)
            {
                var l=r.Beams[b];var t=l.Transfer;double y=r.BeamCentresMm[b],bottom=r.Input.Beams[b].BottomElevationMm;
                foreach(var p in l.StockPlan.Pieces)foreach(int side in new[] {-1,1})
                    foreach(var solid in PrimarySolids($"B{b+1} primary",p.StartMm,y+l.TransverseOffsetMm+side*l.WidthMm/2,p.StockLengthMm,true,bottom-200,l.Primary))yield return solid;
                for(int physical=0;physical<t.PhysicalRowCount;physical++)yield return Timber($"B{b+1}/T{t.PhysicalRowDesignIndex(physical)+1}",t.PhysicalRowPosition(physical),y+t.LowerStockStartMm,t.LowerStockLengthMm,false,bottom);
                foreach(double x in l.PositionsMm)foreach(int side in new[] {-1,1})yield return new FormworkSolid {Name=$"B{b+1} tower leg",TowerOwner=$"B{b+1}",X0=x-40,X1=x+40,Y0=y+l.TransverseOffsetMm+side*l.WidthMm/2-40,Y1=y+l.TransverseOffsetMm+side*l.WidthMm/2+40,Z0=-1e6,Z1=bottom-200-l.Primary.DepthMm,IsLeg=true};
                foreach(var frame in TowerEnvelopes(l,$"B{b+1}",0,y+l.TransverseOffsetMm,true,bottom-200-l.Primary.DepthMm))yield return frame;
            }
            for(int bay=0;bay<r.Bays.Length;bay++)
            {
                var p=r.Bays[bay];if(p==null)continue;
                double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2;
                double top=Math.Max(r.Input.Beams[bay].BottomElevationMm+r.Input.Beams[bay].DepthMm,r.Input.Beams[bay+1].BottomElevationMm+r.Input.Beams[bay+1].DepthMm)-r.Input.Bays[bay].ThicknessMm;
                for(int row=0;row<r.SlabRowsMm.Length;row++)if(r.SlabRowActive(bay,row))yield return Timber($"S{bay+1} slab H20",r.SlabRowsMm[row],face+p.EdgeMm,p.StockLengthMm,false,top);
            }
        }
        internal static IEnumerable<FormworkSolid> Concrete(TransferNetworkResult r)
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
            var indexed=other as FormworkObstacleIndex;
            var a=indexed==null ? added.ToArray() : added as FormworkSolid[] ?? added.ToArray();
            var b=indexed==null ? other.ToArray() : null;
            for(int i=0;i<a.Length;i++)
            {
                if(indexed!=null)
                {
                    var s=indexed.FirstConflict(a[i]);if(s!=null)return $"Physical overlap: {a[i].Name} / {s.Name}.";
                }
                else foreach(var s in b)if(a[i].Conflicts(s))return $"Physical overlap: {a[i].Name} / {s.Name}.";
                for(int j=0;j<i;j++)if(a[i].Conflicts(a[j]))return $"Physical overlap: {a[i].Name} / {a[j].Name}.";
            }
            return null;
        }
        private static CrossBeamSupportResult Shared(TowerInput common,TransferNetworkResult r,int index,out string failure)
        {
            using(var hot=common.Trace?.Hot(BeamHotStage.SharedCrossing))
            {
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;double span=r.Input.Bays[bay].WidthMm;
            var left=r.Beams[bay].Transfer;var right=r.Beams[bay+1].Transfer;double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2;
            failure=null;
            double lf,rf;
            if(!ValidFillerGap(c.BottomElevationMm,r.Input.Beams[bay].BottomElevationMm,out lf) || !ValidFillerGap(c.BottomElevationMm,r.Input.Beams[bay+1].BottomElevationMm,out rf))
            {failure=$"X{index+1}: cross H20 underside {c.BottomElevationMm-200:0.###} mm is below a receiving secondary top (negative filler gap).";return null;}
            var rows=Enumerable.Range(0,left.RowsMm.Length).Where(i=>Math.Abs(left.RowsMm[i]-c.PositionMm)<=c.WidthMm/2-40+1e-7 && right.RowsMm.Any(x=>Math.Abs(x-left.RowsMm[i])<1e-7)).ToArray();
            bool spreader=common.EngineerWorkflow && EngineerPrimaryPlan.Eligible(r.Input,c);
            if(!spreader && rows.Length==0) {failure=$"X{index+1}: no receiving secondary within the cross-beam width including the 80 mm H20 flange.";return null;}
            if(!spreader && rows.Length<2 && SlabEdgeLoads(r,index).Length>0){failure=$"X{index+1}: slab-edge ledgers need two receiving rows for their load transfer.";return null;}
            double maximum=r.Input.CrossSpacing(index);
            if(!spreader && (left.RowsMm[rows[0]]-(c.PositionMm-c.WidthMm/2)>maximum+1e-7
                || c.PositionMm+c.WidthMm/2-left.RowsMm[rows.Last()]>maximum+1e-7
                || rows.Select(i=>left.RowsMm[i]).Zip(rows.Skip(1).Select(i=>left.RowsMm[i]),(a,b)=>b-a).Any(g=>g>maximum+1e-7)))
            {failure=$"X{index+1}: aligned receiving rows exceed this crossing's plywood spacing limit.";return null;}
            var positions=spreader ? PlywoodSpacing.SheetSupportRows(c.PositionMm-c.WidthMm/2,c.PositionMm+c.WidthMm/2,maximum) : rows.Select(i=>left.RowsMm[i]).ToArray();
            if(spreader)
            {
                int requested=Math.Max(left.EngineerReceiverCount,right.EngineerReceiverCount);
                int count=Math.Max(positions.Length,Math.Min(requested,1+(int)Math.Floor((c.WidthMm-80)/80)));
                positions=Enumerable.Range(0,count).Select(k=>c.PositionMm-c.WidthMm/2+40+(c.WidthMm-80)*k/(count-1)).ToArray();
            }
            double near=left.LowerStockStartMm+left.LowerStockLengthMm-r.Input.Beams[bay].WidthMm/2-BearingInsetMm;
            double far=-right.LowerStockStartMm-r.Input.Beams[bay+1].WidthMm/2-BearingInsetMm;
            near=Math.Min(r.Beams[bay].TransverseOffsetMm+r.Beams[bay].WidthMm/2-r.Input.Beams[bay].WidthMm/2,Math.Min(350,Math.Min(near,span/2-150)));far=Math.Min(r.Beams[bay+1].WidthMm/2-r.Beams[bay+1].TransverseOffsetMm-r.Input.Beams[bay+1].WidthMm/2,Math.Min(350,Math.Min(far,span/2-150)));
            if(near<150 || far<150) {failure=$"X{index+1}: receiving stock tails cannot provide 150 mm bearing/end allowances.";return null;}
            // These end strips bear through the parallel-beam side shutters below.
            // They are not unsupported concrete ends governed by the main-primary
            // end allowance. Actual receiving bearings and stock tails limit fit.
            var lengths=common.StockLengthsMm.Where(s=>s<=span+1e-7);
            if(common.AllowCustomBeamLengths)lengths=lengths.Concat(new[] {Math.Floor(span/25)*25}).Distinct();
            foreach(double stock in lengths.OrderBy(x=>x))
            foreach(double edge in (common.EngineerWorkflow ? new[] {(near+span-far-stock)/2,(span-stock)/2} : new[] {(span-stock)/2}).Distinct())
            {
                double farEdge=span-edge-stock;
                if(edge< -1e-7 || farEdge< -1e-7 || near-edge<150 || far-farEdge<150)continue;
                var members=new List<TransferMember>();var bearings=new List<CrossBeamBearing>();var solids=new List<FormworkSolid>();bool valid=true;
                var ledgerRows=LedgerRowLineLoads(r,index,positions);var ledgerBearings=new List<CrossBeamBearing>();var endBearings=new List<CrossBeamBearing>();
                for(int k=0;k<positions.Length;k++)
                {
                    double lo=k==0 ? c.PositionMm-c.WidthMm/2 : (positions[k-1]+positions[k])/2;
                    double hi=k==positions.Length-1 ? c.PositionMm+c.WidthMm/2 : (positions[k]+positions[k+1])/2;
                    // As for slab H20s, only the area above the stock loads the H20.
                    // The remaining strip loads its own shutter on the receiving secondary.
                    var q=new[] {new BeamDistributedLoad {StartMm=0,EndMm=stock,KnPerM=r.Input.AreaLoad(c.DepthMm)*(hi-lo)/1000+ledgerRows[k]}};
                    var points=Array.Empty<BeamPointLoad>();
                    var m=new TransferMember {Name=$"X{index+1} H20 {k+1}",LoadCase="Cross concrete + construction/formwork",H20Count=1,StartMm=face+edge,LengthMm=stock,SupportsMm=new[] {near-edge,span-far-edge},Uniform=q,Points=points};
                    m.Analysis=LoadedBeamAnalysis.Analyze(stock,m.SupportsMm,q,points,TowerOptimizer.PairEI/2,common.DeflectionDenominator);
                    if(!Pass(m,common,$"Cross X{index+1}; H20 {k+1}")) {valid=false;break;}
                    members.Add(m);double x=positions[k];
                    solids.Add(Timber(m.Name,x,face+edge,stock,false,c.BottomElevationMm,m));
                    for(int side=0;side<2;side++)
                    {
                        int beam=bay+side;double y=face+(side==0 ? near : span-far);
                        var bearing=new CrossBeamBearing {Beam=beam,Row=spreader ? 0 : side==0 ? rows[k] : Array.FindIndex(right.RowsMm,v=>Math.Abs(v-x)<1e-7),Xmm=x,Ymm=y,ReceivingTopMm=r.Input.Beams[beam].BottomElevationMm,H20UndersideMm=c.BottomElevationMm-200,ReactionKn=m.Analysis.Reactions[side],FootprintStartMm=lo,FootprintEndMm=hi};
                        if(Math.Abs(ledgerRows[k])>1e-10)
                        {
                            double ledgerForce=bearing.ReactionKn*ledgerRows[k]/q[0].KnPerM;bearing.ReactionKn-=ledgerForce;
                            var extra=bearing.Copy();extra.Label="Slab edge ledger";extra.ReactionKn=ledgerForce;extra.FootprintStartMm=x-40;extra.FootprintEndMm=x+40;ledgerBearings.Add(extra);
                        }
                        bearings.Add(bearing);
                        double endWidth=side==0 ? edge : farEdge;
                        if(endWidth>1e-7)
                        {
                            double start=face+(side==0 ? 0 : span-endWidth),end=start+endWidth;
                            var strip=new CrossBeamBearing {Beam=beam,Row=bearing.Row,Label="Crossing end strip",IsConcreteEndZone=true,IsShutterEndZone=true,
                                EndZoneStartMm=start,EndZoneEndMm=end,EndZoneTributaryWidthMm=hi-lo,Xmm=x,Ymm=(start+end)/2,
                                ReceivingTopMm=bearing.ReceivingTopMm,H20UndersideMm=c.BottomElevationMm,
                                FootprintStartMm=lo,FootprintEndMm=hi,ReactionKn=r.Input.AreaLoad(c.DepthMm)*(hi-lo)*endWidth/1e6};
                            endBearings.Add(strip);
                            if(Math.Abs(ledgerRows[k])>1e-10)
                            {var extra=strip.Copy();extra.Label="Slab edge ledger";extra.ReactionKn=ledgerRows[k]*endWidth/1000;extra.FootprintStartMm=x-40;extra.FootprintEndMm=x+40;endBearings.Add(extra);}
                        }
                        if(!spreader && bearing.FillerHeightMm>1e-7)solids.Add(new FormworkSolid {Name=$"X{index+1} filler B{beam+1}/T{rows[k]+1}",X0=x-40,X1=x+40,Y0=y-40,Y1=y+40,Z0=bearing.ReceivingTopMm,Z1=bearing.H20UndersideMm});
                    }
                }
                if(valid)
                {
                    var support=new CrossBeamSupportResult {CrossingIndex=index,Input=c,Arrangement=CrossBeamSupportMode.ExistingSupportsOnly,Members=members.ToArray(),Bearings=bearings.Concat(ledgerBearings).Concat(endBearings).ToArray(),SecondaryStationsMm=positions,Solids=solids.ToArray(),LoadKn=members.Sum(m=>m.Analysis.TotalLoadKn)+endBearings.Sum(b=>b.ReactionKn),SlabEdgeLoadKn=SlabEdgeLoads(r,index).Sum(p=>p.Kn)*span/1000};
                    if(spreader)
                    {
                        if(!SpreadShared(common,r,support,out failure))continue;
                        // Reserve the transfer zone; plan and section keep the existing
                        // load arrows rather than drawing or designing the spreader.
                        foreach(var group in support.Bearings.Where(b=>!b.IsConcreteEndZone).GroupBy(b=>b.Beam))
                        {
                            var b=group.First();if(b.FillerHeightMm<=1e-7)continue;
                            solids.Add(new FormworkSolid {Name=$"X{index+1} filler load transfer",X0=c.PositionMm-c.WidthMm/2,X1=c.PositionMm+c.WidthMm/2,Y0=b.Ymm-40,Y1=b.Ymm+40,Z0=b.ReceivingTopMm,Z1=b.H20UndersideMm});
                        }
                        support.Solids=solids.ToArray();
                    }
                    return support;
                }
            }
            failure=$"X{index+1}: no permitted shared H20 stock passes moment, shear, deflection{(common.IgnoreUplift ? "" : ", uplift")} and bearing/end geometry.";return null;
            }
        }
        private static CrossBeamSupportResult Dedicated(TowerInput common,TransferNetworkResult r,int index,IEnumerable<FormworkSolid> obstacles,out string failure,CrossShutterAllowance allowance=null)
        {
            var solids=obstacles.ToArray();var cache=common.OptimizeCrossingSearch && common.ReuseSearchWork ? common.CrossingGeometryWork : null;
            var key=cache==null ? null : new CrossGeometryKey(r,index,allowance??CrossShutterAllowance.Standard,solids);
            if(cache!=null && cache.Results.TryGetValue(key,out var saved))
            {
                common.Trace?.Increment(BeamSearchCounter.CrossingGeometryCacheHits);failure=saved.Failure;return saved.Result?.Copy();
            }
            var result=DedicatedUncached(common,r,index,solids,out failure,allowance);
            cache?.Results.Store(key,new DedicatedCrossSearch {Result=result?.Copy(),Failure=failure});return result;
        }
        private static CrossBeamSupportResult DedicatedUncached(TowerInput common,TransferNetworkResult r,int index,IEnumerable<FormworkSolid> obstacles,out string failure,CrossShutterAllowance allowance=null)
        {
            allowance=allowance??CrossShutterAllowance.Standard;failure=null;
            var obstacleArray=obstacles.ToArray();
            bool junctionFirst=common.Trace?.HasTimeLimit==true && r.Input.CrossingBeams[index].SupportMode!=CrossBeamSupportMode.ExistingSupportsOnly;
            if(junctionFirst)
            {
                // Junctions retain checked end-zone transfer to existing receivers.
                // Timed searches try that arrangement before the full-span tower search.
                var joint=Junction(common,r,index,obstacleArray,out string reason,allowance);
                if(joint!=null)return joint;
                failure=reason;
            }
            foreach(double spacing in r.Input.PlywoodSheetLengthMm>0 ? PlywoodSpacing.StandardSpacingsMm.Where(s=>s<=r.Input.CrossSpacing(index)) : new[] {Math.Min(r.Input.UpperSpacingMm,250),225.0,250,175,150}.Distinct().Where(s=>s<=r.Input.UpperSpacingMm+1e-7))
            {
                var result=DedicatedCandidate(common,r,index,spacing,out string reason,allowance:allowance);
                if(result!=null)reason=Clash(result.Solids,obstacleArray);
                if(result!=null && reason==null)return result;
                failure=reason;
            }
            // End zones use existing receiving H20s directly at equal soffits,
            // or the existing side shutter down to a deeper receiver. The centre
            // retains its own checked tower and all primary tails keep the shutter gap.
            if(!junctionFirst && r.Input.CrossingBeams[index].SupportMode!=CrossBeamSupportMode.ExistingSupportsOnly)
            {
                var joint=Junction(common,r,index,obstacleArray,out string reason,allowance);
                if(joint!=null)return joint;
                if(reason!=null)failure=(failure??"")+" Junction fallback: "+reason;
            }
            return null;
        }
        private static CrossBeamSupportResult DedicatedCandidate(TowerInput common,TransferNetworkResult r,int index,double spacing,out string failure,double nearTrim=0,double farTrim=0,double? width=null,double transverseOffset=0,double spanOffset=0,double nearExtension=0,double farExtension=0,CrossShutterAllowance allowance=null)
        {
            allowance=allowance??CrossShutterAllowance.Standard;
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2+nearTrim,span=r.Input.Bays[bay].WidthMm-nearTrim-farTrim;
            // Prefixes can insert earlier crossings. Include labels and all crossing-dependent
            // concrete/load coordinates rather than assuming an index identifies the same sheet.
            var cacheKey=System.FormattableString.Invariant($"{index}|{c.BayNumber}|{c.PositionMm:R}|{c.WidthMm:R}|{c.DepthMm:R}|{c.BottomElevationMm:R}|{r.Input.AreaLoad(c.DepthMm):R}|{face:R}|{span:R}|{spacing:R}|{nearTrim:R}|{farTrim:R}|{width??0:R}|{transverseOffset:R}|{spanOffset:R}|{nearExtension:R}|{farExtension:R}|{allowance.Near:R}|{allowance.Far:R}");
            cacheKey+="|"+string.Join(";",SlabEdgeLoads(r,index).Select(p=>System.FormattableString.Invariant($"{p.PositionMm:R}:{p.Kn:R}")));
            if(common.DedicatedCrossCache!=null && common.DedicatedCrossCache.TryGetValue(cacheKey,out var cached)) {failure=cached.Failure;return cached.Result;}
            failure=null;
            if(nearTrim+common.MaximumConcreteEndOverhangMm<allowance.Near || farTrim+common.MaximumConcreteEndOverhangMm<allowance.Far)
            {
                failure=$"X{index+1} dedicated: primary stock cannot keep the {allowance.Near:0.###}/{allowance.Far:0.###} mm side-shutter gaps within the entered {common.MaximumConcreteEndOverhangMm:0.###} mm concrete end allowance. Provide checked junction end-zone support.";
                if(common.DedicatedCrossCache!=null)common.DedicatedCrossCache[cacheKey]=new DedicatedCrossSearch {Failure=failure};return null;
            }
            // Dedicated transverse secondaries and primary pairs use the same checked tower geometry as main beams.
            var input=common.CopyForSearch();input.LengthMm=span;input.Network=null;input.EngineerPlan=null;
            input.DiagnosticScope=$"X{index+1} dedicated · ";
            input.Diagnostics=new TowerSearchDiagnostics(input.DeflectionDenominator,trace:input.Trace,ignoreUplift:input.IgnoreUplift) {LazyLocations=input.LazySearchDiagnostics};
            input.NetworkLegCeiling=int.MaxValue;
            input.TransferredSlab=new TransferredSlabInput {PlywoodSheetLengthMm=r.Input.PlywoodSheetLengthMm,IsNetwork=true,IncludeConcreteEndStrips=true,FixedTowerWidthMm=width,BeamCentreOffsetMm=-transverseOffset,BeamWidthMm=c.WidthMm,BeamLineLoadKnM=r.Input.AreaLoad(c.DepthMm)*c.WidthMm/1000,BeamEdgeLoadsKnM=SlabEdgeLoads(r,index),UpperSpacingMm=spacing};
            TowerLayout l;
            if(spanOffset==0 && nearExtension==0 && farExtension==0)
            {
                try {l=TransferredSlabOptimizer.Optimize(input,true);}catch(TowerOptimizationException ex) {failure=$"X{index+1} dedicated towers: {ex.Message}";if(common.DedicatedCrossCache!=null)common.DedicatedCrossCache[cacheKey]=new DedicatedCrossSearch {Failure=failure};return null;}
            }
            else
            {
                var centred=DedicatedCandidate(common,r,index,spacing,out failure,nearTrim,farTrim,width,transverseOffset,allowance:allowance);
                if(centred==null)return null;
                l=Copy(centred.Towers);l.Transfer=l.Transfer.Copy();
                l.PositionsMm=l.PositionsMm.Select(x=>x+spanOffset).ToArray();
                if(nearExtension+farExtension>0)
                {
                    var pieces=l.StockPlan.Pieces.Select((p,i)=>new TowerStockPiece {StartMm=p.StartMm-(i==0 ? nearExtension : 0),StockLengthMm=p.StockLengthMm+(i==0 ? nearExtension : 0)+(i==l.StockPlan.Pieces.Length-1 ? farExtension : 0),FirstSupport=p.FirstSupport,LastSupport=p.LastSupport}).ToArray();
                    if(pieces.Any(p=>!common.PrimaryStockLengthsMm.Contains(p.StockLengthMm) && !(common.AllowCustomPrimaryLengths && p.StockLengthMm<=50000 && p.StockLengthMm%25==0))) {failure="Projected primary length is outside the permitted beam stock/custom-length scope.";return null;}
                    foreach(var p in pieces)p.IsCustomLength=!common.PrimaryStockLengthsMm.Contains(p.StockLengthMm);
                    l.StockPlan=new TowerStockPlan {PrimaryMaterial=common.PrimaryMaterial,Pieces=pieces};
                }
                if(l.StockPlan.Pieces.Any(piece=>l.PositionsMm[piece.FirstSupport]-piece.StartMm<common.StockEndSetbackMm-1e-7 || piece.EndMm-l.PositionsMm[piece.LastSupport]<common.StockEndSetbackMm-1e-7))
                {
                    failure="Projected primary supports violate the entered stock end setback.";return null;
                }
            }
            var result=FinishDedicated(common,r,index,face,transverseOffset,spanOffset,l,input,out failure,nearExtension,farExtension,true,allowance);
            if(common.DedicatedCrossCache!=null)common.DedicatedCrossCache[cacheKey]=new DedicatedCrossSearch {Result=result,Failure=failure};return result;
        }
        private static CrossBeamSupportResult FinishDedicated(TowerInput common,TransferNetworkResult r,int index,double face,double transverseOffset,double spanOffset,TowerLayout l,TowerInput input,out string failure,double nearExtension=0,double farExtension=0,bool translate=false,CrossShutterAllowance allowance=null)
        {
            allowance=allowance??CrossShutterAllowance.Standard;failure=null;var c=r.Input.CrossingBeams[index];
            var primary=new TransferMember[l.Transfer.PrimaryCases[0].Length];
            var left=new double[l.PositionsMm.Length];var right=new double[left.Length];
            int primaryRuns=l.StockPlan.Pieces.Length;
            for(int line=0;line<2;line++)for(int run=0;run<primaryRuns;run++)
            {
                var original=l.Transfer.PrimaryCases[0][run+line*primaryRuns];
                if(translate && (spanOffset!=0 || nearExtension+farExtension>0))
                {
                    double projection=run==0 ? nearExtension : 0;
                    original=new TransferMember {PrimaryMaterial=original.PrimaryMaterial,Name=original.Name,StartMm=l.StockPlan.Pieces[run].StartMm,LengthMm=l.StockPlan.Pieces[run].StockLengthMm,H20Count=original.H20Count,PrimarySelfWeightKnM=original.PrimarySelfWeightKnM,Uniform=original.Uniform.Select(q=>new BeamDistributedLoad {StartMm=q.StartMm+projection,EndMm=q.EndMm+projection,KnPerM=q.KnPerM}).ToArray(),Points=original.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm+projection,Kn=p.Kn}).ToArray(),SupportsMm=original.SupportsMm.Select(x=>x+spanOffset+projection).ToArray()};
                }
                var uniform=original.Uniform;
                if(original.IsWS10)
                {
                    if(original.PrimarySelfWeightKnM>0)uniform=uniform.Take(uniform.Length-1).ToArray();
                    uniform=uniform.Concat(new[] {new BeamDistributedLoad {StartMm=0,EndMm=original.LengthMm,KnPerM=original.Primary.SelfWeightKnM(original.LengthMm)}}).ToArray();
                }
                var m=Analyze(original,original.Points,uniform,input,"Cross concrete + construction/formwork + end strips");
                if(!Pass(m,input,$"X{index+1} dedicated primary UDL + end strips") || m.Analysis.Reactions.Max()>common.LegCapacityKn+1e-8) {failure="Dedicated primary UDL or leg check failed.";return null;}
                m.PrimarySelfWeightKnM=m.IsWS10 ? m.Primary.SelfWeightKnM(m.LengthMm) : 0;
                primary[run+line*primaryRuns]=m;
                for(int leg=0;leg<m.Analysis.Reactions.Length;leg++)(line==0 ? left : right)[l.StockPlan.Pieces[run].FirstSupport+leg]=m.Analysis.Reactions[leg];
            }
            l.Transfer.FullLoadingOnly=common.FullLoadingOnly;l.Transfer.PrimaryCases=new[] {primary};l.LeftReactions=left;l.RightReactions=right;l.MinimumLeftReactions=(double[])left.Clone();l.MinimumRightReactions=(double[])right.Clone();l.MaxLegKn=Math.Max(left.Max(),right.Max());
            var checkedMembers=primary.Concat(l.Transfer.LowerCases.SelectMany(s=>s)).Concat(l.Transfer.UpperMembers).ToArray();
            l.MomentKnM=checkedMembers.Max(m=>m.Analysis.MomentKnM);l.ShearKn=checkedMembers.Max(m=>m.Analysis.ShearKn);l.DeflectionMm=checkedMembers.Max(m=>m.Analysis.DeflectionMm);l.Utilization=Math.Max(l.MaxLegKn/l.LegCapacityKn,checkedMembers.Max(m=>m.Utilization));l.Governing=l.MaxLegKn/l.LegCapacityKn>=checkedMembers.Max(m=>m.Utilization) ? "Leg capacity" : checkedMembers.OrderByDescending(m=>m.Utilization).First().Name;
            RefreshStockDemands(l);
            // The full concrete UDL, including end strips, is included in every primary and leg check.
            var solids=new List<FormworkSolid>();var members=new List<TransferMember>();var bearings=new List<CrossBeamBearing>();double bottom=c.BottomElevationMm;
            int runs=l.StockPlan.Pieces.Length;
            for(int run=0;run<runs;run++)for(int side=0;side<2;side++)
            {
                var m=l.Transfer.PrimaryCases[0][run+side*runs];members.Add(m);
                for(int j=0;j<m.SupportsMm.Length;j++)bearings.Add(new CrossBeamBearing {IsDedicated=true,Label=$"Dedicated P{run+1} {(side==0 ? "L" : "R")} / leg {j+1}",Beam=-1,Row=j,Xmm=c.PositionMm+transverseOffset+(side==0 ? -1 : 1)*l.WidthMm/2,Ymm=face+m.StartMm+m.SupportsMm[j],ReceivingTopMm=bottom-200-l.Primary.DepthMm,H20UndersideMm=bottom-200-l.Primary.DepthMm,ReactionKn=m.Analysis.Reactions[j]});
                solids.AddRange(PrimarySolids($"X{index+1} {m.Name}",c.PositionMm+transverseOffset+(side==0 ? -1 : 1)*l.WidthMm/2,face+m.StartMm,m.LengthMm,false,bottom-200,l.Primary,m));
            }
            for(int row=0;row<l.Transfer.RowsMm.Length;row++)
            {
                var m=l.Transfer.LowerCases[0][row];members.Add(m);
                for(int side=0;side<2;side++)bearings.Add(new CrossBeamBearing {IsDedicated=true,Label=$"Dedicated T{row+1} / primary {(side==0 ? "L" : "R")}",Beam=-1,Row=row,Xmm=c.PositionMm+transverseOffset+m.StartMm+m.SupportsMm[side],Ymm=face+l.Transfer.RowsMm[row],ReceivingTopMm=bottom-200,H20UndersideMm=bottom-200,ReactionKn=m.Analysis.Reactions[side]});
                solids.Add(Timber($"X{index+1} {m.Name}",c.PositionMm+transverseOffset+m.StartMm,face+l.Transfer.RowsMm[row],m.LengthMm,true,bottom,m));
            }
            foreach(double station in l.PositionsMm)foreach(int side in new[] {-1,1})solids.Add(new FormworkSolid {Name=$"X{index+1} tower leg",TowerOwner=$"X{index+1}",X0=c.PositionMm+transverseOffset+side*l.WidthMm/2-40,X1=c.PositionMm+transverseOffset+side*l.WidthMm/2+40,Y0=face+station-40,Y1=face+station+40,Z0=-1e6,Z1=bottom-200-l.Primary.DepthMm,IsLeg=true});
            solids.AddRange(TowerEnvelopes(l,$"X{index+1}",c.PositionMm+transverseOffset,face,false,bottom-200-l.Primary.DepthMm));
            var result=new CrossBeamSupportResult {CrossingIndex=index,Input=c,Arrangement=CrossBeamSupportMode.DedicatedTowers,Towers=l,Members=members.ToArray(),Solids=solids.ToArray(),Bearings=bearings.ToArray(),LoadKn=l.Transfer.BeamLoadKn,SlabEdgeLoadKn=SlabEdgeLoads(r,index).Sum(p=>p.Kn)*l.LengthMm/1000,TowerSpanOffsetMm=spanOffset,PrimaryNearExtensionMm=nearExtension,PrimaryFarExtensionMm=farExtension,NearShutterClearanceMm=allowance.Near,FarShutterClearanceMm=allowance.Far};
            failure=PrimaryShutterClash(r,index,result.Solids,allowance.Near,allowance.Far);
            if(failure!=null)return null;
            if(common.EngineerWorkflow && l.CantileverImbalanceMm>1e-7)result.SymmetryNotice="Unequal stock-end cantilevers retained within the selected leg/brace tier after checking permitted stock, support geometry and member limits.";
            return result;
        }
        internal static string PrimaryShutterClash(TransferNetworkResult r,int index,IEnumerable<FormworkSolid> solids,double nearAllowance=SideShutterClearanceMm,double farAllowance=SideShutterClearanceMm)
        {
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;
            double near=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2;
            double far=r.BeamCentresMm[bay+1]-r.Input.Beams[bay+1].WidthMm/2;
            foreach(var s in solids.Where(s=>s.Member?.H20Count==2))
                if(s.Y0<near+nearAllowance-1e-7 || s.Y1>far-farAllowance+1e-7)
                    return $"X{index+1}: primary H20 stock ends need at least {nearAllowance:0.###}/{farAllowance:0.###} mm near/far clearance from B{bay+1}/B{bay+2} concrete faces for side shutters (near {s.Y0-near:0.###} mm, far {far-s.Y1:0.###} mm).";
            return null;
        }
        // A short concrete centre can still fit a complete tower when unloaded primary
        // tails extend into the junction. Keep rows and UDLs on the actual concrete;
        // generating geometry from an artificially longer loaded span would add load.
        private static IEnumerable<double> TailStocks(TowerInput common,double minimum,double maximum,bool primary=false)
        {
            var lengths=(primary ? common.PrimaryStockLengthsMm : common.StockLengthsMm).Where(s=>s>=minimum-1e-7 && s<=maximum+1e-7);
            // Longer custom unloaded tails add timber and clearance demands. As with
            // transverse stock search, add only the shortest enclosing custom stock.
            double custom=Math.Ceiling(minimum/25)*25;
            if((primary ? common.AllowCustomPrimaryLengths : common.AllowCustomBeamLengths) && custom<=Math.Min(50000,maximum)+1e-7)lengths=lengths.Concat(new[] {custom});
            return lengths.Distinct().OrderBy(s=>s);
        }
        private static double[] JunctionOffsets(TowerInput common,TransferNetworkResult r,int index,double width,double offset,double near,double far,FormworkSolid[] obstacles)
        {
            var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;
            double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2+near,span=r.Input.Bays[bay].WidthMm-near-far;
            double x0=c.PositionMm+offset-width/2-40,x1=c.PositionMm+offset+width/2+40;
            var boundaries=obstacles.Where(s=>s.X0<x1 && s.X1>x0 && (s.IsLeg || s.IsTowerEnvelope || s.Name.Contains("primary"))).SelectMany(s=>new[] {s.Y0,s.Y1}).Distinct().ToArray();
            var values=new[] {0.0}.Concat(Enumerable.Range(1,20).SelectMany(i=>new[] {i*25.0,-i*25.0})).ToList();
            foreach(double towerBay in width==1524 ? TowerOptimizer.BaysMm : new[] {1524.0})
            foreach(double y in boundaries)
            foreach(double margin in new[] {40.0,common.StockEndSetbackMm})
            {
                // Column/frame clearance and primary end setbacks produce exact
                // positions; tower settings-out distances need not be multiples of 25.
                values.Add(y+margin-face-(span/2-towerBay/2));
                values.Add(y-margin-face-(span/2+towerBay/2));
            }
            return values.Where(v=>Math.Abs(v)<=500+1e-7).Distinct().ToArray();
        }
        internal static double[] PrimaryStarts(TowerInput common,double preferred,double earliest,double latest,double stock,double face,double centre,double width,double top,IEnumerable<FormworkSolid> obstacles)
        {
            if(!common.OptimizeCrossingSearch)return new[] {preferred};
            double half=common.Primary.WidthMm/2,bottom=top-common.Primary.DepthMm;
            var boundaries=obstacles.Where(o=>!o.IsTowerEnvelope && o.Z0<top-1e-7 && o.Z1>bottom+1e-7
                && new[] {-1,1}.Any(side=>o.X0<centre+side*width/2+half-1e-7 && o.X1>centre+side*width/2-half+1e-7))
                .SelectMany(o=>new[] {o.Y1-face,o.Y0-face-stock});
            // Retain the former preferred start first. A collision there does
            // not invalidate the same stock at another permitted start: exact
            // obstacle contacts and interval ends can free it without moving legs.
            return new[] {preferred,earliest,latest}.Concat(boundaries)
                .Where(s=>s>=earliest-1e-7 && s<=latest+1e-7).Select(s=>Math.Max(earliest,Math.Min(latest,s))).Distinct().ToArray();
        }
        private static CrossBeamSupportResult ProjectedJunction(TowerInput common,TransferNetworkResult r,int index,double spacing,double width,double offset,double alongSpan,double near,double far,IEnumerable<FormworkSolid> obstacles,out string failure,double? preferredBay=null,CrossShutterAllowance allowance=null,TowerInput preparedInput=null)
        {
            allowance=allowance??CrossShutterAllowance.Standard;failure=null;var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;
            double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2+near,span=r.Input.Bays[bay].WidthMm-near-far;
            bool rotated=width!=1524;
            foreach(double towerBay in (rotated ? new[] {1524.0} : TowerOptimizer.BaysMm.Where(x=>!common.Exclude600Standalone || x!=600).OrderByDescending(x=>x).ToArray()).Where(x=>!preferredBay.HasValue || x==preferredBay.Value))
            {
                common.Trace?.CheckCancellation();
                double[] positions={span/2+alongSpan-towerBay/2,span/2+alongSpan+towerBay/2};
                var footprint=new TowerLayout {PositionsMm=positions,Joined=new[] {true},WidthMm=width};
                var footprintClash=Clash(TowerEnvelopes(footprint,$"X{index+1}",c.PositionMm+offset,face,false,c.BottomElevationMm-200-common.Primary.DepthMm),obstacles);if(footprintClash!=null){failure=footprintClash;continue;}
                // The final result would reject these exact legs against the same
                // fixed obstacles. Reject that impossible footprint before stock,
                // row, member and result construction; retain the final full clash check.
                if(common.ReuseSearchWork && common.OptimizeCrossingJunctions && common.PrecheckCrossingFootprints)
                {
                    var legClash=Clash(ProjectedTowerLegs(footprint,$"X{index+1}",c.PositionMm+offset,face,c.BottomElevationMm-200-common.Primary.DepthMm),obstacles);
                    if(legClash!=null){failure=legClash;common.Trace?.Increment(BeamSearchCounter.CrossingLegClashesRejected);continue;}
                }
                double lo=Math.Min(common.MaximumConcreteEndOverhangMm,positions[0]-common.StockEndSetbackMm),hi=Math.Max(span-common.MaximumConcreteEndOverhangMm,positions[1]+common.StockEndSetbackMm);
                foreach(double stock in TailStocks(common,hi-lo,span+near+far-allowance.Near-allowance.Far,true).OrderBy(s=>common.EngineerWorkflow ? Math.Abs(positions[0]+positions[1]-2*Math.Max(Math.Max(hi-s,allowance.Near-near),Math.Min(Math.Min(lo,span+far-allowance.Far-s),(positions[0]+positions[1]-s)/2))-s) : 0).ThenBy(s=>s))
                {
                    double earliest=Math.Max(hi-stock,allowance.Near-near),latest=Math.Min(lo,span+far-allowance.Far-stock);
                    if(earliest>latest+1e-7)continue;
                    double preferred=Math.Max(earliest,Math.Min(latest,common.EngineerWorkflow ? (positions[0]+positions[1]-stock)/2 : (span-stock)/2));
                    foreach(double start in PrimaryStarts(common,preferred,earliest,latest,stock,face,c.PositionMm+offset,width,c.BottomElevationMm-200,obstacles))
                    {
                    double edge=Math.Max(0,Math.Max(start,span-start-stock));
                    if(edge>common.MaximumConcreteEndOverhangMm+1e-7 || 2*edge>=span)continue;
                    var l=new TowerLayout {EngineerSearch=common.EngineerSearch,PrimaryMaterial=common.PrimaryMaterial,IgnoreUplift=common.IgnoreUplift,LengthMm=span,ConcreteEndOverhangMm=edge,WidthMm=width,Rotated=rotated,PositionsMm=positions,Joined=new[] {true},LegCapacityKn=common.LegCapacityKn,DeflectionDenominator=common.DeflectionDenominator,DeflectionLimitMm=common.DeflectionLimitMm,StockPlan=new TowerStockPlan {PrimaryMaterial=common.PrimaryMaterial,Pieces=new[] {new TowerStockPiece {StartMm=start,StockLengthMm=stock,IsCustomLength=!common.PrimaryStockLengthsMm.Contains(stock),FirstSupport=0,LastSupport=1}}}};
                    var geometry=TowerEnvelopes(l,$"X{index+1}",c.PositionMm+offset,face,false,c.BottomElevationMm-200-l.Primary.DepthMm).ToList();
                    foreach(int side in new[] {-1,1})geometry.AddRange(PrimarySolids($"X{index+1} projected primary",c.PositionMm+offset+side*width/2,face+start,stock,false,c.BottomElevationMm-200,common.Primary));
                    var primaryClash=Clash(geometry,obstacles);if(primaryClash!=null){failure=primaryClash;continue;}
                    double lowerLo=Math.Min(-width/2,-offset-c.WidthMm/2)-150,lowerHi=Math.Max(width/2,-offset+c.WidthMm/2)+150;
                    foreach(double lowerStock in TailStocks(common,2*Math.Max(-offset-lowerLo,lowerHi+offset),50000))
                    {
                        double lowerStart=-offset-lowerStock/2;
                        double covered=span-2*edge;
                        // Leave the full 80 mm flange clear of the receiving timber
                        // or shutter at each junction while respecting maximum spacing.
                        int count=Math.Max(2,(int)Math.Ceiling((covered-H20WidthMm)/spacing)+1);double actualSpacing=(covered-H20WidthMm)/(count-1);
                        var rows=r.Input.PlywoodSheetLengthMm>0 ? PlywoodSpacing.SheetSupportRows(edge,span-edge,spacing,r.Input.PlywoodSheetLengthMm) : Enumerable.Range(0,count).Select(i=>(span-(count-1)*actualSpacing)/2+i*actualSpacing).ToArray();
                        if(rows.Any(y=>y<start-1e-7 || y>=start+stock-1e-7))continue;
                        var secondarySolids=rows.Select(y=>Timber($"X{index+1} junction secondary",c.PositionMm+offset+lowerStart,face+y,lowerStock,true,c.BottomElevationMm)).ToArray();
                        var secondaryClash=Clash(secondarySolids,obstacles);if(secondaryClash!=null){failure=secondaryClash;continue;}
                        var tributaries=EndZoneWidths(0,span,rows);double q=r.Input.AreaLoad(c.DepthMm)*c.WidthMm/1000+SlabEdgeLoads(r,index).Sum(p=>p.Kn);
                        var transverse=new TransferMember {H20Count=1,Name="Junction secondary",StartMm=lowerStart,LengthMm=lowerStock,SupportsMm=new[] {-width/2-lowerStart,width/2-lowerStart},Points=SlabEdgeLoads(r,index).Select(p=>new BeamPointLoad {PositionMm=p.PositionMm-offset-lowerStart,Kn=p.Kn}).ToArray(),Uniform=new[] {new BeamDistributedLoad {StartMm=-offset-c.WidthMm/2-lowerStart,EndMm=-offset+c.WidthMm/2-lowerStart,KnPerM=r.Input.AreaLoad(c.DepthMm)}}};
                        var input=preparedInput??common.CopyForSearch();input.Network=null;input.EngineerPlan=null;
                        var unit=Analyze(transverse,transverse.Points,transverse.Uniform,input,"Cross concrete");
                        bool reuse=common.ReuseSearchWork && common.OptimizeCrossingJunctions,passing=true;
                        TransferMember[] lower;
                        if(!reuse)
                        {
                            // The benchmark retains the original allocation/traversal path.
                            lower=rows.Select((y,i)=>{double design=Math.Max(spacing,tributaries[i])/1000;return new TransferMember {H20Count=1,Name=$"T{i+1}",LoadCase="Cross concrete + construction/formwork",StartMm=lowerStart,LengthMm=lowerStock,SupportsMm=unit.SupportsMm,Points=unit.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*design}).ToArray(),Uniform=unit.Uniform.Select(u=>new BeamDistributedLoad {StartMm=u.StartMm,EndMm=u.EndMm,KnPerM=u.KnPerM*design}).ToArray(),Analysis=unit.Analysis.Scale(design)};}).ToArray();
                            passing=!lower.Any(m=>!Pass(m,input,$"X{index+1} junction secondary"));
                        }
                        else
                        {
                            lower=new TransferMember[rows.Length];double previousScale=double.NaN;LoadedBeamAnalysis scaled=null;
                            for(int row=0;row<rows.Length;row++)
                            {
                                double design=Math.Max(spacing,tributaries[row])/1000;
                                // Equal tributaries share the same immutable scaled response.
                                // A changed factor still calls the original exact Scale routine.
                                if(design!=previousScale) {scaled=unit.Analysis.Scale(design);previousScale=design;}
                                lower[row]=new TransferMember {H20Count=1,Name=$"T{row+1}",LoadCase="Cross concrete + construction/formwork",StartMm=lowerStart,LengthMm=lowerStock,SupportsMm=unit.SupportsMm,Points=unit.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*design}).ToArray(),Uniform=unit.Uniform.Select(u=>new BeamDistributedLoad {StartMm=u.StartMm,EndMm=u.EndMm,KnPerM=u.KnPerM*design}).ToArray(),Analysis=scaled};
                                // Preserve check order while avoiding allocation of later rows
                                // after the same first failed check as the reference traversal.
                                if(!Pass(lower[row],input,$"X{index+1} junction secondary")) {passing=false;break;}
                            }
                        }
                        if(preferredBay.HasValue && !(common.ReuseSearchWork && common.OptimizeCrossingJunctions && common.PrecheckCrossingFootprints))
                        {
                            // The late/reference path has now evaluated receiving members.
                            // A clash of these fixed legs cannot be repaired by another stock
                            // or secondary pitch. Avoid repeating member work at the same pose;
                            // the early path rejects it before any member evaluation.
                            string legClash=Clash(ProjectedTowerLegs(footprint,$"X{index+1}",c.PositionMm+offset,face,c.BottomElevationMm-200-common.Primary.DepthMm),obstacles);
                            if(legClash!=null){failure=legClash;return null;}
                        }
                        if(!passing)continue;
                        // As in the existing end-strip model, distribute the full load on
                        // centred covered concrete, conserving force and its exact centroid.
                        var primary=Enumerable.Range(0,2).Select(line=>new TransferMember {PrimaryMaterial=common.PrimaryMaterial,Name=$"P1 {(line==0 ? "L" : "R")}",H20Count=2,StartMm=start,LengthMm=stock,SupportsMm=positions.Select(x=>x-start).ToArray(),Points=Array.Empty<BeamPointLoad>(),Uniform=new[] {new BeamDistributedLoad {StartMm=edge-start,EndMm=span-edge-start,KnPerM=unit.Analysis.Reactions[line]*span/(span-2*edge)}}}).ToArray();
                        l.Transfer=new TransferredSlabResult {RowsMm=rows,TributaryWidthsMm=tributaries,EnteredUpperSpacingMm=spacing,LowerStockStartMm=lowerStart,LowerStockLengthMm=lowerStock,LowerCustom=!common.StockLengthsMm.Contains(lowerStock),PrimaryCases=new[] {primary},LowerCases=new[] {lower},UpperMembers=new TransferMember[0],CaseNames=new[] {"Cross concrete + construction/formwork"},BeamLoadKn=q*span/1000};
                        var result=FinishDedicated(common,r,index,face,offset,alongSpan,l,input,out failure,Math.Max(0,-start),Math.Max(0,start+stock-span),allowance:allowance);
                        if(result!=null && Clash(result.Solids,obstacles)==null)return result;
                    }
                    }
                }
            }
            return null;
        }
        private static CrossBeamSupportResult Junction(TowerInput common,TransferNetworkResult r,int index,FormworkSolid[] obstacles,out string failure,CrossShutterAllowance allowance=null)
        {
            allowance=allowance??CrossShutterAllowance.Standard;failure=null;var c=r.Input.CrossingBeams[index];int bay=c.BayNumber-1;
            double face=r.BeamCentresMm[bay]+r.Input.Beams[bay].WidthMm/2,span=r.Input.Bays[bay].WidthMm;
            var zones=new List<CrossBeamBearing>();double near=0,far=0;
            for(int side=0;side<2;side++)
            {
                int beam=bay+side;var b=r.Input.Beams[beam];var t=r.Beams[beam].Transfer;
                if(b.BottomElevationMm>c.BottomElevationMm+1e-7)continue;
                bool shutter=b.BottomElevationMm<c.BottomElevationMm-1e-7;
                double end=r.BeamCentresMm[beam]+t.LowerStockStartMm+(side==0 ? t.LowerStockLengthMm : 0);
                double trim=shutter ? (side==0 ? allowance.Near : allowance.Far) : side==0 ? end-face : face+span-end;
                if(trim<=0 || trim>=span)continue;
                if(side==0)near=trim;else far=trim;
                // These bearers already continue under the main beam. Their flanges may
                // straddle the cross footprint edge; only the actual concrete tributary is added.
                var rows=Enumerable.Range(0,t.RowsMm.Length).Where(row=>Math.Abs(t.RowsMm[row]-c.PositionMm)<=c.WidthMm/2+1e-7).ToArray();
                if(rows.Length<2) {failure=$"X{index+1}: its concrete end zone needs at least two receiving H20 rows for the sheathing transfer.";return null;}
                double lo=c.PositionMm-c.WidthMm/2,hi=c.PositionMm+c.WidthMm/2;
                var effective=EndZoneWidths(lo,hi,rows.Select(row=>t.RowsMm[row]).ToArray());
                var ledgerRows=LedgerRowLineLoads(r,index,rows.Select(row=>t.RowsMm[row]).ToArray());
                for(int k=0;k<rows.Length;k++)
                {
                    if(k==0 && t.RowsMm[rows[k]]-lo>r.Input.CrossSpacing(index)+1e-7 || k==rows.Length-1 && hi-t.RowsMm[rows[k]]>r.Input.CrossSpacing(index)+1e-7 || k>0 && t.RowsMm[rows[k]]-t.RowsMm[rows[k-1]]>r.Input.CrossSpacing(index)+1e-7) {failure=$"X{index+1}: an end-zone sheathing strip exceeds the entered secondary spacing.";return null;}
                    double a=side==0 ? face : face+span-trim,z=a+trim;
                    double bearing=(a+z)/2,local=bearing-r.BeamCentresMm[beam];
                    if(shutter && (local<t.LowerStockStartMm+40-1e-7 || local>t.LowerStockStartMm+t.LowerStockLengthMm-40+1e-7)) {failure=$"X{index+1}: the side-shutter bearing lies outside B{beam+1}'s receiving H20 stock.";return null;}
                    zones.Add(new CrossBeamBearing {Beam=beam,Row=rows[k],IsConcreteEndZone=true,IsShutterEndZone=shutter,EndZoneStartMm=a,EndZoneEndMm=z,EndZoneTributaryWidthMm=effective[k],Xmm=t.RowsMm[rows[k]],Ymm=bearing,ReceivingTopMm=b.BottomElevationMm,H20UndersideMm=c.BottomElevationMm,FootprintStartMm=lo,FootprintEndMm=hi,ReactionKn=r.Input.AreaLoad(c.DepthMm)*effective[k]*(z-a)/1e6});
                    if(Math.Abs(ledgerRows[k])>1e-10)
                    {
                        var extra=zones.Last().Copy();extra.Label="Slab edge ledger";extra.ReactionKn=ledgerRows[k]*(z-a)/1000;extra.FootprintStartMm=extra.Xmm-40;extra.FootprintEndMm=extra.Xmm+40;zones.Add(extra);
                    }
                }
            }
            if(near+common.MaximumConcreteEndOverhangMm<allowance.Near || far+common.MaximumConcreteEndOverhangMm<allowance.Far || near+far>=span-2*common.StockEndSetbackMm)return null;
            var shutters=zones.Where(z=>z.IsShutterEndZone).GroupBy(z=>z.Beam).Select(g=>g.First()).Select(z=>new FormworkSolid {Name=$"X{index+1} side shutter on B{z.Beam+1}",IsSideShutter=true,X0=z.FootprintStartMm,X1=z.FootprintEndMm,Y0=z.EndZoneStartMm,Y1=z.EndZoneEndMm,Z0=z.ReceivingTopMm,Z1=z.H20UndersideMm}).ToArray();
            if(Clash(shutters,obstacles)!=null){failure=$"X{index+1}: side-shutter assembly clashes with existing formwork.";return null;}
            obstacles=obstacles.Concat(shutters).ToArray();
            IEnumerable<FormworkSolid> collisions=common.ReuseSearchWork && common.OptimizeCrossingJunctions ? new FormworkObstacleIndex(obstacles) : (IEnumerable<FormworkSolid>)obstacles;
            TowerInput preparedInput=null;
            if(common.ReuseSearchWork && common.OptimizeCrossingJunctions) {preparedInput=common.CopyForSearch();preparedInput.Network=null;preparedInput.EngineerPlan=null;}
            var spacings=(r.Input.PlywoodSheetLengthMm>0 ? PlywoodSpacing.StandardSpacingsMm.Where(s=>s<=r.Input.CrossSpacing(index)) : new[] {Math.Min(r.Input.UpperSpacingMm,250),225.0,175,150}.Distinct().Where(s=>s<=r.Input.UpperSpacingMm+1e-7)).ToArray();
            var widths=new[] {1000.0,1500,2000,1524,600};
            var offsets=new[] {0.0,25,-25,50,-50,75,-75,100,-100,125,-125,150,-150};
            string projectedReason=null;
            if(common.EngineerWorkflow)
            {
                var symmetric=SymmetricJunction(common,r,index,spacings,widths,offsets,near,far,obstacles,collisions,zones,shutters,preparedInput,allowance,out string symmetricFailure);
                if(symmetric!=null)return symmetric;
                if(symmetricFailure!=null)failure=projectedReason=symmetricFailure;
            }
            foreach(double spacing in spacings)
            foreach(double width in widths)
            {
                if(width==600 && common.Exclude600Standalone)continue;
                // Compare transverse placements at each longitudinal shift before
                // accepting uneven cantilevers at a fixed transverse position.
                var shifts=offsets.SelectMany(offset=>JunctionOffsets(common,r,index,width,offset,near,far,obstacles)).Distinct().OrderBy(Math.Abs).ToArray();
                if(!common.EngineerWorkflow)foreach(double preferredBay in (width==1524 ? TowerOptimizer.BaysMm.Where(x=>!common.Exclude600Standalone || x!=600).OrderByDescending(x=>x).ToArray() : new[] {1524.0})
                    .Where(towerBay=>TailStocks(common,Math.Max(towerBay+2*common.StockEndSetbackMm,span-near-far-2*common.MaximumConcreteEndOverhangMm),span-allowance.Near-allowance.Far,true).Any()))
                foreach(double shift in shifts)
                foreach(double offset in offsets)
                {
                        var projected=ProjectedJunction(common,r,index,spacing,width,offset,shift,near,far,collisions,out string projectedFailure,preferredBay,allowance,preparedInput);
                        if(projected==null) {if(projectedFailure!=null)failure=projectedFailure;continue;}
                        return JunctionResult(c,index,projected,zones,shutters,near,far,span,offset,shift);
                    }
                    foreach(double offset in offsets)
                    {
                    common.Trace?.CheckCancellation();
                    var centred=DedicatedCandidate(common,r,index,spacing,out string reason,near,far,width,offset,allowance:allowance);
                    if(centred==null) {failure=reason;continue;}
                    // Sliding the legs cannot repair a timber intersection. Discard that
                    // geometry before reanalysing any support translations.
                    reason=Clash(centred.Solids.Where(s=>!s.IsLeg && !s.IsTowerEnvelope),collisions);
                    if(reason!=null) {failure=reason;continue;}
                    foreach(double alongSpan in new[] {0.0}.Concat(Enumerable.Range(1,20).SelectMany(i=>new[] {i*25.0,-i*25.0})))
                    {
                        var shiftedLegs=centred.Solids.Where(s=>s.IsLeg || s.IsTowerEnvelope).Select(s=>new FormworkSolid {Name=s.Name,IsLeg=s.IsLeg,IsTowerEnvelope=s.IsTowerEnvelope,TowerOwner=s.TowerOwner,X0=s.X0,X1=s.X1,Y0=s.Y0+alongSpan,Y1=s.Y1+alongSpan,Z0=s.Z0,Z1=s.Z1});
                        reason=Clash(shiftedLegs,collisions);
                        if(reason!=null) {failure=reason;continue;}
                        // Coordinate support translation with the real primary stock ends.
                        // Only unloaded primary tails project into the junction; coplanar
                        // secondary rows and every physical load stay in their original place.
                        var first=centred.Towers.StockPlan.Pieces.First();var last=centred.Towers.StockPlan.Pieces.Last();
                        double nearExtension=Math.Max(0,25*Math.Ceiling((common.StockEndSetbackMm-(centred.Towers.PositionsMm[first.FirstSupport]+alongSpan-first.StartMm))/25));
                        double farExtension=Math.Max(0,25*Math.Ceiling((common.StockEndSetbackMm-(last.EndMm-centred.Towers.PositionsMm[last.LastSupport]-alongSpan))/25));
                        if(nearExtension>near+1e-7 || farExtension>far+1e-7)continue;
                        var result=alongSpan==0 ? centred : DedicatedCandidate(common,r,index,spacing,out reason,near,far,width,offset,alongSpan,nearExtension,farExtension,allowance);
                        if(result!=null)reason=Clash(result.Solids,collisions);
                        if(result==null || reason!=null) {failure=reason;continue;}
                        return JunctionResult(c,index,result,zones,shutters,near,far,span,offset,alongSpan);
                    }
                }
            }
            if(projectedReason!=null)failure=(failure??"")+" Projected tower comparison: "+projectedReason;
            return null;
        }
        private sealed class CrossPlacement
        {
            internal double Width,Offset,Shift,Bay,Brace,Symmetry;
        }
        private static double ProjectedSymmetry(TowerInput common,double span,double near,double far,double bay,double shift,CrossShutterAllowance allowance)
        {
            double first=span/2+shift-bay/2,last=first+bay;
            double lo=Math.Min(common.MaximumConcreteEndOverhangMm,first-common.StockEndSetbackMm),hi=Math.Max(span-common.MaximumConcreteEndOverhangMm,last+common.StockEndSetbackMm);
            double best=double.PositiveInfinity;
            foreach(double stock in TailStocks(common,hi-lo,span+near+far-allowance.Near-allowance.Far,true))
            {
                double earliest=Math.Max(hi-stock,allowance.Near-near),latest=Math.Min(lo,span+far-allowance.Far-stock);
                if(earliest>latest+1e-7)continue;
                double start=Math.Max(earliest,Math.Min(latest,(first+last-stock)/2));
                double edge=Math.Max(0,Math.Max(start,span-start-stock));
                if(edge>common.MaximumConcreteEndOverhangMm+1e-7 || 2*edge>=span)continue;
                best=Math.Min(best,Math.Abs(first+last-2*start-stock)+2*Math.Abs(shift));
            }
            return best;
        }
        private static CrossBeamSupportResult SymmetricJunction(TowerInput common,TransferNetworkResult r,int index,double[] spacings,double[] widths,double[] offsets,double near,double far,FormworkSolid[] obstacles,IEnumerable<FormworkSolid> collisions,List<CrossBeamBearing> zones,FormworkSolid[] shutters,TowerInput prepared,CrossShutterAllowance allowance,out string failure)
        {
            failure=null;double span=r.Input.Bays[r.Input.CrossingBeams[index].BayNumber-1].WidthMm,central=span-near-far;
            var placements=new List<CrossPlacement>();
            foreach(double width in widths.Where(w=>w!=600 || !common.Exclude600Standalone))
            {
                var shifts=offsets.SelectMany(offset=>JunctionOffsets(common,r,index,width,offset,near,far,obstacles)).Distinct().ToArray();
                foreach(double bay in width==1524 ? TowerOptimizer.BaysMm : new[] {1524.0})
                {
                    if(width==1524 && bay==600 && common.Exclude600Standalone)continue;
                    double brace=width==1524 ? bay : width,penalty=Math.Abs(brace-1500)<1e-7 ? 0 : Math.Abs(brace-1000)<1e-7 ? 500 : Math.Abs(brace-2000)<1e-7 ? 1500 : 2000;
                    foreach(double shift in shifts)
                    {
                        double imbalance=ProjectedSymmetry(common,central,near,far,bay,shift,allowance);if(double.IsInfinity(imbalance))continue;
                        foreach(double offset in offsets)placements.Add(new CrossPlacement {Width=width,Bay=bay,Shift=shift,Offset=offset,Brace=penalty,Symmetry=imbalance+2*Math.Abs(offset)});
                    }
                }
            }
            var ordered=placements.OrderBy(p=>p.Brace).ThenBy(p=>p.Symmetry).ThenBy(p=>Math.Abs(p.Offset)).ThenBy(p=>Math.Abs(p.Shift)).ToArray();
            CrossBeamSupportResult best=null;double bestBrace=double.PositiveInfinity,bestSymmetry=double.PositiveInfinity;string balancedFailure=null;
            foreach(var placement in ordered)
            foreach(double spacing in spacings)
            {
                common.Trace?.CheckCancellation();
                // Placements are ordered by their exact geometric lower bound.
                // A passing layout protects equal-score ties; only a strictly more
                // balanced remaining placement can improve this bounded candidate.
                // This also avoids repeating footprint rejections at every pitch.
                if(best!=null && (placement.Brace>bestBrace || placement.Brace==bestBrace && placement.Symmetry>=bestSymmetry-1e-7))continue;
                var result=ProjectedJunction(common,r,index,spacing,placement.Width,placement.Offset,placement.Shift,near,far,collisions,out string reason,placement.Bay,allowance,prepared);
                if(result==null)
                {
                    if(reason!=null){failure=reason;if(balancedFailure==null)balancedFailure=reason;}
                    // These exact footprints do not depend on secondary spacing.
                    if(reason!=null && reason.StartsWith("Physical overlap:",StringComparison.Ordinal))break;
                    continue;
                }
                double symmetry=result.Towers.SymmetryErrorMm;
                if(best!=null && placement.Brace==bestBrace && (symmetry>bestSymmetry+1e-7 || Math.Abs(symmetry-bestSymmetry)<=1e-7 && result.TimberMm>=best.TimberMm-1e-7))continue;
                best=JunctionResult(r.Input.CrossingBeams[index],index,result,zones,shutters,near,far,span,placement.Offset,placement.Shift);bestBrace=placement.Brace;bestSymmetry=symmetry;
                break; // Coarser passing pitch already uses the fewest rows at this placement.
            }
            if(best!=null && bestSymmetry>1e-7)best.SymmetryNotice="Offset support arrangement retained after comparing more balanced permitted arrangements. "+(balancedFailure??"More balanced stock/support positions failed the available-length, clearance or member checks.");
            return best;
        }
        private static CrossBeamSupportResult JunctionResult(TransferCrossingBeamRow c,int index,CrossBeamSupportResult central,List<CrossBeamBearing> zones,FormworkSolid[] shutters,double near,double far,double span,double offset,double shift)
            =>new CrossBeamSupportResult {CrossingIndex=index,Input=c,Arrangement=CrossBeamSupportMode.DedicatedTowers,Towers=central.Towers,Members=central.Members,Solids=central.Solids.Concat(shutters).ToArray(),Bearings=central.Bearings.Concat(zones).ToArray(),LoadKn=central.LoadKn+zones.Sum(b=>b.ReactionKn),SlabEdgeLoadKn=central.SlabEdgeLoadKn*(span/(span-near-far)),SymmetryNotice=central.SymmetryNotice,TowerSpanOffsetMm=shift,PrimaryNearExtensionMm=central.PrimaryNearExtensionMm,PrimaryFarExtensionMm=central.PrimaryFarExtensionMm,NearShutterClearanceMm=central.NearShutterClearanceMm,FarShutterClearanceMm=central.FarShutterClearanceMm,JunctionNotice=$"Dedicated central span {span-near-far:0.###} mm; near end zone {near:0.###} mm, far end zone {far:0.###} mm carried by existing receiving H20s"+(shutters.Length==0 ? "." : " through the existing side shutters where soffits differ. Each shutter strip reacts at its midpoint outside the concrete face, using the selected local allowance; shutter/connection strength requires separate design.")+$" Every end-zone load is included in receiving H20, primary and individual leg checks. Tower centre offset {offset:0.###} mm along the main beam, leg offset {shift:0.###} mm along the crossing span; eccentric concrete loads are included. Crossing primary stock ends keep at least {central.NearShutterClearanceMm:0.###}/{central.FarShutterClearanceMm:0.###} mm near/far clearance from parallel concrete faces for side shutters. Complete independent tower footprints remain separate."};
        // Exact simply supported strip reactions, including signed cantilever-end influence.
        // Closing tributary rectangles alone conserve force but shift the physical load centroid.
        internal static double[] EndZoneWidths(double start,double end,double[] rows)
        {
            if(rows==null || rows.Length<2 || rows[0]<start || rows.Last()>end || rows.Skip(1).Where((x,i)=>x<=rows[i]).Any())throw new ArgumentException("Invalid concrete end-zone rows.");
            var widths=new double[rows.Length];
            for(int i=1;i<rows.Length;i++) {double half=(rows[i]-rows[i-1])/2;widths[i-1]+=half;widths[i]+=half;}
            double edge=rows[0]-start,influence=edge*edge/(2*(rows[1]-rows[0]));widths[0]+=edge+influence;widths[1]-=influence;
            int last=rows.Length-1;edge=end-rows[last];influence=edge*edge/(2*(rows[last]-rows[last-1]));widths[last]+=edge+influence;widths[last-1]-=influence;
            return widths;
        }
        // Do not mutate the DP's shared tower layouts while evaluating a cross arrangement.
        private static TowerLayout Copy(TowerLayout l)
        {
            return new TowerLayout {EngineerSearch=l.EngineerSearch,SearchAssumedLegKn=l.SearchAssumedLegKn,SearchInitialLegsPerLine=l.SearchInitialLegsPerLine,PrimaryMaterial=l.PrimaryMaterial,IgnoreUplift=l.IgnoreUplift,MinimumLeftReactions=l.MinimumLeftReactions,MinimumRightReactions=l.MinimumRightReactions,Transfer=l.Transfer,PositionsMm=l.PositionsMm,Joined=l.Joined,WidthMm=l.WidthMm,TransverseOffsetMm=l.TransverseOffsetMm,Rotated=l.Rotated,LengthMm=l.LengthMm,ConcreteEndOverhangMm=l.ConcreteEndOverhangMm,LeftReactions=l.LeftReactions,RightReactions=l.RightReactions,LeftLineLoad=l.LeftLineLoad,RightLineLoad=l.RightLineLoad,LegCapacityKn=l.LegCapacityKn,MaxLegKn=l.MaxLegKn,StockPlan=l.StockPlan,DeflectionDenominator=l.DeflectionDenominator,DeflectionLimitMm=l.DeflectionLimitMm,MomentKnM=l.MomentKnM,ShearKn=l.ShearKn,DeflectionMm=l.DeflectionMm,Utilization=l.Utilization,Governing=l.Governing,CentringNotice=l.CentringNotice,Tested=l.Tested,MinimumLegCountProven=l.MinimumLegCountProven,SearchTimeLimitReached=l.SearchTimeLimitReached};
        }
        internal static bool Complete(TowerInput common,TransferNetworkResult r,bool partial=false)
        {
            if(!Modern(r.Input))return true;
            var original=r.Beams;var existing=NetworkSolids(r).ToArray();var concrete=Concrete(r).ToArray();
            string baselineClash=Clash(existing,concrete);
            if(baselineClash!=null)
            {
                string elevations=baselineClash.Contains("cross concrete") ? " Concrete levels: "+string.Join("; ",r.Input.Beams.Select((b,i)=>$"B{i+1} bottom {b.BottomElevationMm:0.###}, top {b.BottomElevationMm+b.DepthMm:0.###} mm").Concat(r.Input.CrossingBeams.Select((c,i)=>$"X{i+1} bottom {c.BottomElevationMm:0.###}, top {c.BottomElevationMm+c.DepthMm:0.###} mm")))+". Review soffits against the intended concrete top levels." : "";
                Reject(common,baselineClash+elevations);return false;
            }
            bool coordinateRows=common.EngineerWorkflow && common.OptimizeCrossingSearch;
            // Lower receiving rows may be set out independently of upper slab
            // feet under the existing bearer assumption. Reserve fixed timber,
            // frames and concrete now; coordinate all receiving rows together
            // before accepting the complete combination, using the same checked
            // setting-out routine as the final centring pass.
            var fixedExisting=coordinateRows ? existing.Where(s=>!s.Name.StartsWith("B") || !s.Name.Contains("/T")).ToArray() : existing;
            var accepted=new List<CrossBeamSupportResult>();
            // Check the mandatory/narrow junctions before flexible shared ones.
            // This changes traversal only; every accepted complete combination is
            // rechecked in original crossing order with all receiver loads present.
            var crossingOrder=Enumerable.Range(0,r.Input.CrossingBeams.Length).ToArray();
            if(common.EngineerWorkflow && common.OptimizeCrossingSearch)
                crossingOrder=crossingOrder.OrderBy(i=>
                {
                    var cross=r.Input.CrossingBeams[i];int bay=cross.BayNumber-1;
                    return cross.SupportMode==CrossBeamSupportMode.DedicatedTowers || cross.SupportMode==CrossBeamSupportMode.Automatic &&
                        (!ValidFillerGap(cross.BottomElevationMm,r.Input.Beams[bay].BottomElevationMm,out var near) || !ValidFillerGap(cross.BottomElevationMm,r.Input.Beams[bay+1].BottomElevationMm,out var far)) ? 0 : 1;
                }).ThenBy(i=>r.Input.Bays[r.Input.CrossingBeams[i].BayNumber-1].WidthMm).ThenBy(i=>i).ToArray();
            Func<int,bool> arrange=null;
            arrange=at=>
            {
                common.Trace?.CheckCancellation();
                if(at==crossingOrder.Length)
                {
                    var supports=accepted.OrderBy(s=>s.CrossingIndex).ToArray();
                    if(!Recheck(common,r,original,supports,pendingAssignments:partial))return false;
                    if(!coordinateRows)return true;
                    r.CrossSupports=supports;string reason=null;
                    if(!TryAdjustSecondaries(r,supports.SelectMany(s=>s.Solids).ToArray(),new FormworkSolid[0],out var adjusted,true,message=>reason=message))
                    {Reject(common,"Crossing receiving-row setting-out: "+(reason??"no clear local row arrangement"));return false;}
                    r.Beams=adjusted;return true;
                }
                int index=crossingOrder[at];
                common.Trace?.Work("Cross-beam support arrangements");
                var c=r.Input.CrossingBeams[index];if(c.SupportMode==CrossBeamSupportMode.LegacySlabRows)return arrange(at+1);
                string sharedFailure=null,towerFailure=null;
                if(c.SupportMode!=CrossBeamSupportMode.DedicatedTowers)
                {
                    var onPrimary=PrimaryBearing(common,r,index,out string primaryFailure);
                    if(onPrimary!=null)
                    {
                        primaryFailure=Clash(onPrimary.Solids,fixedExisting.Concat(concrete).Concat(accepted.SelectMany(a=>a.Solids)));
                        if(primaryFailure==null && !Recheck(common,r,original,accepted.Concat(new[] {onPrimary}).ToArray(),lowerOnly:true))primaryFailure="Primary-bearing end-strip receiver checks fail.";
                        if(primaryFailure==null)
                        {
                            accepted.Add(onPrimary);if(arrange(at+1))return true;accepted.RemoveAt(accepted.Count-1);
                            primaryFailure="Primary-bearing combination fails a later crossing, primary, leg or setting-out check.";
                        }
                    }
                    r.Beams=original;
                    var support=Shared(common,r,index,out sharedFailure);
                    sharedFailure="Primary bearing: "+primaryFailure+" Stacked bearing: "+sharedFailure;
                    if(support!=null)
                    {
                        sharedFailure=Clash(support.Solids,fixedExisting.Concat(concrete).Concat(accepted.SelectMany(a=>a.Solids)));
                        if(sharedFailure==null && !Recheck(common,r,original,accepted.Concat(new[] {support}).ToArray(),lowerOnly:true))sharedFailure=$"X{index+1}: combined receiving-secondary checks fail.";
                        if(sharedFailure==null)
                        {
                            accepted.Add(support);if(arrange(at+1))return true;accepted.RemoveAt(accepted.Count-1);
                            sharedFailure=$"X{index+1}: this shared arrangement does not permit a complete passing cross-beam combination.";
                        }
                    }
                }
                if(c.SupportMode!=CrossBeamSupportMode.ExistingSupportsOnly)
                foreach(var allowance in common.AllowLocalShutterExceptions ? CrossShutterAllowance.Candidates : CrossShutterAllowance.StandardOnly)
                {
                    // A reduced allowance repairs a junction that cannot fit locally.
                    // Keep an already fitting standard detail when a later crossing fails;
                    // network pattern search handles that failure without multiplying
                    // unrelated allowance combinations across the entire network.
                    var support=Dedicated(common,r,index,fixedExisting.Concat(concrete).Concat(accepted.SelectMany(a=>a.Solids)),out string detail,allowance);
                    if(detail!=null)towerFailure=(towerFailure??"")+ $" X{index+1} allowance near {allowance.Near:0} mm / far {allowance.Far:0} mm: "+detail;
                    if(support!=null)
                    {
                        support=new CrossBeamSupportResult {CrossingIndex=support.CrossingIndex,Input=support.Input,Arrangement=support.Arrangement,Towers=support.Towers,Members=support.Members,SecondaryStationsMm=support.SecondaryStationsMm,Solids=support.Solids,Bearings=support.Bearings,LoadKn=support.LoadKn,SlabEdgeLoadKn=support.SlabEdgeLoadKn,SymmetryNotice=support.SymmetryNotice,SharedFailure=sharedFailure,JunctionNotice=support.JunctionNotice,TowerSpanOffsetMm=support.TowerSpanOffsetMm,PrimaryNearExtensionMm=support.PrimaryNearExtensionMm,PrimaryFarExtensionMm=support.PrimaryFarExtensionMm,NearShutterClearanceMm=support.NearShutterClearanceMm,FarShutterClearanceMm=support.FarShutterClearanceMm};
                        accepted.Add(support);if(arrange(at+1))return true;accepted.RemoveAt(accepted.Count-1);
                        towerFailure=(towerFailure??"")+$" X{index+1}: dedicated towers with {allowance.Near:0}/{allowance.Far:0} mm allowances do not permit a complete passing cross-beam combination.";
                        break;
                    }
                }
                var message=(sharedFailure??"")+" "+(towerFailure??"")+" No valid requested cross-beam support arrangement.";
                if(common.Trace?.EnableHotProfiling==true)message+=" Receivers: "+string.Join("; ",original.Select((l,b)=>$"B{b+1}: width {l.WidthMm}, lower {l.Transfer.LowerStockLengthMm}, positions {string.Join(",",l.PositionsMm)}"));
                Reject(common,message);return false;
            };
            if(!arrange(0))
            {
                // Keep every combination the previous bounded traversal could
                // find: another ordering may require a different earlier pose.
                if(!common.OptimizeCrossingSearch || crossingOrder.SequenceEqual(Enumerable.Range(0,crossingOrder.Length)))return false;
                r.Beams=original;accepted.Clear();crossingOrder=Enumerable.Range(0,crossingOrder.Length).ToArray();
                if(!arrange(0))return false;
            }
            r.CrossSupports=accepted.OrderBy(s=>s.CrossingIndex).ToArray();
            // The successful terminal branch has already checked all members and
            // coordinated its physical rows. Rechecking from originals here would
            // erase that setting-out and duplicate the full numerical work.
            // A prefix may include the next bay's slab loading on its final receiver.
            // Full-network conservation and movements are checked after the chain closes.
            if(partial)return true;
            // Cross load is distinct from the reduced slab load and conserved in its full clear-bay footprint.
            foreach(var s in accepted)
            {
                double expected=r.Input.AreaLoad(s.Input.DepthMm)*s.Input.WidthMm*r.Input.Bays[s.Input.BayNumber-1].WidthMm/1e6+SlabEdgeLoads(r,s.CrossingIndex).Sum(p=>p.Kn)*r.Input.Bays[s.Input.BayNumber-1].WidthMm/1000;
                if(Math.Abs(expected-s.LoadKn)>1e-6*Math.Max(1,expected)) {Reject(common,$"X{s.CrossingIndex+1}: cross load conservation failed.");return false;}
            }
            double expectedSlab=0;
            for(int bay=0;bay<r.Bays.Length;bay++)if(r.Bays[bay]!=null)
            {
                double removed=r.Input.CrossingBeams.Where(c=>c.BayNumber==bay+1 && c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).Sum(c=>c.WidthMm);
                expectedSlab+=r.Input.AreaLoad(r.Input.Bays[bay].ThicknessMm)*(common.LengthMm-removed)*r.Input.Bays[bay].WidthMm/1e6;
                expectedSlab-=accepted.Where(s=>s.Input.BayNumber==bay+1).Sum(s=>SlabEdgeLoads(r,s.CrossingIndex).Sum(p=>p.Kn))*r.Input.Bays[bay].WidthMm/1000;
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
            if(b.IsPrimaryBearing)return b.PrimaryLine==0 ? dl : dr;
            var secondary=t.LowerCases[stage][b.Row];return secondary.Analysis.Response(y-secondary.StartMm)[2]+dl+(dr-dl)*(y-l.TransverseOffsetMm+l.WidthMm/2)/l.WidthMm;
        }
        private static void Reject(TowerInput common,string message)
        {
            common.Trace?.CrossingRejected(message);
            common.Diagnostics?.Geometry("cross support",message,"Review elevations, cross position, stock availability, bearing/end allowances and tower geometry.",blocker:true);
        }
        private static TransferMember Analyze(TransferMember source,BeamPointLoad[] points,BeamDistributedLoad[] uniform,TowerInput common,string stage,bool resolvedPrimary=false)
        {
            LoadedBeamAnalysis analysis;var work=common.NetworkNumerics??common.TransferredSlab?.SharedSearchWork;
            if(source.IsPrimary && common.OptimizePrimaryResponses && common.ReuseSearchWork && common.ReuseNetworkNumerics && work!=null && points.Length==0 && uniform.Length==1 && uniform[0].StartMm==0 && uniform[0].EndMm==source.LengthMm && uniform[0].KnPerM>=0)
            {
                var check=common.CopyForSearch();check.PrimaryMaterial=source.PrimaryMaterial;check.StagePrimaryChecks=resolvedPrimary && common.LocalNetworkSearch && common.StagePrimaryChecks;
                if(resolvedPrimary)check.DeferCrossingChecks=false;
                analysis=TransferredSlabOptimizer.AnalyzePrimary(check,source.LengthMm,source.SupportsMm,uniform,points,work.Matrix,work);
            }
            else analysis=TransferredSlabOptimizer.AnalyzeTransverse(common,source.IsPrimary ? BeamSearchCounter.PrimaryAnalyses : BeamSearchCounter.LowerAnalyses,source.LengthMm,source.SupportsMm,uniform,points,source.EI,common.DeflectionDenominator,signed:true,rejectResolvedPrimary:resolvedPrimary && common.LocalNetworkSearch && !source.IsWS10);
            return new TransferMember {PrimaryMaterial=source.PrimaryMaterial,Name=source.Name,LoadCase=stage,StartMm=source.StartMm,LengthMm=source.LengthMm,H20Count=source.H20Count,PrimarySelfWeightKnM=source.PrimarySelfWeightKnM,SupportsMm=source.SupportsMm,Points=points,Uniform=uniform,LegacyPrimaryUniform=source.LegacyPrimaryUniform,Analysis=analysis};
        }
        private static BeamDistributedLoad[] PrimaryIncrement(TransferNetworkResult r,int beam,TransferMember p,int line,IEnumerable<CrossBeamSupportResult> supports,TowerInput common)
        {
            var loads=new List<BeamDistributedLoad>();var t=r.Beams[beam].Transfer;
            foreach(var s in supports)
            {
                var bands=new List<BeamDistributedLoad>();
                foreach(var b in s.Bearings.Where(b=>b.Beam==beam))
                {
                    if(b.IsPrimaryBearing)
                    {
                        if(b.PrimaryLine==line)bands.Add(new BeamDistributedLoad {StartMm=b.FootprintStartMm,EndMm=b.FootprintEndMm,KnPerM=b.ReactionKn*1000/(b.FootprintEndMm-b.FootprintStartMm)});
                        continue;
                    }
                    var lower=t.LowerCases[0][b.Row];
                    var increment=BearingIncrement(r,beam,lower,new[] {b},common);
                    bands.Add(new BeamDistributedLoad {StartMm=b.FootprintStartMm,EndMm=b.FootprintEndMm,KnPerM=increment.Analysis.Reactions[line]*1000/(b.FootprintEndMm-b.FootprintStartMm)});
                }
                if(!TryFlattenCrossing(bands.ToArray(),s.Input.PositionMm-s.Input.WidthMm/2,s.Input.PositionMm+s.Input.WidthMm/2,out var flat))return null;
                if(flat==null)continue;
                double a=Math.Max(p.StartMm,flat.StartMm),z=Math.Min(p.StartMm+p.LengthMm,flat.EndMm);
                if(z>a+1e-7)loads.Add(new BeamDistributedLoad {StartMm=a-p.StartMm,EndMm=z-p.StartMm,KnPerM=flat.KnPerM});
            }
            return loads.ToArray();
        }
        internal static bool TryFlattenCrossing(BeamDistributedLoad[] bands,double start,double end,out BeamDistributedLoad flat)
        {
            flat=null;double force=0,moment=0;
            foreach(var b in bands)
            {double f=b.KnPerM*(b.EndMm-b.StartMm)/1000;force+=f;moment+=f*((b.StartMm+b.EndMm)/2-(start+end)/2);}
            if(Math.Abs(force)<1e-10)return Math.Abs(moment)<1e-7;
            double centre=(start+end)/2+moment/force;
            // A flat rectangle can preserve force and first moment only when centred on
            // the original resultant. Shorten it inside the physical footprint if eccentric.
            double width=2*Math.Min(centre-start,end-centre);
            if(width<=1e-7 || !TowerBeamAnalysis.Finite(width))return false;
            flat=new BeamDistributedLoad {StartMm=centre-width/2,EndMm=centre+width/2,KnPerM=force*1000/width};
            return true;
        }

        private static TransferMember BearingIncrement(TransferNetworkResult r,int beam,TransferMember lower,IEnumerable<CrossBeamBearing> bearings,TowerInput common)
        {
            var b=bearings.Where(v=>!v.IsPrimaryBearing).ToArray();double origin=r.BeamCentresMm[beam]+lower.StartMm;
            var points=b.Where(v=>!v.IsConcreteEndZone || v.IsShutterEndZone).Select(v=>new BeamPointLoad {PositionMm=v.Ymm-origin,Kn=v.ReactionKn}).ToArray();
            var uniform=b.Where(v=>v.IsConcreteEndZone && !v.IsShutterEndZone).Select(v=>new BeamDistributedLoad {StartMm=v.EndZoneStartMm-origin,EndMm=v.EndZoneEndMm-origin,KnPerM=v.ReactionKn*1000/(v.EndZoneEndMm-v.EndZoneStartMm)}).ToArray();
            return Analyze(lower,points,uniform,common,"Cross increment");
        }
        // Include direct and side-shutter junctions before optimizing/ranking the receiving
        // stock. Otherwise the cheapest beam-only winner can hide passing alternatives
        // of the same tower width when the end-zone loads are added later.
        internal static bool CheckJunctionReceiver(TowerInput common,TransferNetworkInput network,int beam,TowerLayout layout)
            =>CheckJunctionReceiver(common,network,beam,layout,200) || common.AllowLocalShutterExceptions && CheckJunctionReceiver(common,network,beam,layout,175);
        internal static bool CheckJunctionLowerReceiver(TowerInput common,TransferNetworkInput network,int beam,TowerLayout layout)
            =>CheckJunctionReceiver(common,network,beam,layout,200,true) || common.AllowLocalShutterExceptions && CheckJunctionReceiver(common,network,beam,layout,175,true);
        private static CrossBeamSupportResult[] JunctionLoads(TowerInput common,TransferNetworkInput network,int beam,TowerLayout layout,double shutterWidth)
        {
            var supports=new List<CrossBeamSupportResult>();var input=network.Beams[beam];var t=layout.Transfer;
            for(int index=0;index<network.CrossingBeams.Length;index++)
            {
                var c=network.CrossingBeams[index];bool right=c.BayNumber==beam+1,left=c.BayNumber==beam;
                // A primary-bearing crossing bypasses these receiving secondaries.
                // Do not reject its receiver using a provisional dedicated end zone.
                if(!common.EngineerDedicatedFallback && PrimaryBearingPossible(common,network,c))continue;
                if(common.EngineerWorkflow && !common.EngineerDedicatedFallback && EngineerPrimaryPlan.Eligible(network,c))continue;
                if((!left && !right) || c.SupportMode==CrossBeamSupportMode.ExistingSupportsOnly || c.SupportMode==CrossBeamSupportMode.LegacySlabRows || input.BottomElevationMm>c.BottomElevationMm+1e-7)continue;
                bool shutter=input.BottomElevationMm<c.BottomElevationMm-1e-7;
                double start=right ? input.WidthMm/2 : shutter ? -input.WidthMm/2-shutterWidth : t.LowerStockStartMm;
                double end=right ? shutter ? input.WidthMm/2+shutterWidth : t.LowerStockStartMm+t.LowerStockLengthMm : -input.WidthMm/2;
                if(end<=start)continue;
                if(shutter && ((start+end)/2<t.LowerStockStartMm+40-1e-7 || (start+end)/2>t.LowerStockStartMm+t.LowerStockLengthMm-40+1e-7))return null;
                double lo=c.PositionMm-c.WidthMm/2,hi=c.PositionMm+c.WidthMm/2;
                var rows=Enumerable.Range(0,t.RowsMm.Length).Where(row=>t.RowsMm[row]>=lo && t.RowsMm[row]<=hi).ToArray();
                if(rows.Length<2)return null;
                var tributaries=EndZoneWidths(lo,hi,rows.Select(row=>t.RowsMm[row]).ToArray());
                var bearings=rows.Select((row,i)=>new CrossBeamBearing {Beam=0,Row=row,IsConcreteEndZone=true,IsShutterEndZone=shutter,Ymm=(start+end)/2,EndZoneStartMm=start,EndZoneEndMm=end,EndZoneTributaryWidthMm=tributaries[i],FootprintStartMm=lo,FootprintEndMm=hi,ReactionKn=network.AreaLoad(c.DepthMm)*tributaries[i]*(end-start)/1e6}).ToArray();
                supports.Add(new CrossBeamSupportResult {CrossingIndex=index,Input=c,Bearings=bearings});
            }
            return supports.ToArray();
        }
        private static bool CheckJunctionReceiver(TowerInput common,TransferNetworkInput network,int beam,TowerLayout layout,double shutterWidth,bool lowerOnly=false)
        {
            var supports=JunctionLoads(common,network,beam,layout,shutterWidth);
            if(supports==null)return false;if(supports.Length==0)return true;
            var local=new TransferNetworkResult {Input=new TransferNetworkInput {Beams=new[] {network.Beams[beam]}},Beams=new[] {layout},BeamCentresMm=new[] {0.0}};
            return Recheck(common,local,new[] {layout},supports,lowerOnly);
        }
        // Prepared for one immutable transverse option and row grid. Primary tower
        // positions are deliberately absent: each candidate still checks its own
        // complete member response and every leg against the same resolved loads.
        internal static Func<TransferredSlabOptimizer.Geometry,double,bool> PrepareReceiver(TowerInput common,TransferNetworkInput network,int beam,TowerLayout probe)
        {
            if(!common.FullLoadingOnly || probe.Transfer.PrimaryPlan?.UseDesignZones!=true)return null;
            var onPrimary=PreparePrimaryReceiver(common,network,beam,probe);
            if(onPrimary!=null)
            {
                var fallback=common.CopyForSearch();fallback.EngineerDedicatedFallback=true;
                var onSecondary=PrepareSecondaryReceiver(fallback,network,beam,probe);
                return (g,e)=>onPrimary(g,e) || onSecondary!=null && onSecondary(g,e);
            }
            return PrepareSecondaryReceiver(common,network,beam,probe);
        }
        private static Func<TransferredSlabOptimizer.Geometry,double,bool> PrepareSecondaryReceiver(TowerInput common,TransferNetworkInput network,int beam,TowerLayout probe)
        {
            var junction=PrepareJunction(common,network,beam,probe);
            if(common.TransferredSlab.EngineerNetwork==null)return junction;
            var eligible=EngineerPrimaryPlan.Adjacent(network,beam).ToArray();
            if(eligible.Length==0)return junction;
            var centres=new double[network.Beams.Length];
            for(int i=1;i<centres.Length;i++)centres[i]=centres[i-1]+network.Beams[i-1].WidthMm/2+network.Bays[i-1].WidthMm+network.Beams[i].WidthMm/2;
            var mirror=new TransferNetworkResult {Input=network,Beams=Enumerable.Repeat(probe,network.Beams.Length).ToArray(),Bays=network.Bays.Select((b,i)=>probe.Transfer.PrimaryPlan.CarriesBay(i) ? new UpperTransferPlan() : null).ToArray(),BeamCentresMm=centres,SlabCoveredLengthMm=common.TransferredSlab.FixedCoveredLengthMm??common.LengthMm};
            var supports=new List<CrossBeamSupportResult>();
            Func<TransferredSlabOptimizer.Geometry,double,bool> fallback=null;
            if(probe.Transfer.EngineerReceiverCount>=8)
            {var dedicated=common.CopyForSearch();dedicated.EngineerDedicatedFallback=true;fallback=PrepareJunction(dedicated,network,beam,probe);}
            foreach(var c in eligible)
            {
                var shared=Shared(common,mirror,Array.IndexOf(network.CrossingBeams,c),out string failure);
                if(shared==null)return fallback==null || c.SupportMode!=CrossBeamSupportMode.Automatic ? (g,e)=>false : (g,e)=>fallback(g,e) && junction(g,e);
                var own=shared.Bearings.Where(b=>b.Beam==beam).Select(b=>
                {var copy=b.Copy();copy.Beam=0;copy.Ymm-=centres[beam];copy.EndZoneStartMm-=centres[beam];copy.EndZoneEndMm-=centres[beam];return copy;}).ToArray();
                supports.Add(new CrossBeamSupportResult {CrossingIndex=Array.IndexOf(network.CrossingBeams,c),Input=c,Bearings=own,UsesSpreader=true});
            }
            var primary=PrepareRecheck(common,network,beam,probe,supports.ToArray());
            bool canFallback=eligible.All(c=>c.SupportMode==CrossBeamSupportMode.Automatic);
            return (g,e)=>(primary(g,e) || canFallback && fallback!=null && fallback(g,e)) && junction(g,e);
        }
        private static Func<TransferredSlabOptimizer.Geometry,double,bool> PrepareJunction(TowerInput common,TransferNetworkInput network,int beam,TowerLayout probe)
        {
            var normal=JunctionLoads(common,network,beam,probe,200);
            var first=normal==null ? (Func<TransferredSlabOptimizer.Geometry,double,bool>)((g,e)=>false) : PrepareRecheck(common,network,beam,probe,normal);
            if(!common.AllowLocalShutterExceptions)return first;
            var narrow=JunctionLoads(common,network,beam,probe,175);
            var second=narrow==null ? (Func<TransferredSlabOptimizer.Geometry,double,bool>)((g,e)=>false) : PrepareRecheck(common,network,beam,probe,narrow);
            return (g,e)=>first(g,e) || second(g,e);
        }
        private struct ResolvedLoadKey : IEquatable<ResolvedLoadKey>
        {
            internal readonly double Start,Length;internal readonly int Line;
            internal ResolvedLoadKey(double start,double length,int line){Start=start;Length=length;Line=line;}
            public bool Equals(ResolvedLoadKey other)=>Start==other.Start && Length==other.Length && Line==other.Line;
            public override bool Equals(object other)=>other is ResolvedLoadKey && Equals((ResolvedLoadKey)other);
            public override int GetHashCode(){unchecked{return (Start.GetHashCode()*31+Length.GetHashCode())*31+Line;}}
        }
        private static Func<TransferredSlabOptimizer.Geometry,double,bool> PrepareRecheck(TowerInput common,TransferNetworkInput network,int beam,TowerLayout probe,CrossBeamSupportResult[] supports)
        {
            if(supports.Length==0)return (g,e)=>true;
            var local=new TransferNetworkResult {Input=new TransferNetworkInput {Beams=new[] {network.Beams[beam]}},Beams=new[] {probe},BeamCentresMm=new[] {0.0}};
            if(!Recheck(common,local,new[] {probe},supports,lowerOnly:true))return (g,e)=>false;
            var plan=probe.Transfer.PrimaryPlan.Resolve(common,local,0,supports);
            if(plan==null)return (g,e)=>false;
            var loads=new Dictionary<ResolvedLoadKey,Tuple<BeamDistributedLoad[],double[],double[]>>();
            // A check belongs to this resolved plan, material, criterion and leg limit.
            // No load rounding or acceptance decision is shared between different plans.
            var responses=new TransferredSlabOptimizer.ResponseCache<TransferredSlabOptimizer.PrimaryKey,bool>(4096);
            var shared=common.ReuseSearchWork && common.ReuseNetworkNumerics ? common.NetworkNumerics??common.TransferredSlab?.SharedSearchWork : null;
            return (geometry,edge)=>
            {
                using(common.Trace?.Hot(BeamHotStage.PrimaryRecheck))
                for(int run=0;run<geometry.Stock.Length;run++)for(int line=0;line<2;line++)
                {
                    common.Trace?.CheckCancellation();var stock=geometry.Stock[run];
                    var loadKey=new ResolvedLoadKey(stock.StartMm+edge,stock.StockLengthMm,line);
                    if(!loads.TryGetValue(loadKey,out var data))
                    {
                        var clipped=plan.Loads(loadKey.Start,stock.StockLengthMm,line,selfWeight:common.Primary.SelfWeightKnM(stock.StockLengthMm));
                        loads[loadKey]=data=Tuple.Create(clipped,clipped.SelectMany(q=>new[] {q.StartMm,q.EndMm}).ToArray(),clipped.SelectMany(q=>new[] {q.KnPerM,q.KnPerM}).ToArray());
                    }
                    var uniform=data.Item1;
                    var key=new TransferredSlabOptimizer.PrimaryKey(stock.StockLengthMm,geometry.MemberSupports[run],data.Item2,data.Item3,uniform.Length);
                    if(responses.TryGetValue(key,out bool passed)){if(!passed)return false;continue;}
                    var sharedKey=new TransferredSlabOptimizer.PreparedPrimaryKey(key,common);
                    if(shared!=null && shared.PreparedPrimaryChecks.TryGetValue(sharedKey,out var saved) && (saved.Passed || ReferenceEquals(saved.Diagnostics,common.Diagnostics) || common.OptimizeCrossingSearch && saved.Analysis!=null))
                    {
                        // An exact rejected response remains valid at another spacing
                        // retry. Replay its scalar checks with this member's current
                        // name/location rather than rebuilding its numerical solution
                        // or silently losing the new pass's failure explanation.
                        if(!saved.Passed && !ReferenceEquals(saved.Diagnostics,common.Diagnostics))
                        {
                            var replay=new TransferMember {PrimaryMaterial=common.PrimaryMaterial,Name="P"+(run+1)+(line==0 ? " L" : " R"),H20Count=2,Analysis=saved.Analysis};
                            Pass(replay,common,"Crossing resolved primary");
                            common.Diagnostics?.Legs(saved.Analysis.Reactions,common.LegCapacityKn,$"B{beam+1} cross stage legs","Crossing resolved primary");
                            saved.Diagnostics=common.Diagnostics;
                        }
                        common.Trace?.Increment(BeamSearchCounter.PreparedPrimaryCacheHits);responses.Store(key,saved.Passed);if(!saved.Passed)return false;continue;
                    }
                    string name="P"+(run+1)+(line==0 ? " L" : " R");
                    if(common.OptimizeCrossingSearch && !PrimaryForcesFit(common,stock.StockLengthMm,geometry.MemberSupports[run],uniform,name))
                    {responses.Store(key,false);return false;}
                    var source=new TransferMember {PrimaryMaterial=common.PrimaryMaterial,PrimarySelfWeightKnM=common.Primary.SelfWeightKnM(stock.StockLengthMm),Name=name,StartMm=loadKey.Start,LengthMm=stock.StockLengthMm,H20Count=2,SupportsMm=geometry.MemberSupports[run]};
                    var member=Analyze(source,Array.Empty<BeamPointLoad>(),uniform,common,"Crossing resolved primary",resolvedPrimary:true);
                    passed=Pass(member,common,"Crossing resolved primary");
                    common.Diagnostics?.Legs(member.Analysis.Reactions,common.LegCapacityKn,$"B{beam+1} cross stage legs","Crossing resolved primary");
                    passed &= member.Analysis.Reactions.Max()<=common.LegCapacityKn+1e-8;
                    shared?.PreparedPrimaryChecks.Store(sharedKey,new TransferredSlabOptimizer.PreparedPrimaryCheck {Passed=passed,Diagnostics=common.Diagnostics,Analysis=common.OptimizeCrossingSearch ? member.Analysis : null});
                    responses.Store(key,passed);if(!passed)return false;
                }
                return true;
            };
        }
        // These necessary bounds use the resolved UDLs of this exact receiver,
        // including its slab allocation. Across a support span, the difference
        // of the two shears equals its applied force; both faces must therefore
        // accommodate at least half that force. Free tails have exact root force
        // and moment. No stiffness solve is needed to reject an impossible span.
        internal static bool PrimaryForcesFit(TowerInput common,double length,double[] supports,BeamDistributedLoad[] loads,string scope)
        {
            double shear=common.Primary.ShearLimitKn,moment=common.Primary.MomentLimitKnM;
            Func<double,double,double> force=(a,b)=>loads.Sum(q=>q.KnPerM*Math.Max(0,Math.Min(b,q.EndMm)-Math.Max(a,q.StartMm))/1000);
            for(int i=1;i<supports.Length;i++)
            {
                double lower=Math.Abs(force(supports[i-1],supports[i]))/2;
                if(lower<=shear+shear*1e-6)continue;
                common.Diagnostics?.Limit(scope,"span shear lower bound",3,lower,shear,"kN","Resolved UDL on this support span","Reduce the support span or revise the primary arrangement.",shear*1e-6);return false;
            }
            for(int side=0;side<2;side++)
            {
                double a=side==0 ? 0 : supports.Last(),b=side==0 ? supports[0] : length,root=side==0 ? b : a;
                double v=Math.Abs(force(a,b)),m=Math.Abs(loads.Sum(q=>
                {
                    double lo=Math.Max(a,q.StartMm),hi=Math.Min(b,q.EndMm);
                    return hi<=lo ? 0 : q.KnPerM*(hi-lo)*((lo+hi)/2-root)/1e6;
                }));
                if(v>shear+shear*1e-6){common.Diagnostics?.Limit(scope,"cantilever shear",3,v,shear,"kN","Resolved UDL on this stock tail","Review primary overhang and support positions.",shear*1e-6);return false;}
                if(m>moment+moment*1e-6){common.Diagnostics?.Limit(scope,"cantilever moment",3,m,moment,"kNm","Resolved UDL on this stock tail","Review primary overhang and support positions.",moment*1e-6);return false;}
            }
            return true;
        }
        private static bool Recheck(TowerInput common,TransferNetworkResult r,TowerLayout[] originals,CrossBeamSupportResult[] supports,bool lowerOnly=false,bool pendingAssignments=false)
        {
            using(var hot=common.Trace?.Hot(BeamHotStage.CrossingRecheck))
            {
            var shared=supports.Where(s=>s.Bearings.Any(b=>b.Beam>=0)).ToArray();
            r.Beams=originals.Select(Copy).ToArray();
            for(int beam=0;beam<r.Beams.Length;beam++)
            {
                var l=r.Beams[beam];var t=originals[beam].Transfer;int runs=l.StockPlan.Pieces.Length;
                EngineerPrimaryPlan resolvedPlan=null;
                var relevant=shared.Where(s=>s.Bearings.Any(b=>b.Beam==beam)).ToArray();
                var columnLoads=new List<ColumnEndStripLoad[]>();var cases=new List<TransferMember[]>();var lowerCases=new List<TransferMember[]>();var names=new List<string>();var left=Enumerable.Repeat(double.NegativeInfinity,l.PositionsMm.Length).ToArray();var right=Enumerable.Repeat(double.NegativeInfinity,left.Length).ToArray();
                var minLeft=Enumerable.Repeat(double.PositiveInfinity,left.Length).ToArray();var minRight=Enumerable.Repeat(double.PositiveInfinity,left.Length).ToArray();
                bool conservative=relevant.Length>8;
                int combinations=conservative ? 2 : 1<<relevant.Length;
                for(int stage=0;stage<t.CaseNames.Length;stage++)for(int mask=common.FullLoadingOnly ? combinations-1 : 0;mask<combinations;mask++)
                {
                    common.Trace?.CheckCancellation();
                    var active=relevant.Where((s,i)=>conservative ? mask==1 : (mask&(1<<i))!=0).ToArray();
                    string name=t.CaseNames[stage]+(mask==0 ? "" : " + "+string.Join(" + ",active.Select(s=>"X"+(s.CrossingIndex+1))));
                    Tuple<TransferMember[],string> lowerKey=null;
                    System.Collections.Concurrent.ConcurrentDictionary<Tuple<TransferMember[],string>,CrossLowerCaseCheck> lowerCache=null;
                    if(common.EngineerSearch && common.ReuseSearchWork && !conservative)
                    {
                        lowerCache=common.CrossLowerCases??(common.CrossLowerCases=new System.Collections.Concurrent.ConcurrentDictionary<Tuple<TransferMember[],string>,CrossLowerCaseCheck>());
                        string signature=FormattableString.Invariant($"{r.BeamCentresMm[beam]:R}|{common.DeflectionDenominator}|{name}|")
                            +string.Join(";",active.SelectMany(s=>s.Bearings).Where(b=>b.Beam==beam).Select(b=>FormattableString.Invariant($"{b.Row},{b.Ymm:R},{b.ReactionKn:R},{b.IsConcreteEndZone},{b.IsShutterEndZone},{b.EndZoneStartMm:R},{b.EndZoneEndMm:R}")));
                        lowerKey=Tuple.Create(t.LowerCases[stage],signature);
                    }
                    TransferMember[] lower;
                    if(lowerCache!=null && lowerCache.TryGetValue(lowerKey,out var saved) && (saved.Passed || ReferenceEquals(saved.Diagnostics,common.Diagnostics)))
                    {
                        if(!saved.Passed)return false;
                        lower=saved.Members;
                    }
                    else
                    {
                        lower=new TransferMember[t.RowsMm.Length];bool lowerPassed=true;
                        for(int row=0;row<lower.Length;row++)
                        {
                            var original=t.LowerCases[stage][row];
                            var rowBearings=active.SelectMany(s=>s.Bearings).Where(b=>b.Beam==beam && !b.IsPrimaryBearing && b.Row==row).ToArray();
                            if(rowBearings.Length==0)lower[row]=StageCopy(original,name);
                            else
                            {
                                var increment=BearingIncrement(r,beam,original,rowBearings,common);
                                lower[row]=Analyze(original,original.Points.Concat(increment.Points).ToArray(),original.Uniform.Concat(increment.Uniform).ToArray(),common,name);
                            }
                            if(!Pass(lower[row],common,name)){lowerPassed=false;break;}
                            if(conservative && mask==1 && !Envelope(common,original,active.Select(s=>BearingIncrement(r,beam,original,s.Bearings.Where(b=>b.Beam==beam && !b.IsPrimaryBearing && b.Row==row),common)).ToArray(),null,lower[row])){lowerPassed=false;break;}
                        }
                        if(lowerCache!=null)
                        {
                            if(lowerCache.Count>=128)lowerCache.Clear();
                            lowerCache[lowerKey]=new CrossLowerCaseCheck {Members=lower,Passed=lowerPassed,Diagnostics=common.Diagnostics};
                        }
                        if(!lowerPassed)return false;
                    }
                    if(lowerOnly)continue;
                    if(common.EngineerWorkflow && t.PrimaryPlan?.UseDesignZones==true && resolvedPlan==null)
                    {
                        // Lower-only screening must not allocate/rebuild primary zones.
                        // Resolve only after the physical receiving H20s have passed.
                        resolvedPlan=t.PrimaryPlan.Resolve(common,r,beam,supports,pendingAssignments);
                        if(resolvedPlan==null)return false;
                    }
                    var primary=new TransferMember[runs*2];
                    // Resolve the crossing first, then check the most highly loaded
                    // long-span members before spending time on the lighter ones.
                    var order=Enumerable.Range(0,runs*2);
                    if(common.OptimizePrimaryResponses)order=order.OrderByDescending(i=>TransferredSlabOptimizer.PrimaryPriority(t.PrimaryCases[stage][i]));
                    foreach(int memberIndex in order)
                    {
                        using(var primaryHot=common.Trace?.Hot(BeamHotStage.PrimaryRecheck))
                        {
                        int line=memberIndex/runs,run=memberIndex%runs;
                        var original=t.PrimaryCases[stage][memberIndex];
                        // Shared production primaries use the agreed concrete tributary zones.
                        // Slab ledger/stock-tail effects belong to the secondary checks above.
                        bool direct=resolvedPlan!=null;
                        var baseline=original.LegacyPrimaryUniform??original.Uniform;
                        var increment=direct ? new BeamDistributedLoad[0] : PrimaryIncrement(r,beam,original,line,active,common);
                        if(increment==null){Reject(common,"Crossing resultant cannot be represented by a flat UDL inside its physical footprint.");return false;}
                        var uniform=direct ? resolvedPlan.Loads(original.StartMm,original.LengthMm,line,selfWeight:original.PrimarySelfWeightKnM) : baseline.Concat(increment).ToArray();
                        var m=EngineerPrimaryPlan.SameLoads(original.Uniform,uniform) ? StageCopy(original,name) : Analyze(original,Array.Empty<BeamPointLoad>(),uniform,common,name);primary[run+line*runs]=m;
                        if(!Pass(m,common,name))return false;
                        common.Diagnostics?.Legs(m.Analysis.Reactions,common.LegCapacityKn,$"B{beam+1} cross stage legs",name);
                        if(m.Analysis.Reactions.Max()>common.LegCapacityKn+1e-8)return false;
                        if(conservative && mask==1 && !common.FullLoadingOnly)
                        {
                            var baseMember=Analyze(original,Array.Empty<BeamPointLoad>(),baseline,common,name);
                            var increments=active.Select(s=>Analyze(original,Array.Empty<BeamPointLoad>(),PrimaryIncrement(r,beam,original,line,new[] {s},common),common,"increment")).ToArray();
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
                        }
                    names.Add(name);cases.Add(primary);lowerCases.Add(lower);
                    if(resolvedPlan!=null)
                        columnLoads.Add(resolvedPlan.Columns(l.StockPlan.Pieces.First().StartMm,l.StockPlan.Pieces.Last().EndMm,name));
                    else
                    {
                        var sourceColumns=t.LegacyColumnSupportLoads??t.ColumnSupportLoads;
                        if(sourceColumns.Length>0)columnLoads.Add(sourceColumns[stage].Select(x=>new ColumnEndStripLoad {LoadCase=name,End=x.End,StartMm=x.StartMm,EndMm=x.EndMm,LeftLineKn=x.LeftLineKn,RightLineKn=x.RightLineKn,LeftLineMomentKnM=x.LeftLineMomentKnM,RightLineMomentKnM=x.RightLineMomentKnM}).ToArray());
                    }
                }
                if(lowerOnly)continue;
                l.Transfer=t.Copy();
                l.Transfer.PrimarySupportNotice=string.Join("; ",supports.Where(s=>s.Input.BayNumber==beam || s.Input.BayNumber==beam+1).Select(s=>s.SupportDescription));
                l.Transfer.PrimaryPlan=resolvedPlan;l.Transfer.ColumnSupportLoads=columnLoads.ToArray();
                l.Transfer.PrimaryCases=cases.ToArray();l.Transfer.LowerCases=lowerCases.ToArray();l.Transfer.CaseNames=names.ToArray();
                l.LeftReactions=left;l.RightReactions=right;l.MinimumLeftReactions=minLeft;l.MinimumRightReactions=minRight;l.MaxLegKn=Math.Max(left.Max(),right.Max());
                var all=cases.Concat(lowerCases).SelectMany(c=>c).Concat(t.UpperMembers).ToArray();
                l.MomentKnM=all.Max(m=>m.Analysis.MomentKnM);l.ShearKn=all.Max(m=>m.Analysis.ShearKn);l.DeflectionMm=all.Max(m=>m.Analysis.DeflectionMm);l.Utilization=Math.Max(l.MaxLegKn/l.LegCapacityKn,all.Max(m=>m.Utilization));l.Governing=l.MaxLegKn/l.LegCapacityKn>=all.Max(m=>m.Utilization) ? "Leg capacity" : all.OrderByDescending(m=>m.Utilization).First().Name;
                RefreshStockDemands(l);
            }
            return true;
            }
        }
        private static TransferMember StageCopy(TransferMember m,string stage)=>new TransferMember {PrimaryMaterial=m.PrimaryMaterial,Name=m.Name,LoadCase=stage,StartMm=m.StartMm,LengthMm=m.LengthMm,H20Count=m.H20Count,PrimarySelfWeightKnM=m.PrimarySelfWeightKnM,SupportsMm=m.SupportsMm,Points=m.Points,Uniform=m.Uniform,LegacyPrimaryUniform=m.LegacyPrimaryUniform,Analysis=m.Analysis,SubsetMinimumReactionsKn=m.SubsetMinimumReactionsKn,SubsetMomentBoundKnM=m.SubsetMomentBoundKnM,SubsetShearBoundKn=m.SubsetShearBoundKn,SubsetDeflectionBound=m.SubsetDeflectionBound};
        private static void RefreshStockDemands(TowerLayout l)
        {
            int runs=l.StockPlan.Pieces.Length;
            var pieces=l.StockPlan.Pieces.Select((p,i)=>
            {
                var members=l.Transfer.PrimaryCases.SelectMany(c=>new[] {c[i],c[i+runs]}).ToArray();
                var d=members.OrderByDescending(m=>m.Analysis.DeflectionUtilization).First().Analysis;
                return new TowerStockPiece {StartMm=p.StartMm,StockLengthMm=p.StockLengthMm,FirstSupport=p.FirstSupport,LastSupport=p.LastSupport,IsCustomLength=p.IsCustomLength,StrengthUtilization=members.Max(m=>m.StrengthUtilization),MomentKnM=members.Max(m=>m.Analysis.MomentKnM),ShearKn=members.Max(m=>m.Analysis.ShearKn),DeflectionMm=members.Max(m=>m.Analysis.DeflectionMm),DeflectionUtilization=d.DeflectionUtilization,GoverningDeflectionMm=d.DeflectionUtilization*d.GoverningLengthMm/l.DeflectionDenominator,GoverningDeflectionLimitMm=d.GoverningLengthMm/l.DeflectionDenominator,GoverningDeflectionRegion=l.Transfer.FullLoadingOnly ? "Fully loaded primary UDL" : "Envelope of primary UDL construction stages"};
            }).ToArray();
            l.StockPlan=new TowerStockPlan {PrimaryMaterial=l.PrimaryMaterial,Pieces=pieces,Reactions=l.LeftReactions.Zip(l.RightReactions,Math.Max).ToArray()};
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
            if(baseline.IsWS10 && m/10+v/82>1+1e-8)return false;
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
