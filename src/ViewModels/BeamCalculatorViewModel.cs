using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace EngineeringDashboard
{
    public class BeamCalculatorViewModel : INotifyPropertyChanged
    {
        private double _depth = 500;
        private double _width = 200;
        private double _secondarySpacing;
        private double _maximumPropSpacing;
        private double _maximumPropLoad;
        private double _primaryBeamCantilever;
        private string _limitingFactor;
        private string _warningMessage;

        private List<BeamDataRow> _beamData;

        public BeamCalculatorViewModel()
        {
            _beamData = BeamData.GetData();
            Calculate();
        }

        public double Depth
        {
            get => _depth;
            set { _depth = value; OnPropertyChanged(); Calculate(); }
        }

        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); Calculate(); }
        }

        public double SecondarySpacing
        {
            get => _secondarySpacing;
            private set { _secondarySpacing = value; OnPropertyChanged(); }
        }

        public double MaximumPropSpacing
        {
            get => _maximumPropSpacing;
            private set { _maximumPropSpacing = value; OnPropertyChanged(); }
        }

        public double MaximumPropLoad
        {
            get => _maximumPropLoad;
            private set { _maximumPropLoad = value; OnPropertyChanged(); }
        }

        public double PrimaryBeamCantilever
        {
            get => _primaryBeamCantilever;
            private set { _primaryBeamCantilever = value; OnPropertyChanged(); }
        }

        public string LimitingFactor
        {
            get => _limitingFactor;
            private set { _limitingFactor = value; OnPropertyChanged(); }
        }

        public string WarningMessage
        {
            get => _warningMessage;
            private set { _warningMessage = value; OnPropertyChanged(); }
        }

        private void Calculate()
        {
            WarningMessage = string.Empty;

            if (Depth <= 0 || Width <= 0)
            {
                WarningMessage = "Depth and Width must be greater than zero.";
                ClearOutputs();
                return;
            }

            var matchingRow = _beamData
                .Where(r => r.Depth >= Depth && r.Width >= Width)
                .OrderBy(r => r.Depth)
                .ThenBy(r => r.Width)
                .FirstOrDefault();

            if (matchingRow != null)
            {
                SecondarySpacing = matchingRow.SecondarySpacing;
                MaximumPropSpacing = matchingRow.MaximumPropSpacing;
                MaximumPropLoad = matchingRow.MaximumPropLoad;
                LimitingFactor = matchingRow.LimitingFactor;

                double cantileverCalc = MaximumPropSpacing * 0.42;
                if (cantileverCalc > 500)
                {
                    PrimaryBeamCantilever = 500;
                }
                else
                {
                    PrimaryBeamCantilever = System.Math.Floor(cantileverCalc / 25.0) * 25.0;
                }
            }
            else
            {
                WarningMessage = "Size exceeds available data table.";
                ClearOutputs();
            }
        }

        private void ClearOutputs()
        {
            SecondarySpacing = 0;
            MaximumPropSpacing = 0;
            MaximumPropLoad = 0;
            PrimaryBeamCantilever = 0;
            LimitingFactor = string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
