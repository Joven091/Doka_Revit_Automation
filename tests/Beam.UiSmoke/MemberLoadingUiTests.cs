using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EngineeringDashboard;

internal static class MemberLoadingUiTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Member loading UI: "+message);}
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd,ref NativePoint point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct NativePoint {internal int X,Y;}
    static Point Centre(Rect r)=>new Point(r.Left+r.Width/2,r.Top+r.Height/2);
    static IEnumerable<TextBlock> Text(DependencyObject root)
    {if(root is TextBlock t)yield return t;foreach(var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in Text(c))yield return item;}
    static void Render(FrameworkElement view,double width)
    {view.Measure(new Size(width,double.PositiveInfinity));view.Arrange(new Rect(0,0,width,view.DesiredSize.Height));view.UpdateLayout();new RenderTargetBitmap((int)width,(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32).Render(view);Pump();}
    internal static void Click(TowerDiagramWindow window,int kind,int index)
    {
        window.Activate();window.UpdateLayout();Pump();var plot=window.LoadingDiagram;
        var target=plot.Targets.Last(t=>t.Kind==kind && t.Index==index);var screen=plot.PointToScreen(Centre(target.Bounds));
        Check(SetCursorPos((int)Math.Round(screen.X),(int)Math.Round(screen.Y)),"mouse can reach the displayed calculation target");
        var source=(HwndSource)PresentationSource.FromVisual(plot);var client=new NativePoint {X=(int)Math.Round(screen.X),Y=(int)Math.Round(screen.Y)};
        Check(ScreenToClient(source.Handle,ref client),"mouse target maps to window client coordinates");
        var packed=new IntPtr((client.Y<<16)|(client.X&0xffff));
        // Deliver the normal native move/down/up sequence. A synthetic routed up
        // immediately after SetCursorPos can use WPF's previous cached position.
        SendMessage(source.Handle,0x0200,IntPtr.Zero,packed);Pump();
        Check(target.Bounds.Contains(Mouse.GetPosition(plot)),"native mouse move reaches target "+kind+"/"+index+"; pointer "+Mouse.GetPosition(plot)+"; bounds "+target.Bounds);
        SendMessage(source.Handle,0x0201,new IntPtr(1),packed);SendMessage(source.Handle,0x0202,IntPtr.Zero,packed);Pump();
        Check(window.MemberView.IsDetailsOpen,"mouse release opens actual member details");
        Check(plot.SelectedKind==kind && plot.SelectedIndex==index,"mouse selected the requested target "+kind+"/"+index+"; actual "+plot.SelectedKind+"/"+plot.SelectedIndex+"; pointer "+Mouse.GetPosition(plot)+"; bounds "+target.Bounds+"; panel "+string.Join(" | ",window.MemberView.VisibleCalculation));
    }
    internal static void CheckWindow(TowerDiagramWindow window,bool nativeClick=false)
    {
        window.UpdateLayout();Pump();var view=window.MemberView;var plot=window.LoadingDiagram;
        Check(view!=null && ReferenceEquals(view.Plot,plot),"the visible actual loading card has interaction attached");
        Check(!Text((DependencyObject)window.Content).Any(t=>t.Text.StartsWith("Fully loaded:") || t.Text.StartsWith("System stage:") || t.Text.Contains(" · score ")),"compact header has no system-score banner");
        var analysis=window.LoadedAnalysis;var member=window.DisplayedTransferMember;
        if(nativeClick)Click(window,0,0);
        foreach(int kind in new[] {0,1,2})
        {
            int count=kind==0 ? plot.CombinedUniform.Length : kind==1 ? plot.PointLoads.Length : plot.ReactionValues.Length;
            for(int i=0;i<count;i++)
            {
                var targets=plot.Targets.Where(t=>t.Kind==kind && t.Index==i).ToArray();
                Check(targets.Length>=2,"every nonzero load and every reaction has both value and arrow targets");
                foreach(var target in targets)
                {
                    Check(plot.SelectAt(Centre(target.Bounds)) && view.IsDetailsOpen,"arrow and value open the selected calculation");
                    if(kind==2)Check(Math.Abs(view.LastReactionContributions.Sum()-plot.ReactionValues[i])<1e-6*Math.Max(1,Math.Abs(plot.ReactionValues[i])),"signed load contributions reproduce each actual multi-support reaction");
                    Check(ReferenceEquals(window.LoadedAnalysis,analysis) && ReferenceEquals(window.DisplayedTransferMember,member),"clicking retains the exact checked analysis and member");
                }
            }
        }
        view.CloseDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(!view.IsDetailsOpen,"close restores the clean actual diagram");
    }
    internal static void ReportedPrimary(TowerDiagramWindow window)
    {
        CheckWindow(window,true);Click(window,0,0);
        Check(window.MemberView.VisibleCalculation.Any(t=>t=="Beam + Slab") && window.MemberView.VisibleCalculation.Any(t=>t.Contains("1062.5")) && window.MemberView.VisibleCalculation.Any(t=>t.Contains("25.99125")),"reported slab UDL exposes the actual formula and tributary width");
        Click(window,0,1);
        Check(window.MemberView.VisibleCalculation.Any(t=>t=="Beam + X1") && window.MemberView.VisibleCalculation.Any(t=>t.Contains("31.65")) && window.MemberView.VisibleCalculation.Any(t=>t.Contains("full width 650 mm")) && window.MemberView.VisibleCalculation.Any(t=>t.Contains("52.2881")),"reported crossing UDL exposes the actual formula: "+string.Join(" | ",window.MemberView.VisibleCalculation));
        Click(window,2,3);Check(window.MemberView.VisibleCalculation.Any(t=>t.StartsWith("Reaction R4")),"fourth reaction is routed to its own calculation");
        var old=window.MemberView;window.SelectMember(window.SelectedRun,window.SelectedLine,-1);Pump();
        Check(!ReferenceEquals(old,window.MemberView) && !window.MemberView.IsDetailsOpen,"reselection cannot retain a stale calculation panel");
    }
    internal static void Run()
    {
        var member=new TransferMember {Name="P1 L",H20Count=2,LengthMm=2000,SupportsMm=new[] {250.0,900,1750},
            Uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=4},new BeamDistributedLoad {StartMm=500,EndMm=1300,KnPerM=2}},
            Points=new[] {new BeamPointLoad {PositionMm=100,Kn=-1},new BeamPointLoad {PositionMm=1500,Kn=0},new BeamPointLoad {PositionMm=1400,Kn=3}}};
        member.Analysis=LoadedBeamAnalysis.AnalyzeSigned(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,270);
        var plot=new TowerDiagramWindow.MemberLoadingPlot(member.LengthMm,member.SupportsMm,member.Analysis.Reactions,member.Uniform,member.Points);
        var view=new MemberLoadingView(plot,member.LengthMm,member.SupportsMm,member.Analysis.Reactions,member.EI,"Dedicated crossing support · actual retained bearing transfers");
        foreach(double width in new[] {1000.0,800,760,520,360})
        {
            Render(view,width);Check(plot.PointLoads.Length==2 && plot.Targets.All(t=>t.Kind!=1 || t.Index<2),"zero point loads have no target; signed loads remain");
            plot.SelectAt(Centre(plot.Targets.First(t=>t.Kind==2).Bounds));Render(view,width);
            Check(Math.Abs(view.LastReactionContributions.Sum()-member.Analysis.Reactions[0])<1e-7,"fallback detail sums signed reactions without applying full crossing load twice");
            var detail=new Rect(view.DetailsPanel.TranslatePoint(new Point(),view),view.DetailsPanel.RenderSize);var chart=new Rect(plot.TranslatePoint(new Point(),view),plot.RenderSize);
            var overlap=Rect.Intersect(detail,chart);
            Check(overlap.IsEmpty || overlap.Width<1e-7 || overlap.Height<1e-7,"open details never obscure the loading diagram");
            Check(detail.Left>=0 && detail.Right<=width+1e-7 && detail.Bottom<=view.ActualHeight+1e-7,"responsive calculation panel remains inside its container");
            Check(view.VisibleCalculation.Any(t=>t.Contains("Dedicated crossing support")),"fallback identifies its actual support path");
            view.CloseDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Check(member.Points.Length==3 && member.Points[1].Kn==0,"presentation preserves original design arrays");
        Console.WriteLine($"Passed {checks} actual-member loading interaction checks.");
    }
}
