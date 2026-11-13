using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;
using Geometry=EngineeringDashboard.TransferredSlabOptimizer.Geometry;
using Bound=EngineeringDashboard.TransferNetworkOptimizer.ReceiverAggregateLoadBound;

// Frozen df9a726 reference builder below is intentionally the PRE-change body.
// Compare actual view artifacts and source callbacks, rather than a second key implementation.
internal static class UniversalViewAllocationTests
{
    static int checks,nativeEntries,randomCases;
    static readonly BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly Func<TowerInput,Geometry[],double,BeamDistributedLoad[],Func<double,double,bool>,BeamDistributedLoad[],Bound.UniversalView> Fast=
        typeof(Bound).GetMethod("BuildUniversalView",hidden).CreateDelegate<Func<TowerInput,Geometry[],double,BeamDistributedLoad[],Func<double,double,bool>,BeamDistributedLoad[],Bound.UniversalView>>();
    static void Check(bool value,string text){checks++;if(!value)throw new Exception("Universal view allocation: "+text);}
    static bool Finite(double value)=>TowerBeamAnalysis.Finite(value);
    static TowerInput Input()=>new TowerInput {EngineerSearch=true,FullLoadingOnly=true,StockEndSetbackMm=150,LegCapacityKn=70,DeflectionDenominator=270,IgnoreUplift=true};
    static BeamDistributedLoad Q(double a,double b,double q)=>new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q};
    static long[] Bits(BeamDistributedLoad[] loads)=>loads?.SelectMany(q=>new[]{BitConverter.DoubleToInt64Bits(q.StartMm),BitConverter.DoubleToInt64Bits(q.EndMm),BitConverter.DoubleToInt64Bits(q.KnPerM)}).ToArray();
    static bool Same<T>(T[] a,T[] b)=>a==null ? b==null : b!=null && a.SequenceEqual(b);
    static Geometry G(double start,double length,double[] supports)=>new Geometry {Stock=new[]{new TowerStockPiece {StartMm=start,StockLengthMm=length,FirstSupport=0,LastSupport=(supports?.Length??0)-1}},MemberSupports=new[]{supports},Positions=supports,Joined=supports==null ? null : Enumerable.Repeat(true,Math.Max(0,supports.Length-1)).ToArray()};
    static void Compare(Geometry[] source,double edge,BeamDistributedLoad[] loads,BeamDistributedLoad[] normal,int policy=0,double[] primitive=null)
    {
        var a=new List<Tuple<double,double>>();var b=new List<Tuple<double,double>>();
        Func<double,double,bool> Window(List<Tuple<double,double>> calls)=>(s,l)=>{calls.Add(Tuple.Create(s,l));return (primitive==null || Bound.PrimitiveWindowSafe(primitive,s,l)) && (policy==0 || policy==2 && s+l<5000);};
        var expected=Reference(Input(),source,edge,loads,Window(a),normal);var actual=Fast(Input(),source,edge,loads,Window(b),normal);
        Check(a.SequenceEqual(b),"stock-window validation queries retain exact order, equality and short circuit");
        Check((actual==null)==(expected==null),"unsupported metadata retains exactly the old whole-view unknown decision");if(actual==null)return;
        Check(actual.Kept.SequenceEqual(expected.Kept) && ReferenceEquals(actual.Kept,source)==ReferenceEquals(expected.Kept,source),"every surviving occurrence/reference and original-array fast path is unchanged");
        Check(actual.Mask.SequenceEqual(expected.Mask) && actual.Counts==expected.Counts,"entire original-parent mask and station-count union are unchanged");
        Check(ReferenceEquals(actual.FirstFailed,expected.FirstFailed) && actual.FirstRun==expected.FirstRun && Same(Bits(actual.FirstLoads),Bits(expected.FirstLoads)),"first failing geometry/run and exact clipped failure loads are unchanged");
        Check(actual.FirstNormalFailure.HasValue==expected.FirstNormalFailure.HasValue && actual.Weight==expected.Weight,"normal diagnostic kind and retained byte accounting are unchanged");
        if(actual.FirstNormalFailure.HasValue)
        {
            var x=actual.FirstNormalFailure.Value;var y=expected.FirstNormalFailure.Value;
            Check(x.Name==y.Name && x.Unit==y.Unit && new[]{x.Demand,x.Limit,x.CellStart,x.CellEnd,x.Density}.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(new[]{y.Demand,y.Limit,y.CellStart,y.CellEnd,y.Density}.Select(BitConverter.DoubleToInt64Bits)),"all normal failure demand/location bits are unchanged");
        }
    }
    static void RandomAndInvalid()
    {
        var random=new Random(37409);
        for(int sample=0;sample<120;sample++)
        {
            double length=900+random.Next(3000),start=random.Next(3)==0 ? -25 : random.Next(4)*25.0;
            var source=Enumerable.Range(0,12).Select(i=>G(start,length,Enumerable.Range(0,2+i%6).Select(_=>random.NextDouble()*length).OrderBy(x=>x).ToArray())).ToArray();
            source=source.Concat(new[]{source[0],source[3]}).ToArray();
            if(sample%4==0)
            {
                var second=G(start+length,1200,new[]{150.0,1050});var first=source[0];
                source[0]=new Geometry {Stock=first.Stock.Concat(second.Stock).ToArray(),MemberSupports=first.MemberSupports.Concat(second.MemberSupports).ToArray(),Positions=first.Positions.Concat(second.Positions.Select(x=>x+length)).ToArray()};
            }
            var loads=new[]{Q(-100,5000,random.NextDouble()*80-20),Q(400,400+5e-8,1e10),Q(600,1500,random.NextDouble()*80-20),Q(1500,1500+1e-7,0)};
            var normal=sample%3==0 ? null : new[]{Q(-100,5000,random.NextDouble()*60)};
            foreach(double edge in new[]{0.0,-5e-8,25.0})foreach(int policy in new[]{0,1,2}){Compare(source,edge,loads,normal,policy);randomCases++;}
        }
        var good=G(0,2000,new[]{150.0,1850});var bad=G(0,2000,new[]{100.0,100});
        foreach(var supports in new[]{new[]{0.0,2000},new[]{-0.0,2000},new[]{double.NaN,1500},new[]{150.0,double.PositiveInfinity},new[]{-1e-20,1500},new[]{150.0,2000+1e-7},new[]{100.0,100},new[]{1500.0,1000},Array.Empty<double>(),null})
            Compare(new[]{good,G(0,2000,supports)},0,new[]{Q(0,2000,50)},new[]{Q(0,2000,100)});
        Compare(null,0,new[]{Q(0,2000,1)},null);Compare(new[]{bad,null},0,new[]{Q(0,2000,1)},null);
        foreach(double edge in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})Compare(new[]{good},edge,new[]{Q(0,2000,1)},null);
        Compare(new[]{good},0,new[]{Q(0,2000,1)},null,1);
    }
    static void NativeSources()
    {
        for(int scenario=1;scenario<=3;scenario++)
        {
            var common=ReportedScenarioTests.DiagnosticPart(scenario,1).Item2.CreateTowerInput();common.CoordinateCrossingLayouts=true;
            var network=common.Network;double coverage=network.SharedEnd(common.LengthMm)-network.SharedStart;
            var bound=Bound.TryCreate(common,network,coverage,true,new TransferredSlabOptimizer.GeometryCache());Check(bound!=null,"actual reported source is in the existing certificate domain");
            foreach(int beam in new[]{0,network.Beams.Length-1})
            {
                var local=common.CopyForSearch();local.LengthMm=network.Beams[beam].SpanLength(common.LengthMm);local.LongitudinalStartMm=network.Beams[beam].SpanStartMm;
                var cover=TransferredSlabOptimizer.StockCovers(local).First();
                var source=EngineerTowerSearch.Geometries(cover,false,local,0,Math.Max(8,2*cover.Length)).Take(96).ToArray();Check(source.Length>0,"actual permitted cover supplies native retained geometry samples");nativeEntries+=source.Length;
                double edge=local.CoverStart(cover.Sum());var average=bound.UniversalAverage(beam);var normal=bound.ExteriorNormalWitness(beam)?.Cells;var primitive=bound.UniversalClippingIntervals(beam);
                Compare(source,edge,average,normal,0,primitive);Compare(source,edge+5e-8,average,normal,0,primitive);Compare(source,edge,average,normal,1,primitive);
            }
        }
    }
    static void KeyContract<T>() where T:struct,IEquatable<T>
    {
        var ctor=typeof(T).GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(double),typeof(double)},null);
        var p=Expression.Parameter(typeof(double));var q=Expression.Parameter(typeof(double));var key=Expression.Lambda<Func<double,double,T>>(Expression.New(ctor,p,q),p,q).Compile();
        double payloadNaN=BitConverter.Int64BitsToDouble(unchecked((long)0xfff0000000000001UL));
        var values=new[]{0.0,-0.0,1.0,-1.0,double.Epsilon,double.NaN,payloadNaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue};
        var old=new Dictionary<Tuple<double,double>,int>();var actual=new Dictionary<T,int>();int index=0;
        foreach(double a in values)foreach(double b in values){old[Tuple.Create(a,b)]=index;actual[key(a,b)]=index++;}
        foreach(double a in values)foreach(double b in values)
        {
            Check(old.TryGetValue(Tuple.Create(a,b),out int x)==actual.TryGetValue(key(a,b),out int y) && x==y,"lookup semantics preserve Double.Equals for signed zero and arbitrary NaN payloads");
            foreach(double c in values)Check(Tuple.Create(a,b).Equals(Tuple.Create(c,b))==key(a,b).Equals(key(c,b)),"value-key equality matches original Tuple equality");
        }
        int sum=0;for(int i=0;i<2048;i++){actual.TryGetValue(key(1,1),out int x);sum+=x;old.TryGetValue(Tuple.Create(1.0,1.0),out int y);sum+=y;}
        const int visits=16384;long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++){actual.TryGetValue(key(1,1),out int value);sum+=value;}long valueBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<visits;i++){old.TryGetValue(Tuple.Create(1.0,1.0),out int value);sum+=value;}long tupleBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(sum>0 && valueBytes<4096 && tupleBytes-valueBytes>=visits*16,"actual private value-key dictionary lookups avoid per-query tuple allocation with equal values");
        Console.WriteLine("WINDOW_KEY_ALLOCATIONS visits="+visits+" oldTupleBytes="+tupleBytes+" valueKeyBytes="+valueBytes);
    }
    static void BuilderAllocations()
    {
        var input=Input();var geometry=G(0,2000,new[]{150.0,1850});var source=Enumerable.Repeat(geometry,4096).ToArray();var loads=new[]{Q(0,2000,1)};
        Reference(input,source,0,loads,null,null);Fast(input,source,0,loads,null,null);
        long before=GC.GetAllocatedBytesForCurrentThread();var expected=Reference(input,source,0,loads,null,null);long oldBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();var actual=Fast(input,source,0,loads,null,null);long valueBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(expected.Kept.SequenceEqual(actual.Kept) && oldBytes-valueBytes>=source.Length*32,"complete hot builder preserves the full view while removing bounded per-visited-run allocations");
        Console.WriteLine("BUILDER_ALLOCATIONS visits="+source.Length+" oldBuilderBytes="+oldBytes+" valueBuilderBytes="+valueBytes+" savedBytes="+(oldBytes-valueBytes));
    }
    internal static void Run()
    {
        Check(typeof(Bound).GetMethods(hidden).Count(m=>m.Name=="BuildUniversalView")==1 && typeof(Bound).GetMethod("BuildUniversalView",hidden).GetParameters().Length==6,"original reflection entry remains unique with six parameters");
        RandomAndInvalid();NativeSources();
        var key=typeof(Bound).GetNestedType("UniversalWindowKey",BindingFlags.NonPublic);Check(key!=null && key.IsValueType,"window key is a private value type");
        typeof(UniversalViewAllocationTests).GetMethod("KeyContract",hidden).MakeGenericMethod(key).Invoke(null,null);BuilderAllocations();
        Console.WriteLine("PASS "+checks+" universal view allocation checks: "+randomCases+" random/tiny/unknown cases; "+nativeEntries+" actual native geometry entries; full old-builder view/mask/first-failure parity");
    }
            private static Bound.UniversalView Reference(TowerInput input,TransferredSlabOptimizer.Geometry[] source,double edge,BeamDistributedLoad[] average,Func<double,double,bool> windowSafe=null,BeamDistributedLoad[] normalWitness=null)
            {
                if(source==null || !Finite(edge))return null;
                int kept=0,index=0;var mask=new ulong[(int)((source.Length+63L)/64)];var windows=new Dictionary<Tuple<double,double>,BeamDistributedLoad[]>();
                var members=new TransferredSlabOptimizer.ResponseCache<CrossBeamSupportOptimizer.NecessaryMemberKey,bool>(4096);
                var result=new Bound.UniversalView();
                foreach(var geometry in source)
                {
                    input.Trace?.CheckCancellation();bool fits=true;
                    if(geometry?.Stock==null || geometry.MemberSupports==null || geometry.Stock.Length!=geometry.MemberSupports.Length)return null;
                    for(int run=0;run<geometry.Stock.Length;run++)
                    {
                        var member=geometry.Stock[run];var supports=geometry.MemberSupports[run];if(member==null)return null;
                        double start=member.StartMm+edge,length=member.StockLengthMm;
                        if(!Finite(start) || !Finite(length) || length<=0 || supports==null || supports.Length<2 || supports.Any(x=>!Finite(x) || x<0 || x>length))return null;
                        for(int i=1;i<supports.Length;i++)if(supports[i]<=supports[i-1])return null;
                        var window=Tuple.Create(start,length);
                        if(!windows.TryGetValue(window,out var loads))
                        {
                            if(windowSafe!=null && !windowSafe(start,length))windows[window]=loads=null;
                            else
                            {
                                var clipped=new List<BeamDistributedLoad>();
                                foreach(var q in average)
                                {
                                    double a=Math.Max(0,Math.Max(start,q.StartMm)-start),b=Math.Min(length,Math.Min(start+length,q.EndMm)-start);
                                    if(b>a+1e-7)clipped.Add(new BeamDistributedLoad {StartMm=a,EndMm=b,KnPerM=q.KnPerM});
                                }
                                windows[window]=loads=clipped.ToArray();
                            }
                        }
                        if(loads==null && normalWitness==null)continue;
                        TransferredSlabOptimizer.NecessarySupportHash(geometry,run,input.Trace,out int polynomial,out int power);
                        var key=new CrossBeamSupportOptimizer.NecessaryMemberKey(start,length,0,supports,polynomial,power);
                        if(!members.TryGetValue(key,out bool memberFits))
                        {
                            Bound.NormalWitnessFailure normalFailure=default;
                            memberFits=normalWitness==null || Bound.ExteriorNormalFits(input,start,length,supports,normalWitness,out normalFailure);
                            if(!memberFits)
                            {
                                input.Trace?.Increment(BeamSearchCounter.ExteriorNormalMembersRejected);
                                if(result.FirstFailed==null)result.FirstNormalFailure=normalFailure;
                            }
                            else memberFits=loads==null || CrossBeamSupportOptimizer.GenericPrimaryForcesFit(input,length,supports,loads,null);
                            input.Trace?.CheckCancellation();members.Store(key,memberFits);
                        }
                        if(memberFits)continue;
                        fits=false;if(result.FirstFailed==null){result.FirstFailed=geometry;result.FirstRun=run;result.FirstLoads=loads;}break;
                    }
                    if(fits)
                    {
                        mask[index/64]|=1UL<<(index%64);kept++;
                        int stations=geometry.Positions?.Length??geometry.MemberSupports.Sum(m=>m.Length);
                        result.Counts|=stations<64 ? 1UL<<stations : ulong.MaxValue;
                    }
                    index++;
                }
                input.Trace?.CheckCancellation();result.Mask=mask;
                if(kept==source.Length)result.Kept=source;
                else
                {
                    result.Kept=new TransferredSlabOptimizer.Geometry[kept];int at=0;
                    for(int i=0;i<source.Length;i++)if((mask[i/64]&(1UL<<(i%64)))!=0)result.Kept[at++]=source[i];
                }
                result.Weight=512L+source.Length*8L+(ReferenceEquals(result.Kept,source) ? 0 : result.Kept.Length*8L)+mask.Length*8L+(result.FirstLoads?.Length??0)*64L;return result;
            }
}
