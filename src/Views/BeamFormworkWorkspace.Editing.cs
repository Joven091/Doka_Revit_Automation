using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal sealed partial class BeamFormworkWorkspace
    {
        internal readonly Popup DimensionPopup=new Popup {StaysOpen=false,AllowsTransparency=true};
        internal readonly TextBox DimensionInput=new TextBox {MinWidth=110,Margin=new Thickness(0,4,0,0)};
        private readonly TextBlock dimensionError=new TextBlock {Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,MaxWidth=260};
        private string dimensionField,draftDistance="";
        private void InitializeDrawingEditors(FrameworkElement root)
        {
            var content=new StackPanel();content.Children.Add(new TextBlock {Text="Dimension (mm)",FontWeight=FontWeights.SemiBold});content.Children.Add(DimensionInput);content.Children.Add(dimensionError);
            DimensionPopup.Child=new Border {Background=Brushes.White,BorderBrush=Brushes.Goldenrod,BorderThickness=new Thickness(1),Padding=new Thickness(10),Child=content};
            DimensionPopup.PlacementTarget=Drawing;DimensionPopup.Placement=PlacementMode.Relative;
            DimensionInput.PreviewKeyDown+=(s,e)=>
            {
                if(e.Key==Key.Escape){DimensionPopup.IsOpen=false;Drawing.Focus();e.Handled=true;}
                else if(e.Key==Key.Enter){if(CommitDimensionInput()){DimensionPopup.IsOpen=false;Drawing.Focus();}e.Handled=true;}
            };
            root.PreviewTextInput+=(s,e)=>
            {
                if(!Drawing.IsKeyboardFocusWithin || Keyboard.Modifiers!=ModifierKeys.None)return;
                if(AppendDraftDistance(e.Text))e.Handled=true;
            };
            root.Unloaded+=(s,e)=>DimensionPopup.IsOpen=false;
        }
        private void OpenDimensionEditor(FormworkObject kind,int index,string field)
        {
            if(sectionMode || Drawing.Formwork || Model.IsOptimizing || !Fields.TryGetValue(field,out var box) || !box.IsEnabled || box.IsReadOnly)return;
            dimensionField=field;dimensionError.Text="";DimensionInput.Text=box.Text;
            DimensionPopup.HorizontalOffset=Math.Max(4,Math.Min(Drawing.ActualWidth-180,Drawing.LastDimensionBounds.Left));
            DimensionPopup.VerticalOffset=Math.Max(4,Math.Min(Drawing.ActualHeight-100,Drawing.LastDimensionBounds.Bottom+4));
            DimensionPopup.IsOpen=true;
            DimensionInput.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>
            {if(DimensionPopup.IsOpen){DimensionInput.Focus();Keyboard.Focus(DimensionInput);DimensionInput.SelectAll();}}));
        }
        internal bool CommitDimensionInput()
        {
            if(!TryEditDimension(dimensionField,DimensionInput.Text,out string error)){dimensionError.Text=error;return false;}
            return true;
        }
        internal bool TryEditDimension(string field,string text,out string error)
        {
            error="Enter a valid dimension in mm.";
            if(Model.IsOptimizing || !Fields.TryGetValue(field??"",out var box) || !box.IsEnabled || box.IsReadOnly || !CommitFields())return false;
            var editor=editors.FirstOrDefault(e=>ReferenceEquals(e.Box,box));
            if(editor==null || !double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value) || !TowerBeamAnalysis.Finite(value) || value<editor.Minimum)return false;
            if(field=="PositionMm")
            {
                var c=Model.CrossingBeams[Drawing.SelectedIndex];
                if(!Model.CrossingFitsSpan(c.BayNumber,value,c.WidthMm)){error="Keep the crossing beam within the beam length.";return false;}
                if(Model.CrossingBeams.Where((c2,i)=>i!=Drawing.SelectedIndex && c2.BayNumber==c.BayNumber).Any(c2=>Math.Abs(c2.PositionMm-value)<(c2.WidthMm+c.WidthMm)/2)){error="Crossing beams cannot overlap.";return false;}
            }
            if(field=="BeamLengthMm" || field=="SpanStartMm")
            {
                double start=field=="SpanStartMm" ? value : Model.BeamSpanStart(Drawing.SelectedIndex),length=field=="BeamLengthMm" ? value : Model.BeamSpanLength(Drawing.SelectedIndex);
                if(start<0 || length<300 || start+length>50000 || Model.IsNetworkMode && Model.CrossingBeams.Any(c=>(c.BayNumber==Drawing.SelectedIndex || c.BayNumber==Drawing.SelectedIndex+1) && (c.PositionMm-c.WidthMm/2<start || c.PositionMm+c.WidthMm/2>start+length)))
                {error="Keep a positive beam length and connected crossings within its actual endpoints.";return false;}
            }
            if(field=="AllBeamLengthMm" && (value<300 || value>30000 || Model.CrossingBeams.Any(c=>c.PositionMm+c.WidthMm/2>value))){error="Keep every crossing within the new beam lengths.";return false;}
            if(field=="LengthMm" && (value<300 || value>30000)){error="Beam length must be between 300 and 30000 mm.";return false;}
            if(field=="WidthMm" && Drawing.SelectedKind==FormworkObject.Slab && (value<500 || value>20000)){error="Slab clear width must be between 500 and 20000 mm.";return false;}
            Mutate(()=>editor.Set(value));error="";return true;
        }
        internal bool AppendDraftDistance(string text)
        {
            if(draftCommand==null || !draftBase.HasValue || text.Length==0 || text.Any(c=>!char.IsDigit(c) && c!='.' && c!=',' && c!='-' && c!='+'))return false;
            draftDistance+=text;ShowDraftDistance();return true;
        }
        private void ShowDraftDistance()
        {
            status.Text=$"{draftCommand} · distance {draftDistance} mm · point in the direction, then Enter · Esc cancels";
            Drawing.DraftInputText=draftDistance.Length==0 ? null : draftDistance+" mm";Drawing.InvalidateVisual();
            if(Drawing.DraftPointerWorld.HasValue)PreviewDraftPoint(Drawing.DraftPointerWorld.Value);
        }
        private bool TryDraftDistancePoint(Point pointer,out Point point)
        {
            point=pointer;
            if(!draftBase.HasValue || !double.TryParse(draftDistance,NumberStyles.Float,CultureInfo.CurrentCulture,out var distance) || !TowerBeamAnalysis.Finite(distance) || Math.Abs(distance)>1000000 || distance==0)
                return false;
            var direction=pointer-draftBase.Value;
            if(!draftFree && (draftIndependent!=null || draftKind==FormworkObject.Crossing))
            {
                if(direction.Length<1e-7)return false;
                direction.Normalize();
            }
            else
            {
                bool alongX=draftFree ? !draftVertical : draftKind==FormworkObject.Crossing;
                double component=alongX ? direction.X : direction.Y;
                if(Math.Abs(component)<1e-7)return false;
                direction=alongX ? new Vector(Math.Sign(component),0) : new Vector(0,Math.Sign(component));
            }
            point=draftBase.Value+direction*distance;return true;
        }
        internal bool CommitDraftDistance()
        {
            if(!TryDraftDistancePoint(Drawing.DraftPointerWorld??draftBase??new Point(),out var point))
            {status.Text="Enter a nonzero distance up to 1000000 mm and point in the move/copy direction.";return false;}
            AcceptDraftPoint(point);
            return draftCommand==null;
        }
    }
}
