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

            double slabExtend = System.Math.Max(w * 0.5, 200);

            double totalWidth = w;
            if (HasLeftSlab) totalWidth += slabExtend;
            if (HasRightSlab) totalWidth += slabExtend;

            double totalHeight = d;

            double canvasW = 200;
            double canvasH = 150;

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

            WidthLabelLeft = startX + sw / 2 - 15;
            WidthLabelTop = startY - 25;

            DepthLabelLeft = startX + sw + 10;
            DepthLabelTop = startY + sd / 2 - 10;

            LeftSlabLabelLeft = startX - sle / 2 - 15;
            LeftSlabLabelTop = startY + slt + 5;

            RightSlabLabelLeft = startX + sw + sle / 2 - 15;
            RightSlabLabelTop = startY + srt + 5;
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
