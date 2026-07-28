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
        private string _h20DepthDimPoints;
        private string _edgeToProp1DimPoints;
        private string _prop1ToProp2DimPoints;
        private string _prop2ToEdgeDimPoints;
        private string _h20WidthDimPoints;

        private string _edgeToProp1Text;
        private string _prop1ToProp2Text;
        private string _prop2ToEdgeText;
        private string _h20WidthText;

        private double _widthLabelLeft;
        private double _widthLabelTop;
        private double _depthLabelLeft;
        private double _depthLabelTop;
        private double _leftSlabLabelLeft;
        private double _leftSlabLabelTop;
        private double _rightSlabLabelLeft;
        private double _rightSlabLabelTop;
        private string _timberPolygonPoints;
        private string _prop1PolygonPoints;
        private string _prop2PolygonPoints;

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

        public string TimberPolygonPoints
        {
            get => _timberPolygonPoints;
            private set { _timberPolygonPoints = value; OnPropertyChanged(); }
        }

        public string Prop1PolygonPoints
        {
            get => _prop1PolygonPoints;
            private set { _prop1PolygonPoints = value; OnPropertyChanged(); }
        }

        public string Prop2PolygonPoints
        {
            get => _prop2PolygonPoints;
            private set { _prop2PolygonPoints = value; OnPropertyChanged(); }
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

        public string H20DepthDimPoints
        {
            get => _h20DepthDimPoints;
            private set { _h20DepthDimPoints = value; OnPropertyChanged(); }
        }

        public string EdgeToProp1DimPoints
        {
            get => _edgeToProp1DimPoints;
            private set { _edgeToProp1DimPoints = value; OnPropertyChanged(); }
        }

        public string Prop1ToProp2DimPoints
        {
            get => _prop1ToProp2DimPoints;
            private set { _prop1ToProp2DimPoints = value; OnPropertyChanged(); }
        }

        public string Prop2ToEdgeDimPoints
        {
            get => _prop2ToEdgeDimPoints;
            private set { _prop2ToEdgeDimPoints = value; OnPropertyChanged(); }
        }

        public string H20WidthDimPoints
        {
            get => _h20WidthDimPoints;
            private set { _h20WidthDimPoints = value; OnPropertyChanged(); }
        }

        public string EdgeToProp1Text
        {
            get => _edgeToProp1Text;
            private set { _edgeToProp1Text = value; OnPropertyChanged(); }
        }

        public string Prop1ToProp2Text
        {
            get => _prop1ToProp2Text;
            private set { _prop1ToProp2Text = value; OnPropertyChanged(); }
        }

        public string Prop2ToEdgeText
        {
            get => _prop2ToEdgeText;
            private set { _prop2ToEdgeText = value; OnPropertyChanged(); }
        }

        public string H20WidthText
        {
            get => _h20WidthText;
            private set { _h20WidthText = value; OnPropertyChanged(); }
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

        private double _h20DepthLabelLeft;
        private double _h20DepthLabelTop;
        private double _edgeToProp1LabelLeft;
        private double _edgeToProp1LabelTop;
        private double _prop1ToProp2LabelLeft;
        private double _prop1ToProp2LabelTop;
        private double _prop2ToEdgeLabelLeft;
        private double _prop2ToEdgeLabelTop;
        private double _h20WidthLabelLeft;
        private double _h20WidthLabelTop;

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

        public double H20DepthLabelLeft
        {
            get => _h20DepthLabelLeft;
            private set { _h20DepthLabelLeft = value; OnPropertyChanged(); }
        }

        public double H20DepthLabelTop
        {
            get => _h20DepthLabelTop;
            private set { _h20DepthLabelTop = value; OnPropertyChanged(); }
        }

        public double EdgeToProp1LabelLeft
        {
            get => _edgeToProp1LabelLeft;
            private set { _edgeToProp1LabelLeft = value; OnPropertyChanged(); }
        }

        public double EdgeToProp1LabelTop
        {
            get => _edgeToProp1LabelTop;
            private set { _edgeToProp1LabelTop = value; OnPropertyChanged(); }
        }

        public double Prop1ToProp2LabelLeft
        {
            get => _prop1ToProp2LabelLeft;
            private set { _prop1ToProp2LabelLeft = value; OnPropertyChanged(); }
        }

        public double Prop1ToProp2LabelTop
        {
            get => _prop1ToProp2LabelTop;
            private set { _prop1ToProp2LabelTop = value; OnPropertyChanged(); }
        }

        public double Prop2ToEdgeLabelLeft
        {
            get => _prop2ToEdgeLabelLeft;
            private set { _prop2ToEdgeLabelLeft = value; OnPropertyChanged(); }
        }

        public double Prop2ToEdgeLabelTop
        {
            get => _prop2ToEdgeLabelTop;
            private set { _prop2ToEdgeLabelTop = value; OnPropertyChanged(); }
        }

        public double H20WidthLabelLeft
        {
            get => _h20WidthLabelLeft;
            private set { _h20WidthLabelLeft = value; OnPropertyChanged(); }
        }

        public double H20WidthLabelTop
        {
            get => _h20WidthLabelTop;
            private set { _h20WidthLabelTop = value; OnPropertyChanged(); }
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

            double timberH = 100;
            double timberW = w + 1400; // overhang 700 on each side
            double propH = 300;
            double propArrowW = 60;

            double totalWidth = w;
            if (HasLeftSlab) totalWidth += slabExtend;
            if (HasRightSlab) totalWidth += slabExtend;
            totalWidth = System.Math.Max(totalWidth, timberW);

            double totalHeight = d + timberH + propH;

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

            double stH = timberH * scale;
            double stW = timberW * scale;
            double spH = propH * scale;
            double spAW = propArrowW * scale;

            double leftExtent = HasLeftSlab ? sle : 0;
            double rightExtent = HasRightSlab ? sle : 0;
            double timberOverhang = (stW - sw) / 2;

            double maxLeftExtent = System.Math.Max(leftExtent, timberOverhang);
            double maxRightExtent = System.Math.Max(rightExtent, timberOverhang);

            double drawW = maxLeftExtent + sw + maxRightExtent;
            double drawH = sd + stH + spH;

            // We use a fixed 250x250 canvas area for centering
            double offsetX = (250 - drawW) / 2;
            double offsetY = (250 - drawH) / 2;

            double startX = offsetX + maxLeftExtent;
            double startY = offsetY;

            // Timber
            double tLeft = startX - timberOverhang;
            double tRight = startX + sw + timberOverhang;
            double tTop = startY + sd;
            double tBot = startY + sd + stH;

            TimberPolygonPoints = $"{tLeft},{tTop} {tRight},{tTop} {tRight},{tBot} {tLeft},{tBot}";

            // Props
            string CreatePropPoints(double px)
            {
                double tipY = tBot;
                double wingY = tBot + spAW;
                double botY = tBot + spH;
                double hw = spAW / 2;
                double shaftW = spAW / 6;

                return $"{px},{tipY} " +
                       $"{px + hw},{wingY} " +
                       $"{px + shaftW},{wingY} " +
                       $"{px + shaftW},{botY} " +
                       $"{px - shaftW},{botY} " +
                       $"{px - shaftW},{wingY} " +
                       $"{px - hw},{wingY}";
            }

            Prop1PolygonPoints = CreatePropPoints(tLeft + stW * 0.25);
            Prop2PolygonPoints = CreatePropPoints(tLeft + stW * 0.75);

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

            // H20 and Prop Spacing Dimensions (Bottom)
            double botDimOffset = 25;
            double h20BotY = tBot;
            double botDimY = h20BotY + botDimOffset;
            double botDimY2 = h20BotY + botDimOffset + 20;

            double prop1X = tLeft + stW * 0.25;
            double prop2X = tLeft + stW * 0.75;

            // Overhang 1
            EdgeToProp1DimPoints =
                $"{tLeft},{h20BotY+5} " +
                $"{tLeft},{botDimY} " +
                $"{tLeft},{botDimY+tick} {tLeft},{botDimY-tick} {tLeft},{botDimY} " +
                $"{prop1X},{botDimY} " +
                $"{prop1X},{botDimY-tick} {prop1X},{botDimY+tick} {prop1X},{botDimY} " +
                $"{prop1X},{h20BotY+spAW+5}"; // prop wing level

            EdgeToProp1LabelLeft = tLeft + (prop1X - tLeft) / 2 - 10;
            EdgeToProp1LabelTop = botDimY - 20;
            EdgeToProp1Text = (timberW * 0.25).ToString("F0");

            // Prop to Prop
            Prop1ToProp2DimPoints =
                $"{prop1X},{botDimY} " +
                $"{prop2X},{botDimY} " +
                $"{prop2X},{botDimY-tick} {prop2X},{botDimY+tick} {prop2X},{botDimY} " +
                $"{prop2X},{h20BotY+spAW+5}";

            Prop1ToProp2LabelLeft = prop1X + (prop2X - prop1X) / 2 - 15;
            Prop1ToProp2LabelTop = botDimY - 20;
            Prop1ToProp2Text = (timberW * 0.5).ToString("F0");

            // Overhang 2
            Prop2ToEdgeDimPoints =
                $"{prop2X},{botDimY} " +
                $"{tRight},{botDimY} " +
                $"{tRight},{botDimY-tick} {tRight},{botDimY+tick} {tRight},{botDimY} " +
                $"{tRight},{h20BotY+5}";

            Prop2ToEdgeLabelLeft = prop2X + (tRight - prop2X) / 2 - 10;
            Prop2ToEdgeLabelTop = botDimY - 20;
            Prop2ToEdgeText = (timberW * 0.25).ToString("F0");

            // Total H20 Width
            H20WidthDimPoints =
                $"{tLeft},{botDimY} " +
                $"{tLeft},{botDimY2} " +
                $"{tLeft},{botDimY2+tick} {tLeft},{botDimY2-tick} {tLeft},{botDimY2} " +
                $"{tRight},{botDimY2} " +
                $"{tRight},{botDimY2-tick} {tRight},{botDimY2+tick} {tRight},{botDimY2} " +
                $"{tRight},{botDimY}";

            H20WidthLabelLeft = tLeft + stW / 2 - 15;
            H20WidthLabelTop = botDimY2 - 20;
            H20WidthText = timberW.ToString("F0");

            // H20 Depth Dimension (Left)
            double hDimX = tLeft - 25;
            H20DepthDimPoints =
                $"{tLeft-5},{tTop} " +
                $"{hDimX},{tTop} " +
                $"{hDimX-tick},{tTop} {hDimX+tick},{tTop} {hDimX},{tTop} " +
                $"{hDimX},{tBot} " +
                $"{hDimX-tick},{tBot} {hDimX+tick},{tBot} {hDimX},{tBot} " +
                $"{tLeft-5},{tBot}";

            H20DepthLabelLeft = hDimX - 25;
            H20DepthLabelTop = tTop + stH / 2 - 10;

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
