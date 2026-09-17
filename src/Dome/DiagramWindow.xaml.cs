#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace EngineeringDashboard.Dome
{
    public partial class DiagramWindow : Window
    {
        public DiagramWindow(DiagramViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void ShowModelScope(object sender, RoutedEventArgs e)
        {
            if (DataContext is DiagramViewModel viewModel)
                MessageBox.Show(this, viewModel.ScopeText, "Model assumptions", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
