using System;
using System.Diagnostics;
using System.Linq;
using EngineeringDashboard;

internal static class CombinedCoordinationTests
{
    internal static void Run(bool benchmarkOnly=false,bool requireWorker=false)
    {
        var source=ReportedCoordinationFixture.Selection();
        var imported=RevitStructureImport.Build(source);
        if(!imported.CanImport)throw new Exception(string.Join(";",imported.Problems));
        if(imported.Areas.Length!=11)throw new Exception("Expected all eleven reported parts.");
        var model=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,FloorHeight=3000,TowerLegCapacityKn=30,
            AllowCustomH20Lengths=false,IgnoreUplift=true,StockEndSetbackMm=150,MaximumConcreteEndOverhangMm=150,Exclude600Standalone=true,
            PlywoodDeflectionLimit="L/350",PlywoodProfileId="doka-18mm-9732-103",StockLengthsText="1250;1800;2450;2650;2900;3300;3600;3900;4500;4900",
            DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        imported.Apply(model);
        var clock=Stopwatch.StartNew();model.OptimizeTowersAsync().GetAwaiter().GetResult();clock.Stop();
        Console.WriteLine("REPORTED11 seconds="+clock.Elapsed.TotalSeconds.ToString("0.000")+" valid="+model.IsCalculationValid+" "+model.WarningMessage+" "+model.TowerFailureDetails);
        var children=(System.Collections.Generic.Dictionary<string,BeamCalculatorViewModel>)typeof(BeamCalculatorViewModel).GetField("revitDrawingModels",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(model);
        if(requireWorker && children.Values.Any(child=>!child.BuildDiagnosticReport().Contains(".NET 10 worker; complete result returned") || child.BuildDiagnosticReport().Contains(".NET Framework fallback")))throw new Exception("The full replay must use the matching modern worker for every part and retry.");
        var fingerprint=typeof(ReportedCombinedTests).GetMethod("Fingerprint",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        foreach(var part in model.RevitDrawings.Where(d=>children[d.Key].NetworkResult!=null))
        {
            string values=(string)fingerprint.Invoke(null,new object[] {children[part.Key].NetworkResult});
            using(var sha=System.Security.Cryptography.SHA256.Create())Console.WriteLine("NETWORK "+part.Label+" "+BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(values))).Replace("-",""));
        }
        if(benchmarkOnly){Console.WriteLine("Baseline expected to reject the combined drawing after solving its parts.");return;}
        Console.WriteLine(model.CombinedRevitDiagnostics().Split(new[] {"--- Combined support coordination ---"},StringSplitOptions.None).Last());
        foreach(var part in model.RevitSceneModels())
        {
            Console.WriteLine(part.Item1.Label+": "+(part.Item2.NetworkResult?.Summary??part.Item2.TowerResult?.Summary));
            foreach(var layout in part.Item2.NetworkResult?.Beams??new[] {part.Item2.TowerResult})
                if(layout==null || layout.MaxLegKn>30+1e-8 || layout.Utilization>1+1e-8)throw new Exception("Invalid member capacity in "+part.Item1.Label);
        }
        if(benchmarkOnly)return;
        if(!model.IsCalculationValid || model.CombinedRevitClash()!=null)throw new Exception("Reported combined drawing still clashes: "+model.TowerFailureDetails);
        if(!model.CombinedRevitDiagnostics().Contains("Retained 7 original part solutions"))throw new Exception("Expected four isolated-beam repairs, retaining all five areas and two other beams.");
        Console.WriteLine("Reported coordinated drawing passes all eleven parts and unchanged 30 kN leg limit.");
    }
}
