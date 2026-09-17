#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace EngineeringDashboard.Dome
{
    public partial class MainWindow : Window
    {
        public MainWindow() : this(new MainViewModel()) { }

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.DialogOwner = this;
            Closed += (_, _) => { if (ReferenceEquals(viewModel.DialogOwner, this)) viewModel.DialogOwner = null; };
        }
    }
}

