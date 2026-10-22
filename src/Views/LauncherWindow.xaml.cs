using System.Windows;



namespace EngineeringDashboard
{
    public partial class LauncherWindow : Window
    {
        // Static ViewModels to persist state across command executions
        private static FlexCalculatorViewModel _flexViewModel;
        private static BeamCalculatorViewModel _beamViewModel;
        private static D3SlabCalculatorViewModel _d3SlabViewModel;
        private static Dome.MainViewModel _domeViewModel;

        public LauncherWindow()
        {
            InitializeComponent();
        }
        private void BtnAccount_Click(object sender, RoutedEventArgs e) => MyRevitAddin.Access.AccessWindow.Manage(this);
        private void BtnUpdates_Click(object sender, RoutedEventArgs e) => new MyRevitAddin.Updates.UpdateWindow { Owner=this }.ShowDialog();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnDomeCalculator_Click(object sender, RoutedEventArgs e)
        {
            if(!BtnDomeCalculator.IsEnabled)return;
            if (!MyRevitAddin.Access.AccessWindow.Require(this)) return;
            Hide();
            try
            {
                if (_domeViewModel == null) _domeViewModel = new Dome.MainViewModel();
                new Dome.MainWindow(_domeViewModel) { Owner = this }.ShowDialog();
            }
            finally { Show(); }
        }

        private void BtnBackpropping_Click(object sender, RoutedEventArgs e)
        {
            if (!MyRevitAddin.Access.AccessWindow.Require(this)) return;
            Hide();
            try { new BackproppingWindow { Owner = this }.ShowDialog(); }
            finally { Show(); }
        }

        private void BtnFlexCalculator_Click(object sender, RoutedEventArgs e)
        {
            if (!MyRevitAddin.Access.AccessWindow.Require(this)) return;
            Hide();
            try
            {
                while (true)
                {
                    var choice = new SlabSystemWindow { Owner = this };
                    if (choice.ShowDialog() != true) break;
                    if (!MyRevitAddin.Access.AccessWindow.Require(this)) break;
                    if (choice.UseD3)
                    {
                        if (_d3SlabViewModel == null) _d3SlabViewModel = new D3SlabCalculatorViewModel();
                        var d3 = new D3SlabCalculatorWindow(_d3SlabViewModel) { Owner = this };
                        d3.ShowDialog(); if (d3.ChangeSystemRequested) continue; break;
                    }
                    if (_flexViewModel == null) _flexViewModel = new FlexCalculatorViewModel();
                    var flex = new FlexCalculatorWindow { ViewModel = _flexViewModel, Owner = this };
                    bool? result = flex.ShowDialog();
                    if (flex.ChangeSystemRequested) continue;
                    if (result == true) { closingAfterImport = true; Close(); return; }
                    break;
                }
            }
            finally { if (!closingAfterImport) Show(); }
        }
        private bool closingAfterImport;
        private void BtnBeamCalculator_Click(object sender, RoutedEventArgs e)
        {
            if (!MyRevitAddin.Access.AccessWindow.Require(this)) return;
            // Hide the launcher
            this.Hide();

            if (_beamViewModel == null)
            {
                _beamViewModel = new BeamCalculatorViewModel();
            }

            BeamCalculatorWindow beamWindow = new BeamCalculatorWindow();
            beamWindow.CheckForUpdates=()=>new MyRevitAddin.Updates.UpdateWindow {Owner=beamWindow}.ShowDialog();
            beamWindow.ReadRevitSelection=()=>RevitStructureSelection.Read(Command.App);
            beamWindow.DataContext = _beamViewModel;

            beamWindow.ShowDialog();

            // Show the launcher again once the calculator is closed
            this.Show();
        }
    }
}

