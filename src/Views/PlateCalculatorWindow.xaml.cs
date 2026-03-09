using System.Windows;

namespace EngineeringDashboard
{
    public partial class PlateCalculatorWindow : Window
    {
        public PlateCalculatorWindow()
        {
            InitializeComponent();
            this.DataContext = new PlateCalculatorViewModel();
        }

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            string info = "--- DESIGN FORMULAS ---\n\n" +
                          "BENDING THICKNESS:\n" +
                          "t >= sqrt( (1.5 * F * L) / (b_eff * py) )\n\n" +
                          "PUNCHING SHEAR THICKNESS:\n" +
                          "t >= F / (PI * d_nut * (0.4 * py))\n\n" +
                          "Where:\n" +
                          "F = Tie Load (Converted to Newtons)\n" +
                          "L = Span (mm)\n" +
                          "b_eff = Plate Width - Hole Dia (mm)\n" +
                          "d_nut = Nut Bearing Diameter (mm)\n" +
                          "py = Steel Yield Strength (N/mm2)";
            
            MessageBox.Show(info, "Formulas", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}