using System;
using System.Windows;
using MyRevitAddin.Views;
namespace EngineeringDashboard
{
    public partial class FlexCalculatorWindow : Window
    {
        private DiagramWindow _primaryDiagramWindow;
        private DiagramWindow _secondaryDiagramWindow;
        private FlexCalculatorViewModel _viewModel;

        public FlexCalculatorWindow()
        {
            InitializeComponent();
            InitializeUi();
            
            _viewModel = new FlexCalculatorViewModel();
            this.DataContext = _viewModel;

            _viewModel.PrimaryBeamDiagramRequested += OnPrimaryBeamDiagramRequested;
            _viewModel.SecondaryBeamDiagramRequested += OnSecondaryBeamDiagramRequested;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_primaryDiagramWindow != null && _primaryDiagramWindow.IsLoaded)
            {
                if (e.PropertyName == nameof(_viewModel.PrimaryDiagramViewModel))
                    _primaryDiagramWindow.DataContext = _viewModel.PrimaryDiagramViewModel;
            }

            if (_secondaryDiagramWindow != null && _secondaryDiagramWindow.IsLoaded)
            {
                if (e.PropertyName == nameof(_viewModel.SecondaryDiagramViewModel))
                    _secondaryDiagramWindow.DataContext = _viewModel.SecondaryDiagramViewModel;
            }
        }

        private void OnPrimaryBeamDiagramRequested(object sender, EventArgs e)
        {
            if (_primaryDiagramWindow == null || !_primaryDiagramWindow.IsLoaded)
            {
                _primaryDiagramWindow = new DiagramWindow
                {
                    WindowTitle = "Primary Beam Diagrams",
                    DataContext = _viewModel.PrimaryDiagramViewModel
                };

                // Keep it non-modal but owned by the main dashboard if we want it to stay on top,
                // or null owner if we want independent floating. Setting owner to this ensures it closes when dashboard closes.
                _primaryDiagramWindow.Owner = this;
                _primaryDiagramWindow.Show();
            }
            else
            {
                _primaryDiagramWindow.Activate();
            }
        }

        private void OnSecondaryBeamDiagramRequested(object sender, EventArgs e)
        {
            if (_secondaryDiagramWindow == null || !_secondaryDiagramWindow.IsLoaded)
            {
                _secondaryDiagramWindow = new DiagramWindow
                {
                    WindowTitle = "Secondary Beam Diagrams",
                    DataContext = _viewModel.SecondaryDiagramViewModel
                };

                _secondaryDiagramWindow.Owner = this;
                _secondaryDiagramWindow.Show();
            }
            else
            {
                _secondaryDiagramWindow.Activate();
            }
        }

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            string info = "--- DESIGN FORMULAS ---\n\n" +
                          "SUPPORT REACTIONS (R_max):\n" +
                          "For a/L <= 0.4534:\n" +
                          "R_max = q * (1.1 * L - 0.6 * (a^2 / L))\n\n" +
                          "For a/L > 0.4534:\n" +
                          "R_max = q * (a + 0.4 * L + 0.6 * (a^2 / L))\n\n" +
                          "PROP LOAD (P):\n" +
                          "P = R_max * P_m\n\n" +
                          "H20 SHEAR (V_max):\n" +
                          "V_max = 0.5 * R_max * P_m\n\n" +
                          "Where:\n" +
                          "q = Slab Load (kN/m2)\n" +
                          "L = Primary Beam Spacing (m)\n" +
                          "P_m = Prop Spacing (m)\n" +
                          "a = Secondary Beam Cantilever (m)";

            MessageBox.Show(info, "Formulas", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public FlexCalculatorViewModel ViewModel
        {
            get => _viewModel;
            set
            {
                // Unsubscribe from old viewmodel if exists
                if (_viewModel != null)
                {
                    _viewModel.PrimaryBeamDiagramRequested -= OnPrimaryBeamDiagramRequested;
                    _viewModel.SecondaryBeamDiagramRequested -= OnSecondaryBeamDiagramRequested;
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                }

                _viewModel = value;
                this.DataContext = _viewModel;

                if (_viewModel != null)
                {
                    _viewModel.PrimaryBeamDiagramRequested += OnPrimaryBeamDiagramRequested;
                    _viewModel.SecondaryBeamDiagramRequested += OnSecondaryBeamDiagramRequested;
                    _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                }
            }
        }

        private void BtnImportValues_Click(object sender,RoutedEventArgs args){}
        private void BtnClose_Click(object sender,RoutedEventArgs args)=>Close();
    }
}
