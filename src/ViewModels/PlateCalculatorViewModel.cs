using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EngineeringDashboard
{
    public class PlateCalculatorViewModel : INotifyPropertyChanged
    {
        private double _loadKN = 90;
        private double _span = 100;
        private double _plateWidth = 100;
        private double _holeDia = 18;
        private double _nutDia = 30;
        private double _selectedYield = 235;
        
        private double _reqBendingThickness;
        private double _reqPunchingThickness;
        private string _recommendedThickness;
        private string _warningMessage;

        public List<double> YieldOptions { get; } = new List<double> { 235.0, 275.0 };

        public double LoadKN
        {
            get => _loadKN;
            set { _loadKN = value; OnPropertyChanged(); Calculate(); }
        }

        public double Span
        {
            get => _span;
            set { _span = value; OnPropertyChanged(); Calculate(); }
        }

        public double PlateWidth
        {
            get => _plateWidth;
            set { _plateWidth = value; OnPropertyChanged(); Calculate(); }
        }

        public double HoleDia
        {
            get => _holeDia;
            set { _holeDia = value; OnPropertyChanged(); Calculate(); }
        }

        public double NutDia
        {
            get => _nutDia;
            set { _nutDia = value; OnPropertyChanged(); Calculate(); }
        }

        public double SelectedYield
        {
            get => _selectedYield;
            set { _selectedYield = value; OnPropertyChanged(); Calculate(); }
        }

        public double ReqBendingThickness
        {
            get => _reqBendingThickness;
            private set { _reqBendingThickness = value; OnPropertyChanged(); }
        }

        public double ReqPunchingThickness
        {
            get => _reqPunchingThickness;
            private set { _reqPunchingThickness = value; OnPropertyChanged(); }
        }

        public string RecommendedThickness
        {
            get => _recommendedThickness;
            private set { _recommendedThickness = value; OnPropertyChanged(); }
        }

        public string WarningMessage
        {
            get => _warningMessage;
            private set { _warningMessage = value; OnPropertyChanged(); }
        }

        public PlateCalculatorViewModel()
        {
            Calculate();
        }

        private void Calculate()
        {
            WarningMessage = string.Empty;

            // 1. Validate Inputs
            if (SelectedYield <= 0 || NutDia <= 0) return;

            double b_eff = PlateWidth - HoleDia;
            if (b_eff <= 0)
            {
                WarningMessage = "Error: Tie Hole Diameter must be smaller than Total Plate Width.";
                ReqBendingThickness = 0;
                ReqPunchingThickness = 0;
                RecommendedThickness = "N/A";
                return;
            }

            // 2. Run the Calculations
            double load_N = LoadKN * 1000.0; // Convert kN to N

            // Bending Check
            double t_bend = Math.Sqrt((1.5 * load_N * Span) / (b_eff * SelectedYield));
            ReqBendingThickness = Math.Round(t_bend, 2);

            // Punching Check (Allowable shear = 0.4 * py)
            double tau_allow = 0.4 * SelectedYield;
            double t_punch = load_N / (Math.PI * NutDia * tau_allow);
            ReqPunchingThickness = Math.Round(t_punch, 2);

            // 3. Compare against standard plate thicknesses
            double max_t = Math.Max(ReqBendingThickness, ReqPunchingThickness);
            
            var recommendedPlate = SteelPlateData.StandardThicknesses
                                                 .OrderBy(t => t)
                                                 .Cast<double?>()
                                                 .FirstOrDefault(t => t >= max_t);

            if (recommendedPlate.HasValue)
            {
                RecommendedThickness = $"{recommendedPlate.Value} mm";
            }
            else
            {
                RecommendedThickness = "Custom (>50mm)";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}