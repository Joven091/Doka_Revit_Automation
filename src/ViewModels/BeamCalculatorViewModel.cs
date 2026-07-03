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

        private bool _hasLeftSlab;
        private bool _hasRightSlab;
        private double _leftSlabThickness = 200;
        private double _rightSlabThickness = 200;

        private string _beamPolygonPoints;
        private string _widthDimPoints;
        private string _depthDimPoints;
        private string _leftSlabDimPoints;
        private string _rightSlabDimPoints;
        private string _leftSlabWidthDimPoints;
        private string _rightSlabWidthDimPoints;
        private double _widthLabelLeft;
        private double _widthLabelTop;
        private double _depthLabelLeft;
        private double _depthLabelTop;
        private double _leftSlabLabelLeft;
        private double _leftSlabLabelTop;
        private double _rightSlabLabelLeft;
        private double _rightSlabLabelTop;

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

        public bool HasLeftSlab
        {
            get => _hasLeftSlab;
            set { _hasLeftSlab = value; OnPropertyChanged(); Calculate(); }
        }

        public bool HasRightSlab
        {
            get => _hasRightSlab;
            set { _hasRightSlab = value; OnPropertyChanged(); Calculate(); }
        }

        public double LeftSlabThickness
        {
            get => _leftSlabThickness;
            set { _leftSlabThickness = value; OnPropertyChanged(); Calculate(); }
        }

        public double RightSlabThickness
        {
            get => _rightSlabThickness;
            set { _rightSlabThickness = value; OnPropertyChanged(); Calculate(); }
        }

        public string BeamPolygonPoints
        {
            get => _beamPolygonPoints;
            private set { _beamPolygonPoints = value; OnPropertyChanged(); }
        }

        public string WidthDimPoints
        {
            get => _widthDimPoints;
            private set { _widthDimPoints = value; OnPropertyChanged(); }
        }

        public string DepthDimPoints
        {
            get => _depthDimPoints;
            private set { _depthDimPoints = value; OnPropertyChanged(); }
        }

        public string LeftSlabDimPoints
        {
            get => _leftSlabDimPoints;
            private set { _leftSlabDimPoints = value; OnPropertyChanged(); }
        }

        public string RightSlabDimPoints
        {
            get => _rightSlabDimPoints;
            private set { _rightSlabDimPoints = value; OnPropertyChanged(); }
        }

        public string LeftSlabWidthDimPoints
        {
            get => _leftSlabWidthDimPoints;
            private set { _leftSlabWidthDimPoints = value; OnPropertyChanged(); }
        }

        public string RightSlabWidthDimPoints
        {
            get => _rightSlabWidthDimPoints;
            private set { _rightSlabWidthDimPoints = value; OnPropertyChanged(); }
        }

        public double WidthLabelLeft
        {
            get => _widthLabelLeft;
            private set { _widthLabelLeft = value; OnPropertyChanged(); }
        }

        public double WidthLabelTop
        {
            get => _widthLabelTop;
            private set { _widthLabelTop = value; OnPropertyChanged(); }
        }

        public double DepthLabelLeft
        {
            get => _depthLabelLeft;
            private set { _depthLabelLeft = value; OnPropertyChanged(); }
        }

        public double DepthLabelTop
        {
            get => _depthLabelTop;
            private set { _depthLabelTop = value; OnPropertyChanged(); }
        }

        public double LeftSlabLabelLeft
        {
            get => _leftSlabLabelLeft;
            private set { _leftSlabLabelLeft = value; OnPropertyChanged(); }
        }

        public double LeftSlabLabelTop
        {
            get => _leftSlabLabelTop;
            private set { _leftSlabLabelTop = value; OnPropertyChanged(); }
        }

        public double RightSlabLabelLeft
        {
            get => _rightSlabLabelLeft;
            private set { _rightSlabLabelLeft = value; OnPropertyChanged(); }
        }

        public double RightSlabLabelTop
        {
            get => _rightSlabLabelTop;
            private set { _rightSlabLabelTop = value; OnPropertyChanged(); }
        }

        private double _leftSlabWidthLabelLeft;
        private double _leftSlabWidthLabelTop;
        private double _rightSlabWidthLabelLeft;
        private double _rightSlabWidthLabelTop;

        public double LeftSlabWidthLabelLeft
        {
            get => _leftSlabWidthLabelLeft;
            private set { _leftSlabWidthLabelLeft = value; OnPropertyChanged(); }
        }

        public double LeftSlabWidthLabelTop
        {
            get => _leftSlabWidthLabelTop;
            private set { _leftSlabWidthLabelTop = value; OnPropertyChanged(); }
        }

        public double RightSlabWidthLabelLeft
        {
            get => _rightSlabWidthLabelLeft;
            private set { _rightSlabWidthLabelLeft = value; OnPropertyChanged(); }
        }

        public double RightSlabWidthLabelTop
        {
            get => _rightSlabWidthLabelTop;
            private set { _rightSlabWidthLabelTop = value; OnPropertyChanged(); }
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
            UpdateVisualization();

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

        private void UpdateVisualization()
        {
            double w = Width > 0 ? Width : 200;
            double d = Depth > 0 ? Depth : 500;

            double slabExtend = 300;

            double totalWidth = w;
            if (HasLeftSlab) totalWidth += slabExtend;
            if (HasRightSlab) totalWidth += slabExtend;

            double totalHeight = d;

            double canvasW = 120;
            double canvasH = 120;

            double scaleX = canvasW / totalWidth;
            double scaleY = canvasH / totalHeight;
            double scale = System.Math.Min(scaleX, scaleY);

            double sw = w * scale;
            double sd = d * scale;
            double sle = slabExtend * scale;
            double slt = (LeftSlabThickness > 0 ? LeftSlabThickness : 200) * scale;
            double srt = (RightSlabThickness > 0 ? RightSlabThickness : 200) * scale;

            double drawW = sw + (HasLeftSlab ? sle : 0) + (HasRightSlab ? sle : 0);
            double drawH = sd;

            // We use a fixed 250x250 canvas area for centering
            double offsetX = (250 - drawW) / 2;
            double offsetY = (250 - drawH) / 2;

            double startX = offsetX + (HasLeftSlab ? sle : 0);
            double startY = offsetY;

            List<string> pts = new List<string>();

            if (HasLeftSlab)
                pts.Add($"{startX - sle},{startY}");
            else
                pts.Add($"{startX},{startY}");

            if (HasRightSlab)
                pts.Add($"{startX + sw + sle},{startY}");
            else
                pts.Add($"{startX + sw},{startY}");

            if (HasRightSlab)
            {
                pts.Add($"{startX + sw + sle},{startY + srt}");
                pts.Add($"{startX + sw},{startY + srt}");
            }
            pts.Add($"{startX + sw},{startY + sd}");

            pts.Add($"{startX},{startY + sd}");

            if (HasLeftSlab)
            {
                pts.Add($"{startX},{startY + slt}");
                pts.Add($"{startX - sle},{startY + slt}");
            }

            BeamPolygonPoints = string.Join(" ", pts);

            double tick = 5;

            // Width Dimension (Top)
            double wDimOffset = 25;
            double wDimY = startY - wDimOffset;
            WidthDimPoints =
                $"{startX},{startY-5} " +
                $"{startX},{wDimY} " +
                $"{startX},{wDimY+tick} {startX},{wDimY-tick} {startX},{wDimY} " +
                $"{startX+sw},{wDimY} " +
                $"{startX+sw},{wDimY-tick} {startX+sw},{wDimY+tick} {startX+sw},{wDimY} " +
                $"{startX+sw},{startY-5}";

            WidthLabelLeft = startX + sw / 2 - 10;
            WidthLabelTop = wDimY - 20;

            // Depth Dimension (Right)
            double rightEdgeX = HasRightSlab ? startX + sw + sle : startX + sw;
            double dDimOffset = HasRightSlab ? 65 : 25;
            double dDimX = rightEdgeX + dDimOffset;

            DepthDimPoints =
                $"{rightEdgeX+5},{startY} " +
                $"{dDimX},{startY} " +
                $"{dDimX-tick},{startY} {dDimX+tick},{startY} {dDimX},{startY} " +
                $"{dDimX},{startY+sd} " +
                $"{dDimX+tick},{startY+sd} {dDimX-tick},{startY+sd} {dDimX},{startY+sd} " +
                $"{startX+sw+5},{startY+sd}";

            DepthLabelLeft = dDimX + 5;
            DepthLabelTop = startY + sd / 2 - 10;

            // Left Slab Thickness Dimension (Left)
            if (HasLeftSlab)
            {
                double lsDimOffset = 25;
                double lsEdgeX = startX - sle;
                double lsDimX = lsEdgeX - lsDimOffset;

                LeftSlabDimPoints =
                    $"{lsEdgeX-5},{startY} " +
                    $"{lsDimX},{startY} " +
                    $"{lsDimX-tick},{startY} {lsDimX+tick},{startY} {lsDimX},{startY} " +
                    $"{lsDimX},{startY+slt} " +
                    $"{lsDimX+tick},{startY+slt} {lsDimX-tick},{startY+slt} {lsDimX},{startY+slt} " +
                    $"{lsEdgeX-5},{startY+slt}";

                LeftSlabLabelLeft = lsDimX - 28;
                LeftSlabLabelTop = startY + slt / 2 - 10;
            }
            else
            {
                LeftSlabDimPoints = string.Empty;
            }

            // Right Slab Thickness Dimension (Right)
            if (HasRightSlab)
            {
                double rsDimOffset = 20;
                double rsEdgeX = startX + sw + sle;
                double rsDimX = rsEdgeX + rsDimOffset;

                RightSlabDimPoints =
                    $"{rsEdgeX+5},{startY} " +
                    $"{rsDimX},{startY} " +
                    $"{rsDimX-tick},{startY} {rsDimX+tick},{startY} {rsDimX},{startY} " +
                    $"{rsDimX},{startY+srt} " +
                    $"{rsDimX+tick},{startY+srt} {rsDimX-tick},{startY+srt} {rsDimX},{startY+srt} " +
                    $"{rsEdgeX+5},{startY+srt}";

                RightSlabLabelLeft = rsDimX + 5;
                RightSlabLabelTop = startY + srt / 2 - 10;
            }
            else
            {
                RightSlabDimPoints = string.Empty;
            }

            // Left Slab Width Dimension (Top)
            if (HasLeftSlab)
            {
                double lswDimOffset = 25;
                double lswDimY = startY - lswDimOffset;
                double lsStartX = startX - sle;
                LeftSlabWidthDimPoints =
                    $"{lsStartX},{startY-5} " +
                    $"{lsStartX},{lswDimY} " +
                    $"{lsStartX},{lswDimY+tick} {lsStartX},{lswDimY-tick} {lsStartX},{lswDimY} " +
                    $"{startX},{lswDimY} " +
                    $"{startX},{lswDimY-tick} {startX},{lswDimY+tick} {startX},{lswDimY} " +
                    $"{startX},{startY-5}";

                LeftSlabWidthLabelLeft = lsStartX + sle / 2 - 10;
                LeftSlabWidthLabelTop = lswDimY - 20;
            }
            else
            {
                LeftSlabWidthDimPoints = string.Empty;
            }

            // Right Slab Width Dimension (Top)
            if (HasRightSlab)
            {
                double rswDimOffset = 25;
                double rswDimY = startY - rswDimOffset;
                double rsStartX = startX + sw;
                double rsEndX = rsStartX + sle;
                RightSlabWidthDimPoints =
                    $"{rsStartX},{startY-5} " +
                    $"{rsStartX},{rswDimY} " +
                    $"{rsStartX},{rswDimY+tick} {rsStartX},{rswDimY-tick} {rsStartX},{rswDimY} " +
                    $"{rsEndX},{rswDimY} " +
                    $"{rsEndX},{rswDimY-tick} {rsEndX},{rswDimY+tick} {rsEndX},{rswDimY} " +
                    $"{rsEndX},{startY-5}";

                RightSlabWidthLabelLeft = rsStartX + sle / 2 - 10;
                RightSlabWidthLabelTop = rswDimY - 20;
            }
            else
            {
                RightSlabWidthDimPoints = string.Empty;
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
