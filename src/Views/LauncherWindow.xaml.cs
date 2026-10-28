using System;
using System.Windows;

namespace EngineeringDashboard
{
    public partial class LauncherWindow : Window
    {
        private static FlexCalculatorViewModel _flexViewModel;
        private static BeamCalculatorViewModel _beamViewModel;
        private static D3SlabCalculatorViewModel _d3SlabViewModel;
        private static Dome.MainViewModel _domeViewModel;
        private static Top50ViewModel _top50ViewModel;
        private Window currentTool;
        private bool closing;
        internal Window CurrentTool=>currentTool;

        public LauncherWindow()
        {
            InitializeComponent();
            Closing+=(s,e)=>closing=true;
        }
        internal void ActivateCurrentTool()
        {
            var target=currentTool??this;
            if(target.WindowState==WindowState.Minimized)target.WindowState=WindowState.Normal;
            if(!target.IsVisible)target.Show();
            target.Activate();
        }
        private void ShowDashboard(){if(!closing){Show();Activate();}}
        // Owned windows remain modeless; returning to Revit does not close or
        // reset the current calculation. Only one dashboard tool is opened.
        internal void ShowTool(Window tool,Action afterClose=null)
        {
            if(currentTool!=null){ActivateCurrentTool();return;}
            tool.Owner=this;currentTool=tool;
            tool.Closed+=(s,e)=>
            {
                currentTool=null;
                if(closing)return;
                if(afterClose!=null)afterClose();else ShowDashboard();
            };
            Hide();
            try {tool.Show();}
            catch {currentTool=null;ShowDashboard();throw;}
        }
        private void BtnAccount_Click(object sender,RoutedEventArgs e)=>MyRevitAddin.Access.AccessWindow.Manage(this);
        private void BtnUpdates_Click(object sender,RoutedEventArgs e)=>ShowTool(new MyRevitAddin.Updates.UpdateWindow());
        private void BtnClose_Click(object sender,RoutedEventArgs e)=>Close();
        private void BtnDomeCalculator_Click(object sender,RoutedEventArgs e)
        {
            if(!BtnDomeCalculator.IsEnabled || !MyRevitAddin.Access.AccessWindow.Require(this))return;
            if(_domeViewModel==null)_domeViewModel=new Dome.MainViewModel();
            ShowTool(new Dome.MainWindow(_domeViewModel));
        }
        private void BtnBackpropping_Click(object sender,RoutedEventArgs e)
        {if(MyRevitAddin.Access.AccessWindow.Require(this))ShowTool(new BackproppingWindow());}
        private void BtnSteelShape_Click(object sender,RoutedEventArgs e)
        {if(MyRevitAddin.Access.AccessWindow.Require(this))ShowTool(new SteelShape.SteelShapeWindow());}
        private void BtnTop50_Click(object sender,RoutedEventArgs e)
        {
            if(!MyRevitAddin.Access.AccessWindow.Require(this))return;
            if(_top50ViewModel==null)_top50ViewModel=new Top50ViewModel();
            Top50Window planner=null;
            var connection=new Top50.Top50RevitConnection
            {
                ReadSelection=()=>Command.Request(app=>{if(planner?.IsLoaded!=true)throw new System.ObjectDisposedException("Top50 Planner");return Top50RevitSelection.ReadSelected(app);}),
                Refresh=request=>Command.Request(app=>{if(planner?.IsLoaded!=true)throw new System.ObjectDisposedException("Top50 Planner");return Top50RevitSelection.Refresh(app,request);}),
                Highlight=request=>Command.Request(app=>{if(planner?.IsLoaded!=true)throw new System.ObjectDisposedException("Top50 Planner");return Top50RevitSelection.Highlight(app,request);})
            };
            planner=new Top50Window(_top50ViewModel,connection);ShowTool(planner);
        }
        private void BtnFlexCalculator_Click(object sender,RoutedEventArgs e)
        {if(MyRevitAddin.Access.AccessWindow.Require(this))OpenSlabChoice();}
        private void OpenSlabChoice()
        {
            var choice=new SlabSystemWindow();
            ShowTool(choice,()=>
            {
                if(!choice.HasSelection){ShowDashboard();return;}
                if(choice.UseD3)
                {
                    if(_d3SlabViewModel==null)_d3SlabViewModel=new D3SlabCalculatorViewModel();
                    var d3=new D3SlabCalculatorWindow(_d3SlabViewModel);
                    ShowTool(d3,()=>{if(d3.ChangeSystemRequested)OpenSlabChoice();else ShowDashboard();});
                }
                else
                {
                    if(_flexViewModel==null)_flexViewModel=new FlexCalculatorViewModel();
                    var flex=new FlexCalculatorWindow{ViewModel=_flexViewModel};
                    ShowTool(flex,()=>{if(flex.ChangeSystemRequested)OpenSlabChoice();else ShowDashboard();});
                }
            });
        }
        private void BtnBeamCalculator_Click(object sender,RoutedEventArgs e)
        {
            if(!MyRevitAddin.Access.AccessWindow.Require(this))return;
            if(_beamViewModel==null)_beamViewModel=new BeamCalculatorViewModel();
            var beam=new BeamCalculatorWindow{DataContext=_beamViewModel};
            beam.CheckForUpdates=()=>
            {
                var update=new MyRevitAddin.Updates.UpdateWindow{Owner=beam};
                update.Show();
            };
            beam.RequestRevitSelection=()=>Command.Request(RevitStructureSelection.Read);
            ShowTool(beam);
        }
    }
}

