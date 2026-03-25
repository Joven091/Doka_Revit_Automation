using System.Windows;

namespace EngineeringDashboard
{
    public partial class FlexCalculatorWindow : Window
    {
        public FlexCalculatorWindow()
        {
            InitializeComponent();
            
            // This line links the UI to your calculation logic
            this.DataContext = new FlexCalculatorViewModel();
        }
    }
}