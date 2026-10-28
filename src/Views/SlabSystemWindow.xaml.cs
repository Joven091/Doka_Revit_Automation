using System.Windows;
namespace EngineeringDashboard
{
    public partial class SlabSystemWindow : Window
    {
        public bool UseD3 { get; private set; }
        internal bool HasSelection { get; private set; }
        public SlabSystemWindow() { InitializeComponent(); }
        private void PickProps_Click(object sender, RoutedEventArgs e) { UseD3 = false; HasSelection=true; Close(); }
        private void PickTowers_Click(object sender, RoutedEventArgs e) { if(!PickTowersButton.IsEnabled)return;UseD3 = true; HasSelection=true; Close(); }
    }
}
