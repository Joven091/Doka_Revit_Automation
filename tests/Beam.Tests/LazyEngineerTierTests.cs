using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using EngineeringDashboard;

internal static class LazyEngineerTierTests
{
    static int checks;
    static void Check(bool passed,string message){checks++;if(!passed)throw new Exception("Lazy engineer tiers: "+message);}
    static string Bits(double value)=>BitConverter.DoubleToInt64Bits(value).ToString("X16",CultureInfo.InvariantCulture);
    static string Values(IEnumerable<double> values)=>string.Join(",",values.Select(Bits));
    static string Geometry(TransferredSlabOptimizer.Geometry g)=>Values(g.Positions)+"/"+string.Join("",g.Joined.Select(j=>j?'1':'0'))+"/"
        +string.Join(";",g.Stock.Select(p=>Values(new[] {p.StartMm,p.StockLengthMm})+":"+p.FirstSupport+":"+p.LastSupport+":"+p.IsCustomLength))+"/"
        +string.Join(";",g.MemberSupports.Select(Values));
    static string Work(Tuple<double[],bool,TransferredSlabOptimizer.Geometry,double,int> w)=>Values(w.Item1)+"/"+w.Item2+"/"+Geometry(w.Item3)+"/"+Bits(w.Item4)+"/"+w.Item5;
    static TowerInput Input(PrimaryBeamMaterial material=PrimaryBeamMaterial.DoubleH20,bool custom=false,double moment=0)=>new TowerInput
    {
        LengthMm=material==PrimaryBeamMaterial.WS10 ? 2500 : 2450,TotalLineLoad=9,TransverseLoadMoment=moment,LegCapacityKn=70,PrimaryMaterial=material,
        StockLengthsMm=new[] {1250.0,1800,2450,3900,4900},AllowCustomH20Lengths=custom,CustomH20Scope=CustomH20LengthScope.Both,
        DeflectionDenominator=270,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=0,Exclude600Standalone=true,EngineerSearch=true,EngineerStationLimit=8,
        Trace=new BeamSearchTrace("Exact lazy engineer tier comparison")
    };
    static void Enumeration()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(bool exclude in new[] {false,true})
        foreach(double moment in new[] {0.0,9.125,40.0})
        {
            var input=Input(material,moment:moment);input.LengthMm=4500;input.Exclude600Standalone=exclude;
            var covers=material==PrimaryBeamMaterial.WS10 ? new[] {new[] {4500.0},new[] {2250.0,2250},new[] {1250.0,2000,1250},new[] {500.0}}
                : new[] {new[] {3900.0},new[] {1800.0,2450},new[] {1250.0,1800,1250},new[] {1250.0}};
            var oldGenerated=new List<int>();var newGenerated=new List<int>();input.ReuseSearchWork=false;
            var expected=TowerOptimizer.EngineerWork(input,covers,generated:oldGenerated.Add).Select(Work).ToArray();input.ReuseSearchWork=true;
            var actual=TowerOptimizer.EngineerWork(input,covers,generated:newGenerated.Add).Select(Work).ToArray();
            Check(expected.SequenceEqual(actual),"complete stable work order includes every cover/rotation/count/geometry/width/initial and coefficient coordinate");
            Check(oldGenerated.Count==newGenerated.Count,"widths with differing initials reuse each count's geometry through its last tier");
            foreach(var stock in covers)foreach(bool rotated in new[] {false,true})
            {
                var all=EngineerTowerSearch.Geometries(stock,rotated,input).Select(Geometry).ToArray();
                var split=Enumerable.Range(1,8).SelectMany(count=>EngineerTowerSearch.Geometries(stock,rotated,input,count-1,count)).Select(Geometry).ToArray();
                Check(all.SequenceEqual(split),"exact count windows preserve default geometry order and within-count duplicate removal");
            }
        }
        // Valid WS10 self weights can put equal-coverage stock families on
        // opposite sides of the initial-station ceiling, without load rounding.
        var weighted=Input(PrimaryBeamMaterial.WS10);weighted.LengthMm=4500;weighted.TotalLineLoad=2*139.13/4.5;
        var paired=new[] {new[] {4500.0},new[] {2250.0,2250}};weighted.ReuseSearchWork=false;
        var old=TowerOptimizer.EngineerWork(weighted,paired).ToArray();weighted.ReuseSearchWork=true;var fresh=TowerOptimizer.EngineerWork(weighted,paired).ToArray();
        Check(old.Select(Work).SequenceEqual(fresh.Select(Work)),"stock-dependent WS10 estimates retain cross-family tie order");
        Check(old.Select(w=>w.Item5).Distinct().Count()>1,"WS10 fixture crosses an actual self-weight estimate ceiling");
        var saved=(double[])TowerOptimizer.BaysMm.Clone();
        try
        {
            for(int i=0;i<TowerOptimizer.BaysMm.Length;i++)TowerOptimizer.BaysMm[i]=600;
            var missing=Input();missing.LengthMm=3900;missing.Exclude600Standalone=true;var covers=new[] {new[] {3900.0}};
            missing.ReuseSearchWork=false;var expected=TowerOptimizer.EngineerWork(missing,covers).Select(Work).ToArray();missing.ReuseSearchWork=true;
            Check(expected.SequenceEqual(TowerOptimizer.EngineerWork(missing,covers).Select(Work)),"headers with no allowed rotated widths add no work");
        }
        finally{Array.Copy(saved,TowerOptimizer.BaysMm,saved.Length);}
    }
    static string[] IncumbentSequence(TowerInput input,double[][] covers,List<int> generated)
    {
        int maximum=int.MaxValue;var result=new List<string>();
        foreach(var work in TowerOptimizer.EngineerWork(input,covers,()=>maximum,generated.Add))
        {
            if(work.Item3.Positions.Length>maximum)continue;
            result.Add(Work(work));maximum=Math.Min(maximum,work.Item3.Positions.Length);
        }
        return result.ToArray();
    }
    static void LiveWinner()
    {
        foreach(double load in new[] {2.0,90.0})foreach(double moment in new[] {0.0,15.0})
        {
            var input=Input(moment:moment);input.LengthMm=3900;input.TotalLineLoad=load;var covers=new[] {new[] {3900.0},new[] {1800.0,1800}};
            var old=new List<int>();var fresh=new List<int>();input.ReuseSearchWork=false;var expected=IncumbentSequence(input,covers,old);
            input.ReuseSearchWork=true;var actual=IncumbentSequence(input,covers,fresh);
            Check(expected.SequenceEqual(actual),"a shrinking winner count preserves the exact old filtered order, including lower counts in later tiers");
            Check(fresh.Count<=old.Count,"live bound never generates more geometry than the eager reference");
            if(load==2&&moment==0)Check(fresh.Count<old.Count/2,"unused higher-count geometry is avoided before generation");
        }
    }
    static void ExplicitDeadline()
    {
        var covers=new[] {new[] {3900.0}};var input=Input();input.LengthMm=3900;
        var eager=new List<int>();input.ReuseSearchWork=false;
        var expected=TowerOptimizer.EngineerWork(input,covers,()=>2,eager.Add).Take(1).Select(Work).ToArray();
        var timed=new List<int>();input.ReuseSearchWork=true;input.Trace=new BeamSearchTrace("Unexpired explicit budget",searchTimeLimit:TimeSpan.FromHours(1));
        var actual=TowerOptimizer.EngineerWork(input,covers,()=>2,timed.Add).Take(1).Select(Work).ToArray();
        Check(expected.SequenceEqual(actual)&&eager.SequenceEqual(timed),"an unexpired explicit deadline preserves eager generation and first-candidate ordering");
        var unlimited=new List<int>();input.Trace=new BeamSearchTrace("Unlimited generation");
        var lazy=TowerOptimizer.EngineerWork(input,covers,()=>2,unlimited.Add).Take(1).Select(Work).ToArray();
        Check(expected.SequenceEqual(lazy)&&unlimited.Count<timed.Count&&unlimited.All(count=>count<=2),"unlimited searches still defer unused higher-count geometry");
    }
    static void Layout(TowerLayout expected,TowerLayout actual)
    {
        foreach(var property in typeof(TowerLayout).GetProperties(BindingFlags.Instance|BindingFlags.Public))
        {
            var a=property.GetValue(expected);var b=property.GetValue(actual);
            if(property.PropertyType==typeof(double))Check(Bits((double)a)==Bits((double)b),"selected scalar/rank IEEE bits: "+property.Name);
            else if(property.PropertyType==typeof(int)||property.PropertyType==typeof(bool)||property.PropertyType==typeof(string)||property.PropertyType.IsEnum)
                Check(Equals(a,b),"selected rank/metadata: "+property.Name);
            else if(property.PropertyType==typeof(double[]))Check(a==null&&b==null||a!=null&&b!=null&&((double[])a).Select(Bits).SequenceEqual(((double[])b).Select(Bits)),"selected reaction/position arrays: "+property.Name);
            else if(property.PropertyType==typeof(bool[]))Check(((bool[])a).SequenceEqual((bool[])b),"selected joins");
        }
        typeof(StandaloneStockReuseTests).GetMethod("Plan",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {expected.StockPlan,actual.StockPlan});checks++;
    }
    static void Selected()
    {
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})foreach(bool custom in new[] {false,true})foreach(double moment in new[] {0.0,.625})
        {
            var input=Input(material,custom,moment);input.ReuseSearchWork=false;var expected=TowerOptimizer.Optimize(input);input.ReuseSearchWork=true;
            var actual=TowerOptimizer.Optimize(input);Layout(expected,actual);Check(actual.Utilization<=1+1e-10&&actual.MaxLegKn<=70+1e-8,"selected layouts retain full physical checks");
        }
        var constrained=Input();constrained.ObstacleBottomMm=-1750;constrained.ReuseSearchWork=false;var original=TowerOptimizer.Optimize(constrained);
        constrained.ExternalObstacles=new[] {new FormworkSolid {Name="Neighboring tower leg",TowerOwner="other part/B1",IsLeg=true,
            X0=original.PositionsMm[0]-40,X1=original.PositionsMm[0]+40,Y0=-original.WidthMm/2-40,Y1=-original.WidthMm/2+40,Z0=-3000,Z1=-2000}};
        Check(!CrossBeamSupportOptimizer.ExternalLayoutFits(constrained,original),"external obstacle fixture conflicts with the original arrangement");
        var expectedConstrained=TowerOptimizer.Optimize(constrained);constrained.ReuseSearchWork=true;var actualConstrained=TowerOptimizer.Optimize(constrained);Layout(expectedConstrained,actualConstrained);
        Check(CrossBeamSupportOptimizer.ExternalLayoutFits(constrained,actualConstrained),"same complete checked alternative clears the physical obstacle");
        var fallback=Input();fallback.EngineerStationLimit=1;fallback.ReuseSearchWork=false;var expectedFallback=TowerOptimizer.Optimize(fallback);fallback.ReuseSearchWork=true;
        Layout(expectedFallback,TowerOptimizer.Optimize(fallback));
        var failed=Input();failed.TotalLineLoad=2000;failed.ReuseSearchWork=false;string old=null,fresh=null,oldDetails=null,freshDetails=null;
        try{TowerOptimizer.Optimize(failed);}catch(TowerOptimizationException e){old=e.Message;oldDetails=e.Details;}
        failed.ReuseSearchWork=true;try{TowerOptimizer.Optimize(failed);}catch(TowerOptimizationException e){fresh=e.Message;freshDetails=e.Details;}
        Check(old!=null&&old==fresh&&!string.IsNullOrWhiteSpace(oldDetails)&&!string.IsNullOrWhiteSpace(freshDetails),"failed search/fallback remains rejected with its physical explanations");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();var cancelled=Input();cancelled.Trace=new BeamSearchTrace("Cancelled lazy tier generation",cancellation.Token);
            bool stopped=false;try{TowerOptimizer.EngineerWork(cancelled,new[] {new[] {2450.0}}).FirstOrDefault();}catch(OperationCanceledException){stopped=true;}
            Check(stopped,"cancellation is checked before lazy tier generation");
        }
    }
    internal static void Run()
    {
        Enumeration();LiveWinner();ExplicitDeadline();Selected();
        Console.WriteLine("PASS "+checks+" lazy engineer tier checks: full order/count-window parity, width/material estimates, live winners, actual selection/ranks/physics, constraints, fallback and cancellation");
    }
}
