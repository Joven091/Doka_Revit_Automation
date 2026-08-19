using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace EngineeringDashboard
{
    public partial class LauncherWindow : Window
    {
        // Static ViewModels to persist state across command executions
        private static FlexCalculatorViewModel _flexViewModel;
        private static BeamCalculatorViewModel _beamViewModel;

        public LauncherWindow()
        {
            InitializeComponent();
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