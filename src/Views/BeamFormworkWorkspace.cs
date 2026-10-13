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
        internal readonly BeamSearchWaitingView Waiting;
        internal readonly SeparateSlabExplanationView SeparateSlabNotice=new SeparateSlabExplanationView();
        internal readonly TransferNetworkPlan Section=new TransferNetworkPlan {SectionOnly=true,Width=1050,IsSelectionEnabled=true};
        internal readonly FormworkDrawingViewport SectionViewport;
        internal readonly Dictionary<string,Control> Buttons=new Dictionary<string,Control>();
        internal readonly Dictionary<string,TextBox> Fields=new Dictionary<string,TextBox>();
        internal TowerDiagramWindow Diagrams {get;private set;}
        internal string FailureDetails=>failure.Text;
        internal IEnumerable<Button> FailureTargets=>failures.Children.OfType<Button>();
        internal readonly TabControl InspectorTabs=new TabControl();
        internal readonly StackPanel SharedProperties=new StackPanel {Margin=new Thickness(16)};
        private readonly HashSet<string> sharedFields=new HashSet<string>();
        private readonly TextBlock networkSummary=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,12)};
        private readonly TextBlock selectedSummary=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};
        private readonly TextBlock resultHeading=new TextBlock {FontWeight=FontWeights.SemiBold,FontSize=15};
        private readonly TextBlock removalInfo=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,8,0,12)};
        private readonly Border resultCard=new Border {Background=Brushes.AliceBlue,Padding=new Thickness(14),Margin=new Thickness(8),CornerRadius=new CornerRadius(6)};
        private bool advancedExpanded;
        private PrimaryBeamMaterial displayedPrimaryMaterial;
        internal string NetworkResultText=>networkSummary.Text;
        internal string SelectedResultText=>selectedSummary.Text;
        private readonly StackPanel properties=new StackPanel {Margin=new Thickness(16)};
        private readonly TextBlock resultDetails=new TextBlock {TextWrapping=TextWrapping.Wrap};
        private readonly Expander detailsExpander=new Expander {Header="Details",Margin=new Thickness(0,8,0,0)};
        private int diagramBeam=-1;
        private readonly CheckBox dimensions=new CheckBox {Content="All dimensions",ToolTip="Show dimension chains for all members. Zoom in to reveal labels where space is limited.",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0)};
        private readonly TextBlock status=new TextBlock {Margin=new Thickness(14,8,14,8),TextWrapping=TextWrapping.Wrap};
        private readonly TextBox failure=new TextBox {IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=180};
        private readonly StackPanel failures=new StackPanel();
        private readonly Expander failureExpander=new Expander {Header="Calculation failure details"};
        private readonly Border sectionHost=new Border {Visibility=Visibility.Collapsed};
        private readonly List<NumberEditor> editors=new List<NumberEditor>();
        private readonly List<StringEditor> stringEditors=new List<StringEditor>();
        private readonly List<Action> syncControls=new List<Action>();
        private readonly Stack<State> undo=new Stack<State>(),redo=new Stack<State>();
        private readonly Action save,load,export,refreshInputs;
        internal Func<bool> ConfirmNewSession {get;set;}
        private bool changing,refreshQueued,closed;
        private bool sectionMode;
        private object selectedObject;
        internal BeamDiagnosticWindow Diagnostics {get;private set;}
        internal Window HostWindow {get;set;}
        internal Window DialogOwner=>HostWindow??this;
        internal readonly FrameworkElement Surface;
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
        private const string StateProperties="Depth,Width,BeamLengthMm,TowerLegCapacityKn,PrimaryMaterial,StockLengthsText,StockEndSetbackMm,MaximumConcreteEndOverhangMm,AllowCustomH20Lengths,CustomH20Scope,IgnoreUplift,Exclude600Standalone,DeflectionLimit,PlywoodDeflectionLimit,SelectedPlywood,TransferSlabLoads,UseBeamNetwork,HasLeftSlab,HasRightSlab,LeftSlabThickness,RightSlabThickness,LeftSlabBayMm,RightSlabBayMm,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad";
        internal BeamFormworkWorkspace(BeamCalculatorViewModel model,Action saveInputs,Action loadInputs,Action exportReport,Action refreshCalculatorInputs=null,Action changeSystem=null)
        {
            Model=model;save=saveInputs;load=loadInputs;export=exportReport;refreshInputs=refreshCalculatorInputs;
            ConfirmNewSession=()=>MessageBox.Show(DialogOwner,"Start a new beam formwork session? Current inputs, results and undo history will be cleared. Saved files are unchanged.","New session",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
            if(Model.IsTransferMode && !Model.UseBeamNetwork)Model.UseBeamNetwork=true;
            Drawing=new BeamFormworkCanvas(model);Waiting=new BeamSearchWaitingView {DataContext=model};SectionViewport=new FormworkDrawingViewport(Section);
            Title="Beam calculator · D3 shoring";Width=1280;Height=860;MinWidth=980;MinHeight=620;WindowState=WindowState.Maximized;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            FontFamily=new FontFamily("Segoe UI");FontSize=13;Background=Brushes.WhiteSmoke;
            var root=new Grid {Background=Brushes.WhiteSmoke};root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            // Revit has no managed entry assembly; never resolve this against the host.
            Surface=root;root.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/MyRevitAddin;component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            var toolbar=new WrapPanel {Margin=new Thickness(8)};
            toolbar.Children.Add(new TextBlock {Text="D3 shoring",FontSize=20,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,14,0)});
            if(changeSystem!=null)toolbar.Children.Add(Button("ChangeSystem","Change system",changeSystem));
            toolbar.Children.Add(Button("Close","Close",()=>DialogOwner.Close()));
            var newSession=Button("NewSession","New session",StartNewSession);newSession.ToolTip="Reset inputs and start a fresh beam formwork session.";toolbar.Children.Add(newSession);
            var files=new Menu {Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Center};
            var file=new MenuItem {Header="_File",Padding=new Thickness(10,6,10,6)};files.Items.Add(file);
            file.Items.Add(MenuAction("Save","Save inputs…",()=> {if(CommitFields())save?.Invoke();}));
            file.Items.Add(MenuAction("Load","Load inputs…",()=> {if(CommitFields()){var old=Capture();load?.Invoke();if(closed)return;if(!Same(old,Capture())){undo.Push(old);redo.Clear();}BuildProperties();Refresh();Drawing.Fit();}}));
            file.Items.Add(new Separator());file.Items.Add(MenuAction("Export","Export report…",()=> {if(CommitFields())export?.Invoke();}));toolbar.Children.Add(files);
            var additions=new Menu {Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Center};
            var add=new MenuItem {Header="+ _Add",Padding=new Thickness(10,6,10,6)};additions.Items.Add(add);
            add.Items.Add(MenuAction("AddAbove","Bay above selected beam",()=>AddBay(true)));
            add.Items.Add(MenuAction("AddBelow","Bay below selected beam",()=>AddBay(false)));
            add.Items.Add(MenuAction("AddCrossing","Crossing beam",()=> {if(CommitFields() && Model.IsNetworkMode){Geometry();Drawing.AddCrossing=true;Drawing.Cursor=Cursors.Cross;Drawing.InvalidateVisual();}}));toolbar.Children.Add(additions);
            toolbar.Children.Add(Button("Delete","Delete selection",RemoveSelected));
            toolbar.Children.Add(Button("Undo","Undo",()=>History(undo,redo)));toolbar.Children.Add(Button("Redo","Redo",()=>History(redo,undo)));
            toolbar.Children.Add(new Border {Width=1,Height=26,Background=Brushes.LightGray,Margin=new Thickness(10,0,10,0)});
            var calculate=Button("Calculate","Calculate",async()=>await CalculateAsync());calculate.SetResourceReference(StyleProperty,"PrimaryAction");toolbar.Children.Add(calculate);
            toolbar.Children.Add(Button("Cancel","Cancel",()=> {Model.CancelOptimization();Refresh();}));
            toolbar.Children.Add(Button("Diagnose","Diagnose",ShowDiagnostics));root.Children.Add(toolbar);
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(325)});Grid.SetRow(body,1);root.Children.Add(body);
            var main=new Grid {Background=Brushes.White};main.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition());body.Children.Add(main);
            var tabs=new WrapPanel {Margin=new Thickness(10,0,10,6)};
            tabs.Children.Add(Button("Geometry","Geometry",Geometry));tabs.Children.Add(Button("Formwork","Formwork",()=> {if(!CommitFields())return;sectionMode=false;Drawing.Formwork=true;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;Drawing.InvalidateVisual();Drawing.Fit();Refresh();}));
            tabs.Children.Add(Button("Section","Section",()=> {if(!CommitFields())return;sectionMode=true;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Visibility=Visibility.Collapsed;sectionHost.Visibility=Visibility.Visible;Refresh();}));
            tabs.Children.Add(new Border {Width=1,Height=24,Background=Brushes.LightGray,Margin=new Thickness(10,0,10,0)});
            tabs.Children.Add(Button("Fit","Fit",()=> {if(sectionMode)SectionViewport.Fit();else Drawing.Fit();}));
            tabs.Children.Add(Button("ZoomOut","−",()=>Zoom(1/1.25)));tabs.Children.Add(Button("ZoomIn","+",()=>Zoom(1.25)));
            dimensions.Checked+=(s,e)=> {Drawing.AllDimensions=true;Section.ShowAllDimensions=true;Drawing.InvalidateVisual();Section.InvalidateVisual();};dimensions.Unchecked+=(s,e)=> {Drawing.AllDimensions=false;Section.ShowAllDimensions=false;Drawing.InvalidateVisual();Section.InvalidateVisual();};tabs.Children.Add(dimensions);main.Children.Add(tabs);
            tabs.Children.Add(Button("SecondaryH20s","Show secondary H20s",()=> {Drawing.ShowSecondaryH20s=!Drawing.ShowSecondaryH20s;Drawing.InvalidateVisual();Refresh();}));
            Grid.SetRow(Drawing,1);main.Children.Add(Drawing);sectionHost.Child=SectionViewport;Grid.SetRow(sectionHost,1);main.Children.Add(sectionHost);Grid.SetRow(Waiting,1);main.Children.Add(Waiting);
            var inspector=new Grid {Background=Brushes.White};inspector.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});inspector.RowDefinitions.Add(new RowDefinition());Grid.SetColumn(inspector,1);body.Children.Add(inspector);
            resultCard.Background=new SolidColorBrush(Color.FromRgb(255,246,219));resultCard.BorderBrush=new SolidColorBrush(Color.FromRgb(212,170,0));resultCard.BorderThickness=new Thickness(4,0,0,0);resultCard.CornerRadius=new CornerRadius(0);
            var results=new StackPanel();results.Children.Add(SeparateSlabNotice);results.Children.Add(resultHeading);results.Children.Add(networkSummary);results.Children.Add(selectedSummary);detailsExpander.Content=new ScrollViewer {Content=resultDetails,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};results.Children.Add(detailsExpander);resultCard.Child=results;inspector.Children.Add(new ScrollViewer {Content=resultCard,MaxHeight=400,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            InspectorTabs.Items.Add(new TabItem {Header="Selection",Content=new ScrollViewer {Content=properties,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
            InspectorTabs.Items.Add(new TabItem {Header="Shared settings",Content=new ScrollViewer {Content=SharedProperties,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
            Grid.SetRow(InspectorTabs,1);inspector.Children.Add(InspectorTabs);
            Grid.SetRow(status,2);root.Children.Add(status);Content=root;
            Drawing.CanInteract=()=>!Model.IsOptimizing && CommitFields();
            Drawing.SelectionChanged+=()=> {InspectorTabs.SelectedIndex=0;BuildProperties();Refresh();
                if(Drawing.SelectedKind==FormworkObject.Slab && Drawing.SelectedObjects.Length==1)SeparateSlabNotice.ShowBay(Drawing.SelectedIndex);};
            Drawing.DimensionSelected+=(kind,index,field)=> {if(Fields.TryGetValue(field,out var box)){InspectorTabs.SelectedIndex=sharedFields.Contains(field) ? 1 : 0;box.Focus();box.SelectAll();}};
            Drawing.BeamOptionsRequested+=async beam=>await NetworkBeamOptionsMenu.OpenAsync(Model,beam,Drawing,()=> {ClearDiagrams();Drawing.RefitIfNeeded();Refresh();});
            Drawing.MemberSelected+=ShowMember;Section.MemberSelected+=ShowMember;Section.SeparateSlabSelected+=bay=>SeparateSlabNotice.ShowBay(bay);
            Drawing.CrossingRequested+=(bay,x)=> {if(!CommitFields())return;Mutate(()=> {Model.AddCrossingBeam();var c=Model.CrossingBeams.Last();c.BayNumber=bay+1;c.PositionMm=Math.Max(c.WidthMm/2,Math.Min(Model.BeamLengthMm-c.WidthMm/2,x));Drawing.Select(FormworkObject.Crossing,Model.CrossingBeams.Count-1,false);});Drawing.Cursor=Cursors.Arrow;};
            Drawing.MoveRequested+=(kind,index,delta)=> {if(!CommitFields())return;Mutate(()=> {if(kind==FormworkObject.Crossing) {var c=Model.CrossingBeams[index];c.PositionMm=Math.Max(c.WidthMm/2,Math.Min(Model.BeamLengthMm-c.WidthMm/2,c.PositionMm+delta));}else Model.NetworkBays[index-1].WidthMm=Math.Max(500,Model.NetworkBays[index-1].WidthMm+delta);});};
            Model.PropertyChanged+=ModelChanged;
            root.Loaded+=(s,e)=>Drawing.Fit();Closed+=(s,e)=> {closed=true;Diagnostics?.Close();Model.PropertyChanged-=ModelChanged;Model.SetAdditionalTextInputErrors(this,false);ClearDiagrams();};
            root.PreviewKeyDown+=(s,e)=>
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
        private MenuItem MenuAction(string name,string text,Action action)
        {var item=new MenuItem {Header=text,Name="Workspace"+name};item.Click+=(s,e)=>action();Buttons[name]=item;return item;}
        private void StartNewSession()
        {
            if(Model.IsOptimizing || ConfirmNewSession==null || !ConfirmNewSession())return;
            changing=true;
            try
            {
                ClearDiagrams();Diagnostics?.Close();Model.StartNewSession();refreshInputs?.Invoke();
                undo.Clear();redo.Clear();advancedExpanded=false;failure.Text="";failures.Children.Clear();
                detailsExpander.IsExpanded=failureExpander.IsExpanded=false;
                Drawing.Select(FormworkObject.Beam,0,false);
                dimensions.IsChecked=false;
                Drawing.AllDimensions=false;
                Drawing.ShowSecondaryH20s=false;
                Section.ShowAllDimensions=false;Section.SetResult(null);SectionViewport.ResetView();InspectorTabs.SelectedIndex=0;
                BuildProperties();Geometry();Drawing.Fit();
            }
            finally {changing=false;}
            Refresh();
        }
        private void ShowDiagnostics()
        {
            if(Diagnostics!=null){Diagnostics.RefreshReport();Diagnostics.Activate();return;}
            Diagnostics=new BeamDiagnosticWindow(remarks=>Model.BuildDiagnosticReport(remarks,"Source: Drawing workspace; selected "+Drawing.SelectedKind+" index "+Drawing.SelectedIndex+" (zero based)\n"+string.Join("\n",Fields.Select(f=>f.Key+" = "+f.Value.Text+"; enabled="+f.Value.IsEnabled))));
            Diagnostics.Closed+=(s,e)=>Diagnostics=null;if(DialogOwner.IsVisible)Diagnostics.Owner=DialogOwner;Diagnostics.Show();
        }
        private void Geometry()
        {sectionMode=false;Drawing.Formwork=false;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Cursor=Cursors.Arrow;Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;Drawing.InvalidateVisual();Refresh();}
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
        private void ConcreteTop(Func<double> bottom,Func<double> depth)
        {
            var level=new TextBlock {Name="ConcreteTopElevation",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8),ToolTip="Changing depth keeps this concrete top level. Edit the soffit to change the top level."};
            Action sync=()=>level.Text=$"Concrete top: {bottom()+depth():0.###} mm";sync();syncControls.Add(sync);properties.Children.Add(level);
        }
        private void Number(string name,string label,Func<double> get,Action<double> set,double minimum=.001)
        {
            Text(label);var box=new TextBox {Name="Edit"+name,Text=get().ToString("0.###",CultureInfo.CurrentCulture),Padding=new Thickness(7),Margin=new Thickness(0,0,0,8),ToolTip=label};
            var editor=new NumberEditor {Box=box,Get=get,Set=set,Minimum=minimum};editors.Add(editor);Fields[name]=box;properties.Children.Add(box);
            box.TextChanged+=(s,e)=> {if(changing)return;editor.Dirty=true;ValidateEditor(editor);Model.SetAdditionalTextInputErrors(this,true);Refresh();};
            box.LostKeyboardFocus+=(s,e)=>CommitFields();box.KeyDown+=(s,e)=> {if(e.Key==Key.Enter){CommitFields();e.Handled=true;}};
        }
        private void CalculatedNumber(string name,string label)
        {
            Text(label);var box=new TextBox {Name="Edit"+name,IsReadOnly=true,IsEnabled=false,Background=Brushes.LightGray,Padding=new Thickness(7),Margin=new Thickness(0,0,0,8),ToolTip="Calculated from the selected plywood chart."};
            box.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding(name) {Source=Model,Mode=System.Windows.Data.BindingMode.OneWay,StringFormat="0.###"});
            Fields[name]=box;properties.Children.Add(box);
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
            changing=true;displayedPrimaryMaterial=Model.PrimaryMaterial;editors.Clear();stringEditors.Clear();syncControls.Clear();Fields.Clear();sharedFields.Clear();properties.Children.Clear();SharedProperties.Children.Clear();
            try
            {
                int count=Model.IsNetworkMode ? Model.NetworkBeams.Count : 1;
                if(Drawing.SelectedKind==FormworkObject.Crossing && Drawing.SelectedIndex>=Model.CrossingBeams.Count || Drawing.SelectedKind==FormworkObject.Slab && Drawing.SelectedIndex>=Model.NetworkBays.Count || Drawing.SelectedKind==FormworkObject.Beam && Drawing.SelectedIndex>=count){Drawing.Select(FormworkObject.Beam,0,false);}
                int index=Drawing.SelectedIndex;selectedObject=SelectedObject();
                if(!Drawing.Formwork && Drawing.SelectedObjects.Length==0)Text("Select a beam or slab in the drawing to edit it.",true);
                else if(Drawing.SelectedKind==FormworkObject.Beam)
                {
                    Text("B"+(index+1)+" · beam",true);
                    if(Model.IsNetworkMode){var b=Model.NetworkBeams[index];Number("WidthMm","Width (mm)",()=>b.WidthMm,v=>b.WidthMm=v);Number("DepthMm","Depth (mm)",()=>b.DepthMm,b.SetDepthKeepingTopMm);Number("BottomElevationMm","Bottom / soffit elevation (mm)",()=>b.BottomElevationMm,v=>b.BottomElevationMm=v,-1000000);ConcreteTop(()=>b.BottomElevationMm,()=>b.DepthMm);}
                    else {Number("WidthMm","Width (mm)",()=>Model.Width,v=>Model.Width=v);Number("DepthMm","Depth (mm)",()=>Model.Depth,v=>Model.Depth=v);}
                }
                else if(Drawing.SelectedKind==FormworkObject.Slab)
                {var s=Model.NetworkBays[index];Text("S"+(index+1)+" · slab",true);Number("WidthMm","Clear span (mm)",()=>s.WidthMm,v=>s.WidthMm=v);Number("ThicknessMm","Thickness (mm)",()=>s.ThicknessMm,v=>s.ThicknessMm=v);}
                else
                {var c=Model.CrossingBeams[index];Text("X"+(index+1)+" · crossing beam",true);Number("PositionMm","Position along beam (mm)",()=>c.PositionMm,v=>c.PositionMm=v,0);Number("WidthMm","Width (mm)",()=>c.WidthMm,v=>c.WidthMm=v);Number("DepthMm","Total depth (mm)",()=>c.DepthMm,c.SetDepthKeepingTopMm);Number("BottomElevationMm","Bottom / soffit elevation (mm)",()=>c.BottomElevationMm,v=>c.BottomElevationMm=v,-1000000);ConcreteTop(()=>c.BottomElevationMm,()=>c.DepthMm);Text("Cross-beam supports");var mode=new ComboBox {ItemsSource=c.SupportModes,DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=c.SupportMode,Margin=new Thickness(0,0,0,12)};mode.SelectionChanged+=(sender,args)=> {if(changing || !(mode.SelectedValue is CrossBeamSupportMode value))return;if(CommitFields())Mutate(()=>c.SupportMode=value);else Refresh();};syncControls.Add(()=>mode.SelectedValue=c.SupportMode);properties.Children.Add(mode);Text(CrossBeamSupportResult.Limitations);Text("Slab bay");var bay=new ComboBox {ItemsSource=Model.NetworkBays.Select(s=>s.Label).ToArray(),SelectedIndex=c.BayNumber-1,Margin=new Thickness(0,0,0,12)};bay.SelectionChanged+=(s,e)=> {if(changing || bay.SelectedIndex<0)return;int choice=bay.SelectedIndex;if(CommitFields())Mutate(()=>c.BayNumber=choice+1);else Refresh();};syncControls.Add(()=>bay.SelectedIndex=c.BayNumber-1);properties.Children.Add(bay);}
                if(Drawing.SelectedObjects.Length>1)Text(Drawing.SelectedObjects.Length+" objects selected. Shift-click toggles selection; Delete removes the selected areas.");
                int selectionControls=properties.Children.Count;var objectFields=new HashSet<string>(Fields.Keys);
                Text("Applies to every beam and slab bay.");Number("BeamLengthMm","Beam length (mm)",()=>Model.BeamLengthMm,v=>Model.BeamLengthMm=v);CalculatedNumber("UpperSupportSpacingMm","Maximum secondary spacing (mm) · calculated");Number("TowerLegCapacityKn","Permissible leg load (kN)",()=>Model.TowerLegCapacityKn,v=>Model.TowerLegCapacityKn=v);
                Text("Plywood");var plywood=new ComboBox {Name="WorkspacePlywoodSelector",ItemsSource=Model.PlywoodOptions,SelectedItem=Model.SelectedPlywood,DisplayMemberPath="Label",Margin=new Thickness(0,0,0,12)};
                plywood.SelectionChanged+=(s,e)=> {if(changing || !(plywood.SelectedItem is PlywoodProfile option))return;if(CommitFields())Mutate(()=>Model.SelectedPlywood=option);else Refresh();};syncControls.Add(()=>plywood.SelectedItem=Model.SelectedPlywood);properties.Children.Add(plywood);
                Text("Plywood limit");var plywoodLimit=new ComboBox {Name="WorkspacePlywoodDeflectionSelector",ItemsSource=Model.PlywoodDeflectionLimitOptions,SelectedItem=Model.PlywoodDeflectionLimit,Margin=new Thickness(0,0,0,12)};
                plywoodLimit.SelectionChanged+=(s,e)=> {if(changing || !(plywoodLimit.SelectedItem is string option))return;if(CommitFields())Mutate(()=>Model.PlywoodDeflectionLimit=option);else Refresh();};syncControls.Add(()=>plywoodLimit.SelectedItem=Model.PlywoodDeflectionLimit);properties.Children.Add(plywoodLimit);
                Text("Primary beam material");
                var material=new ComboBox {Name="PrimaryMaterialSelector",ItemsSource=Model.PrimaryMaterialOptions,DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=Model.PrimaryMaterial,Margin=new Thickness(0,0,0,12)};
                material.SelectionChanged+=(s,e)=> {if(changing || !(material.SelectedValue is PrimaryBeamMaterial choice))return;if(CommitFields())Mutate(()=>Model.PrimaryMaterial=choice);else Refresh();};
                syncControls.Add(()=>material.SelectedValue=Model.PrimaryMaterial);properties.Children.Add(material);
                if(Model.PrimaryMaterial==PrimaryBeamMaterial.WS10)Text("WS10 primaries use uncut catalogue lengths. Custom lengths apply only to H20s.");
                Text("H20 limit");var ratio=new ComboBox {ItemsSource=Model.DeflectionOptions,SelectedItem=Model.DeflectionLimit,DisplayMemberPath="Label",Margin=new Thickness(0,0,0,12)};
                ratio.SelectionChanged+=(s,e)=> {if(changing || !(ratio.SelectedItem is BeamDeflectionOption option))return;if(CommitFields())Mutate(()=>Model.DeflectionLimit=option);else Refresh();};syncControls.Add(()=>ratio.SelectedItem=Model.DeflectionLimit);properties.Children.Add(ratio);
                var exclude=new CheckBox {Content="Exclude standalone 600 mm towers",IsChecked=Model.Exclude600Standalone,Margin=new Thickness(0,0,0,14),ToolTip="Applies in either orientation; joined 600 mm bays remain allowed."};
                exclude.Click+=(s,e)=> {bool value=exclude.IsChecked==true;if(CommitFields())Mutate(()=>Model.Exclude600Standalone=value);else Refresh();};syncControls.Add(()=>exclude.IsChecked=Model.Exclude600Standalone);properties.Children.Add(exclude);
                var customH20=new CheckBox {Content="Allow custom H20 lengths (25 mm)",IsChecked=Model.AllowCustomH20Lengths,Margin=new Thickness(0,0,0,14),ToolTip="Applies to H20s only. WS10 primaries always use catalogue lengths."};
                customH20.Click+=(s,e)=> {bool value=customH20.IsChecked==true;if(CommitFields())Mutate(()=>Model.AllowCustomH20Lengths=value);else Refresh();};syncControls.Add(()=>customH20.IsChecked=Model.AllowCustomH20Lengths);properties.Children.Add(customH20);
                Text("Custom lengths for");
                var customScope=new ComboBox {Name="CustomH20ScopeSelector",ItemsSource=Model.CustomH20ScopeOptions,DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=Model.CustomH20Scope,IsEnabled=Model.AllowCustomH20Lengths,Margin=new Thickness(0,0,0,12),ToolTip="Beam supports: primary and lower H20s, including crossings. Slab supports: slab H20s."};
                customScope.SelectionChanged+=(s,e)=> {if(changing || !(customScope.SelectedValue is CustomH20LengthScope value))return;if(CommitFields())Mutate(()=>Model.CustomH20Scope=value);else Refresh();};
                syncControls.Add(()=> {customScope.SelectedValue=Model.CustomH20Scope;customScope.IsEnabled=Model.AllowCustomH20Lengths;});properties.Children.Add(customScope);
                var uplift=new CheckBox {Content="Exclude uplift check (connected plywood / deadweight assumed)",IsChecked=Model.IgnoreUplift,Margin=new Thickness(0,0,0,14),ToolTip=TowerLayout.ConnectedFormworkAssumption};
                uplift.Click+=(s,e)=> {bool value=uplift.IsChecked==true;if(CommitFields())Mutate(()=>Model.IgnoreUplift=value);else Refresh();};syncControls.Add(()=>uplift.IsChecked=Model.IgnoreUplift);properties.Children.Add(uplift);
                var advanced=new Expander {Header="Stock and load settings",IsExpanded=advancedExpanded,Margin=new Thickness(0,6,0,10)};var advancedPanel=new StackPanel();
                int first=properties.Children.Count;
                Text("H20 stocks (mm)"+ (Model.PrimaryMaterial==PrimaryBeamMaterial.WS10 ? " · secondaries / slabs" : ""));var stock=new TextBox {Text=Model.StockLengthsText,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};properties.Children.Add(stock);Fields["StockLengthsText"]=stock;
                if(Model.PrimaryMaterial==PrimaryBeamMaterial.WS10)Text(Model.WS10CatalogueText);
                var stockEditor=new StringEditor {Box=stock,Get=()=>Model.StockLengthsText,Set=v=>Model.StockLengthsText=v};stringEditors.Add(stockEditor);
                stock.TextChanged+=(s,e)=> {if(changing)return;stockEditor.Dirty=true;Model.SetAdditionalTextInputErrors(this,true);Refresh();};stock.LostKeyboardFocus+=(s,e)=>CommitFields();
                Number("StockEndSetbackMm","Support inset (mm)",()=>Model.StockEndSetbackMm,v=>Model.StockEndSetbackMm=v);Number("MaximumConcreteEndOverhangMm","Concrete end allowance (mm)",()=>Model.MaximumConcreteEndOverhangMm,v=>Model.MaximumConcreteEndOverhangMm=v,0);
                var custom=new CheckBox {Content="Custom load allowances",IsChecked=Model.UseCustomLoads};properties.Children.Add(custom);custom.Click+=(s,e)=> {bool value=custom.IsChecked==true;if(CommitFields())Mutate(()=>Model.UseCustomLoads=value);else Refresh();};syncControls.Add(()=>custom.IsChecked=Model.UseCustomLoads);
                Number("CustomLiveLoad","Placing + working load (kN/m²)",()=>Model.CustomLiveLoad,v=>Model.CustomLiveLoad=v,0);Fields["CustomLiveLoad"].IsEnabled=Model.UseCustomLoads;
                Number("CustomFormworkLoad","Formwork load (kN/m²)",()=>Model.CustomFormworkLoad,v=>Model.CustomFormworkLoad=v,0);Fields["CustomFormworkLoad"].IsEnabled=Model.UseCustomLoads;
                syncControls.Add(()=> {Fields["CustomLiveLoad"].IsEnabled=Fields["CustomFormworkLoad"].IsEnabled=Model.UseCustomLoads;});
                while(properties.Children.Count>first){var child=properties.Children[first];properties.Children.RemoveAt(first);advancedPanel.Children.Add(child);}advanced.Content=advancedPanel;advanced.Expanded+=(s,e)=>advancedExpanded=true;advanced.Collapsed+=(s,e)=>advancedExpanded=false;properties.Children.Add(advanced);
                foreach(var key in Fields.Keys.Where(k=>!objectFields.Contains(k)))sharedFields.Add(key);
                while(properties.Children.Count>selectionControls){var child=properties.Children[selectionControls];properties.Children.RemoveAt(selectionControls);SharedProperties.Children.Add(child);}
                properties.Children.Add(removalInfo);
                failures.Children.Clear();failureExpander.Content=failures;properties.Children.Add(failureExpander);
            }
            finally {changing=false;}
        }
        private object SelectedObject()=>Drawing.SelectedKind==FormworkObject.Crossing ? (object)Model.CrossingBeams.ElementAtOrDefault(Drawing.SelectedIndex) : Drawing.SelectedKind==FormworkObject.Slab ? Model.NetworkBays.ElementAtOrDefault(Drawing.SelectedIndex) : Model.IsNetworkMode ? Model.NetworkBeams.ElementAtOrDefault(Drawing.SelectedIndex) : Model;
        private void ModelChanged(object sender,PropertyChangedEventArgs e)
        {
            if((!Model.UseTowers || !Model.HasSelectedSupportSystem) && !closed){Close();return;}
            if(changing || refreshQueued || closed)return;refreshQueued=true;
            Dispatcher.BeginInvoke(new Action(()=> {refreshQueued=false;if(closed)return;if((!ReferenceEquals(selectedObject,SelectedObject()) || displayedPrimaryMaterial!=Model.PrimaryMaterial) && !editors.Any(x=>x.Dirty))BuildProperties();Refresh();}),DispatcherPriority.DataBind);
        }
        private void ClearDiagrams(){Diagrams?.Close();Diagrams=null;diagramBeam=-1;Drawing.SelectMember(-1,-1,0);Section.SelectMember(-1,-1,0);}
        private void Refresh()
        {
            properties.IsEnabled=SharedProperties.IsEnabled=!Model.IsOptimizing;
            Drawing.InvalidateVisual();changing=true;try {foreach(var e in editors)if(!e.Dirty && !e.Box.IsKeyboardFocused)e.Box.Text=e.Get().ToString("0.###",CultureInfo.CurrentCulture);foreach(var e in stringEditors)if(!e.Dirty && !e.Box.IsKeyboardFocused)e.Box.Text=e.Get();foreach(var sync in syncControls)sync();}finally {changing=false;}
            bool pending=editors.Any(e=>e.Dirty && e.Box.IsEnabled) || stringEditors.Any(e=>e.Dirty),valid=editors.Where(e=>e.Box.IsEnabled).All(ValidateEditor) && stringEditors.All(ValidateStock);
            Model.SetAdditionalTextInputErrors(this,pending);
            Buttons["NewSession"].IsEnabled=!Model.IsOptimizing;
            Buttons["Calculate"].IsEnabled=valid && !Model.IsOptimizing;
            Buttons["Cancel"].IsEnabled=Model.CanCancelOptimization;
            Buttons["Formwork"].IsEnabled=Model.IsCalculationValid && !pending;
            Buttons["Section"].IsEnabled=Model.IsCalculationValid && Model.NetworkResult!=null && !pending;
            Buttons["SecondaryH20s"].IsEnabled=!sectionMode && Drawing.Formwork && Model.IsCalculationValid && !pending;
            ((Button)Buttons["SecondaryH20s"]).Content=Drawing.ShowSecondaryH20s ? "Hide secondary H20s" : "Show secondary H20s";
            if(Buttons.ContainsKey("ChangeSystem"))Buttons["ChangeSystem"].IsEnabled=!Model.IsOptimizing;
            Buttons["AddAbove"].IsEnabled=Buttons["AddBelow"].IsEnabled=!sectionMode && !Drawing.Formwork && !Model.IsOptimizing && (!Model.IsNetworkMode || Model.CanAddNetworkBay);
            Buttons["AddCrossing"].IsEnabled=!sectionMode && !Drawing.Formwork && Model.IsNetworkMode && Model.CrossingBeams.Count<30 && !Model.IsOptimizing;
            Buttons["Delete"].IsEnabled=!sectionMode && !Drawing.Formwork && !Model.IsOptimizing && PlanRemoval().Allowed;
            Buttons["Delete"].ToolTip=RemovalPreview;
            Buttons["Undo"].IsEnabled=undo.Count>0 && !Model.IsOptimizing;Buttons["Redo"].IsEnabled=redo.Count>0 && !Model.IsOptimizing;
            Buttons["Save"].IsEnabled=valid && !Model.IsOptimizing;Buttons["Export"].IsEnabled=Model.IsCalculationValid && !pending;Buttons["Load"].IsEnabled=!Model.IsOptimizing;
            UpdateViewButtons();
            if(!Model.IsCalculationValid || pending) {ClearDiagrams();Section.SetResult(null);if(sectionMode)Geometry();}
            else Section.SetResult(Model.NetworkResult);
            Drawing.RefitIfNeeded();
            status.Text=pending ? (valid ? "Finish editing, then calculate." : "Correct the red input before continuing.") : Model.IsOptimizing ? Model.SearchProgressText : Model.IsCalculationValid ? "Results ready · click a member to open diagrams." : Model.HasTowerFailure ? "No solution found · open calculation failure details." : "Needs recalculation · "+Model.WarningMessage;
            removalInfo.Text=RemovalPreview;removalInfo.Visibility=!sectionMode && !Drawing.Formwork && Drawing.SelectedObjects.Length>0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshResults(pending);
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
        private void RefreshResults(bool pending)
        {
            bool valid=Model.IsCalculationValid && !pending;
            resultCard.Visibility=valid ? Visibility.Visible : Visibility.Collapsed;
            SeparateSlabNotice.SetResult(valid ? Model.NetworkResult : null);
            networkSummary.Text=selectedSummary.Text=resultDetails.Text="";if(!valid)return;
            var network=Model.NetworkResult;
            resultHeading.Text=network!=null ? "Network totals" : "Beam result";
            networkSummary.Text=network!=null ? $"{network.LegCount} tower legs · {network.H20Count} H20s"+(network.WS10Count>0 ? $" · {network.WS10Count} WS10s" : "") : $"{Model.TowerResult.LegCount} tower legs · {Model.TowerResult.StockPlan.TotalPrimaryCount} primary {Model.TowerResult.Primary.Name}s";
            resultDetails.Text=network!=null ? network.Summary+"\n"+network.UpperFootCount+" upper feet (separate from tower legs)" : Model.TowerResult.CompactSummary+"\n"+Model.TowerResult.LegLoadSummary;
            if(Model.IgnoreUplift)resultDetails.Text+=$"\nUplift excluded · minimum signed reaction {(network!=null ? network.Beams.Min(b=>b.MinimumReactionKn) : Model.TowerResult.MinimumReactionKn):0.######} kN";
            if(network==null)return;
            if(Drawing.SelectedObjects.Length==0){selectedSummary.Text="Select a beam or slab to inspect its result.";return;}
            if(Drawing.SelectedObjects.Length>1){selectedSummary.Text=Drawing.SelectedObjects.Length+" objects selected · select one to inspect its result.";return;}
            int index=Drawing.SelectedIndex;
            if(Drawing.SelectedKind==FormworkObject.Beam && index<network.Beams.Length)
            {
                var beam=network.Beams[index];int lower=beam.Transfer.RowsMm.Length;
                selectedSummary.Text=$"Selected beam · B{index+1} · {beam.LegCount} tower legs";
                resultDetails.Text+=$"\n\nB{index+1} · {beam.GroupCount} tower groups\n{beam.LegLoadSummary}\n{beam.StockPlan.TotalPrimaryCount} primary {beam.Primary.Name}s + {lower} lower H20s\nShared slab H20s are counted in network totals.";
            }
            else if(Drawing.SelectedKind==FormworkObject.Slab && index<network.Bays.Length)
            {
                var bay=network.Bays[index];int rows=network.SlabRowCount(index);
                if(bay==null) {selectedSummary.Text=$"Selected slab · S{index+1}\nSeparate slab formwork required.\nDesign and quantities are not calculated here.\nNo slab load transfers to B{index+1} or B{index+2}.";return;}
                string spacing=network.SecondarySpacingNotice.Length==0 ? $"Maximum row spacing {network.Input.UpperSpacingMm:0.###} mm." : network.SecondarySpacingNotice;
                selectedSummary.Text=$"Selected slab · S{index+1}\n{rows} slab H20s × {bay.StockLengthMm:0.###} mm{(bay.IsCustomLength ? " custom" : " stock")}\nEqual foot setback {bay.NearFootMm:0.###} mm from both concrete faces; repeat every row.\n{spacing}\n{2*rows} upper feet · carried by B{index+1} and B{index+2}\nActual member and leg reactions are checked separately for each loading stage.";
            }
            else if(Drawing.SelectedKind==FormworkObject.Crossing && index<Model.CrossingBeams.Count)
                selectedSummary.Text=network.CrossSupports.SingleOrDefault(c=>c.CrossingIndex==index)?.Schedule??$"X{index+1} · Legacy slab rows: loads transfer through S{Model.CrossingBeams[index].BayNumber}.";
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
            if(beam<0)
            {
                var support=Model.NetworkResult?.CrossSupports.SingleOrDefault(c=>c.CrossingIndex==-beam-1);if(support==null)return;
                Drawing.Select(FormworkObject.Crossing,support.CrossingIndex,false);BuildProperties();Refresh();
                if(Diagrams==null || diagramBeam!=beam)
                {
                    ClearDiagrams();var window=new TowerDiagramWindow(Model,support) {Owner=DialogOwner};Diagrams=window;diagramBeam=beam;
                    window.Closed+=(sender,args)=> {if(ReferenceEquals(Diagrams,window)){Diagrams=null;diagramBeam=-1;Drawing.SelectMember(-1,-1,0);Section.SelectMember(-1,-1,0);}};
                    window.SelectMember(run,line,-1);window.Show();
                }
                else {Diagrams.SelectMember(run,line,-1);Diagrams.Activate();}
                Drawing.SelectMember(beam,run,line);Section.SelectMember(beam,run,line);return;
            }
            if(Model.IsNetworkMode)Model.SelectNetworkBeam(beam);
            Drawing.Select(FormworkObject.Beam,beam,false);BuildProperties();Refresh();
            if(Diagrams==null || diagramBeam!=beam)
            {
                ClearDiagrams();var window=new TowerDiagramWindow(Model) {Owner=DialogOwner};Diagrams=window;diagramBeam=beam;
                window.Closed+=(sender,args)=> {if(ReferenceEquals(Diagrams,window)){Diagrams=null;diagramBeam=-1;Drawing.SelectMember(-1,-1,0);Section.SelectMember(-1,-1,0);}};
                window.SelectMember(run,line,-1);window.Show();
            }
            else {Diagrams.SelectMember(run,line,-1);Diagrams.Activate();}
            Drawing.SelectMember(beam,run,line);Section.SelectMember(beam,run,line);
        }
    }
    internal static class NetworkBeamOptionsMenu
    {
        internal static async Task<ContextMenu> OpenAsync(BeamCalculatorViewModel model,int beam,FrameworkElement target,Action selected=null)
        {
            var menu=new ContextMenu {PlacementTarget=target,Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint};
            menu.Items.Add(new MenuItem {Header=$"B{beam+1} layout options",IsEnabled=false});
            menu.Items.Add(new MenuItem {Header="Checking complete arrangements…",IsEnabled=false});
            target.ContextMenu=menu;menu.IsOpen=true;
            var source=model.NetworkResult;var options=await model.GetNetworkBeamOptionsAsync(beam);
            menu.Items.Clear();menu.Items.Add(new MenuItem {Header=$"B{beam+1} layout options",IsEnabled=false});
            if(!ReferenceEquals(source,model.NetworkResult) || !model.IsCalculationValid) {menu.IsOpen=false;return menu;}
            foreach(var option in options)
            {
                var item=new MenuItem {Header=option.Label,IsEnabled=option.IsAvailable,IsCheckable=option.IsAvailable,IsChecked=ReferenceEquals(option.Result,model.NetworkResult),ToolTip=option.RejectionReason};
                if(!option.IsAvailable)
                {
                    ToolTipService.SetShowOnDisabled(item,true);
                    item.ToolTip=new ToolTip {Content=new TextBlock {Text=option.RejectionReason,MaxWidth=500,TextWrapping=TextWrapping.Wrap}};
                }
                item.Click+=(s,e)=> {if(model.ApplyNetworkBeamOption(option))selected?.Invoke();};menu.Items.Add(item);
            }
            if(options.Count(o=>o.IsAvailable)<=1)menu.Items.Add(new MenuItem {Header=options.Length==0 ? "Alternative search unavailable or cancelled" : "No additional layout passed the complete checks",IsEnabled=false});
            return menu;
        }
    }

}



