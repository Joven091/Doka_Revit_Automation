using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EngineeringDashboard
{
    /// <summary>Shared calculator state; drawing edits never introduce an independent solver.</summary>
    internal sealed class BeamFormworkWorkspace : Window
    {
        internal readonly BeamCalculatorViewModel Model;
        internal readonly BeamFormworkCanvas Drawing;
        internal readonly TransferNetworkPlan Section=new TransferNetworkPlan {SectionOnly=true,Width=1050,IsSelectionEnabled=true};
        internal readonly FormworkDrawingViewport SectionViewport;
        internal readonly Dictionary<string,Button> Buttons=new Dictionary<string,Button>();
        internal readonly Dictionary<string,TextBox> Fields=new Dictionary<string,TextBox>();
        internal TowerDiagramWindow Diagrams {get;private set;}
        internal string FailureDetails=>failure.Text;
        internal IEnumerable<Button> FailureTargets=>failures.Children.OfType<Button>();
        private readonly StackPanel properties=new StackPanel {Margin=new Thickness(16)};
        private readonly ContentControl analysis=new ContentControl();
        private readonly Expander analysisExpander=new Expander {Header="Member diagrams",IsExpanded=false};
        private readonly TextBlock status=new TextBlock {Margin=new Thickness(14,8,14,8),TextWrapping=TextWrapping.Wrap};
        private readonly TextBox failure=new TextBox {IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=180};
        private readonly StackPanel failures=new StackPanel();
        private readonly Expander failureExpander=new Expander {Header="Calculation failure details"};
        private readonly Border sectionHost=new Border {Visibility=Visibility.Collapsed};
        private readonly List<NumberEditor> editors=new List<NumberEditor>();
        private readonly List<StringEditor> stringEditors=new List<StringEditor>();
        private readonly List<Action> syncControls=new List<Action>();
        private readonly Stack<State> undo=new Stack<State>(),redo=new Stack<State>();
        private readonly Action save,load,export;
        private bool changing,refreshQueued,closed;
        private bool sectionMode;
        private object selectedObject;
        internal BeamDiagnosticWindow Diagnostics {get;private set;}
        private sealed class NumberEditor {public TextBox Box;public Func<double> Get;public Action<double> Set;public double Minimum;public bool Dirty;}
        private sealed class StringEditor {public TextBox Box;public Func<string> Get;public Action<string> Set;public bool Dirty;}
        private sealed class State
        {
            internal readonly Dictionary<PropertyInfo,object> Values=new Dictionary<PropertyInfo,object>();
            internal string Network;
            internal Tuple<FormworkObject,int>[] Selection;
            internal FormworkObject SelectedKind;
            internal int SelectedIndex;
        }
        private const string StateProperties="Depth,Width,BeamLengthMm,TowerLegCapacityKn,UpperSupportSpacingMm,StockLengthsText,StockEndSetbackMm,MaximumConcreteEndOverhangMm,AllowCustomH20Lengths,Exclude600Standalone,DeflectionLimit,TransferSlabLoads,UseBeamNetwork,HasLeftSlab,HasRightSlab,LeftSlabThickness,RightSlabThickness,LeftSlabBayMm,RightSlabBayMm,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad";
        internal BeamFormworkWorkspace(BeamCalculatorViewModel model,Action saveInputs,Action loadInputs,Action exportReport)
        {
            Model=model;save=saveInputs;load=loadInputs;export=exportReport;
            if(Model.IsTransferMode && !Model.UseBeamNetwork)Model.UseBeamNetwork=true;
            Drawing=new BeamFormworkCanvas(model);SectionViewport=new FormworkDrawingViewport(Section);
            Title="Beam formwork drawing";Width=1280;Height=860;MinWidth=980;MinHeight=620;WindowState=WindowState.Maximized;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            FontFamily=new FontFamily("Segoe UI");FontSize=13;Background=Brushes.WhiteSmoke;
            var root=new Grid {Background=Brushes.WhiteSmoke};root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var toolbar=new WrapPanel {Margin=new Thickness(8)};
            toolbar.Children.Add(Button("AddAbove","+ Bay above",()=>AddBay(true)));
            toolbar.Children.Add(Button("AddBelow","+ Bay below",()=>AddBay(false)));
            toolbar.Children.Add(Button("AddCrossing","+ Crossing beam",()=> {if(CommitFields() && Model.IsNetworkMode){Drawing.AddCrossing=true;Geometry();Drawing.Cursor=Cursors.Cross;}}));
            toolbar.Children.Add(Button("Delete","Delete selection",RemoveSelected));
            toolbar.Children.Add(Button("Undo","Undo",()=>History(undo,redo)));
            toolbar.Children.Add(Button("Redo","Redo",()=>History(redo,undo)));
            toolbar.Children.Add(Button("Fit","Fit",()=> {if(sectionMode)SectionViewport.Fit();else Drawing.Fit();}));
            toolbar.Children.Add(Button("ZoomOut","−",()=>Zoom(1/1.25)));
            toolbar.Children.Add(Button("ZoomIn","+",()=>Zoom(1.25)));
            var dimensions=new CheckBox {Content="All dimensions",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0)};
            dimensions.Checked+=(s,e)=> {Drawing.AllDimensions=true;Drawing.InvalidateVisual();};dimensions.Unchecked+=(s,e)=> {Drawing.AllDimensions=false;Drawing.InvalidateVisual();};toolbar.Children.Add(dimensions);
            toolbar.Children.Add(Button("Calculate","Calculate",async()=>await CalculateAsync()));
            toolbar.Children.Add(Button("Cancel","Cancel",()=> {Model.CancelOptimization();Refresh();}));
            toolbar.Children.Add(Button("Diagnose","Diagnose",ShowDiagnostics));
            toolbar.Children.Add(Button("Save","Save",()=> {if(CommitFields())save?.Invoke();}));
            toolbar.Children.Add(Button("Load","Load",()=> {if(CommitFields()){var old=Capture();load?.Invoke();if(closed)return;if(!Same(old,Capture())){undo.Push(old);redo.Clear();}BuildProperties();Refresh();Drawing.Fit();}}));
            toolbar.Children.Add(Button("Export","Export",()=> {if(CommitFields())export?.Invoke();}));root.Children.Add(toolbar);
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(285)});Grid.SetRow(body,1);root.Children.Add(body);
            var main=new Grid {Background=Brushes.White};main.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition());main.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});body.Children.Add(main);
            var tabs=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(10,0,10,6)};
            tabs.Children.Add(Button("Geometry","Geometry",Geometry));tabs.Children.Add(Button("Formwork","Formwork",()=> {if(!CommitFields())return;sectionMode=false;Drawing.Formwork=true;Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;Drawing.InvalidateVisual();Drawing.Fit();Refresh();}));
            tabs.Children.Add(Button("Section","Section",()=> {if(!CommitFields())return;sectionMode=true;Drawing.Visibility=Visibility.Collapsed;sectionHost.Visibility=Visibility.Visible;Refresh();}));
            tabs.Children.Add(new TextBlock {Text="mm · select an object to edit",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0),Foreground=Brushes.SlateGray});main.Children.Add(tabs);
            Grid.SetRow(Drawing,1);main.Children.Add(Drawing);sectionHost.Child=SectionViewport;Grid.SetRow(sectionHost,1);main.Children.Add(sectionHost);
            analysisExpander.Content=analysis;analysis.MaxHeight=280;Grid.SetRow(analysisExpander,2);main.Children.Add(analysisExpander);
            var inspector=new ScrollViewer {Content=properties,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.White};Grid.SetColumn(inspector,1);body.Children.Add(inspector);
            Grid.SetRow(status,2);root.Children.Add(status);Content=root;
            Drawing.CanInteract=()=>!Model.IsOptimizing && CommitFields();
            Drawing.SelectionChanged+=()=> {BuildProperties();Refresh();};
            Drawing.DimensionSelected+=(kind,index,field)=> {if(Fields.TryGetValue(field,out var box)){box.Focus();box.SelectAll();}};
            Drawing.MemberSelected+=ShowMember;Section.MemberSelected+=ShowMember;
            Drawing.CrossingRequested+=(bay,x)=> {if(!CommitFields())return;Mutate(()=> {Model.AddCrossingBeam();var c=Model.CrossingBeams.Last();c.BayNumber=bay+1;c.PositionMm=Math.Max(c.WidthMm/2,Math.Min(Model.BeamLengthMm-c.WidthMm/2,x));Drawing.Select(FormworkObject.Crossing,Model.CrossingBeams.Count-1,false);});Drawing.Cursor=Cursors.Arrow;};
            Drawing.MoveRequested+=(kind,index,delta)=> {if(!CommitFields())return;Mutate(()=> {if(kind==FormworkObject.Crossing) {var c=Model.CrossingBeams[index];c.PositionMm=Math.Max(c.WidthMm/2,Math.Min(Model.BeamLengthMm-c.WidthMm/2,c.PositionMm+delta));}else Model.NetworkBays[index-1].WidthMm=Math.Max(500,Model.NetworkBays[index-1].WidthMm+delta);});};
            Model.PropertyChanged+=ModelChanged;
            Loaded+=(s,e)=>Drawing.Fit();Closed+=(s,e)=> {closed=true;Diagnostics?.Close();Model.PropertyChanged-=ModelChanged;Model.SetAdditionalTextInputErrors(this,false);ClearDiagrams();};
            PreviewKeyDown+=(s,e)=>
            {
                if(e.Key==Key.Escape){Drawing.AddCrossing=false;Drawing.Cursor=Cursors.Arrow;Drawing.CancelSelectionGesture();}
                if(Keyboard.FocusedElement is TextBox)return;
                if(HandleGeometryKey(e.Key,Keyboard.Modifiers,!Drawing.IsKeyboardFocusWithin)){e.Handled=true;return;}
                if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.Z){History(undo,redo);e.Handled=true;}
                if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.Y){History(redo,undo);e.Handled=true;}
            };
            BuildProperties();Refresh();
        }
        private Button Button(string name,string text,Action action)
        {
            var b=new Button {Content=text,Name="Workspace"+name,Padding=new Thickness(10,5,10,5),Margin=new Thickness(3),MinHeight=30};b.Click+=(s,e)=>action();Buttons[name]=b;return b;
        }
        private void ShowDiagnostics()
        {
            if(Diagnostics!=null){Diagnostics.RefreshReport();Diagnostics.Activate();return;}
            Diagnostics=new BeamDiagnosticWindow(remarks=>Model.BuildDiagnosticReport(remarks,"Source: Drawing workspace; selected "+Drawing.SelectedKind+" index "+Drawing.SelectedIndex+" (zero based)\n"+string.Join("\n",Fields.Select(f=>f.Key+" = "+f.Value.Text+"; enabled="+f.Value.IsEnabled))));
            Diagnostics.Closed+=(s,e)=>Diagnostics=null;if(IsVisible)Diagnostics.Owner=this;Diagnostics.Show();
        }
        private void Geometry()
        {sectionMode=false;Drawing.Formwork=false;Drawing.CancelSelectionGesture();Drawing.Cursor=Cursors.Arrow;Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;Drawing.InvalidateVisual();Refresh();}
        private void Zoom(double factor)
        {if(sectionMode)SectionViewport.ZoomAt(factor,new Point(SectionViewport.ActualWidth/2,SectionViewport.ActualHeight/2));else Drawing.ZoomAt(factor,new Point(Drawing.ActualWidth/2,Drawing.ActualHeight/2));}
        private void UpdateViewButtons()
        {
            Buttons["Geometry"].FontWeight=!sectionMode && !Drawing.Formwork ? FontWeights.Bold : FontWeights.Normal;
            Buttons["Formwork"].FontWeight=!sectionMode && Drawing.Formwork ? FontWeights.Bold : FontWeights.Normal;
            Buttons["Section"].FontWeight=sectionMode ? FontWeights.Bold : FontWeights.Normal;
        }
        private State Capture()
        {
            var state=new State {Network=Model.NetworkBeams.Count>=2 ? Model.NetworkConfigurationXml : null,Selection=Drawing.SelectedObjects,SelectedKind=Drawing.SelectedKind,SelectedIndex=Drawing.SelectedIndex};
            foreach(var name in StateProperties.Split(',')) {var p=typeof(BeamCalculatorViewModel).GetProperty(name);state.Values[p]=p.GetValue(Model);}
            return state;
        }
        private static bool Same(State a,State b)=>a.Network==b.Network && a.Values.All(v=>Equals(v.Value,b.Values[v.Key]));
        private void Apply(State state)
        {
            changing=true;try {foreach(var v in state.Values)v.Key.SetValue(Model,v.Value);if(state.Network!=null)Model.NetworkConfigurationXml=state.Network;}finally {changing=false;}
            Drawing.SetSelection(state.Selection,state.SelectedKind,state.SelectedIndex,false);
            BuildProperties();Refresh();Drawing.Fit();
        }
        private void Mutate(Action change)
        {var old=Capture();changing=true;try {change();}finally {changing=false;}if(!Same(old,Capture())){undo.Push(old);redo.Clear();}BuildProperties();Refresh();Drawing.Fit();}
        private void History(Stack<State> from,Stack<State> to)
        {if(!CommitFields() || from.Count==0 || Model.IsOptimizing)return;to.Push(Capture());Apply(from.Pop());}
        internal void AddBay(bool before)
        {
            if(!CommitFields() || Model.IsOptimizing)return;
            Mutate(()=> {
                int selected=Drawing.SelectedKind==FormworkObject.Beam ? Drawing.SelectedIndex : Math.Min(Drawing.SelectedIndex,Model.NetworkBeams.Count-1);
                if(!Model.IsNetworkMode)
                {
                    Model.TransferSlabLoads=true;Model.UseBeamNetwork=true;
                    Model.NetworkConfigurationXml=SerializeInitialNetwork();Drawing.SelectedIndex=before ? 0 : 1;
                }
                else {Model.InsertNetworkBay(selected,before);Drawing.SelectedIndex=before ? selected : selected+1;}
                Drawing.Select(FormworkObject.Beam,Drawing.SelectedIndex,false);
            });Geometry();
        }
        private string SerializeInitialNetwork()
        {
            var config=new TransferNetworkConfiguration {Beams=new[] {new TransferBeamRow {WidthMm=Model.Width,DepthMm=Model.Depth},new TransferBeamRow {WidthMm=Model.Width,DepthMm=Model.Depth}},Bays=new[] {new TransferBayRow {WidthMm=Model.RightSlabBayMm,ThicknessMm=Model.RightSlabThickness}},CrossingBeams=new TransferCrossingBeamRow[0]};
            using(var text=new System.IO.StringWriter(CultureInfo.InvariantCulture)){new System.Xml.Serialization.XmlSerializer(typeof(TransferNetworkConfiguration)).Serialize(text,config);return text.ToString();}
        }
        private sealed class RemovalPlan
        {
            internal int[] Beams=new int[0];
            internal TransferCrossingBeamRow[] Crossings=new TransferCrossingBeamRow[0];
            internal bool Allowed;
            internal string Message;
        }
        private RemovalPlan PlanRemoval()
        {
            var plan=new RemovalPlan();var selected=Drawing.SelectedObjects;
            if(selected.Length==0){plan.Message="Select objects to remove.";return plan;}
            var beams=new SortedSet<int>();var crosses=new HashSet<TransferCrossingBeamRow>();
            foreach(var item in selected)
            {
                if(item.Item1==FormworkObject.Crossing && item.Item2>=0 && item.Item2<Model.CrossingBeams.Count)crosses.Add(Model.CrossingBeams[item.Item2]);
                else if(Model.IsNetworkMode && item.Item1==FormworkObject.Beam && item.Item2>=0 && item.Item2<Model.NetworkBeams.Count)beams.Add(item.Item2);
                else if(Model.IsNetworkMode && item.Item1==FormworkObject.Slab && item.Item2>=0 && item.Item2<Model.NetworkBays.Count)beams.Add(item.Item2+1);
                else if(!Model.IsNetworkMode && item.Item1==FormworkObject.Beam){plan.Message="Keep at least one beam in the drawing.";return plan;}
            }
            if(Model.IsNetworkMode && Model.NetworkBeams.Count-beams.Count<2)
            {plan.Message="Keep at least two beams and one slab bay in a connected layout. Narrow the selection.";return plan;}
            var remainingBeams=Enumerable.Range(0,Model.NetworkBeams.Count).ToList();
            var remainingBays=Enumerable.Range(0,Model.NetworkBays.Count).ToList();var removedBays=new List<int>();
            foreach(int original in beams.Reverse())
            {
                int current=remainingBeams.IndexOf(original),bay=Math.Max(0,current-1);
                removedBays.Add(remainingBays[bay]);remainingBays.RemoveAt(bay);remainingBeams.RemoveAt(current);
            }
            foreach(var crossing in Model.CrossingBeams.Where(c=>removedBays.Contains(c.BayNumber-1)))crosses.Add(crossing);
            plan.Beams=beams.Reverse().ToArray();plan.Crossings=crosses.ToArray();plan.Allowed=beams.Count>0 || crosses.Count>0;
            var names=beams.Select(i=>"B"+(i+1)).Concat(removedBays.OrderBy(i=>i).Select(i=>"S"+(i+1))).Concat(crosses.Select(c=>"X"+(Model.CrossingBeams.IndexOf(c)+1)).OrderBy(x=>x)).ToArray();
            plan.Message=plan.Allowed ? "Delete removes "+string.Join(", ",names)+". Beam/slab pairs stay connected; Ctrl+Z restores the whole edit." : "Select objects to remove.";
            return plan;
        }
        internal string RemovalPreview=>PlanRemoval().Message;
        internal bool HandleGeometryKey(Key key,ModifierKeys modifiers,bool editingText=false)
        {
            if(editingText || sectionMode || Drawing.Formwork || modifiers!=ModifierKeys.None)return false;
            if(key==Key.Escape){Drawing.ClearSelection();return true;}
            if(key!=Key.Delete)return false;
            RemoveSelected();return true;
        }
        internal void RemoveSelected()
        {
            if(sectionMode || Drawing.Formwork || !CommitFields() || Model.IsOptimizing)return;
            var plan=PlanRemoval();if(!plan.Allowed){status.Text=plan.Message;return;}
            Mutate(()=>
            {
                foreach(var crossing in plan.Crossings)Model.RemoveCrossingBeam(crossing);
                foreach(int beam in plan.Beams)Model.RemoveNetworkBeam(beam);
                Drawing.SetSelection(new Tuple<FormworkObject,int>[0],FormworkObject.Beam,0,false);
            });
            status.Text="Selection removed. Ctrl+Z restores the whole edit.";
        }
        private void Text(string value,bool bold=false)=>properties.Children.Add(new TextBlock {Text=value,TextWrapping=TextWrapping.Wrap,FontWeight=bold ? FontWeights.SemiBold : FontWeights.Normal,Margin=new Thickness(0,4,0,8)});
        private void Number(string name,string label,Func<double> get,Action<double> set,double minimum=.001)
        {
            Text(label);var box=new TextBox {Name="Edit"+name,Text=get().ToString("0.###",CultureInfo.CurrentCulture),Padding=new Thickness(7),Margin=new Thickness(0,0,0,8),ToolTip=label};
            var editor=new NumberEditor {Box=box,Get=get,Set=set,Minimum=minimum};editors.Add(editor);Fields[name]=box;properties.Children.Add(box);
            box.TextChanged+=(s,e)=> {if(changing)return;editor.Dirty=true;ValidateEditor(editor);Model.SetAdditionalTextInputErrors(this,true);Refresh();};
            box.LostKeyboardFocus+=(s,e)=>CommitFields();box.KeyDown+=(s,e)=> {if(e.Key==Key.Enter){CommitFields();e.Handled=true;}};
        }
        private bool ValidateEditor(NumberEditor editor)
        {
            bool valid=double.TryParse(editor.Box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var v) && TowerBeamAnalysis.Finite(v) && v>=editor.Minimum;
            editor.Box.BorderBrush=valid ? Brushes.SlateGray : Brushes.Firebrick;return valid;
        }
        private bool ValidateStock(StringEditor editor)
        {
            bool valid;try {TowerStockPlanner.ParseLengths(editor.Box.Text);valid=true;}catch(ArgumentException){valid=false;}
            editor.Box.BorderBrush=valid ? Brushes.SlateGray : Brushes.Firebrick;return valid;
        }
        internal bool CommitFields()
        {
            if(changing)return true;if(editors.Any(e=>e.Box.IsEnabled && !ValidateEditor(e)) || stringEditors.Any(e=>!ValidateStock(e))) {status.Text="Correct the red input before continuing.";return false;}
            var dirty=editors.Where(e=>e.Dirty && e.Box.IsEnabled).ToArray();var strings=stringEditors.Where(e=>e.Dirty).ToArray();if(dirty.Length==0 && strings.Length==0)return true;
            var old=Capture();changing=true;
            try {Model.SetAdditionalTextInputErrors(this,false);foreach(var e in dirty){double v=double.Parse(e.Box.Text,CultureInfo.CurrentCulture);if(Math.Abs(v-e.Get())>1e-9)e.Set(v);e.Dirty=false;}foreach(var e in strings){e.Set(e.Box.Text);e.Dirty=false;}}
            finally {changing=false;}
            undo.Push(old);redo.Clear();Refresh();return true;
        }
        private void BuildProperties()
        {
            changing=true;editors.Clear();stringEditors.Clear();syncControls.Clear();Fields.Clear();properties.Children.Clear();
            try
            {
                int count=Model.IsNetworkMode ? Model.NetworkBeams.Count : 1;
                if(Drawing.SelectedKind==FormworkObject.Crossing && Drawing.SelectedIndex>=Model.CrossingBeams.Count || Drawing.SelectedKind==FormworkObject.Slab && Drawing.SelectedIndex>=Model.NetworkBays.Count || Drawing.SelectedKind==FormworkObject.Beam && Drawing.SelectedIndex>=count){Drawing.Select(FormworkObject.Beam,0,false);}
                int index=Drawing.SelectedIndex;selectedObject=SelectedObject();
                if(!Drawing.Formwork && Drawing.SelectedObjects.Length==0)Text("Select a beam or slab in the drawing to edit it.",true);
                else if(Drawing.SelectedKind==FormworkObject.Beam)
                {
                    Text("B"+(index+1)+" · beam",true);
                    if(Model.IsNetworkMode){var b=Model.NetworkBeams[index];Number("WidthMm","Width (mm)",()=>b.WidthMm,v=>b.WidthMm=v);Number("DepthMm","Depth (mm)",()=>b.DepthMm,v=>b.DepthMm=v);}
                    else {Number("WidthMm","Width (mm)",()=>Model.Width,v=>Model.Width=v);Number("DepthMm","Depth (mm)",()=>Model.Depth,v=>Model.Depth=v);}
                }
                else if(Drawing.SelectedKind==FormworkObject.Slab)
                {var s=Model.NetworkBays[index];Text("S"+(index+1)+" · slab",true);Number("WidthMm","Clear span (mm)",()=>s.WidthMm,v=>s.WidthMm=v);Number("ThicknessMm","Thickness (mm)",()=>s.ThicknessMm,v=>s.ThicknessMm=v);}
                else
                {var c=Model.CrossingBeams[index];Text("X"+(index+1)+" · crossing beam",true);Number("PositionMm","Position along beam (mm)",()=>c.PositionMm,v=>c.PositionMm=v,0);Number("WidthMm","Width (mm)",()=>c.WidthMm,v=>c.WidthMm=v);Number("DepthMm","Total depth (mm)",()=>c.DepthMm,v=>c.DepthMm=v);Text("Slab bay");var bay=new ComboBox {ItemsSource=Model.NetworkBays.Select(s=>s.Label).ToArray(),SelectedIndex=c.BayNumber-1,Margin=new Thickness(0,0,0,12)};bay.SelectionChanged+=(s,e)=> {if(changing || bay.SelectedIndex<0)return;int choice=bay.SelectedIndex;if(CommitFields())Mutate(()=>c.BayNumber=choice+1);else Refresh();};syncControls.Add(()=>bay.SelectedIndex=c.BayNumber-1);properties.Children.Add(bay);}
                if(Drawing.SelectedObjects.Length>1)Text(Drawing.SelectedObjects.Length+" objects selected. Shift-click toggles selection; Delete removes the selected areas.");
                Text("Shared settings",true);Number("BeamLengthMm","Beam length (mm)",()=>Model.BeamLengthMm,v=>Model.BeamLengthMm=v);Number("UpperSupportSpacingMm","Secondary row spacing (mm)",()=>Model.UpperSupportSpacingMm,v=>Model.UpperSupportSpacingMm=v);Number("TowerLegCapacityKn","Permissible leg load (kN)",()=>Model.TowerLegCapacityKn,v=>Model.TowerLegCapacityKn=v);
                Text("Deflection criterion");var ratio=new ComboBox {ItemsSource=Model.DeflectionOptions,SelectedItem=Model.DeflectionLimit,DisplayMemberPath="Label",Margin=new Thickness(0,0,0,12)};
                ratio.SelectionChanged+=(s,e)=> {if(changing || !(ratio.SelectedItem is BeamDeflectionOption option))return;if(CommitFields())Mutate(()=>Model.DeflectionLimit=option);else Refresh();};syncControls.Add(()=>ratio.SelectedItem=Model.DeflectionLimit);properties.Children.Add(ratio);
                var exclude=new CheckBox {Content="Exclude standalone 600 mm towers",IsChecked=Model.Exclude600Standalone,Margin=new Thickness(0,0,0,14),ToolTip="Applies in either orientation; joined 600 mm bays remain allowed."};
                exclude.Click+=(s,e)=> {bool value=exclude.IsChecked==true;if(CommitFields())Mutate(()=>Model.Exclude600Standalone=value);else Refresh();};syncControls.Add(()=>exclude.IsChecked=Model.Exclude600Standalone);properties.Children.Add(exclude);
                var customH20=new CheckBox {Content="Allow custom H20 lengths (25 mm)",IsChecked=Model.AllowCustomH20Lengths,Margin=new Thickness(0,0,0,14),ToolTip="Optimize primary, lower secondary and slab H20s using catalogue or custom lengths in 25 mm increments."};
                customH20.Click+=(s,e)=> {bool value=customH20.IsChecked==true;if(CommitFields())Mutate(()=>Model.AllowCustomH20Lengths=value);else Refresh();};syncControls.Add(()=>customH20.IsChecked=Model.AllowCustomH20Lengths);properties.Children.Add(customH20);
                var advanced=new Expander {Header="Stock and load settings",IsExpanded=false,Margin=new Thickness(0,6,0,10)};var advancedPanel=new StackPanel();
                int first=properties.Children.Count;
                Text("H20 stocks (mm)");var stock=new TextBox {Text=Model.StockLengthsText,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};properties.Children.Add(stock);Fields["StockLengthsText"]=stock;
                var stockEditor=new StringEditor {Box=stock,Get=()=>Model.StockLengthsText,Set=v=>Model.StockLengthsText=v};stringEditors.Add(stockEditor);
                stock.TextChanged+=(s,e)=> {if(changing)return;stockEditor.Dirty=true;Model.SetAdditionalTextInputErrors(this,true);Refresh();};stock.LostKeyboardFocus+=(s,e)=>CommitFields();
                Number("StockEndSetbackMm","Support inset (mm)",()=>Model.StockEndSetbackMm,v=>Model.StockEndSetbackMm=v);Number("MaximumConcreteEndOverhangMm","Concrete end allowance (mm)",()=>Model.MaximumConcreteEndOverhangMm,v=>Model.MaximumConcreteEndOverhangMm=v,0);
                var custom=new CheckBox {Content="Custom load allowances",IsChecked=Model.UseCustomLoads};properties.Children.Add(custom);custom.Click+=(s,e)=> {bool value=custom.IsChecked==true;if(CommitFields())Mutate(()=>Model.UseCustomLoads=value);else Refresh();};syncControls.Add(()=>custom.IsChecked=Model.UseCustomLoads);
                Number("CustomLiveLoad","Placing + working load (kN/m²)",()=>Model.CustomLiveLoad,v=>Model.CustomLiveLoad=v,0);Fields["CustomLiveLoad"].IsEnabled=Model.UseCustomLoads;
                Number("CustomFormworkLoad","Formwork load (kN/m²)",()=>Model.CustomFormworkLoad,v=>Model.CustomFormworkLoad=v,0);Fields["CustomFormworkLoad"].IsEnabled=Model.UseCustomLoads;
                syncControls.Add(()=> {Fields["CustomLiveLoad"].IsEnabled=Fields["CustomFormworkLoad"].IsEnabled=Model.UseCustomLoads;});
                while(properties.Children.Count>first){var child=properties.Children[first];properties.Children.RemoveAt(first);advancedPanel.Children.Add(child);}advanced.Content=advancedPanel;properties.Children.Add(advanced);
                failures.Children.Clear();failureExpander.Content=failures;properties.Children.Add(failureExpander);
            }
            finally {changing=false;}
        }
        private object SelectedObject()=>Drawing.SelectedKind==FormworkObject.Crossing ? (object)Model.CrossingBeams.ElementAtOrDefault(Drawing.SelectedIndex) : Drawing.SelectedKind==FormworkObject.Slab ? Model.NetworkBays.ElementAtOrDefault(Drawing.SelectedIndex) : Model.IsNetworkMode ? Model.NetworkBeams.ElementAtOrDefault(Drawing.SelectedIndex) : Model;
        private void ModelChanged(object sender,PropertyChangedEventArgs e)
        {
            if((!Model.UseTowers || !Model.HasSelectedSupportSystem) && !closed){Close();return;}
            if(changing || refreshQueued || closed)return;refreshQueued=true;
            Dispatcher.BeginInvoke(new Action(()=> {refreshQueued=false;if(closed)return;if(!ReferenceEquals(selectedObject,SelectedObject()) && !editors.Any(x=>x.Dirty))BuildProperties();Refresh();}),DispatcherPriority.DataBind);
        }
        private void ClearDiagrams(){analysis.Content=null;Diagrams?.Close();Diagrams=null;analysisExpander.Header="Member diagrams";analysisExpander.IsExpanded=false;Drawing.SelectMember(-1,-1,0);Section.SelectMember(-1,-1,0);}
        private void Refresh()
        {
            Drawing.InvalidateVisual();changing=true;try {foreach(var e in editors)if(!e.Dirty && !e.Box.IsKeyboardFocused)e.Box.Text=e.Get().ToString("0.###",CultureInfo.CurrentCulture);foreach(var e in stringEditors)if(!e.Dirty && !e.Box.IsKeyboardFocused)e.Box.Text=e.Get();foreach(var sync in syncControls)sync();}finally {changing=false;}
            bool pending=editors.Any(e=>e.Dirty && e.Box.IsEnabled) || stringEditors.Any(e=>e.Dirty),valid=editors.Where(e=>e.Box.IsEnabled).All(ValidateEditor) && stringEditors.All(ValidateStock);
            Model.SetAdditionalTextInputErrors(this,pending);
            Buttons["Calculate"].IsEnabled=valid && !Model.IsOptimizing;
            Buttons["Cancel"].IsEnabled=Model.CanCancelOptimization;
            Buttons["Formwork"].IsEnabled=Model.IsCalculationValid && !pending;
            Buttons["Section"].IsEnabled=Model.IsCalculationValid && Model.NetworkResult!=null && !pending;
            Buttons["AddAbove"].IsEnabled=Buttons["AddBelow"].IsEnabled=!Model.IsOptimizing && (!Model.IsNetworkMode || Model.CanAddNetworkBay);
            Buttons["AddCrossing"].IsEnabled=Model.IsNetworkMode && Model.CrossingBeams.Count<30 && !Model.IsOptimizing;
            Buttons["Delete"].IsEnabled=!sectionMode && !Drawing.Formwork && !Model.IsOptimizing && PlanRemoval().Allowed;
            Buttons["Delete"].ToolTip=RemovalPreview;
            Buttons["Undo"].IsEnabled=undo.Count>0 && !Model.IsOptimizing;Buttons["Redo"].IsEnabled=redo.Count>0 && !Model.IsOptimizing;
            Buttons["Save"].IsEnabled=valid && !Model.IsOptimizing;Buttons["Export"].IsEnabled=Model.IsCalculationValid && !pending;Buttons["Load"].IsEnabled=!Model.IsOptimizing;
            UpdateViewButtons();
            if(!Model.IsCalculationValid || pending) {ClearDiagrams();Section.SetResult(null);if(sectionMode)Geometry();}
            else Section.SetResult(Model.NetworkResult);
            Drawing.RefitIfNeeded();
            status.Text=pending ? (valid ? "Finish editing, then calculate." : "Correct the red input before continuing.") : Model.IsOptimizing ? Model.WarningMessage : Model.IsCalculationValid ? (Model.NetworkResult?.Summary??Model.TowerResult.CompactSummary+" · "+Model.TowerResult.LegLoadSummary).Replace("\n"," · ") : Model.HasTowerFailure ? "No solution found · open calculation failure details." : "Needs recalculation · "+Model.WarningMessage;
            if(!sectionMode && !Drawing.Formwork && !pending && !Model.IsOptimizing && Drawing.SelectedObjects.Length>0)status.Text+=" · "+RemovalPreview;
            failureExpander.Visibility=Model.HasTowerFailure ? Visibility.Visible : Visibility.Collapsed;
            if(Model.HasTowerFailure && (failure.Text!=Model.TowerFailureDetails || failures.Children.Count==0))
            {
                failure.Text=Model.TowerFailureDetails;failures.Children.Clear();
                foreach(string line in failure.Text.Split('\n').Where(l=>l.StartsWith("B") || l.StartsWith("S")).Take(12))
                {
                    var match=System.Text.RegularExpressions.Regex.Match(line,"^([BS])([0-9]+)");if(!match.Success)continue;
                    var button=new Button {Content=new TextBlock {Text=line,TextWrapping=TextWrapping.Wrap},HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3,0,3),Padding=new Thickness(5)};
                    var kind=match.Groups[1].Value=="B" ? FormworkObject.Beam : FormworkObject.Slab;int index=int.Parse(match.Groups[2].Value)-1;
                    button.Click+=(s,e)=> {if(CommitFields()){Geometry();Drawing.Select(kind,index);Drawing.Fit();}};failures.Children.Add(button);
                }
                failures.Children.Add(failure);
            }
        }
        internal async Task CalculateAsync()
        {
            if(!CommitFields())return;await Model.OptimizeTowersAsync();Refresh();
            if(Model.IsCalculationValid){Drawing.Formwork=true;Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;sectionMode=false;Drawing.Fit();Refresh();}
            else {Geometry();failureExpander.IsExpanded=Model.HasTowerFailure;}
        }
        internal void ShowMember(int beam,int run,int line)
        {
            if(!Model.IsCalculationValid || !CommitFields())return;
            if(Model.IsNetworkMode)Model.SelectNetworkBeam(beam);
            Drawing.Select(FormworkObject.Beam,beam,false);BuildProperties();
            ClearDiagrams();Diagrams=new TowerDiagramWindow(Model);Diagrams.SelectMember(run,line,-1);
            var content=(FrameworkElement)Diagrams.Content;Diagrams.Content=null;analysis.Content=content;
            analysisExpander.Header=Diagrams.Title;analysisExpander.IsExpanded=true;Drawing.SelectMember(beam,run,line);Section.SelectMember(beam,run,line);
        }
    }
}

