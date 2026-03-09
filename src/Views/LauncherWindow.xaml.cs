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

        private void BtnPlateCalculator_Click(object sender, RoutedEventArgs e)
        {
            // Hide the launcher
            this.Hide();

            // Instantiate and show the Plate Calculator window
            PlateCalculatorWindow plateWindow = new PlateCalculatorWindow();
            plateWindow.ShowDialog();

            // Show the launcher again once the calculator is closed
            this.Show();
        }
    }
}