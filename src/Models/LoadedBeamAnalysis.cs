using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public sealed class BeamPointLoad { public double PositionMm, Kn; }
    public sealed class BeamDistributedLoad { public double StartMm, EndMm, KnPerM; }
    /// <summary>Euler-Bernoulli analysis of free-ended stock, with arbitrary point and piecewise uniform loads.</summary>
    public sealed class LoadedBeamAnalysis
    {
        public double[] Reactions { get; private set; }
        public double MomentKnM { get; private set; }
        public double ShearKn { get; private set; }
        public double DeflectionMm { get; private set; }
        public double DeflectionUtilization { get; private set; }
        public double TotalLoadKn { get; private set; }
        public double GoverningLengthMm { get; private set; }
        private sealed class Element { public double X, L, Q, U, T, C2, C3, C4; }
        private readonly List<Element> elements = new List<Element>();
        // The one-argument API returns the interior response at stock ends, preserving existing callers.
        public double[] Response(double mm) => Response(mm,mm < (elements.Last().X+elements.Last().L)*1000-1e-7);
        public double[] Response(double mm, bool rightSide)
        {
            if (!Finite(mm) || mm < 0 || mm > (elements.Last().X + elements.Last().L) * 1000 + 1e-7) throw new ArgumentOutOfRangeException(nameof(mm));
            var e = (rightSide ? elements.LastOrDefault(v => mm >= v.X * 1000 - 1e-7) : elements.FirstOrDefault(v => mm <= (v.X+v.L)*1000+1e-7)) ?? elements[0];
            double x = Math.Max(0, Math.Min(e.L, mm / 1000 - e.X));
            var response = new[] { -(6*e.C3+24*e.C4*x)*rigidity, -(2*e.C2+6*e.C3*x+12*e.C4*x*x)*rigidity,
                -(e.U+e.T*x+e.C2*x*x+e.C3*x*x*x+e.C4*x*x*x*x)*1000 };
            // Outside a stock end there is no internal force; include support/load jumps at end stations too.
            if (!rightSide && mm <= 1e-7 || rightSide && Math.Abs(mm-(elements.Last().X+elements.Last().L)*1000)<=1e-7) response[0]=response[1]=0;
            return response;
        }
        internal double WS10StrengthUtilization()=>PrimaryBeamProfile.WS10Strength((elements.Last().X+elements.Last().L)*1000,elements.SelectMany(e=>new[] {e.X*1000,(e.X+e.L)*1000}),Response);
        public LoadedBeamAnalysis Scale(double factor)
        {
            if (!Finite(factor) || factor < 0) throw new ArgumentException("Load scale must be nonnegative.");
            var result = new LoadedBeamAnalysis { rigidity = rigidity, DeflectionEvaluated=DeflectionEvaluated, Reactions = Reactions.Select(v => v * factor).ToArray(), MomentKnM = MomentKnM * factor, ShearKn = ShearKn * factor, DeflectionMm = DeflectionMm * factor, DeflectionUtilization = DeflectionUtilization * factor, TotalLoadKn = TotalLoadKn * factor, GoverningLengthMm = GoverningLengthMm };
            foreach (var e in elements) result.elements.Add(new Element { X = e.X, L = e.L, Q = e.Q * factor, U = e.U * factor, T = e.T * factor, C2 = e.C2 * factor, C3 = e.C3 * factor, C4 = e.C4 * factor });
            return result;
        }
        /// <summary>Exact peak absolute displacement after an equal prescribed displacement of every support.</summary>
        public double DeflectionWithOffsetMm(double offset) => DeflectionWithAffineOffsetMm(offset,offset,0,(elements.Last().X+elements.Last().L)*1000);
        public double DeflectionWithAffineOffsetMm(double near,double far,double nearSupportMm,double farSupportMm)
        {
            if (new[] {near,far,nearSupportMm,farSupportMm}.Any(x=>!Finite(x)) || farSupportMm<=nearSupportMm) throw new ArgumentException("Support displacement or span is invalid.");
            double slopeOffset=(far-near)/(farSupportMm-nearSupportMm)*1000;
            double result=0;
            foreach(var e in elements)
            {
                Func<double,double> slope=x=>e.T-slopeOffset/1000+2*e.C2*x+3*e.C3*x*x+4*e.C4*x*x*x;
                var cuts=new List<double> {0,e.L}; double disc=36*e.C3*e.C3-96*e.C4*e.C2;
                if(Math.Abs(e.C4)>1e-20 && disc>=0) foreach(double root in new[] {(-6*e.C3-Math.Sqrt(disc))/(24*e.C4),(-6*e.C3+Math.Sqrt(disc))/(24*e.C4)}) {if(root>0 && root<e.L)cuts.Add(root);}
                else if(Math.Abs(e.C3)>1e-20) {double root=-e.C2/(3*e.C3);if(root>0 && root<e.L)cuts.Add(root);}
                cuts.Sort();var peaks=new List<double>(cuts);
                for(int i=1;i<cuts.Count;i++)
                {
                    double lo=cuts[i-1],hi=cuts[i],sign=slope(lo);if(sign*slope(hi)>=0)continue;
                    for(int j=0;j<42;j++) {double mid=(lo+hi)/2;if(sign*slope(mid)>0)lo=mid;else hi=mid;}peaks.Add((lo+hi)/2);
                }
                result=Math.Max(result,peaks.Max(x=>Math.Abs(near+slopeOffset*(e.X+x-nearSupportMm/1000)-(e.U+e.T*x+e.C2*x*x+e.C3*x*x*x+e.C4*x*x*x*x)*1000)));
            }
            return result;
        }
        internal bool DeflectionEvaluated {get;private set;}
        private double rigidity;
        internal sealed class MatrixKey : IEquatable<MatrixKey>
        {
            private readonly double length,ei;
            private readonly double[] supports,positions,boundaries;
            private readonly int hash;
            internal MatrixKey(double length,double[] supports,BeamPointLoad[] points,double ei,BeamDistributedLoad[] uniform=null)
            {
                this.length=length;this.ei=ei;this.supports=supports;positions=points.Select(p=>p.PositionMm).ToArray();boundaries=(uniform??new BeamDistributedLoad[0]).SelectMany(q=>new[] {q.StartMm,q.EndMm}).ToArray();
                unchecked
                {
                    int h=length.GetHashCode()*31+ei.GetHashCode();
                    for(int i=0;i<supports.Length;i++)h=h*31+supports[i].GetHashCode();
                    for(int i=0;i<positions.Length;i++)h=h*31+positions[i].GetHashCode();for(int i=0;i<boundaries.Length;i++)h=h*31+boundaries[i].GetHashCode();hash=h;
                }
            }
            public override int GetHashCode()=>hash;
            public override bool Equals(object other)=>Equals(other as MatrixKey);
            public bool Equals(MatrixKey other)
            {
                if(other==null || hash!=other.hash || length!=other.length || ei!=other.ei || supports.Length!=other.supports.Length || positions.Length!=other.positions.Length || boundaries.Length!=other.boundaries.Length)return false;
                for(int i=0;i<supports.Length;i++)if(supports[i]!=other.supports[i])return false;
                for(int i=0;i<positions.Length;i++)if(positions[i]!=other.positions[i])return false;
                for(int i=0;i<boundaries.Length;i++)if(boundaries[i]!=other.boundaries[i])return false;
                return true;
            }
        }
        internal sealed class PreparedBeam
        {
            internal List<double> Nodes;
            internal double[] Supports;
            internal double[,] Stiffness,Factor;
            internal int[] FixedDof,PointDof;
        }
        internal sealed class MatrixCache
        {
            private readonly Dictionary<MatrixKey,PreparedBeam> shapes=new Dictionary<MatrixKey,PreparedBeam>();
            internal long Hits;
            private readonly int capacity;
            private readonly bool evict;
            private readonly Queue<MatrixKey> order=new Queue<MatrixKey>();
            internal MatrixCache(int capacity=1024,bool evict=false) {this.capacity=capacity;this.evict=evict;}
            internal PreparedBeam Get(MatrixKey key)
            {
                if(!shapes.TryGetValue(key,out var prepared))return null;
                Hits++;return prepared;
            }
            internal void Store(MatrixKey key,PreparedBeam prepared)
            {
                if(shapes.ContainsKey(key))return;
                if(shapes.Count>=capacity) {if(!evict)return;shapes.Remove(order.Dequeue());}
                shapes.Add(key,prepared);if(evict)order.Enqueue(key);
            }
        }
        internal static LoadedBeamAnalysis AnalyzePointsCached(double lengthMm,double[] supportMm,BeamPointLoad[] points,double ei,int denominator,MatrixCache cache,double reactionCapacity=double.PositiveInfinity,bool rejectFailedPrimary=false,bool ignoreUplift=false)
            =>AnalyzeCore(lengthMm,supportMm,null,points,ei,denominator,true,cache,reactionCapacity,rejectFailedPrimary,allowSigned:ignoreUplift,ignoreUplift:ignoreUplift);
        internal static LoadedBeamAnalysis AnalyzeUniformCached(double lengthMm,double[] supportMm,BeamDistributedLoad[] uniform,double ei,int denominator,MatrixCache cache,double reactionCapacity=double.PositiveInfinity,bool rejectFailedPrimary=false,bool ignoreUplift=false)
            =>AnalyzeCore(lengthMm,supportMm,uniform,null,ei,denominator,true,cache,reactionCapacity,rejectFailedPrimary,allowSigned:ignoreUplift,ignoreUplift:ignoreUplift);
        internal static LoadedBeamAnalysis AnalyzeCombinedCached(double lengthMm,double[] supportMm,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator,MatrixCache cache,double reactionCapacity=double.PositiveInfinity,bool rejectFailedPrimary=false,bool ignoreUplift=false,bool allowSignedLoads=false)
            =>AnalyzeCore(lengthMm,supportMm,uniform,points,ei,denominator,true,cache,reactionCapacity,rejectFailedPrimary,allowSigned:ignoreUplift || allowSignedLoads,ignoreUplift:ignoreUplift);
        public static LoadedBeamAnalysis Analyze(double lengthMm, double[] supportMm, BeamDistributedLoad[] uniform, BeamPointLoad[] points, double ei, int denominator)
            => AnalyzeCore(lengthMm,supportMm,uniform,points,ei,denominator,true);
        internal static LoadedBeamAnalysis AnalyzeSigned(double lengthMm,double[] supportMm,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator)
            =>AnalyzeCore(lengthMm,supportMm,uniform,points,ei,denominator,true,allowSigned:true);
        // Retain the former dense storage path for exact numerical regression comparisons.
        internal static LoadedBeamAnalysis AnalyzeDenseReference(double lengthMm,double[] supportMm,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator)
            => AnalyzeCore(lengthMm,supportMm,uniform,points,ei,denominator,false);
        private static LoadedBeamAnalysis AnalyzeCore(double lengthMm,double[] supportMm,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator,bool banded,MatrixCache matrixCache=null,double reactionCapacity=double.PositiveInfinity,bool rejectFailedPrimary=false,bool allowSigned=false,bool ignoreUplift=false)
        {
            if (!Finite(lengthMm) || lengthMm <= 0 || !Finite(ei) || ei <= 0 || supportMm == null || supportMm.Length < 2
                || supportMm.Any(v => !Finite(v) || v < 0 || v > lengthMm) || supportMm.Distinct().Count() != supportMm.Length
                || !new[] { 500, 350, 270 }.Contains(denominator)) throw new ArgumentException("Invalid stock/support geometry or deflection criterion.");
            uniform = uniform ?? new BeamDistributedLoad[0]; points = points ?? new BeamPointLoad[0];
            if (uniform.Any(v => v == null || !Finite(v.StartMm) || !Finite(v.EndMm) || v.StartMm < 0 || v.EndMm > lengthMm || v.EndMm <= v.StartMm || !Finite(v.KnPerM) || !allowSigned && v.KnPerM < 0)
                || points.Any(v => v == null || !Finite(v.PositionMm) || v.PositionMm < 0 || v.PositionMm > lengthMm || !Finite(v.Kn) || !allowSigned && v.Kn < 0)) throw new ArgumentException("Invalid beam loads.");
            if(banded && supportMm.Length==2 && Math.Abs(supportMm[1]-supportMm[0])>1e-4)
                return AnalyzeTwoSupports(lengthMm,supportMm,uniform,points,ei,denominator,reactionCapacity,rejectFailedPrimary,ignoreUplift);
            // Primary pairs reuse only their immutable structure. Each load vector and
            // displacement solution is new; forces are never rounded or omitted from checks.
            var shapeKey=matrixCache==null ? null : new MatrixKey(lengthMm,supportMm,points,ei,uniform);
            var prepared=shapeKey==null ? null : matrixCache.Get(shapeKey);
            double[] supports;List<double> nodes;double[,] original,factor;int[] fixedDof,pointDof;
            if(prepared!=null)
            {
                supports=prepared.Supports;nodes=prepared.Nodes;original=prepared.Stiffness;factor=prepared.Factor;fixedDof=prepared.FixedDof;pointDof=prepared.PointDof;
            }
            else
            {
                supports=supportMm.OrderBy(v=>v).ToArray();
                nodes=new[] {0.0,lengthMm}.Concat(supports).Concat(uniform.SelectMany(v=>new[] {v.StartMm,v.EndMm})).Concat(points.Select(v=>v.PositionMm)).OrderBy(v=>v).ToList();
                for(int i=nodes.Count-1;i>0;i--)if(nodes[i]-nodes[i-1]<1e-7)nodes.RemoveAt(i);
                fixedDof=supports.Select(v=>2*nodes.FindIndex(x=>Math.Abs(x-v)<1e-7)).ToArray();
                pointDof=points.Select(p=>2*nodes.FindIndex(x=>Math.Abs(x-p.PositionMm)<1e-7)).ToArray();
                original=null;factor=null;
            }
            int n=2*nodes.Count;var f=new double[n];
            var k=prepared==null ? new double[n,banded ? 7 : n] : null;
            // On a cached point-only structure, this loop would add only zero UDL forces.
            if(prepared==null || uniform.Length!=0)for(int e=0;e<nodes.Count-1;e++)
            {
                double l=(nodes[e+1]-nodes[e])/1000,q=uniform.Where(v=>v.StartMm<(nodes[e]+nodes[e+1])/2 && v.EndMm>(nodes[e]+nodes[e+1])/2).Sum(v=>v.KnPerM),c=ei/(l*l*l);
                var ke=k==null ? null : new double[,] {{12,6*l,-12,6*l},{6*l,4*l*l,-6*l,2*l*l},{-12,-6*l,12,-6*l},{6*l,2*l*l,-6*l,4*l*l}};
                var fe=new[] {q*l/2,q*l*l/12,q*l/2,-q*l*l/12};
                for(int i=0;i<4;i++)
                {
                    f[2*e+i]+=fe[i];
                    if(k!=null)for(int j=0;j<4;j++)k[2*e+i,banded ? j-i+3 : 2*e+j]+=c*ke[i,j];
                }
            }
            for(int i=0;i<points.Length;i++)f[pointDof[i]]+=points[i].Kn;
            var originalF=(double[])f.Clone();
            if(k!=null)
            {
                original=(double[,])k.Clone();
                foreach(int index in fixedDof)
                {
                    for(int j=Math.Max(0,index-3);j<=Math.Min(n-1,index+3);j++) {k[index,banded ? j-index+3 : j]=0;k[j,banded ? index-j+3 : index]=0;}
                    k[index,banded ? 3 : index]=1;
                }
                // All coefficients and the original arithmetic order are retained in the band.
                factor=new double[n,banded ? 4 : n];
                for(int i=0;i<n;i++)for(int j=Math.Max(0,i-3);j<=i;j++)
                {
                    double value=k[i,banded ? j-i+3 : j];
                    for(int h=Math.Max(0,i-3);h<j;h++)value-=factor[i,banded ? i-h : h]*factor[j,banded ? j-h : h];
                    if(i==j) {if(value<=0 || !Finite(value))throw new ArgumentException("Unstable beam supports.");factor[i,banded ? 0 : i]=Math.Sqrt(value);}
                    else factor[i,banded ? i-j : j]=value/factor[j,banded ? 0 : j];
                }
                if(shapeKey!=null)matrixCache.Store(shapeKey,new PreparedBeam {Nodes=nodes,Supports=supports,Stiffness=original,Factor=factor,FixedDof=fixedDof,PointDof=pointDof});
            }
            foreach(int index in fixedDof)f[index]=0;
            var d = new double[n]; for (int i = 0; i < n; i++) { double value = f[i]; for (int j = Math.Max(0, i - 3); j < i; j++) value -= factor[i, banded ? i-j : j] * d[j]; d[i] = value / factor[i, banded ? 0 : i]; }
            for (int i = n - 1; i >= 0; i--) { double value = d[i]; for (int j = i + 1; j <= Math.Min(n - 1, i + 3); j++) value -= factor[j, banded ? j-i : i] * d[j]; d[i] = value / factor[i, banded ? 0 : i]; }
            var result = new LoadedBeamAnalysis { rigidity = ei, Reactions = fixedDof.Select(i => originalF[i] - Enumerable.Range(Math.Max(0, i - 3), Math.Min(n - 1, i + 3) - Math.Max(0, i - 3) + 1).Sum(j => original[i, banded ? j-i+3 : j] * d[j])).ToArray(), TotalLoadKn = uniform.Sum(v => (v.EndMm - v.StartMm) / 1000 * v.KnPerM) + points.Sum(v => v.Kn) };
            for (int index = 0; index < nodes.Count - 1; index++)
            {
                double l = (nodes[index + 1] - nodes[index]) / 1000, u = d[2 * index], v = d[2 * index + 2], t = d[2 * index + 1], z = d[2 * index + 3];
                double q = uniform.Where(w => w.StartMm < (nodes[index]+nodes[index+1])/2 && w.EndMm > (nodes[index]+nodes[index+1])/2).Sum(w => w.KnPerM);
                var e = new Element { X = nodes[index] / 1000, L = l, Q = q, U = u, T = t, C2 = (-3 * u + 3 * v) / (l * l) - (2 * t + z) / l + q * l * l / (24 * ei), C3 = (2 * u - 2 * v) / (l * l * l) + (t + z) / (l * l) - q * l / (12 * ei), C4 = q / (24 * ei) };
                result.elements.Add(e);
                result.AddForcePeaks(e);
            }
            return FinishAnalysis(result,lengthMm,supports,denominator,reactionCapacity,rejectFailedPrimary,ignoreUplift,banded && uniform.Length==0,banded);
        }
        private static LoadedBeamAnalysis FinishAnalysis(LoadedBeamAnalysis result,double lengthMm,double[] supports,int denominator,double reactionCapacity,bool rejectFailedPrimary,bool ignoreUplift,bool pointOnly,bool fastRoots)
        {
            if (Math.Abs(result.Reactions.Sum() - result.TotalLoadKn) > 1e-6 * Math.Max(1, result.TotalLoadKn)) throw new ArgumentException("Beam load equilibrium failed.");
            if(rejectFailedPrimary && (result.MomentKnM>TowerOptimizer.PairMoment+1e-8 || result.ShearKn>TowerOptimizer.PairShear+1e-8 || result.Reactions.Any(r=>(!ignoreUplift && r< -1e-7) || r>reactionCapacity+1e-8)))return result;
            for(int index=0;index<result.elements.Count;index++)
            {
                var e=result.elements[index];double l=e.L;
                Func<double, double> slope = x => e.T + 2 * e.C2 * x + 3 * e.C3 * x * x + 4 * e.C4 * x * x * x;
                var cuts = new List<double> { 0, l };
                double disc = 36 * e.C3 * e.C3 - 96 * e.C4 * e.C2;
                if (Math.Abs(e.C4) > 1e-20 && disc >= 0) { foreach (double root in new[] { (-6 * e.C3 - Math.Sqrt(disc)) / (24 * e.C4), (-6 * e.C3 + Math.Sqrt(disc)) / (24 * e.C4) }) if (root > 0 && root < l) cuts.Add(root); }
                else if (Math.Abs(e.C3) > 1e-20) { double root = -e.C2 / (3 * e.C3); if (root > 0 && root < l) cuts.Add(root); }
                cuts.Sort(); var peaks = new List<double> { 0, l };
                for(int i=1;i<cuts.Count;i++)
                {
                    double lo=cuts[i-1],hi=cuts[i],sign=slope(lo);if(sign*slope(hi)>=0)continue;
                    // Point-only members have a quadratic slope. Use stable roots of the
                    // same displacement polynomial; retain bisection for UDLs or an
                    // ill-conditioned root that fails its residual/interval checks.
                    if(pointOnly && TryQuadraticRoot(3*e.C3,2*e.C2,e.T,lo,hi,out double root)) {peaks.Add(root);continue;}
                    if(fastRoots && !pointOnly && TryCubicRoot(4*e.C4,3*e.C3,2*e.C2,e.T,lo,hi,out double cubicRoot)){peaks.Add(cubicRoot);continue;}
                    for(int j=0;j<42;j++) {double middle=(lo+hi)/2;if(sign*slope(middle)>0)lo=middle;else hi=middle;}
                    peaks.Add((lo+hi)/2);
                }
                double peak = peaks.Max(x => Math.Abs(e.U + e.T * x + e.C2 * x * x + e.C3 * x * x * x + e.C4 * x * x * x * x)) * 1000;
                double middleMm = (e.X+e.L/2)*1000; int span = Array.FindLastIndex(supports, s => s < middleMm);
                double region = span < 0 ? supports[0] : span == supports.Length - 1 ? lengthMm - supports.Last() : supports[span + 1] - supports[span];
                double use = peak / (region / denominator); if (use > result.DeflectionUtilization) { result.DeflectionUtilization = use; result.GoverningLengthMm = region; }
                result.DeflectionMm = Math.Max(result.DeflectionMm, peak);
            }
            result.DeflectionEvaluated=true;
            return result;
        }
        private void AddForcePeaks(Element e)
        {
            double end=e.L;
            MomentKnM=Math.Max(MomentKnM,Math.Max(Math.Abs(2*e.C2*rigidity),Math.Abs((2*e.C2+6*e.C3*end+12*e.C4*end*end)*rigidity)));
            if(Math.Abs(e.C4)>1e-20)
            {
                double x=-e.C3/(4*e.C4);
                if(x>0 && x<end)MomentKnM=Math.Max(MomentKnM,Math.Abs((2*e.C2+6*e.C3*x+12*e.C4*x*x)*rigidity));
            }
            ShearKn=Math.Max(ShearKn,Math.Max(Math.Abs(6*e.C3*rigidity),Math.Abs((6*e.C3+24*e.C4*end)*rigidity)));
        }
        private static LoadedBeamAnalysis AnalyzeTwoSupports(double lengthMm,double[] supportMm,BeamDistributedLoad[] uniform,BeamPointLoad[] points,double ei,int denominator,double reactionCapacity,bool rejectFailedPrimary,bool ignoreUplift)
        {
            var supports=supportMm.OrderBy(x=>x).ToArray();double a=supports[0]/1000,b=supports[1]/1000;
            double force=0,moment=0;
            foreach(var q in uniform){double p=q.KnPerM*(q.EndMm-q.StartMm)/1000;force+=p;moment+=p*(q.StartMm+q.EndMm)/2000;}
            foreach(var p in points){force+=p.Kn;moment+=p.Kn*p.PositionMm/1000;}
            double right=(moment-force*a)/(b-a);
            var result=new LoadedBeamAnalysis {rigidity=ei,TotalLoadKn=force,Reactions=new[] {force-right,right}};
            var nodes=new[] {0.0,lengthMm}.Concat(supports).Concat(uniform.SelectMany(q=>new[] {q.StartMm,q.EndMm})).Concat(points.Select(p=>p.PositionMm)).OrderBy(x=>x).ToList();
            for(int i=nodes.Count-1;i>0;i--)if(nodes[i]-nodes[i-1]<1e-7)nodes.RemoveAt(i);
            double v=0,m=0,u=0,t=0,ua=0,ub=0;
            for(int i=0;i<nodes.Count-1;i++)
            {
                double x=nodes[i],l=(nodes[i+1]-x)/1000,mid=(x+nodes[i+1])/2;
                if(Math.Abs(x-supports[0])<1e-7){v+=result.Reactions[0];ua=u;}
                if(Math.Abs(x-supports[1])<1e-7){v+=result.Reactions[1];ub=u;}
                foreach(var p in points)if(Math.Abs(p.PositionMm-x)<1e-7)v-=p.Kn;
                double q=0;foreach(var load in uniform)if(load.StartMm<mid && load.EndMm>mid)q+=load.KnPerM;
                var e=new Element {X=x/1000,L=l,Q=q,U=u,T=t,C2=-m/(2*ei),C3=-v/(6*ei),C4=q/(24*ei)};
                result.elements.Add(e);result.AddForcePeaks(e);
                u=e.U+e.T*l+e.C2*l*l+e.C3*l*l*l+e.C4*l*l*l*l;
                t=e.T+2*e.C2*l+3*e.C3*l*l+4*e.C4*l*l*l;
                m+=v*l-q*l*l/2;v-=q*l;
            }
            if(Math.Abs(supports[1]-lengthMm)<1e-7)ub=u;
            double slope=(ub-ua)/(b-a),offset=ua-slope*a;
            foreach(var e in result.elements){e.U-=offset+slope*e.X;e.T-=slope;}
            return FinishAnalysis(result,lengthMm,supports,denominator,reactionCapacity,rejectFailedPrimary,ignoreUplift,uniform.Length==0,true);
        }
        private static double CubeRoot(double value)=>Math.Sign(value)*Math.Pow(Math.Abs(value),1.0/3);
        internal static bool TryCubicRoot(double a,double b,double c,double d,double lo,double hi,out double root)
        {
            if(Math.Abs(a)<1e-24)return TryQuadraticRoot(b,c,d,lo,hi,out root);
            root=double.NaN;double shift=b/(3*a),p=c/a-3*shift*shift,q=2*shift*shift*shift-shift*c/a+d/a;
            double discriminant=q*q/4+p*p*p/27;
            if(discriminant>=0)
            {
                double radical=Math.Sqrt(discriminant);
                root=CubeRoot(-q/2+radical)+CubeRoot(-q/2-radical)-shift;
            }
            else if(p<0)
            {
                double radius=2*Math.Sqrt(-p/3),angle=Math.Acos(Math.Max(-1,Math.Min(1,3*q/(p*radius))))/3;
                for(int i=0;i<3;i++)
                {
                    double candidate=radius*Math.Cos(angle-2*Math.PI*i/3)-shift;
                    if(candidate>lo && candidate<hi){root=candidate;break;}
                }
            }
            if(!Finite(root) || root<=lo || root>=hi)return false;
            double ar=a*root*root*root,br=b*root*root,cr=c*root;
            return Math.Abs(ar+br+cr+d)<=1e-12*(Math.Abs(ar)+Math.Abs(br)+Math.Abs(cr)+Math.Abs(d));
        }
        internal static bool TryQuadraticRoot(double a,double b,double c,double lo,double hi,out double root)
        {
            root=double.NaN;
            if(a==0) {if(b!=0)root=-c/b;}
            else
            {
                double disc=b*b-4*a*c;
                if(disc<0 || !Finite(disc))return false;
                // This form avoids subtracting nearly equal values. The second root
                // follows from the product c/a; zero q is the repeated zero root.
                double q=-.5*(b+(b>=0 ? Math.Sqrt(disc) : -Math.Sqrt(disc)));
                double first=q/a,second=q==0 ? 0 : c/q;
                root=first>lo && first<hi ? first : second;
            }
            if(!Finite(root) || root<=lo || root>=hi)return false;
            double ar=a*root*root,br=b*root;
            return Math.Abs(ar+br+c)<=1e-12*(Math.Abs(ar)+Math.Abs(br)+Math.Abs(c));
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
