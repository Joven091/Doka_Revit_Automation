using System.Windows;

namespace EngineeringDashboard
{
    public partial class LauncherWindow : Window
    {
        public LauncherWindow()
        {
            InitializeComponent();
        }

        private void BtnFlexCalculator_Click(object sender, RoutedEventArgs e)
        {
            // Hide the launcher
            this.Hide();

            // Instantiate and show the Flex Calculator window
            FlexCalculatorWindow flexWindow = new FlexCalculatorWindow();
            flexWindow.ShowDialog();

            // Show the launcher again once the calculator is closed
            this.Show();
        }

        private void BtnBeamCalculator_Click(object sender, RoutedEventArgs e)
        {
            // Hide the launcher
            this.Hide();

            // Instantiate and show the Beam Calculator window
            BeamCalculatorWindow beamWindow = new BeamCalculatorWindow();
            beamWindow.ShowDialog();

            // Show the launcher again once the calculator is closed
            this.Show();
        }
    }
}