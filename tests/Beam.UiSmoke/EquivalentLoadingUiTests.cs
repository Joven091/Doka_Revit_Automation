using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EngineeringDashboard;

internal static class EquivalentLoadingUiTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Equivalent UI: "+message);}
    static void Render(FrameworkElement element,double width,string path=null)
    {
        element.Measure(new Size(width,double.PositiveInfinity));element.Arrange(new Rect(0,0,width,element.DesiredSize.Height));element.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(element.DesiredSize.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        if(path!=null){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
    }
    static Point Centre(Rect b)=>new Point(b.X+b.Width/2,b.Y+b.Height/2);
    static string Preview(string path,string suffix)=>path==null ? null : Path.ChangeExtension(path,suffix+".png");
    static System.Collections.Generic.IEnumerable<T> Controls<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T value)yield return value;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var descendant in Controls<T>(child))yield return descendant;
    }
    static System.Collections.Generic.IEnumerable<Drawing> Primitives(Drawing drawing)
    {
        if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var leaf in Primitives(child))yield return leaf;
        else yield return drawing;
    }
    static void RenderWindow(TowerDiagramWindow window,double width,string path=null)
        =>Render((FrameworkElement)((ScrollViewer)window.Content).Content,width,path);
    static void CheckResponse(TowerDiagramWindow window,TransferMember actual)
    {
        RenderWindow(window,900);
        var plots=Controls<TowerDiagramWindow.LoadedResponsePlot>((DependencyObject)window.Content).ToArray();
        Check(plots.Length==3 && plots.Select(p=>p.DiagramIndex).SequenceEqual(new[] {0,1,2}),"all three response diagrams remain in the unified view");
        Check(plots.All(p=>ReferenceEquals(p.Member,actual) && ReferenceEquals(p.Member.Analysis,actual.Analysis)),"shear, moment and deflection retain the actual checked member analysis");
        foreach(var plot in plots)
            Check(Primitives(VisualTreeHelper.GetDrawing(plot)).OfType<GeometryDrawing>().Any(d=>d.Pen?.Brush is SolidColorBrush brush && brush.Color==Colors.SteelBlue),"actual member response curve renders");
    }
    static double[] Snapshot(TransferMember m)=>new[] {m.Analysis.TotalLoadKn,m.Analysis.ShearKn,m.Analysis.MomentKnM,m.Analysis.DeflectionMm,m.Utilization}
        .Concat(m.Analysis.Reactions).Concat(m.Uniform.Select(q=>q.KnPerM)).Concat(m.Points.Select(p=>p.Kn))
        .Concat(new[] {0.0,199,425,475,900,1425,1800}.SelectMany(x=>m.Analysis.Response(x,true))).ToArray();
    static TransferMember Primary(double q,int line)
    {
        var m=new TransferMember {Name=line==0 ? "P1 L" : "P1 R",LoadCase="Beam",H20Count=2,LengthMm=6900,SupportsMm=new[] {400.0,6400},Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=6900,KnPerM=q}},Points=new BeamPointLoad[0]};
        m.Analysis=LoadedBeamAnalysis.AnalyzeSigned(m.LengthMm,m.SupportsMm,m.Uniform,m.Points,TowerOptimizer.PairEI,270);return m;
    }
    internal static void CheckWindow(TowerDiagramWindow window,bool native=false)
    {
        RenderWindow(window,900);var view=window.EquivalentView;var actual=window.DisplayedTransferMember;var analysis=window.LoadedAnalysis;
        Check(view!=null && view.model.IsAvailable,"secondary reference diagram exists");view.CloseAllDetails();Render(view,900);
        Check(!view.IsDetailsOpen,"cards start hidden until clicked");
        foreach(var component in view.Plot.Targets.Where(t=>t.Component>=0).Select(t=>t.Component).Distinct().ToArray())
        {
            Render(view,900);var target=view.Plot.Targets.Last(t=>t.Component==component);
            if(native){MemberLoadingUiTests.NativeClick(view.Plot,target.Bounds);native=false;}else view.Plot.SelectAt(Centre(target.Bounds));
            Check(view.LoadCard.IsOpen && view.VisibleCalculation.Any(s=>s.StartsWith("Equivalent load =")),"clicked load shows spacing conversion");
        }
        for(int reaction=0;reaction<2;reaction++)
        {
            Render(view,900);var target=view.Plot.Targets.Last(t=>t.Reaction==reaction);view.Plot.SelectAt(Centre(target.Bounds));
            Check(view.ReactionCard.IsOpen && view.VisibleCalculation.Length==5 && view.VisibleCalculation.Any(s=>s.StartsWith("UDL =")) && view.VisibleCalculation.Any(s=>s.StartsWith("Primary UDL =")),"reaction click shows only the primary UDL calculation");
            Check(Math.Abs(view.model.Member.Analysis.Reactions[reaction]/(view.model.ReferenceSpacingMm/1000)-view.model.PrimaryTransferKnM[reaction])<1e-7,"clicked displayed reaction produces the checked primary UDL");
        }
        Check(view.LoadCard.IsOpen && view.ReactionCard.IsOpen,"load and reaction cards remain open together");
        view.LoadCard.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!view.LoadCard.IsOpen && view.ReactionCard.IsOpen,"load closes independently");
        view.ReactionCard.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!view.IsDetailsOpen,"last close restores clean diagram");
        Check(ReferenceEquals(window.LoadedAnalysis,analysis) && ReferenceEquals(window.DisplayedTransferMember,actual),"clicking does not change actual physical checks");
    }
    internal static void Run(string preview=null)
    {
        MemberLoadingUiTests.Run();
        PositionedTransferUiTests.Run(preview);
        var layout=EquivalentLoadingTests.Fixture();var t=layout.Transfer;var unit=t.UnitLowerCases[0];
        var actual=new TransferMember {Name="T1",LoadCase=unit.LoadCase,H20Count=1,LengthMm=1800,StartMm=-900,SupportsMm=unit.SupportsMm,
            Uniform=new[] {new BeamDistributedLoad {StartMm=575,EndMm=1225,KnPerM=9.65325}},
            Points=new[] {new BeamPointLoad {PositionMm=199,Kn=3.44322},new BeamPointLoad {PositionMm=475,Kn=.8646308}}};
        actual.Analysis=LoadedBeamAnalysis.AnalyzeSigned(actual.LengthMm,actual.SupportsMm,actual.Uniform,actual.Points,TowerOptimizer.PairEI/2,270);
        t.LowerCases=new[] {new[] {actual}};t.TributaryWidthsMm=new[] {305.0};t.SlabRowIndices=new[] {0};
        layout.StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StockLengthMm=6900}}};
        t.PrimaryCases[0]=t.PrimaryCases[0].Select((p,line)=>Primary(p.Uniform[0].KnPerM,line)).ToArray();
        var beamUnit=new TransferMember {LoadCase="Beam",H20Count=1,LengthMm=unit.LengthMm,SupportsMm=unit.SupportsMm,Uniform=unit.Uniform,Points=new BeamPointLoad[0]};
        beamUnit.Analysis=LoadedBeamAnalysis.AnalyzeSigned(beamUnit.LengthMm,beamUnit.SupportsMm,beamUnit.Uniform,beamUnit.Points,TowerOptimizer.PairEI/2,270);
        var beamActual=new TransferMember {Name="T1",LoadCase="Beam",H20Count=1,LengthMm=actual.LengthMm,SupportsMm=actual.SupportsMm,Uniform=actual.Uniform,Points=new BeamPointLoad[0]};
        beamActual.Analysis=LoadedBeamAnalysis.AnalyzeSigned(beamActual.LengthMm,beamActual.SupportsMm,beamActual.Uniform,beamActual.Points,TowerOptimizer.PairEI/2,270);
        t.CaseNames=t.CaseNames.Concat(new[] {"Beam"}).ToArray();t.UnitLowerCases=new[] {unit,beamUnit};t.UnitPointLabels=t.UnitPointLabels.Concat(new[] {new string[0]}).ToArray();
        t.LowerCases=new[] {new[] {actual},new[] {beamActual}};
        t.PrimaryCases=t.PrimaryCases.Concat(new[] {beamUnit.Analysis.Reactions.Select((q,line)=>Primary(q,line)).ToArray()}).ToArray();
        var originalValues=Snapshot(actual);var beamValues=Snapshot(beamActual);
        var vm=new BeamCalculatorViewModel {UseTowers=true};typeof(BeamCalculatorViewModel).GetProperty("TowerResult").SetValue(vm,layout);
        var window=new TowerDiagramWindow(vm);window.SelectMember(1,0,-1);
        RenderWindow(window,900,Preview(preview,"unified-response"));
        Check(!Controls<TabControl>((DependencyObject)window.Content).Any() && window.EquivalentView!=null && window.LoadingDiagram==null,"one equivalent loading card replaces both loading tabs");
        Check(window.ResponseBasis.Text.Contains("actual checked loads") && window.ResponseBasis.FontSize==12,"actual response basis stays visible in a small caption");
        CheckResponse(window,actual);
        Check(ReferenceEquals(window.LoadedAnalysis,actual.Analysis) && ReferenceEquals(window.DisplayedTransferMember,actual),"equivalent display never replaces checked analysis");
        // Resize an independent copy; the window's parent layout otherwise
        // restores its own width while a nested element is being captured.
        var view=new EquivalentLoadingView(EquivalentTransferredLoading.Create(layout,0,0),true);Render(view,900,Preview(preview,"closed"));
        Check(!view.IsDetailsOpen,"calculation panel closed initially");
        Check(view.Plot.DrawnLabels.All(s=>!s.Contains("slab") && !s.Contains("shutter") && !s.Contains("concrete")),"diagram contains values and geometry without source identifiers");
        var strokes=Primitives(VisualTreeHelper.GetDrawing(view.Plot)).OfType<GeometryDrawing>().Where(d=>d.Pen?.Brush is SolidColorBrush).ToArray();
        Check(strokes.Any(d=>((SolidColorBrush)d.Pen.Brush).Color==Colors.Firebrick) && strokes.Any(d=>((SolidColorBrush)d.Pen.Brush).Color==Colors.SeaGreen),"equivalent plot uses established red load and green reaction strokes");
        var foot=view.Plot.Targets.First(x=>x.Component==1);Check(view.Plot.SelectAt(Centre(foot.Bounds)),"foot arrow/value is selectable");
        Render(view,900,Preview(preview,"foot"));
        Check(view.IsDetailsOpen && view.VisibleCalculation.Any(s=>s.Contains("3.44322")) && view.VisibleCalculation.Any(s=>s.Contains("305 / 407")) && view.VisibleCalculation.Any(s=>s.Contains("2.5803")),"compact foot panel shows source, nominal conversion and equivalent result");
        var reaction=view.Plot.Targets.First(x=>x.Reaction==0);Check(view.Plot.SelectAt(Centre(reaction.Bounds)),"reaction is selectable");Render(view,900,Preview(preview,"reaction"));
        Check(!view.VisibleCalculation.Any(s=>s.Contains("3.16345")) && view.VisibleCalculation.Length==5 && view.VisibleCalculation.Any(s=>s.Contains("7.07316 / 0.305")) && view.VisibleCalculation.Any(s=>s.Contains("23.1907")),"reaction card has only the reaction, pitch and UDL division");
        Check(ReferenceEquals(window.LoadedAnalysis,actual.Analysis),"clicks leave actual member checks intact");
        Check(view.LoadCard.IsOpen && view.ReactionCard.IsOpen,"both cards remain open after the second selection");
        Render(view,1100,Preview(preview,"equivalent-cards"));
        var selectedStrokes=Primitives(VisualTreeHelper.GetDrawing(view.Plot)).OfType<GeometryDrawing>().Where(d=>d.Pen!=null && d.Pen.Thickness>=3).ToArray();
        Check(selectedStrokes.Any(d=>d.Pen.Brush is SolidColorBrush b && b.Color==Colors.Firebrick) && selectedStrokes.Any(d=>d.Pen.Brush is SolidColorBrush b && b.Color==Colors.SeaGreen),"selected load and reaction arrows are both highlighted");
        view.LoadCard.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!view.LoadCard.IsOpen && view.ReactionCard.IsOpen,"each calculation has its own close button");

        view.CloseAllDetails();Render(view,900);Check(!view.IsDetailsOpen,"close restores clean diagram");
        foreach(double width in new[] {900.0,760,700,520,360})
        {
            Render(view,width);view.Plot.SelectAt(Centre(view.Plot.Targets.First(x=>x.Component==1).Bounds));Render(view,width,Preview(preview,"open-"+width));
            Check(Math.Abs(view.ActualWidth-width)<1e-7,"narrow checks exercise the requested width");
            var detailBounds=new Rect(view.DetailsPanel.TranslatePoint(new Point(),view),new Size(view.DetailsPanel.ActualWidth,view.DetailsPanel.ActualHeight));
            var plotBounds=new Rect(view.Plot.TranslatePoint(new Point(),view),new Size(view.Plot.ActualWidth,view.Plot.ActualHeight));
            Check(detailBounds.Right<=view.ActualWidth+1e-7 && detailBounds.Bottom<=view.ActualHeight+1e-7 && plotBounds.Right<=view.ActualWidth+1e-7,"open panel and plot remain within actual container bounds");
            var overlap=Rect.Intersect(detailBounds,plotBounds);
            Check(overlap.IsEmpty || overlap.Width<=1e-7 || overlap.Height<=1e-7,"open panel cannot cover the diagram");
            foreach(var box in view.Plot.TextBounds)Check(box.Left>=-1e-7 && box.Right<=view.Plot.ActualWidth+1e-7 && box.Top>=0 && box.Bottom<=view.Plot.ActualHeight+1e-7,"rendered text remains inside actual plot");
            for(int i=0;i<view.Plot.TextBounds.Count;i++)for(int j=i+1;j<view.Plot.TextBounds.Count;j++)
                Check(!view.Plot.TextBounds[i].IntersectsWith(view.Plot.TextBounds[j]),"actual rendered value/dimension labels do not overlap");
            view.CloseAllDetails();
        }
        Check(ReferenceEquals(window.SelectedTransferMember.Uniform,actual.Uniform) && ReferenceEquals(window.SelectedTransferMember.Points,actual.Points),"actual higher nominal slab forces still generate the response plots");
        CheckResponse(window,actual);
        window.SelectMember(1,0,-1);Check(!window.EquivalentView.IsDetailsOpen,"repeated selection clears stale calculation selection");
        Render(window.EquivalentView,900);window.EquivalentView.Plot.SelectAt(Centre(window.EquivalentView.Plot.Targets.First(x=>x.Reaction==1).Bounds));
        Check(window.EquivalentView.VisibleCalculation.Any(s=>s.StartsWith("R2 · Primary UDL")),"repeated selection uses current reaction");
        foreach(int index in Enumerable.Range(0,t.UnitLowerCases[0].Uniform.Length+t.UnitLowerCases[0].Points.Length))
        {
            Render(window.EquivalentView,900);
            Check(window.EquivalentView.Plot.SelectAt(Centre(window.EquivalentView.Plot.Targets.First(x=>x.Component==index).Bounds)),"every equivalent load value is clickable");
            string source=EquivalentTransferredLoading.Create(layout,0,0).Components[index].Name;
            Check(window.EquivalentView.VisibleCalculation[0]==source,"arrow click reveals only the current source name");
            Render(window.EquivalentView,900);
            Check(window.EquivalentView.Plot.SelectAt(Centre(window.EquivalentView.Plot.Targets.Last(x=>x.Component==index).Bounds)) && window.EquivalentView.VisibleCalculation[0]==source,"every load value label also opens its own calculation");
            CheckResponse(window,actual);
        }
        window.LoadCaseSelector.SelectedIndex=1;
        Check(window.SelectedTransferMember==beamActual && window.EquivalentView!=null && !window.EquivalentView.IsDetailsOpen,"case change updates equivalent view and closes prior calculation panel");
        Check(window.ResponseBasis.Text.EndsWith("Beam"),"response basis identifies the selected design case");CheckResponse(window,beamActual);
        window.LoadCaseSelector.SelectedIndex=0;CheckResponse(window,actual);
        window.SelectMember(0,0,-1);RenderWindow(window,900);MemberLoadingUiTests.CheckWindow(window);Check(window.EquivalentView==null && window.ResponseBasis==null && window.LoadingDiagram!=null,"changing to primary clears equivalent panel and basis");
        window.SelectMember(1,0,-1);Check(window.EquivalentView!=null && !window.EquivalentView.IsDetailsOpen,"returning to lower member starts with a clean panel");
        RenderWindow(window,520,Preview(preview,"unified-narrow"));
        Check(window.EquivalentView.ActualWidth<=520,"unified window loading card fits the requested narrow layout");CheckResponse(window,actual);
        // Unavailable localized conversion must still expose one actual loading card.
        var primary=t.PrimaryCases[0][0];double original=primary.Uniform[0].KnPerM;
        primary.Uniform[0].KnPerM+=1;window.SelectMember(1,0,-1);
        Check(window.EquivalentView==null && window.LoadingDiagram!=null && !Controls<TextBlock>((DependencyObject)window.Content).Any(b=>b.Text.Contains("Secondary design checks")),"localized load fallback has one actual loading view and no removed-tab instruction");
        RenderWindow(window,900);MemberLoadingUiTests.CheckWindow(window);
        var actualPoint=window.LoadingDiagram.Targets.First(tg=>tg.Kind==1 && tg.Index==0);
        Check(window.LoadingDiagram.SelectAt(Centre(actualPoint.Bounds)) && window.MemberView.VisibleCalculation[0]==t.UnitPointLabels[0][0] && window.MemberView.VisibleCalculation.Any(line=>line.StartsWith("Actual tributary width:")),"actual secondary details preserve source names and unit-width calculations after reference conversion is unavailable");
        CheckResponse(window,actual);primary.Uniform[0].KnPerM=original;
        Check(Snapshot(actual).SequenceEqual(originalValues) && Snapshot(beamActual).SequenceEqual(beamValues),"clicks, cases and member changes preserve numeric forces, signed reactions, moments, deflections and utilization exactly");
        window.Close();
        var both=EquivalentTransferredLoading.Create(EquivalentLoadingTests.BothFixture(),0,0);
        var bothView=new EquivalentLoadingView(both,true);
        foreach(double width in new[] {900.0,760,700,520,360})
        {
            Render(bothView,width);bothView.Plot.SelectAt(Centre(bothView.Plot.Targets.First(x=>x.Reaction==0).Bounds));Render(bothView,width);
            Check(Math.Abs(bothView.ActualWidth-width)<1e-7,"both-sided checks exercise the requested width");
            Check(bothView.VisibleCalculation.Length==5 && !bothView.VisibleCalculation.Any(s=>s.Contains("slab foot")),"both-sided reaction card remains compact with one final division");
            foreach(var box in bothView.Plot.TextBounds)Check(box.Left>=0 && box.Right<=bothView.Plot.ActualWidth+1e-7 && box.Bottom<=bothView.Plot.ActualHeight,"both-sided labels stay inside plot");
            for(int i=0;i<bothView.Plot.TextBounds.Count;i++)for(int j=i+1;j<bothView.Plot.TextBounds.Count;j++)
                Check(!bothView.Plot.TextBounds[i].IntersectsWith(bothView.Plot.TextBounds[j]),"both-sided load/reaction labels remain separate");
            bothView.CloseAllDetails();
        }
        Console.WriteLine($"Passed {checks} equivalent loading WPF interaction/render checks.");
    }
}
