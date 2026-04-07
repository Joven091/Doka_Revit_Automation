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
        private double _secondaryBeamCantilever = 500;
        private string _warningMessage;

        public List<string> H20Options => FloorPropData.H20Data.Keys.ToList();

        private double _floorHeight = 3000;
        private string _propType = "300";
        private string _propOrientation = "at bottom";

        public List<string> PropTypeOptions => new List<string> { "250", "300", "350", "400", "550" };
        public List<string> PropOrientationOptions => new List<string> { "at bottom", "at top" };

        public double FloorHeight
        {
            get => _floorHeight;
            set { _floorHeight = value; OnPropertyChanged(); Calculate(); }
        }

        public string PropType
        {
            get => _propType;
            set { _propType = value; OnPropertyChanged(); Calculate(); }
        }

        public string PropOrientation
        {
            get => _propOrientation;
            set { _propOrientation = value; OnPropertyChanged(); Calculate(); }
        }


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

        public double SecondaryBeamCantilever
        {
            get => _secondaryBeamCantilever;
            set { _secondaryBeamCantilever = value; OnPropertyChanged(); Calculate(); }
        }

        public double PrimaryBeamSpacing
        {
            get => _primaryBeamSpacing;
            private set { _primaryBeamSpacing = value; OnPropertyChanged(); }
        }

        private double _primaryBeamSpacingMeters;
        public double PrimaryBeamSpacingMeters
        {
            get => _primaryBeamSpacingMeters;
            private set { _primaryBeamSpacingMeters = value; OnPropertyChanged(); }
        }

        private double _actualPropLoad;
        public double ActualPropLoad
        {
            get => _actualPropLoad;
            private set { _actualPropLoad = value; OnPropertyChanged(); }
        }

        private double _propCapacityLimit;
        public double PropCapacityLimit
        {
            get => _propCapacityLimit;
            private set { _propCapacityLimit = value; OnPropertyChanged(); }
        }

        private double _utilizationRate;
        public double UtilizationRate
        {
            get => _utilizationRate;
            private set { _utilizationRate = value; OnPropertyChanged(); }
        }

        private double _h20Shear;
        public double H20Shear
        {
            get => _h20Shear;
            private set { _h20Shear = value; OnPropertyChanged(); }
        }

        private double _h20Utilization;
        public double H20Utilization
        {
            get => _h20Utilization;
            private set { _h20Utilization = value; OnPropertyChanged(); }
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

            // Get Slab Load
            double? slabLoadNullable = SlabLoadHelper.GetSlabLoad((int)SlabThickness);
            if (!slabLoadNullable.HasValue)
            {
                WarningMessage = "Out of bounds";
                PrimaryBeamSpacing = 0;
                return;
            }
            double slabLoad = slabLoadNullable.Value;

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

            // Initial safe beam spacing (rounded down to nearest 25mm)
            double currentBeamSpacing = Math.Floor(finalZ / 25.0) * 25.0;

            // Now perform Capacity Cross-Check
            double extensionMeters = (FloorHeight / 1000.0) - (SlabThickness / 1000.0) - 0.418;
            double roundedExtension = Math.Ceiling(extensionMeters * 10) / 10.0;
            string key = PropType + " " + PropOrientation;
            double vCap = 11.00; // Maximum allowable H20 shear capacity in kN

            while (true)
            {
                double L = currentBeamSpacing / 1000.0;
                double P_m = PropSpacing / 1000.0;
                double a = SecondaryBeamCantilever / 1000.0;

                double R_max = 0;
                if (L > 0)
                {
                    double a_over_L = a / L;
                    if (a_over_L <= 0.4534)
                    {
                        R_max = slabLoad * (1.1 * L - 0.6 * (a * a / L));
                    }
                    else
                    {
                        R_max = slabLoad * (a + 0.4 * L + 0.6 * (a * a / L));
                    }
                }

                double propLoad = R_max * P_m;
                double vMax = 0.5 * R_max * P_m;

                double? capacity = PropDataHelper.GetCapacity(roundedExtension, key);

                if (!capacity.HasValue || propLoad > capacity.Value || vMax > vCap)
                {
                    currentBeamSpacing -= 25.0;
                    if (currentBeamSpacing <= 0)
                    {
                        WarningMessage = "Cannot find safe primary beam spacing for given load, prop capacity, and H20 shear capacity.";
                        currentBeamSpacing = 0;
                        ActualPropLoad = 0;
                        PropCapacityLimit = 0;
                        UtilizationRate = 0;
                        H20Shear = 0;
                        H20Utilization = 0;
                        break;
                    }
                }
                else
                {
                    ActualPropLoad = Math.Round(propLoad, 2);
                    PropCapacityLimit = Math.Round(capacity.Value, 2);
                    if (capacity.Value > 0)
                    {
                        UtilizationRate = Math.Round((propLoad / capacity.Value) * 100, 1);
                    }
                    else
                    {
                        UtilizationRate = 0;
                    }
                    H20Shear = Math.Round(vMax, 2);
                    H20Utilization = Math.Round((vMax / vCap) * 100, 1);
                    break;
                }
            }

            PrimaryBeamSpacing = currentBeamSpacing;
            PrimaryBeamSpacingMeters = currentBeamSpacing / 1000.0;
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