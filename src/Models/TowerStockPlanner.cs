using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard
{
    public sealed class TowerStockPiece
    {
        public double StockLengthMm { get; internal set; }
        public bool IsCustomLength { get; internal set; }
        public double StartMm { get; internal set; }
        public double EndMm => StartMm + StockLengthMm;
        public int FirstSupport { get; internal set; }
        public int LastSupport { get; internal set; }
        public double StrengthUtilization {get;internal set;}
        public double MomentKnM { get; internal set; }
        public double ShearKn { get; internal set; }
        public double DeflectionMm { get; internal set; }
        public double DeflectionUtilization { get; internal set; }
        public double GoverningDeflectionMm { get; internal set; }
        public double GoverningDeflectionLimitMm { get; internal set; }
        public string GoverningDeflectionRegion { get; internal set; }
    }
    public sealed class TowerStockPlan
    {
        public TowerStockPiece[] Pieces { get; internal set; }
        public double[] Reactions { get; internal set; }
        public double MomentKnM => Pieces.Max(x => x.MomentKnM);
        public double ShearKn => Pieces.Max(x => x.ShearKn);
        public double DeflectionMm => Pieces.Max(x => x.DeflectionMm);
        public double DeflectionUtilization => Pieces.Max(x => x.DeflectionUtilization);
        public int TotalH20Count => Primary.IsWS10 ? 0 : Pieces.Length * 4;
        public double TotalStockLengthMm => Primary.IsWS10 ? 0 : Pieces.Sum(x => x.StockLengthMm) * 4;
        public PrimaryBeamMaterial PrimaryMaterial {get;internal set;}
        public PrimaryBeamProfile Primary=>PrimaryBeamProfile.For(PrimaryMaterial);
        public int TotalPrimaryCount=>Pieces.Length*2*Primary.CountPerLine;
        public int WS10Count=>Primary.IsWS10 ? TotalPrimaryCount : 0;
        public double PrimaryLengthMm=>Pieces.Sum(x=>x.StockLengthMm)*2*Primary.CountPerLine;
        public double StrengthUtilization=>Pieces.Max(x=>x.StrengthUtilization);
        internal double Utilization;
        public string Schedule
        {
            get
            {
                var lines = new List<string> { "Same stock arrangement on both primary lines: "+Primary.Description+" per line. No cutting, overlaps or projection beyond concrete ends. Member breaks have no shared support or bending continuity." };
                for (int i = 0; i < Pieces.Length; i++)
                {
                    var p = Pieces[i];
                    lines.Add(string.Format(CultureInfo.CurrentCulture, "P{0}: {9} × {10} · {1:0.###} mm · x={2:0.###}–{3:0.###} mm · supports {4}–{5}\n  M {6:0.00} kNm · V {7:0.00} kN · d {8:0.00} mm",
                        i + 1, p.StockLengthMm, p.StartMm, p.EndMm, p.FirstSupport + 1, p.LastSupport + 1, p.MomentKnM, p.ShearKn, p.DeflectionMm, 2*Primary.CountPerLine, Primary.Name) + (p.IsCustomLength ? " · custom length (25 mm increments)" : ""));
                    lines.Add($"  Deflection: {p.GoverningDeflectionMm:0.00}/{p.GoverningDeflectionLimitMm:0.00} mm · {p.GoverningDeflectionRegion}");
                }
                lines.Add("Stock quantities: " + string.Join("; ", Pieces.GroupBy(p => p.StockLengthMm).OrderBy(g => g.Key).Select(g => string.Format(CultureInfo.CurrentCulture, "{0} × {1:0.###} mm", g.Count() * 2*Primary.CountPerLine, g.Key))));
                if(Primary.IsWS10)lines.Add(Primary.Assumptions);
                return string.Join("\n", lines);
            }
        }
    }
    /// <summary>Exact uncut stock cover, no overlaps. Independent members have free ends and at least two supports each.
    /// No support at a member break is allowed; support-centre end setback is a planning input, not a bearing check.</summary>
    public static class TowerStockPlanner
    {
        // One engineer search repeatedly checks the same independent stock at
        // different global offsets and tower widths. Its numerical response is
        // determined solely by these exact local coordinates and loads. Keep
        // acceptance, stock selection and diagnostics outside this bounded cache.
        internal sealed class FindWorkCache
        {
            private struct Key : IEquatable<Key>
            {
                private readonly double length,load,ei;
                private readonly double[] supports;
                private readonly int hash;
                internal Key(double length,double[] supports,double load,double ei)
                {
                    this.length=length;this.supports=supports;this.load=load;this.ei=ei;
                    unchecked {int h=length.GetHashCode();h=h*31+load.GetHashCode();h=h*31+ei.GetHashCode();foreach(double x in supports)h=h*31+x.GetHashCode();hash=h;}
                }
                public bool Equals(Key other)
                {
                    if(hash!=other.hash || length!=other.length || load!=other.load || ei!=other.ei || supports.Length!=other.supports.Length)return false;
                    for(int i=0;i<supports.Length;i++)if(supports[i]!=other.supports[i])return false;
                    return true;
                }
                public override bool Equals(object other)=>other is Key && Equals((Key)other);
                public override int GetHashCode()=>hash;
            }
            private readonly TransferredSlabOptimizer.ResponseCache<Key,TowerBeamAnalysis> responses;
            private sealed class PlanResult {internal TowerStockPlan Plan;internal TowerSearchDiagnostics Diagnostics;}
            private TransferredSlabOptimizer.ResponseCache<Key,PlanResult> plans=new TransferredSlabOptimizer.ResponseCache<Key,PlanResult>(2048);
            private TowerInput context;
            private bool collectDiagnostics,lazyLocations;
            internal readonly bool OptimizeStockLoop;
            internal int PlanHits {get;private set;}
            internal FindWorkCache(int capacity=8192,bool optimizeStockLoop=true) {responses=new TransferredSlabOptimizer.ResponseCache<Key,TowerBeamAnalysis>(capacity);OptimizeStockLoop=optimizeStockLoop;}
            private bool SameContext(TowerInput input)
                =>context!=null && context.PrimaryMaterial==input.PrimaryMaterial
                    && context.LegCapacityKn==input.LegCapacityKn && context.DeflectionLimitMm==input.DeflectionLimitMm
                    && context.DeflectionDenominator==input.DeflectionDenominator && context.StockEndSetbackMm==input.StockEndSetbackMm
                    && context.IgnoreUplift==input.IgnoreUplift && context.AllowCustomPrimaryLengths==input.AllowCustomPrimaryLengths
                    && context.DiagnosticOffsetMm==input.DiagnosticOffsetMm && context.StockLengthsMm.SequenceEqual(input.StockLengthsMm)
                    && collectDiagnostics==(input.Diagnostics!=null) && lazyLocations==(input.Diagnostics?.LazyLocations??true)
                    && (!collectDiagnostics || context.Diagnostics.SameConfiguration(input.Diagnostics));
            internal TowerStockPlan Find(TowerInput input,double[] positions,double load)
            {
                input.Trace?.CheckCancellation();
                if(!SameContext(input))
                {
                    context=input.CopyForSearch();context.StockLengthsMm=(double[])input.StockLengthsMm.Clone();context.Diagnostics=input.Diagnostics?.Fork();
                    collectDiagnostics=input.Diagnostics!=null;lazyLocations=input.Diagnostics?.LazyLocations??true;
                    plans=new TransferredSlabOptimizer.ResponseCache<Key,PlanResult>(2048);
                }
                var key=new Key(input.LengthMm,positions,load,input.Primary.EI);
                if(plans.TryGetValue(key,out var found))
                {
                    PlanHits++;
                    if(found.Diagnostics!=null)input.Diagnostics?.Merge(found.Diagnostics);
                    return ClonePlan(found.Plan);
                }
                var diagnostic=input.Diagnostics?.Fork();
                TowerStockPlan plan;
                try {plan=FindCore(input,positions,load,this,diagnostic);}
                finally {if(diagnostic!=null)input.Diagnostics?.Merge(diagnostic);}
                input.Trace?.CheckCancellation();
                // Keys own their coordinates; neither the geometry caller nor a
                // returned mutable stock schedule can alter retained evidence.
                plans.Store(new Key(input.LengthMm,(double[])positions.Clone(),load,input.Primary.EI),new PlanResult {Plan=plan,Diagnostics=diagnostic});
                return ClonePlan(plan);
            }
            private static TowerStockPlan ClonePlan(TowerStockPlan plan)
            {
                if(plan==null)return null;
                return new TowerStockPlan {PrimaryMaterial=plan.PrimaryMaterial,Utilization=plan.Utilization,Reactions=(double[])plan.Reactions.Clone(),
                    Pieces=plan.Pieces.Select(p=>new TowerStockPiece {StockLengthMm=p.StockLengthMm,IsCustomLength=p.IsCustomLength,StartMm=p.StartMm,FirstSupport=p.FirstSupport,LastSupport=p.LastSupport,
                        StrengthUtilization=p.StrengthUtilization,MomentKnM=p.MomentKnM,ShearKn=p.ShearKn,DeflectionMm=p.DeflectionMm,DeflectionUtilization=p.DeflectionUtilization,
                        GoverningDeflectionMm=p.GoverningDeflectionMm,GoverningDeflectionLimitMm=p.GoverningDeflectionLimitMm,GoverningDeflectionRegion=p.GoverningDeflectionRegion}).ToArray()};
            }
            internal TowerBeamAnalysis Analyze(TowerInput input,double length,double[] supports,double load,double ei)
                =>AnalyzeCore(input,length,supports,load,ei,false);
            // Only FindCore may transfer its freshly allocated local supports;
            // the existing cache entry point keeps caller-owned input isolated.
            internal TowerBeamAnalysis AnalyzeOwned(TowerInput input,double length,double[] supports,double load,double ei)
                =>AnalyzeCore(input,length,supports,load,ei,true);
            private TowerBeamAnalysis AnalyzeCore(TowerInput input,double length,double[] supports,double load,double ei,bool ownSupports)
            {
                input.Trace?.CheckCancellation();var key=new Key(length,supports,load,ei);
                if(responses.TryGetValue(key,out var found)){input.Trace?.Increment(BeamSearchCounter.PrimaryCacheHits);return found;}
                input.Trace?.Increment(BeamSearchCounter.PrimaryAnalyses);
                TowerBeamAnalysis response;
                using(input.Trace?.Measure(BeamSearchStage.PrimaryAnalysis))
                    response=ownSupports ? TowerBeamAnalysis.AnalyzeOwned(length,supports,load,ei) : TowerBeamAnalysis.Analyze(length,supports,load,ei);
                input.Trace?.CheckCancellation();responses.Store(key,response);return response;
            }
        }
        internal static bool UniformMemberForcesFit(TowerInput input,double length,double[] supports,double load)
        {
            // A UDL changes shear by q*l across each clear interval; a free tail
            // contributes q*a shear and q*a²/2 moment. A support jump cannot
            // exceed twice the shear capacity. These are necessary conditions,
            // independent of continuity, deflection or the selected stock plan.
            if(input.Primary.IsWS10)return true;
            double force=load*length,capacity=supports.Length*Math.Min(input.LegCapacityKn,2*input.Primary.ShearLimitKn);
            if(ExceedsNecessaryBound(force,capacity))return false;
            double near=supports[0],far=length-supports[supports.Length-1];
            if(ExceedsNecessaryBound(load*near,input.Primary.ShearLimitKn) || ExceedsNecessaryBound(load*far,input.Primary.ShearLimitKn)
                || ExceedsNecessaryBound(load*near*near/2,input.Primary.MomentLimitKnM) || ExceedsNecessaryBound(load*far*far/2,input.Primary.MomentLimitKnM))return false;
            for(int i=1;i<supports.Length;i++)if(ExceedsNecessaryBound(load*(supports[i]-supports[i-1]),2*input.Primary.ShearLimitKn))return false;
            return true;
        }
        private static bool ExceedsNecessaryBound(double demand,double capacity)
            =>demand>capacity+1e-6*Math.Max(1,Math.Max(Math.Abs(demand),Math.Abs(capacity)));
        private static bool UniformMemberForcesFitAt(TowerInput input,double length,double[] positions,int start,int first,int last,double load)
        {
            if(input.Primary.IsWS10)return true;
            double force=load*length,capacity=(last-first+1)*Math.Min(input.LegCapacityKn,2*input.Primary.ShearLimitKn);
            if(ExceedsNecessaryBound(force,capacity))return false;
            double near=(positions[first]-start)/1000,far=length-(positions[last]-start)/1000;
            if(ExceedsNecessaryBound(load*near,input.Primary.ShearLimitKn) || ExceedsNecessaryBound(load*far,input.Primary.ShearLimitKn)
                || ExceedsNecessaryBound(load*near*near/2,input.Primary.MomentLimitKnM) || ExceedsNecessaryBound(load*far*far/2,input.Primary.MomentLimitKnM))return false;
            double previous=near;
            for(int i=first+1;i<=last;i++)
            {
                double current=(positions[i]-start)/1000;
                if(ExceedsNecessaryBound(load*(current-previous),2*input.Primary.ShearLimitKn))return false;
                previous=current;
            }
            return true;
        }
        private static bool FiniteNecessaryMargin(double demand,double capacity)
            =>TowerBeamAnalysis.Finite(demand) && demand>=0 && TowerBeamAnalysis.Finite(capacity) && capacity>0
                && TowerBeamAnalysis.Finite(capacity+1e-6*Math.Max(1,Math.Max(Math.Abs(demand),Math.Abs(capacity))));
        // For fixed first/last supports H20 has fixed q, near tail and gaps.
        // Force and the nonnegative far-tail demands increase with stock length.
        // Certify every original intermediate before relying on that monotonicity:
        // the original predicate deliberately remains the authority at each probe.
        internal static bool TryUniformFailureSuffix(TowerInput input,double[] stocks,double[] positions,int start,int first,int last,int begin,int end,double q,TowerSearchDiagnostics diagnostics,out int failedFrom)
        {
            failedFrom=end;
            if(input.Primary.IsWS10 || !TowerBeamAnalysis.Finite(q) || q<0)return false;
            double moment=input.Primary.MomentLimitKnM,shear=input.Primary.ShearLimitKn;
            double maximumLength=stocks[end-1]/1000,near=(positions[first]-start)/1000,lastPosition=(positions[last]-start)/1000;
            double minimumFar=stocks[begin]/1000-lastPosition,maximumFar=maximumLength-lastPosition;
            double forceCapacity=(last-first+1)*Math.Min(input.LegCapacityKn,2*shear);
            double nearShear=q*near,farShear=q*maximumFar,nearProduct=nearShear*near,farProduct=farShear*maximumFar;
            if(near<0 || minimumFar<0 || !FiniteNecessaryMargin(q*maximumLength,forceCapacity)
                || !FiniteNecessaryMargin(nearShear,shear) || !FiniteNecessaryMargin(farShear,shear)
                || !TowerBeamAnalysis.Finite(nearProduct) || !TowerBeamAnalysis.Finite(farProduct)
                || !FiniteNecessaryMargin(nearProduct/2,moment) || !FiniteNecessaryMargin(farProduct/2,moment))return false;
            double previous=near;
            for(int i=first+1;i<=last;i++)
            {
                double current=(positions[i]-start)/1000;
                if(!FiniteNecessaryMargin(q*(current-previous),2*shear))return false;
                previous=current;
            }
            bool Passes(int index)
            {
                input.Trace?.CheckCancellation();diagnostics?.CheckCancellation();
                return UniformMemberForcesFitAt(input,stocks[index]/1000,positions,start,first,last,q);
            }
            if(!Passes(begin)){failedFrom=begin;return true;}
            if(end-begin==1 || Passes(end-1))return true;
            int lo=begin+1,hi=end-1;
            while(lo<hi)
            {
                int mid=(lo+hi)/2;if(Passes(mid))lo=mid+1;else hi=mid;
            }
            failedFrom=lo;return true;
        }
        private static bool OrderedStockSupports(double[] positions,double length)
        {
            if(!TowerBeamAnalysis.Finite(length) || length<=0)return false;
            for(int i=0;i<positions.Length;i++)if(!TowerBeamAnalysis.Finite(positions[i]) || positions[i]<0 || positions[i]>length || i>0 && positions[i]<=positions[i-1])return false;
            return true;
        }
        // Each possible last support defines a disjoint stock interval. Search
        // with the original arithmetic/predicates, then retain all original
        // guards in FindCore; no rounded/inverted tolerance changes are needed.
        private static bool NextStockInterval(double[] stocks,double[] positions,int start,double length,double setback,ref int last,out int begin,out int end,BeamSearchTrace trace)
        {
            while(++last<positions.Length)
            {
                trace?.CheckCancellation();int lo=0,hi=stocks.Length;
                while(lo<hi)
                {
                    int mid=(lo+hi)/2;double finish=start+stocks[mid];
                    bool before=!(positions[last]<finish-1e-7) || finish-positions[last]<setback-1e-7
                        || last+1==positions.Length && finish<length-1e-7;
                    if(before)lo=mid+1;else hi=mid;
                }
                begin=lo;hi=stocks.Length;
                while(lo<hi)
                {
                    int mid=(lo+hi)/2;double finish=start+stocks[mid];
                    bool after=finish>length+1e-7 || last+1<positions.Length
                        && (positions[last+1]<finish-1e-7 || positions[last+1]<finish+setback-1e-7);
                    if(after)hi=mid;else lo=mid+1;
                }
                end=lo;if(begin<end)return true;
            }
            begin=end=stocks.Length;return false;
        }
        // NextStockInterval encodes every remaining stock guard except the
        // nonfinal-member test abs(finish-length)<epsilon. Finite subtraction is
        // monotone over sorted stocks; abs(d)<epsilon is exactly the open range
        // -epsilon<d<epsilon, including signed zero and tolerance endpoints.
        private static int EligibleStockIntervalCount(double[] stocks,int start,double length,int begin,int end,bool finalSupport)
        {
            if(finalSupport)return end-begin;
            double firstDifference=(start+stocks[begin])-length,lastDifference=(start+stocks[end-1])-length;
            if(lastDifference<= -1e-7 || firstDifference>=1e-7)return end-begin;
            if(firstDifference> -1e-7 && lastDifference<1e-7)return 0;
            int lo=begin,hi=end;
            while(lo<hi)
            {
                int mid=(lo+hi)/2;double finish=start+stocks[mid],difference=finish-length;
                if(difference> -1e-7)hi=mid;else lo=mid+1;
            }
            int excludedBegin=lo;hi=end;
            while(lo<hi)
            {
                int mid=(lo+hi)/2;double finish=start+stocks[mid],difference=finish-length;
                if(difference>=1e-7)hi=mid;else lo=mid+1;
            }
            return end-begin-(lo-excludedBegin);
        }
        // Call only after the fixed H20 near-tail proof and the original first
        // support/setback guard. A whole interval has no passing transition, so
        // its exact failure count needs no per-stock allocation or traversal.
        internal static void CountNearFailureStocks(TowerInput input,double[] stocks,double[] positions,int start,int first,double length,double setback,TowerSearchDiagnostics diagnostics,ref bool analyzed,ref int failures)
        {
            int last=first;
            while(NextStockInterval(stocks,positions,start,length,setback,ref last,out int begin,out int end,input.Trace))
            {
                input.Trace?.CheckCancellation();diagnostics?.CheckCancellation();
                int count=EligibleStockIntervalCount(stocks,start,length,begin,end,last+1==positions.Length);
                // Publish only completed range proofs. Both token owners are
                // checked around each bounded logarithmic operation; an aborted
                // range contributes no invented partial failure records.
                input.Trace?.CheckCancellation();diagnostics?.CheckCancellation();
                if(count>0){analyzed=true;if(diagnostics!=null)failures+=count;}
            }
        }
        public const string DefaultLengths = "1250; 1800; 2450; 2650; 2900; 3300; 3600; 3900; 4500; 4900";
        // Keep the entered catalogue separate: only additional lengths must be multiples of 25 mm.
        internal static double[] CandidateLengths(TowerInput input, double minimum, double maximum)
        {
            var stocks = input.PrimaryStockLengthsMm.Where(x => x >= minimum && x <= maximum);
            if (!input.AllowCustomPrimaryLengths) return stocks.OrderBy(x => x).ToArray();
            int first = (int)Math.Ceiling(Math.Max(25, minimum) / 25), last = (int)Math.Floor(Math.Min(50000, maximum) / 25);
            return stocks.Concat(Enumerable.Range(first, Math.Max(0, last-first+1)).Select(x => x*25.0)).Distinct().OrderBy(x => x).ToArray();
        }
        // Evaluate the previous stock search as well. Candidate caps must never replace a better stock solution.
        internal static T SearchWithCustom<T>(TowerInput input, Func<T> search, Func<T,T,bool> better) where T : class
        {
            if (!input.AllowCustomH20Lengths) return RunPass(input,false,search);
            T stock = null;
            input.AllowCustomH20Lengths = false;
            try { stock = RunPass(input,false,search); } catch (ArgumentException) { }
            finally { input.AllowCustomH20Lengths = true; }
            if(input.FirstCompleteNetworkOnly && stock is TransferNetworkResult)return stock;
            if(input.Trace?.BudgetReached==true)return TimeLimited(stock??throw new BeamSearchDeadlineException());
            try
            {
                var custom=RunPass(input,true,search);
                var selected=stock==null || better(custom,stock) ? custom : stock;
                return input.Trace?.BudgetReached==true ? TimeLimited(selected) : selected;
            }
            catch (BeamSearchDeadlineException) { if(stock!=null)return TimeLimited(stock);throw; }
            catch (ArgumentException) { if (stock != null) return stock; throw; }
        }
        private static T TimeLimited<T>(T result) where T:class
        {
            if(result is TowerLayout layout) {layout.SearchTimeLimitReached=true;layout.MinimumLegCountProven=false;}
            if(result is TransferNetworkResult network)
            {
                network.SearchTimeLimitReached=true;
                foreach(var beam in network.Beams) {beam.SearchTimeLimitReached=true;beam.MinimumLegCountProven=false;}
            }
            return result;
        }
        private static T RunPass<T>(TowerInput input,bool custom,Func<T> search)=>input.Trace==null ? search() : input.Trace.RunPass(custom,search);
        internal static int[] CoverageCounts(int total, double[] stocks,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var counts = Enumerable.Repeat(int.MaxValue,total+1).ToArray(); counts[0]=0;
            int step=0;
            foreach(double stock in stocks) {int b=(int)stock;while(b!=0){int r=step%b;step=b;b=r;}}
            if(step==0)return counts;
            for(int end=step;end<=total;end+=step)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach(double stock in stocks)
                    if(stock<=end && counts[end-(int)stock]!=int.MaxValue) counts[end]=Math.Min(counts[end],counts[end-(int)stock]+1);
            }
            return counts;
        }
        public static double[] ParseLengths(string text)
        {
            var tokens = (text ?? "").Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens.Length > 30) throw new ArgumentException("Enter 1–30 H20 stock lengths in whole millimetres, separated by semicolons.");
            var result = new List<double>();
            foreach (string token in tokens)
            {
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int mm) || mm <= 0 || mm > 50000)
                    throw new ArgumentException("H20 stock lengths must be whole millimetres from 1 to 50,000 (for example 1250; 2450; 4900).");
                result.Add(mm);
            }
            return result.Distinct().OrderBy(x => x).ToArray();
        }
        public static int MinimumPieces(double length, double[] stocks,CancellationToken cancellationToken=default)
        {
            if (!TowerBeamAnalysis.Finite(length) || length <= 0 || length > 50000 || Math.Abs(length - Math.Round(length)) > 1e-7) return int.MaxValue;
            if(stocks.Contains(length))return 1;
            return CoverageCounts((int)Math.Round(length),stocks,cancellationToken)[(int)Math.Round(length)];
        }
        public static TowerStockPlan Find(TowerInput input, double[] positions, double load)
            =>Find(input,positions,load,null);
        internal static TowerStockPlan Find(TowerInput input, double[] positions, double load,FindWorkCache cache)
            =>cache==null ? FindCore(input,positions,load,null,input.Diagnostics) : cache.Find(input,positions,load);
        private static TowerStockPlan FindCore(TowerInput input,double[] positions,double load,FindWorkCache cache,TowerSearchDiagnostics diagnostics)
        {
            bool analyzed=false; int n = positions.Length; double length = input.LengthMm, setback = input.StockEndSetbackMm;
            var stocks = CandidateLengths(input,25,length);
            bool optimizeStockLoop=cache?.OptimizeStockLoop??false;
            bool ownSupports=optimizeStockLoop && input.Trace?.HasTimeLimit!=true && diagnostics?.HasTimeLimit!=true;
            bool intervals=optimizeStockLoop && stocks.Length>32 && OrderedStockSupports(positions,length);
            bool monotoneStockBounds=intervals && !input.Primary.IsWS10 && input.Trace?.HasTimeLimit!=true && diagnostics?.HasTimeLimit!=true;
            var states = new Dictionary<int, TowerStockPlan>();
            states[0] = new TowerStockPlan { PrimaryMaterial=input.PrimaryMaterial, Pieces = new TowerStockPiece[0], Reactions = new double[n] };
            var pending = new SortedSet<int> { 0 };
            while (pending.Count > 0)
            {
                input.Trace?.CheckCancellation();
                int start = pending.Min; pending.Remove(start); var previous = states[start];
                int first = previous.Pieces.Length == 0 ? 0 : previous.Pieces.Last().LastSupport + 1;
                if (first >= n) continue;
                if(optimizeStockLoop && (first+1>=n || positions[first]-start<setback-1e-7))continue;
                double near=(positions[first]-start)/1000;
                bool nearFits=!optimizeStockLoop || input.Primary.IsWS10 || !ExceedsNecessaryBound((load+0)*near,input.Primary.ShearLimitKn)
                    && !ExceedsNecessaryBound((load+0)*near*near/2,input.Primary.MomentLimitKnM);
                int intervalLast=first,intervalEnd=0,failedSuffix=-1;
                int nearFailures=0;
                bool batchNearFailures=!nearFits && input.Trace?.HasTimeLimit!=true && diagnostics?.HasTimeLimit!=true;
                try
                {
                if(intervals && batchNearFailures)
                {
                    CountNearFailureStocks(input,stocks,positions,start,first,length,setback,diagnostics,ref analyzed,ref nearFailures);
                    continue;
                }
                for(int stockIndex=0;stockIndex<stocks.Length;stockIndex++)
                {
                    if(intervals && stockIndex>=intervalEnd)
                    {
                        if(!NextStockInterval(stocks,positions,start,length,setback,ref intervalLast,out int begin,out intervalEnd,input.Trace))break;
                        stockIndex=Math.Max(stockIndex,begin);failedSuffix=-1;
                        if(monotoneStockBounds && TryUniformFailureSuffix(input,stocks,positions,start,first,intervalLast,stockIndex,intervalEnd,load+0,diagnostics,out int rejectedFrom))failedSuffix=rejectedFrom;
                    }
                    if(failedSuffix>=0 && stockIndex>=failedSuffix)
                    {
                        // Passing prefix work and its diagnostics have already
                        // run in original order. Only now record the proved
                        // failed suffix, before visiting the next interval.
                        input.Trace?.CheckCancellation();diagnostics?.CheckCancellation();
                        int rejected=EligibleStockIntervalCount(stocks,start,length,stockIndex,intervalEnd,intervalLast+1==n);
                        input.Trace?.CheckCancellation();diagnostics?.CheckCancellation();
                        if(rejected>0)
                        {
                            analyzed=true;
                            diagnostics?.GeometryRecorded("standalone stock force bound","A proposed primary stock cannot carry its UDL within the available support, shear or free-tail moment capacities.","Review primary stock length and support spacing.",rejected);
                        }
                        stockIndex=intervalEnd-1;failedSuffix=-1;continue;
                    }
                    input.Trace?.CheckCancellation();
                    double stock=stocks[stockIndex];
                    double end = start + stock;
                    if (end > length + 1e-7 || positions[first] - start < setback - 1e-7) continue;
                    int last=intervals ? intervalLast : first;
                    if(!intervals)while (last + 1 < n && positions[last + 1] < end - 1e-7) last++;
                    if (last <= first || end - positions[last] < setback - 1e-7) continue;
                    if (last + 1 < n && positions[last + 1] < end + setback - 1e-7) continue;
                    if (end < length - 1e-7 && last + 1 == n || Math.Abs(end - length) < 1e-7 && last + 1 != n) continue;
                    analyzed=true;TowerBeamAnalysis response;
                    if(batchNearFailures)
                    {
                        // All accepted stocks in this state have the same H20
                        // near-tail failure. Keep every original geometry guard
                        // and diagnostic cancellation checkpoint, then record
                        // the identical failures together before leaving it.
                        diagnostics?.CheckCancellation();
                        if(diagnostics!=null)nearFailures++;
                        continue;
                    }
                    double[] supports=null;
                    if(cache!=null && !optimizeStockLoop)supports=positions.Skip(first).Take(last-first+1).Select(x=>(x-start)/1000).ToArray();
                    if(cache!=null && (optimizeStockLoop
                        ? !nearFits || !UniformMemberForcesFitAt(input,stock/1000,positions,start,first,last,load+input.Primary.SelfWeightKnM(stock))
                        : !UniformMemberForcesFit(input,stock/1000,supports,load+input.Primary.SelfWeightKnM(stock))))
                    {
                        diagnostics?.Geometry("standalone stock force bound","A proposed primary stock cannot carry its UDL within the available support, shear or free-tail moment capacities.","Review primary stock length and support spacing.");
                        continue;
                    }
                    if(supports==null)supports=positions.Skip(first).Take(last-first+1).Select(x=>(x-start)/1000).ToArray();
                    if(cache!=null)
                        response=ownSupports ? cache.AnalyzeOwned(input,stock/1000,supports,load+input.Primary.SelfWeightKnM(stock),input.Primary.EI)
                            : cache.Analyze(input,stock/1000,supports,load+input.Primary.SelfWeightKnM(stock),input.Primary.EI);
                    else
                    {
                        input.Trace?.Increment(BeamSearchCounter.PrimaryAnalyses);
                        using(input.Trace?.Measure(BeamSearchStage.PrimaryAnalysis))
                            response=TowerBeamAnalysis.Analyze(stock/1000,supports,load+input.Primary.SelfWeightKnM(stock),input.Primary.EI);
                    }
                    var u = new[] { response.Reactions.Max() / input.LegCapacityKn, response.Moment / input.Primary.MomentLimitKnM,
                        response.Shear / input.Primary.ShearLimitKn, response.DeflectionUtilization(input.DeflectionLimitMm, input.DeflectionDenominator), input.Primary.Strength(response) };
                    diagnostics?.Primary(response,input,$"stock {stock:0} mm · x={start+input.DiagnosticOffsetMm:0.###}–{end+input.DiagnosticOffsetMm:0.###} mm · support x={string.Join(", ",positions.Skip(first).Take(last-first+1).Select(x=>(x+input.DiagnosticOffsetMm).ToString("0.###")))} mm · governing loaded line");
                    if ((!input.IgnoreUplift && response.Reactions.Min() < -1e-9 * response.Reactions.Max()) || u.Any(x => !TowerBeamAnalysis.Finite(x) || x > 1 + 1e-10)) continue;
                    // All member checks and diagnostics above remain ordered. A
                    // losing DP transition cannot contribute to any later state;
                    // compare its exact ranking before copying retained metadata.
                    if(optimizeStockLoop && states.TryGetValue((int)Math.Round(end),out var incumbent))
                    {
                        int nextCount=(previous.Pieces.Length+1)*2*input.Primary.CountPerLine;
                        if(!(nextCount<incumbent.TotalPrimaryCount || nextCount==incumbent.TotalPrimaryCount
                            && Math.Max(previous.Utilization,u.Max())<incumbent.Utilization))continue;
                    }
                    var region = response.GoverningDeflection(input.DeflectionLimitMm, input.DeflectionDenominator);
                    var piece = new TowerStockPiece { StartMm = start, StockLengthMm = stock, IsCustomLength = !input.PrimaryStockLengthsMm.Contains(stock), FirstSupport = first, LastSupport = last,
                        StrengthUtilization=u[4], MomentKnM = response.Moment, ShearKn = response.Shear, DeflectionMm = response.DeflectionMm, DeflectionUtilization = u[3], GoverningDeflectionMm = region.PeakMm,
                        GoverningDeflectionLimitMm = region.AllowableMm(input.DeflectionLimitMm, input.DeflectionDenominator),
                        GoverningDeflectionRegion = $"{(region.IsOverhang ? "overhang" : "span")} {region.EndMm-region.StartMm:0.###} mm" };
                    var reactions = (double[])previous.Reactions.Clone();
                    for (int k = first; k <= last; k++) reactions[k] = response.Reactions[k - first];
                    var next = new TowerStockPlan { PrimaryMaterial=input.PrimaryMaterial, Pieces = previous.Pieces.Concat(new[] { piece }).ToArray(), Reactions = reactions, Utilization = Math.Max(previous.Utilization, u.Max()) };
                    int key = (int)Math.Round(end);
                    if (!states.TryGetValue(key, out var old) || next.TotalPrimaryCount < old.TotalPrimaryCount || next.TotalPrimaryCount == old.TotalPrimaryCount && next.Utilization < old.Utilization)
                    { states[key] = next; if (end < length - 1e-7) pending.Add(key); }
                }
                }
                finally
                {
                    diagnostics?.GeometryRecorded("standalone stock force bound","A proposed primary stock cannot carry its UDL within the available support, shear or free-tail moment capacities.","Review primary stock length and support spacing.",nearFailures);
                }
            }
            if(!analyzed)diagnostics?.Geometry("primary stock supports",$"Primary stock/support geometry: no member can be checked in this support pattern with at least two supports and {setback:0.###} mm stock-end setbacks.","Review the stock lengths, support positions and end setbacks.");
            return states.TryGetValue((int)Math.Round(length), out var result) ? result : null;
        }
    }
}

