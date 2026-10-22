using System.Windows;
namespace EngineeringDashboard
{
    public partial class SlabSystemWindow : Window
    {
        public bool UseD3 { get; private set; }
        public SlabSystemWindow() { InitializeComponent(); }
        private void PickProps_Click(object sender, RoutedEventArgs e) { UseD3 = false; DialogResult = true; }
        private void PickTowers_Click(object sender, RoutedEventArgs e) { if(!PickTowersButton.IsEnabled)return;UseD3 = true; DialogResult = true; }
    }
}
