using System.Windows;

namespace EngineeringDashboard
{
    public partial class BeamCalculatorWindow : Window
    {
        public BeamCalculatorWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
