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
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnDomeCalculator_Click(object sender, RoutedEventArgs e)
        {
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
            Hide();
            try { new BackproppingWindow { Owner = this }.ShowDialog(); }
            finally { Show(); }
        }

        private void BtnFlexCalculator_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            try
            {
                while (true)
                {
                    var choice = new SlabSystemWindow { Owner = this };
                    if (choice.ShowDialog() != true) break;
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
            // Hide the launcher
            this.Hide();

            if (_beamViewModel == null)
            {
                _beamViewModel = new BeamCalculatorViewModel();
            }

            BeamCalculatorWindow beamWindow = new BeamCalculatorWindow();
            beamWindow.DataContext = _beamViewModel;

            beamWindow.ShowDialog();

            // Show the launcher again once the calculator is closed
            this.Show();
        }
    }
}

