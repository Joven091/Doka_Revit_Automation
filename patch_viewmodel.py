import re

with open('src/ViewModels/FlexCalculatorViewModel.cs', 'r') as f:
    content = f.read()

# Add properties
properties_str = """
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
"""

content = re.sub(
    r'(public List<string> H20Options => FloorPropData.H20Data.Keys.ToList\(\);)',
    r'\1\n' + properties_str,
    content
)

new_calculate = """
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

            while (true)
            {
                double propLoad = slabLoad * (currentBeamSpacing / 1000.0) * (PropSpacing / 1000.0);
                double? capacity = PropDataHelper.GetCapacity(roundedExtension, key);

                if (!capacity.HasValue || propLoad > capacity.Value)
                {
                    currentBeamSpacing -= 25.0;
                    if (currentBeamSpacing <= 0)
                    {
                        WarningMessage = "Cannot find safe primary beam spacing for given load and capacity.";
                        currentBeamSpacing = 0;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            PrimaryBeamSpacing = currentBeamSpacing;
        }
"""

content = re.sub(
    r'private void Calculate\(\)\s*\{.*?\}\n\n\s*private double GetInterpolatedBeamSpacing',
    new_calculate.strip() + '\n\n        private double GetInterpolatedBeamSpacing',
    content,
    flags=re.DOTALL
)

with open('src/ViewModels/FlexCalculatorViewModel.cs', 'w') as f:
    f.write(content)
