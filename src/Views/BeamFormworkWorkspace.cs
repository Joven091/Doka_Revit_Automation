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
        internal bool IsResultCardVisible=>resultCard.Visibility==Visibility.Visible;
        private readonly StackPanel properties=new StackPanel {Margin=new Thickness(16)};
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
        private string draftCommand,shortcutPrefix;
        private int draftIndex;
        private FormworkObject draftKind;
        private Point? draftBase;
        private Rect draftSource;
        private TransferBeamRow draftBeam;
        private TransferCrossingBeamRow draftCrossing;
        private bool draftFree,draftVertical;
        private IndependentBeamRow draftIndependent,copiedIndependent;
        private TransferBeamRow copiedBeam;
        private TransferCrossingBeamRow copiedCrossing;
        internal ContextMenu GeometryMenu {get;private set;}
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
        private const string StateProperties="Depth,Width,PlanOriginYmm,BeamLengthMm,TowerLegCapacityKn,PrimaryMaterial,StockLengthsText,StockEndSetbackMm,MaximumConcreteEndOverhangMm,AllowCustomH20Lengths,CustomH20Scope,IgnoreUplift,Exclude600Standalone,DeflectionLimit,PlywoodDeflectionLimit,SelectedPlywood,TransferSlabLoads,UseBeamNetwork,HasLeftSlab,HasRightSlab,LeftSlabThickness,RightSlabThickness,LeftSlabBayMm,RightSlabBayMm,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad";
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
            add.Items.Add(MenuAction("AddIndependent","Independent beam (two endpoints)",BeginIndependent));
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
            tabs.Children.Add(Button("Geometry","Geometry",Geometry));tabs.Children.Add(Button("Formwork","Formwork",()=> {if(!CommitFields())return;CancelDrafting();sectionMode=false;Drawing.Formwork=true;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;Drawing.InvalidateVisual();Drawing.Fit();Refresh();}));
            tabs.Children.Add(Button("Section","Section",()=> {if(!CommitFields())return;CancelDrafting();sectionMode=true;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Visibility=Visibility.Collapsed;sectionHost.Visibility=Visibility.Visible;Refresh();}));
            tabs.Children.Add(new Border {Width=1,Height=24,Background=Brushes.LightGray,Margin=new Thickness(10,0,10,0)});
            tabs.Children.Add(Button("Fit","Fit",()=> {if(sectionMode)SectionViewport.Fit();else Drawing.Fit();}));
            tabs.Children.Add(Button("ZoomOut","−",()=>Zoom(1/1.25)));tabs.Children.Add(Button("ZoomIn","+",()=>Zoom(1.25)));
            dimensions.Checked+=(s,e)=> {Drawing.AllDimensions=true;Section.ShowAllDimensions=true;Drawing.InvalidateVisual();Section.InvalidateVisual();};dimensions.Unchecked+=(s,e)=> {Drawing.AllDimensions=false;Section.ShowAllDimensions=false;Drawing.InvalidateVisual();Section.InvalidateVisual();};tabs.Children.Add(dimensions);main.Children.Add(tabs);
            tabs.Children.Add(Button("SecondaryH20s","Show secondary H20s",()=> {Drawing.ShowSecondaryH20s=!Drawing.ShowSecondaryH20s;Drawing.InvalidateVisual();Refresh();}));
            Grid.SetRow(Drawing,1);main.Children.Add(Drawing);sectionHost.Child=SectionViewport;Grid.SetRow(sectionHost,1);main.Children.Add(sectionHost);Grid.SetRow(Waiting,1);main.Children.Add(Waiting);
            var inspector=new Grid {Background=Brushes.White};inspector.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});inspector.RowDefinitions.Add(new RowDefinition());Grid.SetColumn(inspector,1);body.Children.Add(inspector);
            resultCard.Background=new SolidColorBrush(Color.FromRgb(255,246,219));resultCard.BorderBrush=new SolidColorBrush(Color.FromRgb(212,170,0));resultCard.BorderThickness=new Thickness(4,0,0,0);resultCard.CornerRadius=new CornerRadius(0);
            var results=new StackPanel();results.Children.Add(SeparateSlabNotice);results.Children.Add(resultHeading);results.Children.Add(networkSummary);results.Children.Add(selectedSummary);resultCard.Child=results;inspector.Children.Add(new ScrollViewer {Content=resultCard,MaxHeight=400,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            InspectorTabs.Items.Add(new TabItem {Header="Selection",Content=new ScrollViewer {Content=properties,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
            InspectorTabs.Items.Add(new TabItem {Header="Shared settings",Content=new ScrollViewer {Content=SharedProperties,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
            Grid.SetRow(InspectorTabs,1);inspector.Children.Add(InspectorTabs);
            Grid.SetRow(status,2);root.Children.Add(status);Content=root;
            Drawing.CanInteract=()=>!Model.IsOptimizing && CommitFields();
            Drawing.SelectionChanged+=()=> {InspectorTabs.SelectedIndex=0;BuildProperties();Refresh();
                if(Drawing.SelectedKind==FormworkObject.Slab && Drawing.SelectedObjects.Length==1)SeparateSlabNotice.ShowBay(Drawing.SelectedIndex);};
            Drawing.DimensionSelected+=(kind,index,field)=> {if(Fields.TryGetValue(field,out var box)){InspectorTabs.SelectedIndex=sharedFields.Contains(field) ? 1 : 0;box.Focus();box.SelectAll();}};
            Drawing.BeamOptionsRequested+=async beam=>await NetworkBeamOptionsMenu.OpenAsync(Model,beam,Drawing,()=> {ClearDiagrams();Drawing.RefitIfNeeded();Refresh();});
            Drawing.GeometryContextRequested+=OpenGeometryMenu;
            Drawing.DraftPointRequested+=AcceptDraftPoint;Drawing.DraftPointerChanged+=PreviewDraftPoint;
            Drawing.MemberSelected+=ShowMember;Section.MemberSelected+=ShowMember;Section.SeparateSlabSelected+=bay=>SeparateSlabNotice.ShowBay(bay);
            Drawing.CrossingRequested+=(bay,x)=> {if(!CommitFields())return;Mutate(()=> {Model.AddCrossingBeam();var c=Model.CrossingBeams.Last();c.BayNumber=bay+1;c.PositionMm=Math.Max(c.WidthMm/2,Math.Min(Model.BeamLengthMm-c.WidthMm/2,x));Drawing.Select(FormworkObject.Crossing,Model.CrossingBeams.Count-1,false);});Drawing.Cursor=Cursors.Arrow;};
            Drawing.MoveRequested+=(kind,index,delta)=> {if(!CommitFields())return;Mutate(()=> {if(kind==FormworkObject.Crossing) {var c=Model.CrossingBeams[index];c.PositionMm=Math.Max(c.WidthMm/2,Math.Min(Model.BeamLengthMm-c.WidthMm/2,c.PositionMm+delta));}else if(!Model.TryMoveParallelBeam(index,delta))status.Text="Move needs at least 500 mm of clear slab bay on each side.";});};
            Model.PropertyChanged+=ModelChanged;
            root.Loaded+=(s,e)=>Drawing.Fit();Closed+=(s,e)=> {closed=true;Diagnostics?.Close();Model.PropertyChanged-=ModelChanged;Model.SetAdditionalTextInputErrors(this,false);ClearDiagrams();};
            root.PreviewKeyDown+=(s,e)=>
            {
                if(e.Key==Key.Escape){HandleGeometryKey(e.Key,Keyboard.Modifiers);e.Handled=true;return;}
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
                ClearDiagrams();Diagnostics?.Close();Model.StartNewSession();refreshInputs?.Invoke();CancelDrafting();copiedBeam=null;copiedCrossing=null;copiedIndependent=null;if(GeometryMenu!=null)GeometryMenu.IsOpen=false;
                undo.Clear();redo.Clear();advancedExpanded=false;failure.Text="";failures.Children.Clear();
                failureExpander.IsExpanded=false;
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
        {CancelDrafting();sectionMode=false;Drawing.Formwork=false;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Cursor=Cursors.Arrow;Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;Drawing.InvalidateVisual();Refresh();}
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
            var state=new State {Network=Model.NetworkConfigurationXml,Selection=Drawing.SelectedObjects,SelectedKind=Drawing.SelectedKind,SelectedIndex=Drawing.SelectedIndex};
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
        private void Mutate(Action change,bool fit=true)
        {if(fit)CancelDrafting();var old=Capture();changing=true;try {change();}finally {changing=false;}if(!Same(old,Capture())){undo.Push(old);redo.Clear();}BuildProperties();Refresh();if(fit)Drawing.Fit();}
        private void History(Stack<State> from,Stack<State> to)
        {if(!CommitFields() || from.Count==0 || Model.IsOptimizing)return;CancelDrafting();to.Push(Capture());Apply(from.Pop());}
        internal void AddBay(bool before)
        {
            if(!CommitFields() || Model.IsOptimizing || !Model.CanAddNetworkBay)return;
            Mutate(()=>InsertBay(before));Geometry();
        }
        private int InsertBay(bool before)
        {
            int selected=Drawing.SelectedKind==FormworkObject.Beam ? Drawing.SelectedIndex : Math.Min(Drawing.SelectedIndex,Model.NetworkBeams.Count-1);
            selected=Math.Max(0,selected);
            if(!Model.IsNetworkMode)
            {
                var initial=SerializeInitialNetwork();
                Model.TransferSlabLoads=true;Model.UseBeamNetwork=true;
                Model.NetworkConfigurationXml=initial;selected=before ? 0 : 1;
            }
            else {Model.InsertNetworkBay(selected,before);if(!before)selected++;}
            Drawing.Select(FormworkObject.Beam,selected,false);return selected;
        }
        internal bool CopySelectedGeometry()
        {
            if(sectionMode || Drawing.Formwork || Model.IsOptimizing || !CommitFields() || Drawing.SelectedObjects.Length!=1)return false;
            int index=Drawing.SelectedIndex;
            if(Drawing.SelectedKind==FormworkObject.Independent)
            {copiedIndependent=Model.IndependentBeams[index].Copy();copiedBeam=null;copiedCrossing=null;}
            else if(Drawing.SelectedKind==FormworkObject.Beam)
            {
                copiedIndependent=null;var beam=Model.IsNetworkMode ? Model.NetworkBeams[index] : Model.StandaloneDefinition();
                copiedBeam=new TransferBeamRow {WidthMm=beam.WidthMm,DepthMm=beam.DepthMm,BottomElevationMm=beam.BottomElevationMm};copiedCrossing=null;
            }
            else if(Drawing.SelectedKind==FormworkObject.Crossing)
            {
                copiedIndependent=null;var beam=Model.CrossingBeams[index];
                copiedCrossing=new TransferCrossingBeamRow {WidthMm=beam.WidthMm,DepthMm=beam.DepthMm,BottomElevationMm=beam.BottomElevationMm,SupportMode=beam.SupportMode};copiedBeam=null;
            }
            else return false;
            status.Text="Beam copied. Right-click to paste.";return true;
        }
        internal bool PasteGeometry(bool before,Tuple<int,double> bay=null,Point? placement=null)
        {
            if(sectionMode || Drawing.Formwork || Model.IsOptimizing || !CommitFields())return false;
            if(copiedIndependent!=null && Model.IndependentBeams.Count<30 && placement.HasValue)
            {var beam=copiedIndependent.Copy();beam.StartXmm=placement.Value.X;beam.StartYmm=placement.Value.Y;Mutate(()=> {Model.IndependentBeams.Add(beam);Drawing.Select(FormworkObject.Independent,Model.IndependentBeams.Count-1,false);});return true;}
            if(copiedBeam!=null && Model.CanAddNetworkBay)
            {
                Mutate(()=> {int index=InsertBay(before);var beam=Model.NetworkBeams[index];beam.WidthMm=copiedBeam.WidthMm;beam.DepthMm=copiedBeam.DepthMm;beam.BottomElevationMm=copiedBeam.BottomElevationMm;});return true;
            }
            if(copiedCrossing==null || !Model.IsNetworkMode || Model.CrossingBeams.Count>=30 || bay==null)return false;
            Mutate(()=> {Model.AddCrossingBeam();var beam=Model.CrossingBeams.Last();beam.BayNumber=bay.Item1+1;beam.WidthMm=copiedCrossing.WidthMm;beam.DepthMm=copiedCrossing.DepthMm;beam.BottomElevationMm=copiedCrossing.BottomElevationMm;beam.SupportMode=copiedCrossing.SupportMode;beam.PositionMm=Math.Max(beam.WidthMm/2,Math.Min(Model.BeamLengthMm-beam.WidthMm/2,bay.Item2));Drawing.Select(FormworkObject.Crossing,Model.CrossingBeams.Count-1,false);});return true;
        }
        private void OpenGeometryMenu(Point point)
        {
            CancelDrafting();var bay=Drawing.BayAt(point);var menu=new ContextMenu {PlacementTarget=Drawing};GeometryMenu=menu;
            menu.PreviewKeyDown+=(sender,args)=> {if(args.Key==Key.Escape){HandleGeometryKey(args.Key,Keyboard.Modifiers);args.Handled=true;}};
            Action<string,bool,Action> add=(text,enabled,action)=> {var item=new MenuItem {Header=text,IsEnabled=enabled};item.Click+=(s,e)=>action();menu.Items.Add(item);};
            add("Insert bay above",Model.CanAddNetworkBay,()=>AddBay(true));
            add("Insert bay below",Model.CanAddNetworkBay,()=>AddBay(false));
            add("Independent beam (two endpoints)",Model.IndependentBeams.Count<30,BeginIndependent);
            add("Insert cross beam",Model.IsNetworkMode && Model.CrossingBeams.Count<30,()=> {
                if(bay!=null) {Drawing.AddCrossing=true;Drawing.SelectAt(point);}
                else {Drawing.AddCrossing=true;Drawing.Cursor=Cursors.Cross;status.Text="Click inside a slab bay to place the cross beam.";}
            });
            menu.Items.Add(new Separator());
            bool selectedBeam=Drawing.SelectedObjects.Length==1 && Drawing.SelectedKind!=FormworkObject.Slab;
            add("Move (MV)",selectedBeam,()=>BeginDrafting("MV"));
            add("Copy with base point (CO)",selectedBeam,()=>BeginDrafting("CO"));
            add("Create similar (CS)",selectedBeam,()=>BeginDrafting("CS"));
            add("Copy",Drawing.SelectedObjects.Length==1 && Drawing.SelectedKind!=FormworkObject.Slab,()=>CopySelectedGeometry());
            if(copiedBeam!=null)
            {
                var paste=new MenuItem {Header="Paste",IsEnabled=Model.CanAddNetworkBay};menu.Items.Add(paste);
                foreach(bool above in new[] {true,false}) {var item=new MenuItem {Header=above ? "Above selected beam" : "Below selected beam"};item.Click+=(s,e)=>PasteGeometry(above);paste.Items.Add(item);}
            }
            else if(copiedIndependent!=null)add("Paste",Model.IndependentBeams.Count<30,()=>PasteGeometry(false,placement:Drawing.World(point)));
            else add("Paste",copiedCrossing!=null && bay!=null && Model.IsNetworkMode && Model.CrossingBeams.Count<30,()=>PasteGeometry(false,bay));
            menu.IsOpen=true;
        }
        private string SerializeInitialNetwork()
        {
            var source=Model.StandaloneDefinition();
            var config=new TransferNetworkConfiguration {SupportFormatVersion=2,PlanOriginYmm=Model.PlanOriginYmm,Beams=new[] {source,new TransferBeamRow {WidthMm=source.WidthMm,DepthMm=source.DepthMm,BottomElevationMm=source.BottomElevationMm}},Bays=new[] {new TransferBayRow {WidthMm=Model.RightSlabBayMm,ThicknessMm=Model.RightSlabThickness}},CrossingBeams=new TransferCrossingBeamRow[0],IndependentBeams=Model.IndependentBeams.Select(b=>b.Copy()).ToArray()};
            using(var text=new System.IO.StringWriter(CultureInfo.InvariantCulture)){new System.Xml.Serialization.XmlSerializer(typeof(TransferNetworkConfiguration)).Serialize(text,config);return text.ToString();}
        }
        private void CancelDrafting()
        {draftCommand=null;draftBase=null;draftFree=false;draftIndependent=null;shortcutPrefix=null;Drawing.CancelDrafting();}
        internal bool BeginDrafting(string command)
        {
            if(sectionMode || Drawing.Formwork || Model.IsOptimizing || !CommitFields() || Drawing.SelectedObjects.Length!=1 || Drawing.SelectedKind==FormworkObject.Slab || command!="MV" && command!="CO" && command!="CS")return false;
            CancelDrafting();Drawing.CancelSelectionGesture();Drawing.AddCrossing=false;
            draftKind=Drawing.SelectedKind;draftIndex=Drawing.SelectedIndex;
            var ys=Drawing.Centres();
            if(draftKind==FormworkObject.Independent)
            {
                draftIndependent=Model.IndependentBeams[draftIndex].Copy();draftBeam=draftIndependent;draftVertical=draftIndependent.IsVertical;draftFree=command=="CS";
                draftSource=IndependentRect(draftIndependent);
            }
            else if(draftKind==FormworkObject.Beam)
            {
                draftVertical=false;
                var b=Model.IsNetworkMode ? Model.NetworkBeams[draftIndex] : Model.StandaloneDefinition();
                draftBeam=new TransferBeamRow {WidthMm=b.WidthMm,DepthMm=b.DepthMm,BottomElevationMm=b.BottomElevationMm};
                draftSource=new Rect(0,ys[draftIndex]-b.WidthMm/2,Model.BeamLengthMm,b.WidthMm);
            }
            else
            {
                draftVertical=true;var b=Model.CrossingBeams[draftIndex];int bay=b.BayNumber-1;
                draftCrossing=new TransferCrossingBeamRow {WidthMm=b.WidthMm,DepthMm=b.DepthMm,BottomElevationMm=b.BottomElevationMm,SupportMode=b.SupportMode};
                double top=ys[bay]+Model.NetworkBeams[bay].WidthMm/2,bottom=ys[bay+1]-Model.NetworkBeams[bay+1].WidthMm/2;
                draftSource=new Rect(b.PositionMm-b.WidthMm/2,top,b.WidthMm,bottom-top);
            }
            Drawing.PreserveView();draftCommand=command;Drawing.DraftCommand=command;Drawing.Cursor=Cursors.Cross;Drawing.Focus();
            status.Text=command=="CS" ? "Create similar: pick a placement point; Space rotates into independent endpoint placement. Esc cancels." : command+": pick a base point, then the destination. Esc cancels.";
            return true;
        }
        private static Rect IndependentRect(IndependentBeamRow b)=>b.IsVertical ? new Rect(b.StartXmm-b.WidthMm/2,b.StartYmm,b.WidthMm,b.LengthMm) : new Rect(b.StartXmm,b.StartYmm-b.WidthMm/2,b.LengthMm,b.WidthMm);
        private void BeginIndependent()
        {
            Geometry();if(Drawing.SelectedObjects.Length!=1 || Drawing.SelectedKind==FormworkObject.Slab)Drawing.Select(FormworkObject.Beam,0);
            if(!BeginDrafting("CS"))return;draftFree=true;status.Text="Independent beam: pick the start, then the end. Space rotates the direction.";
        }
        internal bool RotateDraft()
        {
            if(draftCommand!="CS")return false;
            draftFree=true;draftVertical=!draftVertical;
            if(Drawing.DraftPointerWorld.HasValue)PreviewDraftPoint(Drawing.DraftPointerWorld.Value);
            Drawing.InvalidateVisual();status.Text="Independent beam: "+(draftBase.HasValue ? "pick the end" : "pick the start")+". Space rotates the direction.";return true;
        }
        private IndependentBeamRow FreeDraftBeam(Point point)
        {
            var source=draftKind==FormworkObject.Crossing ? (TransferBeamRow)new TransferBeamRow {WidthMm=draftCrossing.WidthMm,DepthMm=draftCrossing.DepthMm,BottomElevationMm=draftCrossing.BottomElevationMm} : draftBeam;
            var start=draftBase??point;double end=draftBase.HasValue ? (draftVertical ? point.Y : point.X) : (draftVertical ? start.Y : start.X)+(draftIndependent?.LengthMm??Model.BeamLengthMm);
            return new IndependentBeamRow {WidthMm=source.WidthMm,DepthMm=source.DepthMm,BottomElevationMm=source.BottomElevationMm,IsVertical=draftVertical,
                StartXmm=draftVertical ? start.X : Math.Min(start.X,end),StartYmm=draftVertical ? Math.Min(start.Y,end) : start.Y,LengthMm=Math.Abs(end-(draftVertical ? start.Y : start.X))};
        }
        private Tuple<int,double,double> DraftDestination(Point point)
        {
            double deltaX=draftBase.HasValue ? point.X-draftBase.Value.X : 0,deltaY=draftBase.HasValue ? point.Y-draftBase.Value.Y : 0;
            double x=draftCommand=="CS" ? point.X : draftSource.Left+draftSource.Width/2+deltaX;
            double y=draftCommand=="CS" ? point.Y : draftSource.Top+draftSource.Height/2+deltaY;
            int bay=-1;
            if(draftKind==FormworkObject.Crossing && Model.IsNetworkMode)
            {
                var ys=Drawing.Centres();
                for(int i=0;i<Model.NetworkBays.Count;i++)if(y>=ys[i]+Model.NetworkBeams[i].WidthMm/2 && y<=ys[i+1]-Model.NetworkBeams[i+1].WidthMm/2){bay=i;break;}
            }
            return Tuple.Create(bay,x,y);
        }
        private void PreviewDraftPoint(Point point)
        {
            if(draftCommand==null || draftCommand!="CS" && !draftBase.HasValue)return;
            if(draftFree) {var beam=FreeDraftBeam(point);Drawing.DraftPreview=IndependentRect(beam);return;}
            if(draftKind==FormworkObject.Independent)
            {var beam=draftIndependent.Copy();beam.StartXmm+=point.X-draftBase.Value.X;beam.StartYmm+=point.Y-draftBase.Value.Y;Drawing.DraftPreview=IndependentRect(beam);return;}
            var destination=DraftDestination(point);
            if(draftKind==FormworkObject.Beam)Drawing.DraftPreview=new Rect(0,destination.Item3-draftSource.Height/2,Model.BeamLengthMm,draftSource.Height);
            else if(destination.Item1>=0)
            {
                var ys=Drawing.Centres();int bay=destination.Item1;double top=ys[bay]+Model.NetworkBeams[bay].WidthMm/2,bottom=ys[bay+1]-Model.NetworkBeams[bay+1].WidthMm/2;
                Drawing.DraftPreview=new Rect(destination.Item2-draftSource.Width/2,top,draftSource.Width,bottom-top);
            }
            else Drawing.DraftPreview=null;
        }
        private void AcceptDraftPoint(Point point)
        {
            if(draftCommand==null)return;
            if(draftFree)
            {
                if(!draftBase.HasValue){draftBase=point;Drawing.DraftBasePoint=point;status.Text="Independent beam: pick the end. Space rotates the direction.";return;}
                var beam=FreeDraftBeam(point);try {beam.Validate();}catch(ArgumentException ex){status.Text=ex.Message;return;}
                if(Model.IndependentBeams.Count>=30){status.Text="Use at most 30 independent beams.";return;}
                Mutate(()=> {Model.IndependentBeams.Add(beam);Drawing.Select(FormworkObject.Independent,Model.IndependentBeams.Count-1,false);},false);CancelDrafting();status.Text="Independent beam placed. Its own length is calculated; slab loads remain on the connected network.";return;
            }
            if(draftCommand!="CS" && !draftBase.HasValue){draftBase=point;Drawing.DraftBasePoint=point;status.Text=draftCommand+": pick the destination. Main beams stay parallel; slab bays adjust automatically.";return;}
            if(draftKind==FormworkObject.Independent)
            {
                var beam=draftIndependent.Copy();beam.StartXmm+=point.X-draftBase.Value.X;beam.StartYmm+=point.Y-draftBase.Value.Y;
                try {beam.Validate();}catch(ArgumentException ex){status.Text=ex.Message;return;}
                if(draftCommand!="MV" && Model.IndependentBeams.Count>=30)return;
                Mutate(()=> {if(draftCommand=="MV") {Model.IndependentBeams[draftIndex].StartXmm=beam.StartXmm;Model.IndependentBeams[draftIndex].StartYmm=beam.StartYmm;}else {Model.IndependentBeams.Add(beam);Drawing.Select(FormworkObject.Independent,Model.IndependentBeams.Count-1,false);}},false);
                CancelDrafting();status.Text="Independent beam edit complete. Ctrl+Z restores it.";return;
            }
            var destination=DraftDestination(point);bool applied=false;
            if(draftKind==FormworkObject.Beam)
            {
                Mutate(()=> {
                    if(draftCommand=="MV")applied=Model.TryMoveParallelBeam(draftIndex,destination.Item3-(draftSource.Top+draftSource.Height/2));
                    else {applied=Model.TryInsertParallelBeam(draftBeam,destination.Item3,out int index);if(applied)Drawing.Select(FormworkObject.Beam,index,false);}
                },false);
            }
            else if(destination.Item1>=0 && destination.Item2>=draftCrossing.WidthMm/2 && destination.Item2<=Model.BeamLengthMm-draftCrossing.WidthMm/2 && (draftCommand=="MV" || Model.CrossingBeams.Count<30))
            {
                Mutate(()=> {
                    if(draftCommand!="MV")Model.AddCrossingBeam();
                    int index=draftCommand=="MV" ? draftIndex : Model.CrossingBeams.Count-1;var beam=Model.CrossingBeams[index];
                    beam.WidthMm=draftCrossing.WidthMm;beam.DepthMm=draftCrossing.DepthMm;beam.BottomElevationMm=draftCrossing.BottomElevationMm;beam.SupportMode=draftCrossing.SupportMode;beam.PositionMm=destination.Item2;beam.BayNumber=destination.Item1+1;
                    Drawing.Select(FormworkObject.Crossing,index,false);applied=true;
                },false);
            }
            if(applied){string command=draftCommand;CancelDrafting();status.Text=command+" complete. Ctrl+Z restores the whole edit.";}
            else status.Text="Placement unavailable. Keep 500–20000 mm of clear slab bay, avoid overlapping beams, and place cross beams inside a bay. Pick another point or press Esc.";
        }
        private sealed class RemovalPlan
        {
            internal int[] Independent=new int[0];
            internal int[] Beams=new int[0];
            internal TransferCrossingBeamRow[] Crossings=new TransferCrossingBeamRow[0];
            internal bool Allowed;
            internal string Message;
        }
        private RemovalPlan PlanRemoval()
        {
            var plan=new RemovalPlan();var selected=Drawing.SelectedObjects;
            if(selected.Length==0){plan.Message="Select objects to remove.";return plan;}
            plan.Independent=selected.Where(i=>i.Item1==FormworkObject.Independent && i.Item2>=0 && i.Item2<Model.IndependentBeams.Count).Select(i=>i.Item2).Distinct().OrderByDescending(i=>i).ToArray();
            var beams=new SortedSet<int>();var crosses=new HashSet<TransferCrossingBeamRow>();
            var explicitBeams=new HashSet<int>(selected.Where(i=>i.Item1==FormworkObject.Beam).Select(i=>i.Item2));
            foreach(var item in selected)
            {
                if(item.Item1==FormworkObject.Crossing && item.Item2>=0 && item.Item2<Model.CrossingBeams.Count)crosses.Add(Model.CrossingBeams[item.Item2]);
                else if(Model.IsNetworkMode && item.Item1==FormworkObject.Beam && item.Item2>=0 && item.Item2<Model.NetworkBeams.Count)beams.Add(item.Item2);
                else if(Model.IsNetworkMode && item.Item1==FormworkObject.Slab && item.Item2>=0 && item.Item2<Model.NetworkBays.Count)
                {if(!explicitBeams.Contains(item.Item2) && !explicitBeams.Contains(item.Item2+1))beams.Add(item.Item2+1);}
                else if(!Model.IsNetworkMode && item.Item1==FormworkObject.Beam){plan.Message="Keep at least one beam in the drawing.";return plan;}
            }
            if(Model.IsNetworkMode && Model.NetworkBeams.Count-beams.Count<1)
            {plan.Message="Keep at least one beam in the drawing. Narrow the selection.";return plan;}
            var remainingBeams=Enumerable.Range(0,Model.NetworkBeams.Count).ToList();
            var remainingBays=Enumerable.Range(0,Model.NetworkBays.Count).ToList();var removedBays=new List<int>();
            foreach(int original in beams.Reverse())
            {
                int current=remainingBeams.IndexOf(original),bay=Math.Max(0,current-1);
                removedBays.Add(remainingBays[bay]);remainingBays.RemoveAt(bay);remainingBeams.RemoveAt(current);
            }
            foreach(var crossing in Model.CrossingBeams.Where(c=>removedBays.Contains(c.BayNumber-1)))crosses.Add(crossing);
            plan.Beams=beams.Reverse().ToArray();plan.Crossings=crosses.ToArray();plan.Allowed=beams.Count>0 || crosses.Count>0 || plan.Independent.Length>0;
            var names=plan.Independent.Select(i=>"I"+(i+1)).Concat(beams.Select(i=>"B"+(i+1))).Concat(removedBays.OrderBy(i=>i).Select(i=>"S"+(i+1))).Concat(crosses.Select(c=>"X"+(Model.CrossingBeams.IndexOf(c)+1)).OrderBy(x=>x)).ToArray();
            plan.Message=plan.Allowed ? "Delete removes "+string.Join(", ",names)+". The remaining beams stay connected; a single beam uses beam-only design. Ctrl+Z restores the whole edit." : "Select objects to remove.";
            return plan;
        }
        internal string RemovalPreview=>PlanRemoval().Message;
        internal bool HandleGeometryKey(Key key,ModifierKeys modifiers,bool editingText=false)
        {
            if(key==Key.Escape)
            {
                CancelDrafting();if(GeometryMenu!=null)GeometryMenu.IsOpen=false;
                Drawing.AddCrossing=false;Drawing.Cursor=Cursors.Arrow;Drawing.CancelSelectionGesture();
                ClearDiagrams();Drawing.ClearSelection();return true;
            }
            if(editingText || sectionMode || Drawing.Formwork || modifiers!=ModifierKeys.None)return false;
            if(key==Key.Space && draftCommand=="CS")return RotateDraft();
            if(key==Key.Delete){CancelDrafting();RemoveSelected();return true;}
            string letter=key==Key.M ? "M" : key==Key.V ? "V" : key==Key.C ? "C" : key==Key.O ? "O" : key==Key.S ? "S" : null;
            string command=(shortcutPrefix??"")+(letter??"");
            if(command=="MV" || command=="CO" || command=="CS"){shortcutPrefix=null;BeginDrafting(command);return true;}
            shortcutPrefix=letter=="M" || letter=="C" ? letter : null;return letter!=null;
        }
        internal void RemoveSelected()
        {
            if(sectionMode || Drawing.Formwork || !CommitFields() || Model.IsOptimizing)return;
            var plan=PlanRemoval();if(!plan.Allowed){status.Text=plan.Message;return;}
            Mutate(()=>
            {
                foreach(int index in plan.Independent)Model.IndependentBeams.RemoveAt(index);
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
        private void CalculatedNumber(string name,string label,object source=null)
        {
            Text(label);var box=new TextBox {Name="Edit"+name,IsReadOnly=true,IsEnabled=false,Background=Brushes.LightGray,Padding=new Thickness(7),Margin=new Thickness(0,0,0,8),ToolTip="Calculated from the selected plywood chart."};
            box.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding(name) {Source=source??Model,Mode=System.Windows.Data.BindingMode.OneWay,StringFormat="0.###"});
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
            CancelDrafting();var old=Capture();changing=true;
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
                if(Drawing.SelectedKind==FormworkObject.Independent && Drawing.SelectedIndex>=Model.IndependentBeams.Count || Drawing.SelectedKind==FormworkObject.Crossing && Drawing.SelectedIndex>=Model.CrossingBeams.Count || Drawing.SelectedKind==FormworkObject.Slab && Drawing.SelectedIndex>=Model.NetworkBays.Count || Drawing.SelectedKind==FormworkObject.Beam && Drawing.SelectedIndex>=count){Drawing.Select(FormworkObject.Beam,0,false);}
                int index=Drawing.SelectedIndex;selectedObject=SelectedObject();
                if(Drawing.SelectedObjects.Length==0)Text("Select a beam or slab in the drawing to edit it.",true);
                else if(Drawing.SelectedKind==FormworkObject.Independent)
                {
                    var b=Model.IndependentBeams[index];Text("I"+(index+1)+" · independent beam",true);Text("Own endpoints and length. Carries this beam's load; slab loads remain on the connected bays.");
                    Number("StartXmm","Start X (mm)",()=>b.StartXmm,v=>b.StartXmm=v,-1000000);Number("StartYmm","Start Y (mm)",()=>b.StartYmm,v=>b.StartYmm=v,-1000000);Number("LengthMm","Length (mm)",()=>b.LengthMm,v=>b.LengthMm=v);
                    Number("WidthMm","Width (mm)",()=>b.WidthMm,v=>b.WidthMm=v);Number("DepthMm","Depth (mm)",()=>b.DepthMm,b.SetDepthKeepingTopMm);Number("BottomElevationMm","Bottom / soffit elevation (mm)",()=>b.BottomElevationMm,v=>b.BottomElevationMm=v,-1000000);ConcreteTop(()=>b.BottomElevationMm,()=>b.DepthMm);
                    var endpoint=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};Action syncEndpoint=()=>endpoint.Text=$"End: X {b.EndXmm:0.###}, Y {b.EndYmm:0.###} mm";syncEndpoint();syncControls.Add(syncEndpoint);properties.Children.Add(endpoint);
                    var rotate=new Button {Content="Rotate 90°",Margin=new Thickness(0,0,0,8)};rotate.Click+=(sender,args)=> {if(CommitFields())Mutate(()=>b.IsVertical=!b.IsVertical);};properties.Children.Add(rotate);
                }
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
                if(!Drawing.Formwork && !sectionMode)Text("Geometry commands: MV move · CO copy · CS create similar. Pick a beam, then type the two-letter command.");
                if(Drawing.SelectedObjects.Length==1)CalculatedNumber(nameof(TransferBeamRow.MaximumSecondarySpacingMm),"Spacing limit (mm)",SelectedObject());
                int selectionControls=properties.Children.Count;var objectFields=new HashSet<string>(Fields.Keys);
                Text("Shared material and load settings apply to every beam. Independent beams keep their own lengths.");Number("BeamLengthMm","Parallel beam length (mm)",()=>Model.BeamLengthMm,v=>Model.BeamLengthMm=v);CalculatedNumber("UpperSupportSpacingMm",Model.IsNetworkMode ? "Minimum beam spacing limit (mm)" : "Beam spacing limit (mm)");Number("TowerLegCapacityKn","Permissible leg load (kN)",()=>Model.TowerLegCapacityKn,v=>Model.TowerLegCapacityKn=v);
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
        private object SelectedObject()=>Drawing.SelectedKind==FormworkObject.Independent ? (object)Model.IndependentBeams.ElementAtOrDefault(Drawing.SelectedIndex) : Drawing.SelectedKind==FormworkObject.Crossing ? (object)Model.CrossingBeams.ElementAtOrDefault(Drawing.SelectedIndex) : Drawing.SelectedKind==FormworkObject.Slab ? Model.NetworkBays.ElementAtOrDefault(Drawing.SelectedIndex) : Model.IsNetworkMode ? Model.NetworkBeams.ElementAtOrDefault(Drawing.SelectedIndex) : Model;
        private void ModelChanged(object sender,PropertyChangedEventArgs e)
        {
            if((!Model.UseTowers || !Model.HasSelectedSupportSystem) && !closed){Close();return;}
            if(!changing && draftCommand!=null && (e.PropertyName==null || e.PropertyName=="NetworkConfigurationXml" || StateProperties.Split(',').Contains(e.PropertyName)))CancelDrafting();
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
            Buttons["AddIndependent"].IsEnabled=!sectionMode && !Drawing.Formwork && !Model.IsOptimizing && Model.IndependentBeams.Count<30;
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
            resultCard.Visibility=valid && Drawing.SelectedObjects.Length>0 ? Visibility.Visible : Visibility.Collapsed;
            SeparateSlabNotice.SetResult(valid ? Model.NetworkResult : null);
            networkSummary.Text=selectedSummary.Text="";if(!valid || Drawing.SelectedObjects.Length==0)return;
            resultCard.ToolTip="Efficiency = combined design load ÷ total leg capacity. Individual legs are checked separately. Load includes concrete, working and formwork allowances over the calculated coverage.";
            if(Drawing.SelectedKind==FormworkObject.Independent && Drawing.SelectedObjects.Length==1)
            {
                var beam=Model.IndependentBeamResults.ElementAtOrDefault(Drawing.SelectedIndex);resultHeading.Text="Independent beam result";networkSummary.Text="";
                selectedSummary.Text=beam==null ? "" : $"Selected beam · I{Drawing.SelectedIndex+1}\n"+BeamLoadSummary(beam).Replace("beam + slab","independent beam")+"\nSlab loads remain on the connected bays.";return;
            }
            var network=Model.NetworkResult;
            resultHeading.Text=network!=null ? "Network totals" : "Beam result";
            networkSummary.Text=network!=null ? $"{network.LegCount+Model.IndependentBeamResults.Sum(b=>b.LegCount)} tower legs · {network.H20Count} H20s"+(network.WS10Count>0 ? $" · {network.WS10Count} WS10s" : "") : $"{Model.TowerResult.LegCount} tower legs · {Model.TowerResult.StockPlan.TotalPrimaryCount} primary {Model.TowerResult.Primary.Name}s";
            if(network==null)
            {
                resultHeading.Text="Beam result";networkSummary.Text="";
                selectedSummary.Text="Selected beam · B1\n"+BeamLoadSummary(Model.TowerResult);return;
            }
            if(Drawing.SelectedObjects.Length>1){selectedSummary.Text=Drawing.SelectedObjects.Length+" objects selected · select one to inspect its result.";return;}
            int index=Drawing.SelectedIndex;
            if(Drawing.SelectedKind==FormworkObject.Beam && index<network.Beams.Length)
            {
                resultHeading.Text="Beam result";networkSummary.Text="";
                selectedSummary.Text=$"Selected beam · B{index+1}\n"+BeamLoadSummary(network.Beams[index]);
            }
            else if(Drawing.SelectedKind==FormworkObject.Slab && index<network.Bays.Length)
            {
                var bay=network.Bays[index];int rows=network.SlabRowCount(index);
                if(bay==null) {selectedSummary.Text=$"Selected slab · S{index+1}\nSeparate slab formwork required.\nDesign and quantities are not calculated here.\nNo slab load transfers to B{index+1} or B{index+2}.";return;}
                string spacing=network.Input.HasIndividualSpacings ? $"Slab spacing {network.Input.BaySpacing(index):0} mm · limit {Model.NetworkBays[index].MaximumSecondarySpacingMm:0} mm." : network.SecondarySpacingNotice.Length==0 ? $"Maximum row spacing {network.Input.UpperSpacingMm:0.###} mm." : network.SecondarySpacingNotice;
                selectedSummary.Text=$"Selected slab · S{index+1}\n{rows} slab H20s × {bay.StockLengthMm:0.###} mm{(bay.IsCustomLength ? " custom" : " stock")}\nEqual foot setback {bay.NearFootMm:0.###} mm from both concrete faces; repeat every row.\n{spacing}\n{2*rows} upper feet · carried by B{index+1} and B{index+2}\nActual member and leg reactions are checked separately for each loading stage.";
            }
            else if(Drawing.SelectedKind==FormworkObject.Crossing && index<Model.CrossingBeams.Count)
                selectedSummary.Text=network.CrossSupports.SingleOrDefault(c=>c.CrossingIndex==index)?.Schedule??$"X{index+1} · Legacy slab rows: loads transfer through S{Model.CrossingBeams[index].BayNumber}.";
        }
        private static string BeamLoadSummary(TowerLayout beam)=>
            $"Total load (beam + slab): {beam.TotalSupportedLoadKn:0.00} kN\n"+
            $"{beam.LegCount} tower legs × {beam.LegCapacityKn:0.##} kN = {beam.TotalLegCapacityKn:0.00} kN\n"+
            $"Efficiency: {beam.SupportEfficiencyPercent:0.0}%";
        internal async Task CalculateAsync(bool useTimeLimit=false)
        {
            if(!CommitFields())return;CancelDrafting();await Model.OptimizeTowersAsync(useTimeLimit);
            try
            {
                using(Model.MeasureDrawingPreparation())
                {
                    Refresh();
                    if(Model.IsCalculationValid){Drawing.Formwork=true;Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;sectionMode=false;Drawing.Fit();Refresh();}
                    else {Geometry();failureExpander.IsExpanded=Model.HasTowerFailure;}
                }
            }
            finally {Model.FinishDrawingPreparation();}
        }
        internal void ShowMember(int beam,int run,int line)
        {
            if(!Model.IsCalculationValid || !CommitFields())return;
            if(beam<=-1000)
            {
                int index=-1000-beam;var result=Model.IndependentBeamResults.ElementAtOrDefault(index);if(result==null)return;
                Drawing.Select(FormworkObject.Independent,index,false);BuildProperties();Refresh();
                if(Diagrams==null || diagramBeam!=beam)
                {
                    ClearDiagrams();var window=new TowerDiagramWindow(Model,result,$"I{index+1} · ") {Owner=DialogOwner};Diagrams=window;diagramBeam=beam;
                    window.Closed+=(sender,args)=> {if(ReferenceEquals(Diagrams,window)){Diagrams=null;diagramBeam=-1;Drawing.SelectMember(-1,-1,0);}};
                    window.PreviewKeyDown+=(sender,args)=> {if(args.Key==Key.Escape){HandleGeometryKey(args.Key,Keyboard.Modifiers);args.Handled=true;}};window.SelectMember(run,line,-1);window.Show();
                }
                else {Diagrams.SelectMember(run,line,-1);Diagrams.Activate();}
                Drawing.SelectMember(beam,run,line);return;
            }
            if(beam<0)
            {
                var support=Model.NetworkResult?.CrossSupports.SingleOrDefault(c=>c.CrossingIndex==-beam-1);if(support==null)return;
                Drawing.Select(FormworkObject.Crossing,support.CrossingIndex,false);BuildProperties();Refresh();
                if(Diagrams==null || diagramBeam!=beam)
                {
                    ClearDiagrams();var window=new TowerDiagramWindow(Model,support) {Owner=DialogOwner};Diagrams=window;diagramBeam=beam;
                    window.Closed+=(sender,args)=> {if(ReferenceEquals(Diagrams,window)){Diagrams=null;diagramBeam=-1;Drawing.SelectMember(-1,-1,0);Section.SelectMember(-1,-1,0);}};
                    window.PreviewKeyDown+=(sender,args)=> {if(args.Key==Key.Escape){HandleGeometryKey(args.Key,Keyboard.Modifiers);args.Handled=true;}};
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
                window.PreviewKeyDown+=(sender,args)=> {if(args.Key==Key.Escape){HandleGeometryKey(args.Key,Keyboard.Modifiers);args.Handled=true;}};
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



