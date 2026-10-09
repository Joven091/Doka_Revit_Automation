using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EngineeringDashboard
{
    /// <summary>A drawing-area overlay. Timer and model subscriptions belong to the loaded view.</summary>
    public sealed class BeamSearchWaitingView : Grid
    {
        internal readonly HugoMascot Hugo=new HugoMascot {Width=90,Height=100,HorizontalAlignment=HorizontalAlignment.Center};
        internal readonly TextBlock Stage=new TextBlock {TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,2)};
        internal readonly TextBlock Elapsed=new TextBlock {TextAlignment=TextAlignment.Center,Foreground=Brushes.SlateGray};
        internal readonly Button Cancel=new Button {Content="Cancel analysis",HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,14,0,0),Padding=new Thickness(14,5,14,5)};
        private readonly DispatcherTimer timer;
        private BeamCalculatorViewModel model;
        internal bool IsTimerRunning=>timer.IsEnabled;
        public BeamSearchWaitingView()
        {
            Visibility=Visibility.Collapsed;Background=new SolidColorBrush(Color.FromArgb(245,255,255,255));
            var stack=new StackPanel {VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=320,Margin=new Thickness(20)};
            stack.Children.Add(Hugo);
            stack.Children.Add(new TextBlock {Text="Finding a tower layout…",FontSize=17,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center});
            stack.Children.Add(Stage);stack.Children.Add(Elapsed);stack.Children.Add(Cancel);Children.Add(stack);
            AutomationProperties.SetName(Hugo,"Hugo, the H20 helper");
            AutomationProperties.SetLiveSetting(Stage,AutomationLiveSetting.Polite);
            timer=new DispatcherTimer(DispatcherPriority.Background,Dispatcher) {Interval=TimeSpan.FromMilliseconds(120)};
            timer.Tick+=(s,e)=>RefreshProgress();
            Cancel.Click+=(s,e)=> {model?.CancelOptimization();RefreshProgress();};
            Loaded+=(s,e)=>Attach();Unloaded+=(s,e)=>Detach();DataContextChanged+=(s,e)=> {if(IsLoaded)Attach();};
        }
        private void Attach()
        {
            Detach();model=DataContext as BeamCalculatorViewModel;
            if(model!=null)model.PropertyChanged+=ModelChanged;
            RefreshSearch();
        }
        private void Detach()
        {
            timer.Stop();Hugo.Animate=false;if(model!=null)model.PropertyChanged-=ModelChanged;model=null;Visibility=Visibility.Collapsed;
        }
        private void ModelChanged(object sender,PropertyChangedEventArgs e)
        {
            if(e.PropertyName==nameof(BeamCalculatorViewModel.IsOptimizing) || e.PropertyName==nameof(BeamCalculatorViewModel.CanCancelOptimization))RefreshSearch();
        }
        private void RefreshSearch()
        {
            bool active=model?.IsOptimizing==true;
            Visibility=active ? Visibility.Visible : Visibility.Collapsed;
            if(active){RefreshProgress();timer.Start();}else {timer.Stop();Hugo.Animate=false;}
        }
        internal void RefreshProgress()
        {
            if(model==null)return;
            Hugo.Visibility=model.SearchElapsedSeconds>=1 ? Visibility.Visible : Visibility.Hidden;
            // Respect the Windows animation preference; progress and cancel remain available.
            Hugo.Animate=model.IsOptimizing && Hugo.Visibility==Visibility.Visible && SystemParameters.ClientAreaAnimation;
            Stage.Text=model.SearchProgressText;Elapsed.Text=$"{model.SearchElapsedSeconds:0} seconds elapsed";
            Cancel.IsEnabled=model.CanCancelOptimization;
        }
    }
    internal sealed class HugoMascot : FrameworkElement
    {
        private double phase;
        private readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
        private bool animate;
        internal bool Animate
        {
            get=>animate;
            set
            {
                if(animate==value)return;animate=value;
                if(value){clock.Restart();CompositionTarget.Rendering+=Frame;}
                else {CompositionTarget.Rendering-=Frame;clock.Reset();Phase=0;}
            }
        }
        internal HugoMascot(){Unloaded+=(s,e)=>Animate=false;}
        private void Frame(object sender,EventArgs args)=>Phase=clock.Elapsed.TotalSeconds;
        internal double Phase {get=>phase;set {phase=value;InvalidateVisual();}}
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);if(ActualWidth<=0 || ActualHeight<=0)return;
            double scale=Math.Min(ActualWidth/90,ActualHeight/100),bob=Math.Sin(phase*2.4)*2.5,sway=Math.Sin(phase*1.6)*3;
            dc.PushTransform(new ScaleTransform(scale,scale));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(25,60,60,60)),null,new Point(45,94),25,3);
            dc.PushTransform(new TranslateTransform(0,bob));
            dc.PushTransform(new RotateTransform(sway,45,84));
            var edge=new Pen(Brushes.SaddleBrown,1.1);
            // Boots, two broad flanges and a narrower timber web.
            dc.DrawRoundedRectangle(Brushes.SlateGray,null,new Rect(22,83,17,10),4,4);
            dc.DrawRoundedRectangle(Brushes.SlateGray,null,new Rect(51,83,17,10),4,4);
            dc.DrawRoundedRectangle(Brushes.Goldenrod,edge,new Rect(19,70,52,15),3,3);
            dc.DrawRectangle(Brushes.BurlyWood,edge,new Rect(30,33,30,37));
            dc.DrawRoundedRectangle(Brushes.Goldenrod,edge,new Rect(19,23,52,16),3,3);
            // Hard hat and brim.
            dc.DrawEllipse(Brushes.Gold,edge,new Point(45,22),22,16);
            dc.DrawRoundedRectangle(Brushes.Gold,edge,new Rect(16,23,58,7),3,3);
            dc.DrawLine(new Pen(Brushes.DarkGoldenrod,2),new Point(45,9),new Point(45,22));
            double blink=phase>0 ? Math.Max(.12,1-Math.Exp(-Math.Pow((phase%4.8-4.5)/.10,2))) : 1;
            foreach(double x in new[] {38.0,52})
                dc.DrawEllipse(Brushes.DarkSlateGray,null,new Point(x,47),2,2.5*blink);
            dc.DrawEllipse(Brushes.SandyBrown,null,new Point(34,55),3,2);
            dc.DrawEllipse(Brushes.SandyBrown,null,new Point(56,55),3,2);
            var smile=new StreamGeometry();using(var p=smile.Open()){p.BeginFigure(new Point(40,56),false,false);p.QuadraticBezierTo(new Point(45,62),new Point(50,56),true,false);}dc.DrawGeometry(null,new Pen(Brushes.SaddleBrown,1.4),smile);
            // Clipboard and a small measuring hand.
            double wave=Math.Sin(phase*2.1)*7;
            dc.DrawLine(new Pen(Brushes.BurlyWood,5) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},new Point(30,57),new Point(17+Math.Sin(phase*2.1)*2,57+wave));
            dc.DrawEllipse(Brushes.BurlyWood,edge,new Point(17+Math.Sin(phase*2.1)*2,57+wave),3.5,3.5);
            dc.DrawLine(new Pen(Brushes.BurlyWood,5),new Point(60,57),new Point(70,61));
            dc.PushTransform(new RotateTransform(Math.Sin(phase*1.9)*5,65,57));
            dc.DrawRoundedRectangle(Brushes.Wheat,edge,new Rect(60,57,22,27),2,2);
            dc.DrawRectangle(Brushes.SlateGray,null,new Rect(66,54,10,5));
            foreach(double y in new[] {65.0,71,77})dc.DrawLine(new Pen(Brushes.Tan,1),new Point(65,y),new Point(77,y));
            dc.DrawLine(new Pen(Brushes.SeaGreen,1.5),new Point(64,65),new Point(66,67));
            dc.DrawLine(new Pen(Brushes.SeaGreen,1.5),new Point(66,67),new Point(69,63));
            dc.Pop();dc.Pop();dc.Pop();dc.Pop();
        }
    }
}
