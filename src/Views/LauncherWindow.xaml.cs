using System.Windows;



namespace EngineeringDashboard
{
    public partial class LauncherWindow : Window
    {
        // Static ViewModels to persist state across command executions
        private static FlexCalculatorViewModel _flexViewModel;
        private static BeamCalculatorViewModel _beamViewModel;
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
            // Hide the launcher
            this.Hide();

            if (_flexViewModel == null)
            {
                _flexViewModel = new FlexCalculatorViewModel();
            }

            FlexCalculatorWindow flexWindow = new FlexCalculatorWindow();
            flexWindow.ViewModel = _flexViewModel;

            bool? result = flexWindow.ShowDialog();

            if (result == true)
            {
                // Operation completed successfully and user wants to stop, close launcher
                this.Close();
            }
            else
            {
                // Show the launcher again once the calculator is closed normally
                this.Show();
            }
        }

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

