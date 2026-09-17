using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using EngineeringDashboard.Dome;
using OxyPlot.Series;

static partial class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Near(double actual, double expected, double tolerance, string message) =>
        Check(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= tolerance, $"{message}: actual={actual:G17}, expected={expected:G17}");
    private static void Run(string name, Action action) { action(); Console.WriteLine($"PASS {name}"); }

    [STAThread]
    static int Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Run("Bounded expression parser and invalid input", Inputs);
            Run("Startup, collection changes and valid/invalid state transitions", State);
            Run("Beam closed-form benchmarks", Benchmarks);
            Run("Exact extrema, equilibrium and continuity", Invariants);
            Run("Original false-pass and uplift regressions", Regressions);
            Run("143-case optimizer scan with independent support-side equilibrium", Scan);
            Run("WPF binding, precision, validation and plot endpoints", Wpf);
            Console.WriteLine($"PASS ALL: {assertions} assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FAIL after {assertions} assertions: {ex}"); return 1; }
    }

    private static void Inputs()
    {
        foreach (string text in new[] { "", " ", "10+", "1/0", "0/0", "(2+3", "1,000", "1,5", "NaN", "Infinity", "2**3",
            new string('9',257), new string('(',40)+"1"+new string(')',40) })
            Check(!MathEvaluator.TryEvaluate(text,out _,out var error) && error.Length > 0,$"Reject {text}");
        foreach (var (text,expected) in new[] { ("(2+3)*4",20.0),("2+-3",-1.0),("2/-2",-1.0),(".5",.5),("1.524",1.524),("1/3",1.0/3),("1--2",3.0) })
        {
            Check(MathEvaluator.TryEvaluate(text,out double value,out _),$"Parse {text}");
            Near(value,expected,1e-14,$"Value {text}");
        }
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        Near(MathEvaluator.Evaluate("1.524"),1.524,0,"Decimal interpretation independent of culture");
        Check(!MathEvaluator.TryEvaluate("1,524",out _,out _),"Ambiguous comma rejected in every culture");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        foreach(double length in new[]{0,-1,double.NaN,double.PositiveInfinity,20.01})
            Check(BeamDesign.Select(5,length).Result is null,"Reject invalid length");
        foreach(double q in new[]{-1,double.NaN,double.PositiveInfinity,10001})
            Check(BeamDesign.Select(q,3).Result is null,"Reject invalid pressure");
        Check(BeamDesign.Select(5,.6).Status.Contains("No admissible"),"Short beam reason");
        Check(BeamDesign.Select(5,.600001).Result is not null,"Positive short overhang accepted");
        Check(BeamDesign.Select(10000,20).Result is null,"Bounded extreme input finishes and fails checks");
    }

    private static void State()
    {
        var vm=new MainViewModel();
        Near(vm.LoadTransferredToSecondary!.Value,5,0,"Startup pressure");
        Near(vm.TotalLoad!.Value,10,0,"Startup total");
        Check(vm.ShowDiagramCommand.CanExecute(null),"Startup command enabled");
        vm.AddSlabCommand.Execute(null);
        Near(vm.TotalLoad!.Value,260,1e-12,"First slab included immediately");
        vm.AddBeamCommand.Execute(null);
        Near(vm.TotalLoad!.Value,510,1e-12,"First beam included immediately");
        vm.CalculateTotalLoad();
        Near(vm.TotalLoad!.Value,510,1e-12,"Recalculation idempotent");
        vm.Elements[0].SurfaceArea="10+";
        Check(vm.TotalLoad is null && vm.LoadTransferredToSecondary is null,"Invalid load unavailable");
        Check(vm.Elements[0].HasErrors && vm.HasErrors,"Field and aggregate errors");
        Check(!vm.ShowDiagramCommand.CanExecute(null) && !vm.CopyResultsCommand.CanExecute(null),"Invalid commands blocked");
        Check(vm.Elements[0].SurfaceArea=="10+","Invalid text retained");
        vm.Elements[0].SurfaceArea="10";
        Near(vm.TotalLoad!.Value,510,1e-12,"Correction restores totals");
        var beam=(BeamViewModel)vm.Elements[1];
        beam.BeamWidth="1/0";
        Check(vm.IsCalculationValid,"Inactive width not used");
        beam.IsPerpendicular=true;
        Check(!vm.IsCalculationValid,"Active invalid width blocks results");
        beam.BeamWidth="0.5";
        Near(vm.TotalLoad!.Value,385,1e-12,"Perpendicular beam recalculates");
        beam.IsParallel=true;
        Check(!beam.IsPerpendicular,"Single orientation");
        vm.Elements[0].RemoveCommand.Execute(null);
        Near(vm.TotalLoad!.Value,260,1e-12,"Remove updates aggregate");
        vm.Elements.Clear();
        Near(vm.TotalLoad!.Value,10,1e-12,"Clear updates aggregate");
        foreach(string invalid in new[]{"0","-2","1/0","0/0","21"})
        {
            vm.PrimaryBeamSize=invalid;
            Check(!vm.IsCalculationValid && vm.SupportArea is null,$"Invalid dimension {invalid}");
        }
        vm.PrimaryBeamSize="2";
        vm.CombinedLoad="-5"; Check(!vm.IsCalculationValid,"Negative load rejected");
        vm.CombinedLoad="0"; Check(vm.IsCalculationValid,"Explicit zero load supported");
        vm.CombinedLoad="5";
        vm.AddSlabCommand.Execute(null); vm.Elements[0].SurfaceArea="0.2*2";
        Near(vm.LoadTransferredToSecondary!.Value,10,1e-12,"Documented slab example: 5 concrete + 5 combined");
        Check(vm.CreateResultsText().Contains("0.2*2"),"Export retains input expression");
        vm.Elements[0].SurfaceArea="0"; Check(!vm.IsCalculationValid,"Zero section area rejected");
        vm.Elements.Clear();
        for(int i=0;i<100;i++) vm.AddSlabCommand.Execute(null);
        Check(!vm.AddSlabCommand.CanExecute(null),"Element cap enforced");
        vm.AddSlabCommand.Execute(null);
        Check(vm.Elements.Count==100,"Disabled command cannot mutate");
    }

    private static void Benchmarks()
    {
        var one=BeamAnalysis.Analyze(new[]{1.0},10,0,0);
        Near(one.Reactions[0],5,1e-12,"Simply supported left reaction");
        Near(one.Reactions[1],5,1e-12,"Simply supported right reaction");
        Near(one.MaximumShear.Value,5,1e-12,"Left support shear");
        Near(one.MinimumShear.Value,-5,1e-12,"Right support shear included");
        Near(one.GoverningMoment.Magnitude,1.25,1e-12,"wL²/8");
        Near(one.GoverningDeflection.Magnitude,5*10/(384*BeamAnalysis.Rigidity)*1000,1e-12,"5wL⁴/384EI");
        Near(one.GoverningDeflection.Position,.5,1e-12,"Midspan deflection root");
        var two=BeamAnalysis.Analyze(new[]{1.0,1.0},10,0,0);
        foreach(var (i,r) in new[]{(0,3.75),(1,12.5),(2,3.75)}) Near(two.Reactions[i],r,1e-12,"Two-span reaction");
        Near(two.MinimumMoment.Value,-1.25,1e-12,"Two-span internal moment");
        // Interior deflection root is not constrained to the old 10 mm grid.
        Near(Math.Min(two.MinimumDeflection.Position,2-two.MinimumDeflection.Position),0.4215351654086268,1e-10,"Two-span analytical slope root (either symmetric span)");
        var overhang=BeamAnalysis.Analyze(new[]{1.0},10,.2,.2);
        Near(overhang.Reactions[0],7,1e-12,"Overhang reaction");
        Near(overhang.Reactions[1],7,1e-12,"Overhang symmetry");
        Near(overhang.Segments[0].Deflection(0),overhang.Segments[overhang.Segments.Count - 1].Deflection(.2),1e-12,"Free-end symmetry");
    }

    private static void Invariants()
    {
        foreach(var spans in new[]{new[]{.6},new[]{1.524},new[]{.6,1.5},new[]{1.524,1.0,.6},new[]{1.5,.6,1.0,1.5}})
        foreach(var c in new[]{0.0,.137,.543})
        {
            var r=BeamAnalysis.Analyze(spans,17,c,c+.017);
            Near(r.Reactions.Sum(),r.W*r.TotalLength,1e-9,"Vertical equilibrium");
            Near(r.Reactions.Select((force,i)=>force*r.SupportLocations[i]).Sum(),r.W*r.TotalLength*r.TotalLength/2,1e-8,"Moment equilibrium");
            for(int i=1;i<r.Segments.Count;i++)
            {
                var previous=r.Segments[i-1]; var next=r.Segments[i];
                Near(previous.Moment(previous.Length),next.Moment(0),1e-9,"Moment continuity");
                Near(previous.Deflection(previous.Length),next.Deflection(0),1e-9,"Displacement continuity");
                Near(previous.SlopeEI(previous.Length),next.SlopeEI(0),1e-9,"Rotation continuity");
            }
            foreach(var segment in r.Segments)
            {
                if(r.SupportLocations.Any(x=>Math.Abs(x-segment.Start)<1e-9)) Near(segment.Deflection(0),0,1e-9,"Support displacement");
                if(r.SupportLocations.Any(x=>Math.Abs(x-segment.Start-segment.Length)<1e-9)) Near(segment.Deflection(segment.Length),0,1e-9,"Support displacement");
                // Independent dense locations must never exceed the computed extrema.
                for(int i=0;i<=3000;i++)
                {
                    double x=segment.Length*i/3000;
                    Check(Math.Abs(segment.Moment(x))<=r.GoverningMoment.Magnitude+1e-9,"Moment extrema envelope");
                    Check(Math.Abs(segment.Deflection(x))<=r.GoverningDeflection.Magnitude+1e-9,"Deflection extrema envelope");
                }
            }
            var doubled=BeamAnalysis.Analyze(spans,34,c,c+.017);
            Near(doubled.GoverningDeflection.Magnitude,2*r.GoverningDeflection.Magnitude,1e-9,"Load linearity");
        }
        var limit=BeamAnalysis.Analyze(new[]{1.0},22,0,0);
        Check(BeamDesign.Passes(limit),"At shear limit passes");
        Check(!BeamDesign.Passes(BeamAnalysis.Analyze(new[]{1.0},22.0001,0,0)),"Above shear limit fails");
        // Moment controls for this span; deflection remains below the legacy 5 mm limit.
        Check(BeamDesign.Passes(BeamAnalysis.Analyze(new[]{2.0},9.999,0,0)),"Below moment limit");
        Check(!BeamDesign.Passes(BeamAnalysis.Analyze(new[]{2.0},10.001,0,0)),"Above moment limit");
        double deflectionLoad=5*384*BeamAnalysis.Rigidity/(5*Math.Pow(3.0,4)*1000);
        Check(BeamDesign.Passes(BeamAnalysis.Analyze(new[]{3.0},deflectionLoad*.9999,0,0)),"Below deflection limit");
        Check(!BeamDesign.Passes(BeamAnalysis.Analyze(new[]{3.0},deflectionLoad*1.0001,0,0)),"Above deflection limit");
    }

    private static void Regressions()
    {
        var cases=new[]{
            (q:50.0,l:3.3,spacing:.407,spans:new[]{.6,1.0,1.0}),
            (q:30.0,l:3.6,spacing:.488,spans:new[]{1.0,1.5}),
            (q:50.0,l:3.6,spacing:.407,spans:new[]{.6,1.0,1.0}),
            (q:75.0,l:3.6,spacing:.275,spans:new[]{.6,1.0,1.0})};
        foreach(var test in cases)
        {
            double c=(test.l-test.spans.Sum())/2;
            var oldLayout=BeamAnalysis.Analyze(test.spans,test.q*test.spacing,c,c);
            Check(oldLayout.GoverningShear.Magnitude>11,"Previously missed shear found");
            Check(!BeamDesign.Passes(oldLayout),"Old false-pass layout rejected");
            var replacement=BeamDesign.Select(test.q,test.l);
            Check(replacement.Passed,"Compliant replacement found");
            Console.WriteLine($"Replacement q={test.q}, L={test.l}: spacing={replacement.SecondarySpacing*1000:0.###} mm; shear={replacement.Result!.GoverningShear.Magnitude:F6} kN");
        }
        var uplift=BeamAnalysis.Analyze(new[]{1.524,1.524},5*.407,1.426,1.426);
        Near(uplift.Reactions[1],-.19628135826771675,1e-10,"Original negative reaction reproduced");
        Check(uplift.HasUplift && !BeamDesign.Passes(uplift),"Bearing-only model rejects hold-down requirement");
        var revised=BeamDesign.Select(5,5.9);
        Check(revised.Passed && !revised.Result!.HasUplift,"Replacement layout has no uplift");
        var summary=new DiagramViewModel(50,3);
        var r=summary.Selection.Result!;
        Check(summary.MomentSummary.StartsWith(r.GoverningMoment.Magnitude.ToString("0.###")),"Governing moment shown");
        Check(summary.DeflectionSummary.StartsWith(r.GoverningDeflection.Magnitude.ToString("0.###")),"Governing deflection shown");
        Check(new DiagramViewModel(5,.6).HasResult==false,"No fake zero results");
    }

    private static void Scan()
    {
        int count=0,passes=0,failures=0;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        foreach(double length in new[]{1.8,2.0,2.12,2.45,2.65,2.9,3.0,3.3,3.6,3.9,4.5,4.9,5.9})
        foreach(double pressure in new[]{5.0,10,20,30,40,50,60,75,100,125,150})
        {
            count++;
            var selected=BeamDesign.Select(pressure,length);
            if(!selected.Passed){ failures++; continue; }
            passes++;
            var r=selected.Result!;
            double previous=0,peak=0;
            for(int i=0;i<r.Reactions.Count;i++)
            {
                double before=previous-r.W*r.SupportLocations[i];
                peak=Math.Max(peak,Math.Abs(before));
                previous+=r.Reactions[i];
                peak=Math.Max(peak,Math.Abs(previous-r.W*r.SupportLocations[i]));
                Check(r.Reactions[i]>=-1e-8,"No negative reaction in accepted layout");
            }
            Near(peak,r.GoverningShear.Magnitude,1e-8,"Independent exact support-side shear");
            Check(peak<=11+1e-9,"Accepted layout meets exact shear limit");
            Check(r.GoverningMoment.Magnitude<=5 && r.GoverningDeflection.Magnitude<=5,"Accepted other limits");
        }
        Console.WriteLine($"Scan: {count} cases, {passes} passing layouts, {failures} no passing layout; zero false shear passes or unrestrained uplift; {watch.ElapsedMilliseconds} ms.");
    }

    private static void Wpf()
    {
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var window=new MainWindow();
        var vm=(MainViewModel)window.DataContext;
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
        var box=(TextBox)window.FindName("PrimaryInput");
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
        box.SetCurrentValue(TextBox.TextProperty,"1.524");
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(box.Text=="1.524" && vm.PrimaryBeamSize=="1.524",$"Blur preserves complete precision: text={box.Text}, source={vm.PrimaryBeamSize}");
        Check(BindingOperations.IsDataBound(box,TextBox.TextProperty),"Binding retained");
        box.SetCurrentValue(TextBox.TextProperty,"1/0");
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
        Check(Validation.GetHasError(box),"WPF field error displayed");
        Check(!vm.IsCalculationValid && box.Text=="1/0","Invalid input retained without numeric output");
        box.SetCurrentValue(TextBox.TextProperty,"2");
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Check(vm.IsCalculationValid,"WPF correction recovers");
        var diagram=new DiagramViewModel(30,3.6);
        var dialog=new DiagramWindow(diagram);
        var shear=(AreaSeries)diagram.ShearPlot.Series[0];
        Near(shear.Points[0].X,0,0,"Plot starts at zero");
        Near(shear.Points[shear.Points.Count - 1].X,3.6,1e-12,"Plot contains exact end");
        foreach(double x in diagram.Selection.Result!.SupportLocations)
            Check(shear.Points.Count(p=>Math.Abs(p.X-x)<1e-10)>=2,"Both sides of support plotted");
        Check(new DiagramViewModel(double.PositiveInfinity,3).LoadPlot.Series.Count==0,"Invalid diagram has no plot");
        dialog.Close();
        DiagramPresentation();
        Integration();
        window.Close(); app.Shutdown();
    }

    private static void DiagramPresentation()
    {
        var vm = new DiagramViewModel(100, 3);
        var result = vm.Selection.Result!;
        Check(vm.SupportReactions.Count == 5, "Five-support presentation fixture");
        for (int i = 0; i < vm.SupportReactions.Count; i++)
        {
            var support = vm.SupportReactions[i];
            Check(support.Name == ((char)('A' + i)).ToString(), "Support names in spatial order");
            Near(support.Position, result.SupportLocations[i], 0, "Reaction position from analysis");
            Near(support.Force, result.Reactions[i], 0, "Reaction value from analysis");
            var arrow = vm.LoadPlot.Annotations.OfType<OxyPlot.Annotations.ArrowAnnotation>().Single(a => Equals(a.Tag, "reaction-arrow:" + support.Name));
            Near(arrow.StartPoint.X, support.Position, 0, "Reaction arrow at support");
            Check(arrow.EndPoint.Y > arrow.StartPoint.Y, "Reaction arrow points upward");
            var label = vm.LoadPlot.Annotations.OfType<OxyPlot.Annotations.TextAnnotation>().Single(a => Equals(a.Tag, "reaction-label:" + support.Name));
            Check(label.Text?.Contains(support.ForceText) == true, "Beam label matches reaction card");
        }
        foreach (var (plot, max, min, limit) in new[] {
            (vm.ShearPlot, result.MaximumShear, result.MinimumShear, 11.0),
            (vm.MomentPlot, result.MaximumMoment, result.MinimumMoment, 5.0),
            (vm.DeflectionPlot, result.MaximumDeflection, result.MinimumDeflection, 5.0) })
        {
            var series = (TwoColorAreaSeries)plot.Series[0];
            Check(series.Limit == 0 && series.Fill != series.Fill2, "Signed response colours split at zero");
            var guides = plot.Annotations.OfType<OxyPlot.Annotations.LineAnnotation>().Where(a => Equals(a.Tag, "support-guide")).ToArray();
            Check(guides.Select(a => a.X).SequenceEqual(result.SupportLocations), "Guides aligned with every support");
            foreach (var (name, demand) in new[] { ("max", max), ("min", min) })
            {
                var marker = plot.Annotations.OfType<OxyPlot.Annotations.PointAnnotation>().Single(a => Equals(a.Tag, "extreme:" + name));
                Near(marker.X, demand.Position, 0, "Exact extremum position");
                Near(marker.Y, demand.Value, 0, "Exact signed extremum value");
            }
            Check(plot.Annotations.OfType<OxyPlot.Annotations.LineAnnotation>().Where(a => Equals(a.Tag, "capacity")).Select(a => a.Y).OrderBy(value => value).SequenceEqual(new[] { -limit, limit }), "Both limit lines");
        }
        vm.ShowCapacityLimits = false;
        Check(ReferenceEquals(result, vm.Selection.Result), "Scale toggle preserves analysis");
        Check(!vm.DeflectionPlot.Annotations.Any(a => Equals(a.Tag, "capacity")), "Response view hides limits");
        Near(vm.DeflectionPlot.Axes[1].Maximum, result.GoverningDeflection.Magnitude * 1.5, 1e-12, "Response view fits demand");
        vm.ShowCapacityLimits = true;
        vm.ShowCapacityLimits = false;
        vm.ShowCapacityLimits = true;
        Check(vm.ShearPlot.Annotations.Count(a => Equals(a.Tag, "capacity")) == 2, "Toggling does not duplicate limits");
        Check(vm.ShearPlot.Annotations.Count(a => Equals(a.Tag, "support-label")) == 5, "Toggling does not duplicate support labels");
        var dialog = new DiagramWindow(vm);
        var content = (FrameworkElement)dialog.Content;
        foreach (var size in new[] { new Size(1260, 740), new Size(1030, 600) })
        {
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            foreach (string name in new[] { "LoadView", "ShearView", "MomentView", "DeflectionView" })
            {
                var view = (FrameworkElement)dialog.FindName(name);
                var bounds = view.TransformToAncestor(content).TransformBounds(new Rect(view.RenderSize));
                Check(bounds.Width > 350 && bounds.Height > 90, "All four plots retain usable area");
                Check(new Rect(size).Contains(bounds), "Every plot fits without scrolling");
            }
            Check(!Descendants(content).OfType<ScrollViewer>().Any(), "Diagram layout has no scrolling container");
        }
        dialog.Close();
        var zero = new DiagramViewModel(0, 3);
        Check(zero.SupportReactions.All(r => r.Force == 0), "Zero-load reaction cards");
        Check(!zero.LoadPlot.Annotations.OfType<OxyPlot.Annotations.ArrowAnnotation>().Any(), "No force arrows for zero load");
        Check(zero.ShearPlot.Annotations.Count(a => Equals(a.Tag, "extreme:zero")) == 1, "Single zero marker");
        Console.WriteLine("PASS Diagram reactions, exact markers, scale invariance and four-panel layout at two sizes");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
