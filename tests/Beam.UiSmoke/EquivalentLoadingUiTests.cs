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
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        if(path!=null){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
    }
    static Point Centre(Rect b)=>new Point(b.X+b.Width/2,b.Y+b.Height/2);
    static string Preview(string path,string suffix)=>path==null ? null : Path.ChangeExtension(path,suffix+".png");
    internal static void Run(string preview=null)
    {
        var layout=EquivalentLoadingTests.Fixture();var t=layout.Transfer;var unit=t.UnitLowerCases[0];
        var actual=new TransferMember {Name="T1",LoadCase=unit.LoadCase,H20Count=1,LengthMm=1800,StartMm=-900,SupportsMm=unit.SupportsMm,
            Uniform=new[] {new BeamDistributedLoad {StartMm=575,EndMm=1225,KnPerM=9.65325}},
            Points=new[] {new BeamPointLoad {PositionMm=199,Kn=3.44322},new BeamPointLoad {PositionMm=475,Kn=.8646308}}};
        actual.Analysis=LoadedBeamAnalysis.AnalyzeSigned(actual.LengthMm,actual.SupportsMm,actual.Uniform,actual.Points,TowerOptimizer.PairEI/2,270);
        t.LowerCases=new[] {new[] {actual}};t.TributaryWidthsMm=new[] {305.0};t.SlabRowIndices=new[] {0};
        layout.StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StockLengthMm=6900}}};
        var vm=new BeamCalculatorViewModel {UseTowers=true};typeof(BeamCalculatorViewModel).GetProperty("TowerResult").SetValue(vm,layout);
        var window=new TowerDiagramWindow(vm);window.SelectMember(1,0,-1);
        Check(window.LoadingTabs.Items.Count==2 && window.LoadingTabs.SelectedIndex==0,"equivalent and actual design checks are separate tabs");
        Check(ReferenceEquals(window.LoadedAnalysis,actual.Analysis) && ReferenceEquals(window.DisplayedTransferMember,actual),"equivalent display never replaces checked analysis");
        var view=window.EquivalentView;Render(view,900,Preview(preview,"closed"));
        Check(!view.IsDetailsOpen,"calculation panel closed initially");
        var foot=view.Plot.Targets.First(x=>x.Component==1);Check(view.Plot.SelectAt(Centre(foot.Bounds)),"foot arrow/value is selectable");
        Render(view,900,Preview(preview,"foot"));
        Check(view.IsDetailsOpen && view.VisibleCalculation.Any(s=>s.Contains("3.44322")) && view.VisibleCalculation.Any(s=>s.Contains("305 / 407")) && view.VisibleCalculation.Any(s=>s.Contains("2.5803")),"compact foot panel shows source, nominal conversion and equivalent result");
        var reaction=view.Plot.Targets.First(x=>x.Reaction==0);Check(view.Plot.SelectAt(Centre(reaction.Bounds)),"reaction is selectable");Render(view,900,Preview(preview,"reaction"));
        Check(view.VisibleCalculation.Any(s=>s.Contains("3.16345")) && view.VisibleCalculation.Any(s=>s.Contains("7.07316 / 0.305")) && view.VisibleCalculation.Any(s=>s.Contains("23.1907")),"reaction panel shows components and one final division");
        Check(ReferenceEquals(window.LoadedAnalysis,actual.Analysis),"clicks leave actual member checks intact");
        view.CloseDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render(view,900);Check(!view.IsDetailsOpen,"close restores clean diagram");
        foreach(double width in new[] {900.0,700,520})
        {
            Render(view,width);view.Plot.SelectAt(Centre(view.Plot.Targets.First(x=>x.Component==1).Bounds));Render(view,width);
            var detailBounds=new Rect(view.DetailsPanel.TranslatePoint(new Point(),view),new Size(view.DetailsPanel.ActualWidth,view.DetailsPanel.ActualHeight));
            var plotBounds=new Rect(view.Plot.TranslatePoint(new Point(),view),new Size(view.Plot.ActualWidth,view.Plot.ActualHeight));
            Check(detailBounds.Right<=view.ActualWidth+1e-7 && detailBounds.Bottom<=view.ActualHeight+1e-7 && plotBounds.Right<=view.ActualWidth+1e-7,"open panel and plot remain within actual container bounds");
            var overlap=Rect.Intersect(detailBounds,plotBounds);
            Check(overlap.IsEmpty || overlap.Width<=1e-7 || overlap.Height<=1e-7,"open panel cannot cover the diagram");
            foreach(var box in view.Plot.TextBounds)Check(box.Left>=-1e-7 && box.Right<=view.Plot.ActualWidth+1e-7 && box.Top>=0 && box.Bottom<=view.Plot.ActualHeight+1e-7,"rendered text remains inside actual plot");
            for(int i=0;i<view.Plot.TextBounds.Count;i++)for(int j=i+1;j<view.Plot.TextBounds.Count;j++)
                Check(!view.Plot.TextBounds[i].IntersectsWith(view.Plot.TextBounds[j]),"actual rendered value/dimension labels do not overlap");
            view.CloseDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        window.LoadingTabs.SelectedIndex=1;Check(window.LoadingTabs.SelectedIndex==1 && ReferenceEquals(window.LoadingDiagram.SourceUniform,actual.Uniform) && ReferenceEquals(window.SelectedTransferMember.Points,actual.Points),"design tab retains actual higher nominal slab forces");
        window.SelectMember(1,0,-1);Check(window.LoadingTabs.SelectedIndex==1 && !window.EquivalentView.IsDetailsOpen,"repeated selection preserves tab and clears stale calculation selection");
        window.LoadingTabs.SelectedIndex=0;Render(window.EquivalentView,900);window.EquivalentView.Plot.SelectAt(Centre(window.EquivalentView.Plot.Targets.First(x=>x.Reaction==1).Bounds));
        Check(window.EquivalentView.VisibleCalculation.Any(s=>s.StartsWith("Right reaction")),"repeated selection uses current reaction");
        window.Close();
        var both=EquivalentTransferredLoading.Create(EquivalentLoadingTests.BothFixture(),0,0);
        var bothView=new EquivalentLoadingView(both,true,()=>{});
        foreach(double width in new[] {900.0,700,520})
        {
            Render(bothView,width);bothView.Plot.SelectAt(Centre(bothView.Plot.Targets.First(x=>x.Reaction==0).Bounds));Render(bothView,width);
            Check(bothView.VisibleCalculation.Count(s=>s.Contains(" kN"))==7,"both-sided reaction panel includes every source and final division");
            foreach(var box in bothView.Plot.TextBounds)Check(box.Left>=0 && box.Right<=bothView.Plot.ActualWidth+1e-7 && box.Bottom<=bothView.Plot.ActualHeight,"both-sided labels stay inside plot");
            for(int i=0;i<bothView.Plot.TextBounds.Count;i++)for(int j=i+1;j<bothView.Plot.TextBounds.Count;j++)
                Check(!bothView.Plot.TextBounds[i].IntersectsWith(bothView.Plot.TextBounds[j]),"both-sided load/reaction labels remain separate");
            bothView.CloseDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Console.WriteLine($"Passed {checks} equivalent loading WPF interaction/render checks.");
    }
}
