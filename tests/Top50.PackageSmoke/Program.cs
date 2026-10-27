using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        Console.WriteLine("Shipping assembly: "+typeof(Top50ViewModel).Assembly.Location);
        Top50Runtime.Initialize();
        if(IntPtr.Size!=8)throw new Exception("64-bit host required");
        var source=new Top50DxfImporter().Import(args[0],new DxfImportOptions());
        if(source.Walls.Count!=65||source.Walls.Count(w=>w.Included)!=63)throw new Exception("Import regression");
        var limits=new SearchLimits(12,1,350,15,5,2,50);
        var run=new Top50Search().Run(source.Walls,Precision.CatalogMm,limits,PlanningPreset.BalancedOptimization,4,CancellationToken.None);
        if(run.Proposals.Count==0||run.Proposals.Any(p=>Top50Validation.Validate(source.Walls,p,limits).Count!=0))throw new Exception("Invalid packaged search");
        var resolver=typeof(Top50Runtime).GetMethod("Resolve",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var dependencyRequest=new ResolveEventArgs("System.Runtime.CompilerServices.Unsafe, Version=4.0.4.1, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Google.OrTools"));
        Func<object> resolve=()=>resolver.Invoke(null,new object[]{null,dependencyRequest});
        if(resolve()!=null)throw new Exception("Resolver escaped operation scope");
        using(Top50Runtime.Enter())
        {
            if(resolve()==null)throw new Exception("Known scoped dependency unresolved");
            if(resolver.Invoke(null,new object[]{null,new ResolveEventArgs(dependencyRequest.Name,typeof(Program).Assembly)})!=null)throw new Exception("Unrelated caller redirected");
            if(System.Threading.Tasks.Task.Run(resolve).Result!=null)throw new Exception("Resolver scope leaked across threads");
        }
        if(resolve()!=null)throw new Exception("Resolver remained active after disposal");
        Console.WriteLine("PASS compatibility resolution is operation/thread scoped and rejects unrelated callers");
        Top50Workbook.Export(source,run.Proposals[0],run.Proposals,limits,Path.Combine(output,"shipping-assembly.xlsx"));
        var app=new Application();var vm=new Top50ViewModel();vm.Import(args[0]);var window=new Top50Window(vm);
        for(int i=0;i<5;i++)window.ShowPage(i);
        if(typeof(Top50Window).Assembly!=typeof(Top50Search).Assembly)throw new Exception("Not integrated in add-in");
        if(typeof(Top50Search).Assembly.GetType("Top50RecordedFixture")!=null)throw new Exception("Test fixture leaked into shipping assembly");
        var launcher=new LauncherWindow();
        foreach(var name in new[]{"BtnTop50","BtnBeamCalculator","BtnFlexCalculator","BtnBackpropping","BtnDomeCalculator"})
            if(!(launcher.FindName(name) is System.Windows.Controls.Button))throw new Exception("Missing dashboard entry: "+name);
        var root=(FrameworkElement)launcher.Content;root.Measure(new Size(620,720));root.Arrange(new Rect(0,0,620,720));root.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(620,720,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var stream=File.Create(Path.Combine(output,"shipping-dashboard.png")))encoder.Save(stream);
        Console.WriteLine("PASS compiled dashboard contains Top50 and all existing calculator entries; test fixture absent");
        Console.WriteLine("PASS shipping add-in: x64 native solver, audited import, validated fresh search, workbook and five native views");
        Console.WriteLine("Host Revit2024 API loading and dashboard access gate remain unrun outside Revit.");
        return 0;
    }
}
