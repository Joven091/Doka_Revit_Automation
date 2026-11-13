using System;
using System.Linq;
using EngineeringDashboard;

internal static class StandaloneReportedValidationTests
{
    static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    static void Reject(Action check,string message)
    {
        bool rejected=false;try{check();}catch(Exception){rejected=true;}
        Check(rejected,message);
    }
    internal static void Run()
    {
        var input=ReportedScenarioTests.DiagnosticPart(1,6).Item2.CreateTowerInput();
        var covered=input.CopyForSearch();covered.LengthMm=3800;covered.LongitudinalStartMm=0;
        var layout=TowerOptimizer.Evaluate(covered,new[] {900.0,2900},new[] {true},1524,false);
        Check(layout!=null,"Reported standalone covered stock fixture must pass engineering checks");
        double edge=(input.LengthMm-covered.LengthMm)/2;
        layout.LengthMm=input.LengthMm;layout.ConcreteEndOverhangMm=edge;layout.LongitudinalStartMm=input.LongitudinalStartMm;
        layout.PositionsMm=layout.PositionsMm.Select(x=>x+edge+input.LongitudinalStartMm).ToArray();
        foreach(var piece in layout.StockPlan.Pieces)piece.StartMm+=edge+input.LongitudinalStartMm;
        ReportedScenarioTests.ValidateStandalone(layout,input);
        double supported=layout.LeftReactions.Sum()+layout.RightReactions.Sum();
        double external=input.TotalLineLoad*2*edge/1000;
        Check(Math.Abs(supported-141.81600108197023)<1e-7 && Math.Abs(external-11.12772761183188)<1e-7,"Actual reported force split changed");
        // Translation must not alter stock lengths, loaded force or local analyses.
        input.LongitudinalStartMm+=450;layout.LongitudinalStartMm+=450;
        layout.PositionsMm=layout.PositionsMm.Select(x=>x+450).ToArray();foreach(var piece in layout.StockPlan.Pieces)piece.StartMm+=450;
        ReportedScenarioTests.ValidateStandalone(layout,input);
        double allowance=input.MaximumConcreteEndOverhangMm;input.MaximumConcreteEndOverhangMm=100;
        Reject(()=>ReportedScenarioTests.ValidateStandalone(layout,input),"End-strip allowance violation must fail");input.MaximumConcreteEndOverhangMm=allowance;
        layout.LeftReactions[0]+=1;Reject(()=>ReportedScenarioTests.ValidateStandalone(layout,input),"Missing supported reaction must fail");layout.LeftReactions[0]-=1;
        double moment=layout.StockPlan.Pieces[0].MomentKnM;layout.StockPlan.Pieces[0].MomentKnM*=.99;
        Reject(()=>ReportedScenarioTests.ValidateStandalone(layout,input),"Incorrect standalone member response must fail");layout.StockPlan.Pieces[0].MomentKnM=moment;
        Console.WriteLine(FormattableString.Invariant($"PASS reported standalone: supported {supported:0.000000} kN + external column strips {external:0.000000} kN = {supported+external:0.000000} kN; dense member/leg checks, translated origin and rejection guards"));
    }
}
