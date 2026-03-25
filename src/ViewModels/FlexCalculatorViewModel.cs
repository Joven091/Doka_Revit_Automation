using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EngineeringDashboard
{
    public class FlexCalculatorViewModel : INotifyPropertyChanged
    {
        private double _slabThickness = 300;
        private double _propSpacing = 1000;
        private string _selectedH20 = "2.90m";
        private double _recommendedPropSpacing;
        private double _primaryBeamSpacing;
        private string _warningMessage;

        public List<string> H20Options => FloorPropData.H20Data.Keys.ToList();

        public double SlabThickness
        {
            get => _slabThickness;
            set { _slabThickness = value; OnPropertyChanged(); Calculate(); }
        }

        public string SelectedH20
        {
            get => _selectedH20;
            set
            {
                _selectedH20 = value;
                OnPropertyChanged();
                if (FloorPropData.H20Data.TryGetValue(value, out double rec))
                {
                    RecommendedPropSpacing = rec;
                }
            }
        }

        public double RecommendedPropSpacing
        {
            get => _recommendedPropSpacing;
            set { _recommendedPropSpacing = value; OnPropertyChanged(); }
        }

        public double PropSpacing
        {
            get => _propSpacing;
            set { _propSpacing = value; OnPropertyChanged(); Calculate(); }
        }

        public double PrimaryBeamSpacing
        {
            get => _primaryBeamSpacing;
            private set { _primaryBeamSpacing = value; OnPropertyChanged(); }
        }

        public string WarningMessage
        {
            get => _warningMessage;
            private set { _warningMessage = value; OnPropertyChanged(); }
        }

        public FlexCalculatorViewModel()
        {
            SelectedH20 = "2.90m"; // Triggers initial recommended setup
            Calculate();
        }

        private void Calculate()
        {
            WarningMessage = string.Empty;

            // Clamp Slab Thickness to table bounds
            double sT = Math.Max(200, Math.Min(500, SlabThickness));
            if (sT != SlabThickness)
            {
                WarningMessage = $"Slab Thickness adjusted to {sT}mm (Table Limits).";
            }

            var keys = FloorPropData.TableData.Keys.OrderBy(k => k).ToList();
            double x1 = keys.LastOrDefault(k => k <= sT);
            double x2 = keys.FirstOrDefault(k => k >= sT);

            if (x1 == 0) x1 = x2; // Edge case safety
            if (x2 == 0) x2 = x1;

            // Find max allowable prop spacing for current slab thickness interpolation
            double maxP1 = FloorPropData.TableData[x1].Max(p => p.Prop);
            double maxP2 = FloorPropData.TableData[x2].Max(p => p.Prop);
            double interpolatedMaxProp = (x1 == x2) ? maxP1 : maxP1 + (sT - x1) * ((maxP2 - maxP1) / (x2 - x1));

            // Clamp Prop Spacing
            if (PropSpacing > interpolatedMaxProp)
            {
                PropSpacing = Math.Round(interpolatedMaxProp, 1);
                WarningMessage = $"Max capacity exceeded. Prop spacing automatically reduced to {PropSpacing}mm.";
                return; // Setter will re-trigger Calculate() with valid data
            }

            // Interpolate Beam Spacing
            double z1 = GetInterpolatedBeamSpacing(FloorPropData.TableData[x1], PropSpacing);
            double z2 = GetInterpolatedBeamSpacing(FloorPropData.TableData[x2], PropSpacing);

            double finalZ = (x1 == x2) ? z1 : z1 + (sT - x1) * ((z2 - z1) / (x2 - x1));

            // Round down to nearest 25mm
            PrimaryBeamSpacing = Math.Floor(finalZ / 25.0) * 25.0;
        }

        private double GetInterpolatedBeamSpacing(List<(double Prop, double Beam)> data, double targetProp)
        {
            var sorted = data.OrderBy(d => d.Prop).ToList();
            if (targetProp <= sorted.First().Prop) return sorted.First().Beam;
            if (targetProp >= sorted.Last().Prop) return sorted.Last().Beam;

            var lower = sorted.Last(d => d.Prop <= targetProp);
            var upper = sorted.First(d => d.Prop >= targetProp);

            if (lower.Prop == upper.Prop) return lower.Beam;

            return lower.Beam + (targetProp - lower.Prop) * ((upper.Beam - lower.Beam) / (upper.Prop - lower.Prop));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}