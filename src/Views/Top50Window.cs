using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    /// <summary>Native WPF reconstruction of the user-recorded original V1; resources are local to this window.</summary>
    public sealed class Top50Window : Window
    {
        private static Brush Color(string color)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color));
        internal static readonly Brush Navy=Color("#152941"), MainBackground=Color("#f2f3f6"), Amber=Color("#d58b29"), Line=Color("#d6dde7"), Muted=Color("#66788e");
        public const string Disclaimer="Planning proposal only — engineering verification required for Doka assembly, connector geometry, walings, ties, plywood/filler backing, pour pressure, wall height, corners, column clearance, collisions, two-face configuration. Geometric fit is not structural approval. Casting groups are mathematical equipment allocations, not site-approved pour sequences.";
        private readonly Top50ViewModel model;
        private readonly ContentControl content=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        private readonly TextBlock heading=new TextBlock();
        private readonly TextBlock progress=new TextBlock();
        private readonly List<Button> navigation=new List<Button>();
        private readonly Button optimize,cancel,open,save,diagnose;
        private bool renderQueued,rendering;
        private int page,castingIndex,unitsChoice;
        public Top50ViewModel Model=>model;
        public int CurrentPage=>page;
        public Top50Window(Top50ViewModel model=null)
        {
            this.model=model??new Top50ViewModel();Title="Top50 Planner — Engineering Dashboard";Width=1500;Height=960;MinWidth=1000;MinHeight=680;Background=MainBackground;FontFamily=new FontFamily("Segoe UI");FontSize=13;Foreground=Navy;
            SetLocalStyles();
            var root=new Grid{Background=MainBackground};root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});root.ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar=new DockPanel{Background=Navy,LastChildFill=true};
            var brand=new StackPanel{Margin=new Thickness(20,34,12,0)};brand.Children.Add(Label("TOP 50\nPLANNER",21,Brushes.White,FontWeights.Bold));DockPanel.SetDock(brand,Dock.Top);sidebar.Children.Add(brand);
            var nav=new StackPanel{Margin=new Thickness(4,40,14,0)};
            string[] labels={"01   Import","02   Settings","03   Results","04   Casting groups","05   Reports"};
            for(int i=0;i<labels.Length;i++){int index=i;var b=new Button{Content=labels[i],Height=48,Margin=new Thickness(0,0,0,7),HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(18,0,0,0),BorderThickness=new Thickness(4,0,0,0),Foreground=Brushes.White,Background=Brushes.Transparent,BorderBrush=Brushes.Transparent};b.Click+=(s,e)=>ShowPage(index);navigation.Add(b);nav.Children.Add(b);}
            sidebar.Children.Add(nav);root.Children.Add(sidebar);
            var main=new Grid{Margin=new Thickness(28,24,16,12)};Grid.SetColumn(main,1);main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition());main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid{Margin=new Thickness(0,0,0,22)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var title=new StackPanel();heading.FontSize=28;heading.FontWeight=FontWeights.Bold;heading.TextWrapping=TextWrapping.Wrap;title.Children.Add(heading);header.Children.Add(title);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};Grid.SetColumn(actions,1);
            open=Action("Open project",OpenProject);save=Action("Save project",SaveProject);optimize=Action("Optimize",async()=>{try{await this.model.OptimizeAsync();if(this.model.Selected!=null)ShowPage(2);}catch(Exception e){Error(e);}},true);cancel=Action("Cancel",()=>this.model.Cancel());diagnose=Action("Diagnose",()=>new Top50DiagnosticWindow(this.model.BuildDiagnosticReport){Owner=this}.ShowDialog());
            System.Windows.Automation.AutomationProperties.SetAutomationId(diagnose,"Top50Diagnose");
            foreach(var button in new[]{open,save,optimize,cancel,diagnose}){button.Margin=new Thickness(12,0,0,0);actions.Children.Add(button);}header.Children.Add(actions);main.Children.Add(header);
            Grid.SetRow(content,1);main.Children.Add(content);
            progress.FontSize=11;progress.Foreground=Muted;progress.Visibility=Visibility.Collapsed;progress.Margin=new Thickness(0,10,0,0);Grid.SetRow(progress,2);main.Children.Add(progress);root.Children.Add(main);Content=root;
            this.model.PropertyChanged+=ModelChanged;Closing+=OnClosing;castingIndex=this.model.SelectedGroupIndex;ShowPage(this.model.CurrentPage);
        }
        private void SetLocalStyles()
        {
            var button=new Style(typeof(Button));button.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(14,7,14,7)));button.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.White));button.Setters.Add(new Setter(Control.BorderBrushProperty,Line));button.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));button.Setters.Add(new Setter(Control.ForegroundProperty,Navy));button.Setters.Add(new Setter(Control.FontSizeProperty,13d));button.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));button.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{x:Type Button}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Border x:Name='Shell' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' RecognizesAccessKey='True'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Shell' Property='Opacity' Value='0.45'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Shell' Property='Opacity' Value='0.85'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")));Resources[typeof(Button)]=button;
            var table=new Style(typeof(DataGrid));table.Setters.Add(new Setter(Control.FontSizeProperty,12d));table.Setters.Add(new Setter(Control.BorderBrushProperty,Line));table.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));table.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.White));table.Setters.Add(new Setter(DataGrid.RowBackgroundProperty,Brushes.White));table.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty,Color("#f5f6f9")));table.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty,Color("#e7eaf0")));table.Setters.Add(new Setter(DataGrid.VerticalGridLinesBrushProperty,Color("#e7eaf0")));table.Setters.Add(new Setter(DataGrid.RowHeightProperty,30d));table.Setters.Add(new Setter(DataGrid.ColumnHeaderHeightProperty,42d));Resources[typeof(DataGrid)]=table;
            var column=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));column.Setters.Add(new Setter(Control.BackgroundProperty,Color("#e7ebf1")));column.Setters.Add(new Setter(Control.ForegroundProperty,Navy));column.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));column.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,6,12,6)));column.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));Resources[typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)]=column;
            column.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Center));
            var cell=new Style(typeof(DataGridCell));cell.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,2,5,2)));cell.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));Resources[typeof(DataGridCell)]=cell;
        }
        private static TextBlock Label(string text,double size=13,Brush brush=null,FontWeight? weight=null,Thickness? margin=null)=>new TextBlock{Text=text,FontSize=size,Foreground=brush??Navy,FontWeight=weight??FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0)};
        private static Border Card(UIElement child,Thickness? padding=null)=>new Border{Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=padding??new Thickness(20),Child=child};
        private static Border Notice(string text)=>new Border{Background=Color("#fff2d4"),BorderBrush=Color("#ecd7a4"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(12,9,12,9),Child=Label(text,12,Color("#7d6741"))};
        private static GroupBox Group(string name,UIElement child)=>new GroupBox{Header=name,Content=child,Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),Padding=new Thickness(20,20,20,18),Margin=new Thickness(10,0,10,12)};
        private Button Action(string text,Action handler,bool primary=false){var b=new Button{Content=text};if(primary){b.Background=Amber;b.BorderBrush=Amber;b.FontWeight=FontWeights.SemiBold;}b.Click+=(s,e)=>handler();return b;}
        private static ScrollViewer Scroll(UIElement child)=>new ScrollViewer{Content=child,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        private static DataGrid Table(object items,params string[] columns)
        {
            var grid=new DataGrid{AutoGenerateColumns=false,ItemsSource=items as System.Collections.IEnumerable,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,EnableRowVirtualization=true};
            foreach(var entry in columns){var parts=entry.Split('|');grid.Columns.Add(new DataGridTextColumn{Header=parts[0],Binding=new Binding(parts[1]),Width=parts.Length>2&&parts[2]=="*"?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(parts.Length>2?double.Parse(parts[2],CultureInfo.InvariantCulture):DataGridLength.Auto.Value,parts.Length>2?DataGridLengthUnitType.Pixel:DataGridLengthUnitType.Auto)});}
            var flexible=grid.Columns.Where(c=>c.Width.IsStar).ToArray();
            grid.SizeChanged+=(s,e)=>{if(flexible.Length==0)return;double fixedWidth=grid.Columns.Except(flexible).Sum(c=>c.Width.IsAbsolute?c.Width.Value:Math.Max(c.ActualWidth,60));double share=Math.Max(180,(e.NewSize.Width-fixedWidth-20)/flexible.Length);foreach(var c in flexible)c.Width=new DataGridLength(share);};
            return grid;
        }
        public void ShowPage(int index)
        {
            page=Math.Max(0,Math.Min(4,index));model.CurrentPage=page;string[] names={"Import wall dimensions","Define your offering","Compare equipment proposals","Inspect casting groups","Export your planning report"};heading.Text=names[page];
            for(int i=0;i<navigation.Count;i++){navigation[i].Background=i==page?Color("#304b66"):Brushes.Transparent;navigation[i].BorderBrush=i==page?Color("#e6ac46"):Brushes.Transparent;}
            rendering=true;try{content.Content=page==0?ImportPage():page==1?SettingsPage():page==2?ResultsPage():page==3?CastingPage():ReportsPage();}finally{rendering=false;}
            UpdateBusy();
        }
        private UIElement ImportPage()
        {
            var root=new DockPanel{LastChildFill=true};var top=new StackPanel();
            var commands=new WrapPanel{Margin=new Thickness(0,0,0,6)};commands.Children.Add(Action("Import DXF…",ImportDxf));var manual=Action("Add manual wall",AddManual);manual.Margin=new Thickness(8,0,0,0);commands.Children.Add(manual);commands.Children.Add(Label("Units override:",13,null,null,new Thickness(8,8,5,0)));var units=new ComboBox{ItemsSource=new[]{"Use DXF INSUNITS","Reviewed millimetres","Reviewed metres"},SelectedIndex=unitsChoice,MinWidth=145,VerticalContentAlignment=VerticalAlignment.Center,ToolTip="Applies to the next DXF import after an explicit review reason."};units.SelectionChanged+=(s,e)=>{if(!rendering)unitsChoice=units.SelectedIndex;};commands.Children.Add(units);top.Children.Add(commands);
            DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
            var labels=new Top50WallLabels(model.Input?.Walls??Array.Empty<WallRow>());
            var rows=model.Input?.Walls.Select(w=>new ImportDisplay(w,labels[w.Id])).ToArray()??Array.Empty<ImportDisplay>();
            var table=Table(rows,"Wall|Id|245","Length mm (editable)|Length|160","DXF raw geometry|Raw|150","Stored measurement|Stored|155","Layer|Layer|350","Status / diagnostics|Diagnostics|*");
            var checkbox=new Style(typeof(CheckBox));checkbox.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left));checkbox.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));checkbox.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(6,0,0,0)));
            table.Columns.Insert(0,new DataGridCheckBoxColumn{Header="Include",Binding=new Binding(nameof(ImportDisplay.Included)){Mode=BindingMode.OneWay},Width=70,ElementStyle=checkbox});table.Columns.Add(new DataGridCheckBoxColumn{Header="Acknowledge",Binding=new Binding(nameof(ImportDisplay.Acknowledged)){Mode=BindingMode.OneWay},Width=110,ElementStyle=checkbox});
            table.MouseDoubleClick+=(s,e)=>{if(table.SelectedItem is ImportDisplay row)Review(row.Wall);};
            table.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Enter&&table.SelectedItem is ImportDisplay row){Review(row.Wall);e.Handled=true;}};
            table.ToolTip="Double-click a row or press Enter to review length, inclusion and an explicit reason.";root.Children.Add(table);return root;
        }
        private UIElement SettingsPage()
        {
            var stack=new StackPanel();var form=new StackPanel();
            var fixedInputs=new StackPanel();var percentageInputs=new StackPanel();
            System.Windows.Automation.AutomationProperties.SetAutomationId(fixedInputs,"Top50FixedMethodInputs");System.Windows.Automation.AutomationProperties.SetAutomationId(percentageInputs,"Top50PercentageMethodInputs");
            Action showMethod=()=>{fixedInputs.Visibility=model.Settings.OfferingMode==0?Visibility.Visible:Visibility.Collapsed;percentageInputs.Visibility=model.Settings.OfferingMode==1?Visibility.Visible:Visibility.Collapsed;};
            var method=new ComboBox{ItemsSource=new[]{"Fixed longest-wall sets","Percentage reference"},SelectedIndex=model.Settings.OfferingMode};System.Windows.Automation.AutomationProperties.SetAutomationId(method,"Top50OfferingMethod");method.SelectionChanged+=(s,e)=>{if(!rendering){model.Settings.OfferingMode=method.SelectedIndex;model.Changed();showMethod();}};form.Children.Add(FormRow("Method",method));
            fixedInputs.Children.Add(FormRow("Longest walls required simultaneously",Number(model.Settings.LongestCount,v=>model.Settings.LongestCount=(int)v)));
            var identical=new CheckBox{Content="Use identical panel sets",IsChecked=model.Settings.RequireIdenticalKits,ToolTip="Every set contains the same panel sizes and quantities. For example: 3 sets, each with two 4.5 m panels."};identical.Click+=(s,e)=>{model.Settings.RequireIdenticalKits=identical.IsChecked==true;model.Changed();};fixedInputs.Children.Add(identical);form.Children.Add(fixedInputs);
            percentageInputs.Children.Add(FormRow("Target stock / total wall length",Number(model.Settings.Percentage,v=>model.Settings.Percentage=v," %")));
            percentageInputs.Children.Add(FormRow("Search above / below target",Number(model.Settings.BandPoints,v=>model.Settings.BandPoints=v," percentage points")));
            var expansion=new CheckBox{Content="Allow search beyond this percentage range",IsChecked=model.Settings.AllowExpansion};expansion.Click+=(s,e)=>{model.Settings.AllowExpansion=expansion.IsChecked==true;model.Changed();};percentageInputs.Children.Add(expansion);form.Children.Add(percentageInputs);showMethod();
            var preset=new ComboBox{ItemsSource=new[]{"Economical Offer","Balanced Optimization","Fastest Construction"},SelectedIndex=model.Settings.Preset};preset.SelectionChanged+=(s,e)=>{if(!rendering){model.Settings.Preset=preset.SelectedIndex;model.Changed();}};form.Children.Add(FormRow("Planning preset",preset));stack.Children.Add(Group("Offering method",form));
            var panels=new UniformGridProxy(6);foreach(var size in Precision.CatalogMm){var check=new CheckBox{Content=(size/1000m).ToString("F2",CultureInfo.InvariantCulture)+" m",IsChecked=model.Settings.CatalogMm.Contains(size),Margin=new Thickness(0,3,0,3)};check.Click+=(s,e)=>{model.Settings.CatalogMm=Precision.CatalogMm.Where(n=>n==size?check.IsChecked==true:model.Settings.CatalogMm.Contains(n)).ToArray();model.Changed();};panels.Children.Add(check);}stack.Children.Add(Group("Permitted nominal panel lengths — user-supplied catalog",panels));
            var connector=new StackPanel();connector.Children.Add(Label("Default: one connector per joint, per wall face. Connectors are reused between casting groups.",12,Muted));
            var adjustable=new CheckBox{Content="Use spare universal connectors at joints without fillers",IsChecked=model.Settings.AdjustableOnDirect,Margin=new Thickness(0,6,0,0)};adjustable.Click+=(s,e)=>{model.Settings.AdjustableOnDirect=adjustable.IsChecked==true;model.Changed(false);};connector.Children.Add(adjustable);
            var quantities=new StackPanel();quantities.Children.Add(FormRow("Connectors per joint, per wall face",OptionalInteger(model.Settings.ConnectionLevels,value=>model.Settings.ConnectionLevels=value)));quantities.Children.Add(FormRow("Wall faces to include",OptionalInteger(model.Settings.ConnectionFaces,value=>model.Settings.ConnectionFaces=value)));
            var reviewed=new CheckBox{Content="Apply these custom quantities at every panel joint",IsChecked=model.Settings.ConnectionReviewed};reviewed.Click+=(s,e)=>{model.Settings.ConnectionReviewed=reviewed.IsChecked==true;model.Changed(false);};quantities.Children.Add(reviewed);
            connector.Children.Add(new Expander{Header="Custom connector quantities (optional)",Content=quantities,IsExpanded=false,Margin=new Thickness(0,6,0,0)});stack.Children.Add(Group("Connectors",connector));
            var advanced=new StackPanel();advanced.Children.Add(FormRow("Search effort",Number((decimal)model.Settings.SearchEffort,v=>model.Settings.SearchEffort=(double)v)));advanced.Children.Add(FormRow("Maximum panel components",Number(model.Settings.MaxComponents,v=>model.Settings.MaxComponents=(int)v)));advanced.Children.Add(FormRow("Maximum internal fillers",Number(model.Settings.MaxFillers,v=>model.Settings.MaxFillers=(int)v)));advanced.Children.Add(FormRow("Maximum filler mm",Number(model.Settings.MaxFillerMm,v=>model.Settings.MaxFillerMm=(int)v)));advanced.Children.Add(FormRow("Candidate display limit",Number(model.Settings.CandidateDisplayLimit,v=>model.Settings.CandidateDisplayLimit=(int)v)));advanced.Children.Add(Label("Same inputs and effort repeat the search. Higher effort may take longer. Retained-model bounds do not establish a global optimum.",12,Muted));stack.Children.Add(new Expander{Header="Advanced geometry and search",Content=advanced,IsExpanded=false,Background=Brushes.White,Margin=new Thickness(10,0,10,0),Padding=new Thickness(12)});return Scroll(stack);
        }
        private static Grid FormRow(string label,UIElement editor){var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(Label(label,13,null,null,new Thickness(0,5,10,5)));Grid.SetColumn(editor,1);row.Children.Add(editor);return row;}
        private TextBox Number(decimal initial,Action<decimal> apply,string suffix="")
        {
            var box=new TextBox{Text=initial.ToString(CultureInfo.InvariantCulture)+suffix,Padding=new Thickness(10,5,10,5),BorderBrush=Line,BorderThickness=new Thickness(1)};
            box.LostKeyboardFocus+=(s,e)=>{var text=box.Text.Replace(suffix.Length==0?"\0":suffix,"").Trim();if(decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out var value)){if(value!=initial){try{apply(value);}catch(OverflowException){box.BorderBrush=Brushes.Firebrick;Error("Value is outside the supported numeric range.");return;}initial=value;model.Changed();}box.ClearValue(Border.BorderBrushProperty);}else{box.BorderBrush=Brushes.Firebrick;Error("Enter a valid numeric value.");}};return box;
        }
        private TextBox OptionalInteger(int? initial,Action<int?> apply)
        {
            var box=new TextBox{Text=initial?.ToString(CultureInfo.InvariantCulture)??"Not supplied",Padding=new Thickness(10,5,10,5),BorderBrush=Line,BorderThickness=new Thickness(1)};
            box.LostKeyboardFocus+=(s,e)=>{int? value=null;if(!string.IsNullOrWhiteSpace(box.Text)&&box.Text!="Not supplied"){if(!int.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var parsed)||parsed<=0){box.BorderBrush=Brushes.Firebrick;Error("Supply a positive reviewed integer, or leave Not supplied.");return;}value=parsed;}if(value!=initial){apply(value);initial=value;model.Changed(false);}box.BorderBrush=Line;};return box;
        }
        private UIElement ResultsPage()
        {
            var selected=model.Selected;if(selected==null)return Notice("Optimize reviewed walls to compare equipment proposals.");
            var grid=new Grid();foreach(var h in new[]{new GridLength(110),GridLength.Auto,new GridLength(1,GridUnitType.Star),new GridLength(1,GridUnitType.Star),GridLength.Auto})grid.RowDefinitions.Add(new RowDefinition{Height=h});
            var kpis=new Grid();for(int i=0;i<4;i++)kpis.ColumnDefinitions.Add(new ColumnDefinition());string[] labels={"PANEL TYPES","OFFERED PANELS","OFFERED LENGTH","CASTING GROUPS"},values={selected.Stock.Count.ToString(),selected.PanelCount.ToString(),(selected.StockMicrometres/1000000m).ToString("F3",CultureInfo.InvariantCulture)+" m",selected.Groups.Count.ToString()};
            for(int i=0;i<4;i++){var stack=new StackPanel();stack.Children.Add(Label(labels[i],12,Muted));stack.Children.Add(Label(values[i],24,null,FontWeights.Bold,new Thickness(0,8,0,0)));var card=Card(stack,new Thickness(24));card.Margin=new Thickness(i==0?0:6,0,i==3?0:6,8);Grid.SetColumn(card,i);kpis.Children.Add(card);}grid.Children.Add(kpis);
            var offers=new Top50OfferPresentation(model.Input,model.Settings,model.Proposals,model.IsStale);
            var choices=model.Proposals.OrderBy(p=>p.Rank).Select(p=>new AlternativeChoice(p,offers)).ToArray();var selector=new ComboBox{ItemsSource=choices,DisplayMemberPath="Label",SelectedItem=choices.First(c=>c.Proposal==selected),Margin=new Thickness(130,0,0,6),Padding=new Thickness(8,5,8,5)};selector.SelectionChanged+=(s,e)=>{if(!rendering&&selector.SelectedItem is AlternativeChoice choice)model.Selected=choice.Proposal;};var selection=new Grid();selection.Children.Add(Label("Selected rank:",13,null,null,new Thickness(0,7,0,0)));selection.Children.Add(selector);Grid.SetRow(selection,1);grid.Children.Add(selection);
            var rows=model.Proposals.OrderBy(p=>p.Rank).Select(p=>new ComparisonDisplay(p,offers)).ToArray();var comparison=Table(rows,"Rank|Rank|75","Types|Types|60","Panels|Panels|70","Stock m|Stock|90","Casting Groups|Groups|120","Why this rank|Reason|*");comparison.Columns[0].SortMemberPath="RankNumber";comparison.RowHeight=double.NaN;comparison.MinRowHeight=30;var reasonStyle=new Style(typeof(TextBlock));reasonStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));reasonStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(4)));((DataGridTextColumn)comparison.Columns.Last()).ElementStyle=reasonStyle;comparison.Margin=new Thickness(0,6,0,6);comparison.SelectedItem=rows.FirstOrDefault(r=>r.Proposal==selected);comparison.SelectionChanged+=(s,e)=>{if(!rendering&&comparison.SelectedItem is ComparisonDisplay row)model.Selected=row.Proposal;};Grid.SetRow(comparison,2);grid.Children.Add(comparison);
            bool identicalSets=model.Settings.OfferingMode==0&&selected.IdenticalKitCount>1;
            var stockRows=selected.Stock.OrderBy(p=>p.Key).Select(p=>new{Length=(p.Key/1000m).ToString("F2",CultureInfo.InvariantCulture),Quantity=p.Value,Total=(p.Key*p.Value/1000m).ToString("F3",CultureInfo.InvariantCulture),Kit=identicalSets?(p.Value/selected.IdenticalKitCount).ToString():""}).ToArray();
            var stock=Table(stockRows,identicalSets?new[]{"Nominal length m|Length|140","Stock quantity|Quantity|140","Total m|Total|100","Panels in each identical set|Kit|*"}:new[]{"Nominal length m|Length|140","Stock quantity|Quantity|140","Total m|Total|*"});
            if(identicalSets)stock.ToolTip=selected.IdenticalKitCount+" identical sets. Each set contains the panel quantities shown in the last column.";Grid.SetRow(stock,3);grid.Children.Add(stock);
            var connectors=ConnectorResults(selected);Grid.SetRow(connectors,4);grid.Children.Add(connectors);return grid;
        }
        private UIElement ConnectorResults(Proposal proposal)
        {
            var allocation=ConnectorAllocation.Calculate(proposal,model.Settings);var stack=new StackPanel{Margin=new Thickness(0,10,0,0)};stack.Children.Add(Label("Reusable connector stock",14,null,FontWeights.SemiBold));
            if(!allocation.Determined){stack.Children.Add(Label("Set connectors per joint and wall faces in Settings to show quantities.",12,Muted,null,new Thickness(0,5,0,0)));return stack;}
            var table=Table(new[]{new{Type="Without fillers",Quantity=allocation.StandardStock.ToString("0",CultureInfo.InvariantCulture)},new{Type="Universal — with or without fillers",Quantity=allocation.AdjustableStock.ToString("0",CultureInfo.InvariantCulture)}},"Connector type|Type|*","Reusable quantity|Quantity|150");table.Height=105;table.Margin=new Thickness(0,6,0,0);stack.Children.Add(table);
            stack.Children.Add(Label((allocation.AssumedSingleLevel?"One connector per joint, per wall face. ":allocation.Levels+" connectors per joint × "+allocation.Faces+" wall faces. ")+"Stock is reused between casting groups.",11,Muted,null,new Thickness(0,4,0,0)));return stack;
        }
        private UIElement CastingPage()
        {
            var p=model.Selected;if(p==null)return Notice("Select a validated proposal to inspect casting groups.");
            var labels=new Top50WallLabels(model.Input.Walls);var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(365)});grid.RowDefinitions.Add(new RowDefinition());
            var rows=p.Groups.Select((g,i)=>new GroupDisplay(g,p,i,labels)).ToArray();var groups=Table(rows,"Casting Group|Id|120","Walls|Count|60","Installed m|Installed|100","Use %|Use|85","Direct locations|Direct|120","Filler locations|Fillers|120","Wall names|Ids|*");
            groups.RowHeight=double.NaN;groups.MinRowHeight=30;
            var wallStyle=new Style(typeof(TextBlock));wallStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));wallStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(5,4,5,4)));((DataGridTextColumn)groups.Columns.Last()).ElementStyle=wallStyle;
            castingIndex=Math.Min(castingIndex,rows.Length-1);groups.SelectedIndex=Math.Max(0,castingIndex);grid.Children.Add(groups);
            var diagrams=new ScrollViewer{Margin=new Thickness(0,12,0,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,BorderBrush=Line,BorderThickness=new Thickness(1)};Grid.SetRow(diagrams,1);grid.Children.Add(diagrams);
            Action update=()=>{if(groups.SelectedItem is GroupDisplay row){castingIndex=row.Index;model.SelectedGroupIndex=castingIndex;diagrams.Content=new Top50Diagram(row.Group,labels);}};groups.SelectionChanged+=(s,e)=>update();update();return grid;
        }
        private UIElement ReportsPage()
        {
            var root=new DockPanel{LastChildFill=true};var stack=new StackPanel();var p=model.Selected;stack.Children.Add(Group("One consistent workbook",Label(p==null?"Select a validated proposal first.":"Export selected rank "+Top50OfferPresentation.Ordinal(p.Rank)+": "+p.Groups.Count+" casting groups.\nOne workbook, nine worksheets. Source audit, precise arrangements, stock and idle quantities, connector locations/units, comparison and engineering checks.\n\n"+ConnectorAllocation.Calculate(p,model.Settings).Description+"\n\n"+Disclaimer,12)));
            var export=Action("Export selected solution to Excel…",Export,true);export.IsEnabled=p!=null&&!model.IsStale;export.HorizontalAlignment=HorizontalAlignment.Stretch;export.Margin=new Thickness(0,0,0,6);stack.Children.Add(export);DockPanel.SetDock(stack,Dock.Top);root.Children.Add(stack);root.Children.Add(Card(Label(string.Join("\n",Top50Workbook.SheetNames.Select((n,i)=>(i+1).ToString("00")+"  "+n)),13),new Thickness(10)));return root;
        }
        private void ImportDxf(){if(!ConfirmDiscard())return;var dialog=new OpenFileDialog{Title="Import DXF wall dimensions",Filter="DXF drawings (*.dxf)|*.dxf"};if(dialog.ShowDialog(this)==true)try{var options=new DxfImportOptions();if(unitsChoice!=0){string reason=AskText("Reviewed source units","Reason for the explicit units conversion","");if(reason==null)return;options.MmPerUnitOverride=unitsChoice==1?1:1000;options.OverrideReason=reason;}model.Import(dialog.FileName,options);ShowPage(0);}catch(Exception e){Error(e);}}
        private void AddManual(){string length=AskText("Add manual wall","Reviewed wall length in millimetres","");if(length==null)return;string reason=AskText("Manual wall source","Source / engineering review reason","");if(reason==null)return;try{model.AddManual(decimal.Parse(length,CultureInfo.InvariantCulture),reason);ShowPage(0);}catch(Exception e){Error(e);}}
        private string AskText(string title,string label,string initial){var dialog=new Window{Title=title,Owner=this,Width=570,Height=190,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};var stack=new StackPanel{Margin=new Thickness(20)};stack.Children.Add(Label(label));var text=new TextBox{Text=initial,Margin=new Thickness(0,10,0,10)};stack.Children.Add(text);var ok=new Button{Content="OK",IsDefault=true};ok.Click+=(s,e)=>dialog.DialogResult=true;stack.Children.Add(ok);dialog.Content=stack;return dialog.ShowDialog()==true?text.Text:null;}
        private void OpenProject(){if(!ConfirmDiscard())return;var dialog=new OpenFileDialog{Title="Open Top50 project",Filter="Top50 projects (*.top50)|*.top50"};if(dialog.ShowDialog(this)==true)try{model.Open(dialog.FileName,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DokaTop50Native","sources"));ShowPage(0);}catch(Exception e){Error(e);}}
        private void SaveProject(){var dialog=new SaveFileDialog{Title="Save Top50 project",Filter="Top50 projects (*.top50)|*.top50",FileName=model.ProjectPath??"Top50 project.top50"};if(dialog.ShowDialog(this)==true)try{model.Save(dialog.FileName);}catch(Exception e){Error(e);}}
        private void Export(){var dialog=new SaveFileDialog{Title="Export selected solution",Filter="Excel workbook (*.xlsx)|*.xlsx",FileName="Top50 planning report.xlsx"};if(dialog.ShowDialog(this)==true)try{model.Export(dialog.FileName);}catch(Exception e){Error(e);}}
        private bool ConfirmDiscard()=>!model.Dirty||MessageBox.Show(this,"Discard unsaved changes? Save the project first to keep them.","Top50 project",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK;
        private void Review(WallRow wall)
        {
            var dialog=new Window{Title="Review "+new Top50WallLabels(model.Input.Walls)[wall.Id],Owner=this,Width=650,Height=370,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};var stack=new StackPanel{Margin=new Thickness(20)};stack.Children.Add(Label(string.Join("\n",wall.Diagnostics)+"\nProjected raw mm: "+wall.ProjectedRawMm+"; aligned endpoint distance mm: "+wall.EndpointDistanceMm+"; stored: "+wall.StoredMeasurementMm,12,Muted));var length=new TextBox{Text=wall.LengthMm.ToString(CultureInfo.InvariantCulture),Margin=new Thickness(0,12,0,6)};stack.Children.Add(FormRow("Reviewed length mm",length));var include=new CheckBox{Content="Include this reviewed wall",IsChecked=wall.Included};stack.Children.Add(include);var reason=new TextBox{Margin=new Thickness(0,8,0,8)};stack.Children.Add(FormRow("Explicit review reason",reason));var ok=new Button{Content="Apply review",IsDefault=true};ok.Click+=(s,e)=>{try{wall.Review(decimal.Parse(length.Text,CultureInfo.InvariantCulture),include.IsChecked==true,reason.Text);dialog.DialogResult=true;}catch(Exception ex){model.RecordDiagnosticError("Review wall "+wall.Id,ex);MessageBox.Show(dialog,ex.Message);}};stack.Children.Add(ok);dialog.Content=stack;if(dialog.ShowDialog()==true){model.Changed();ShowPage(0);}
        }
        private void ModelChanged(object sender,PropertyChangedEventArgs e){if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>ModelChanged(sender,e)));return;}if(e.PropertyName==nameof(Top50ViewModel.Status)||e.PropertyName==nameof(Top50ViewModel.IsBusy))UpdateBusy();if(e.PropertyName==nameof(Top50ViewModel.Input)||e.PropertyName==nameof(Top50ViewModel.Proposals)||e.PropertyName==nameof(Top50ViewModel.Selected)||e.PropertyName==nameof(Top50ViewModel.Settings)){if(renderQueued)return;renderQueued=true;Dispatcher.BeginInvoke(new Action(()=>{renderQueued=false;ShowPage(page);}));}}
        private void UpdateBusy(){bool busy=model.IsBusy;cancel.IsEnabled=busy;optimize.IsEnabled=!busy;open.IsEnabled=!busy;save.IsEnabled=!busy;content.IsEnabled=!busy;progress.Text=model.Status;progress.Visibility=busy||model.IsStale?Visibility.Visible:Visibility.Collapsed;}
        private void OnClosing(object sender,CancelEventArgs e){if(model.IsBusy){model.Cancel();e.Cancel=true;return;}if(!ConfirmDiscard())e.Cancel=true;else model.PropertyChanged-=ModelChanged;}
        private void Error(Exception error){model.RecordDiagnosticError("Planner action",error);ShowError(error.Message);}
        private void Error(string message){model.RecordDiagnosticError("Edit planner settings",new ArgumentException(message));ShowError(message);}
        private void ShowError(string message)=>MessageBox.Show(this,message,"Top50 Planner",MessageBoxButton.OK,MessageBoxImage.Information);
        private sealed class UniformGridProxy : System.Windows.Controls.Primitives.UniformGrid{public UniformGridProxy(int columns){Columns=columns;}}
        private sealed class AlternativeChoice{public Proposal Proposal{get;}private readonly Top50OfferPresentation offers;public string Label=>offers.Rank(Proposal)+" • "+(Proposal.StockMicrometres/1000000m).ToString("F3",CultureInfo.InvariantCulture)+" m • "+Proposal.Groups.Count+" casting groups";public AlternativeChoice(Proposal proposal,Top50OfferPresentation offers){Proposal=proposal;this.offers=offers;}}
        private sealed class ImportDisplay
        {
            public WallRow Wall{get;}public string Id{get;}public bool Included=>Wall.Included;public bool Acknowledged=>Wall.Acknowledged;public string Length=>Wall.LengthMm.ToString("F3",CultureInfo.InvariantCulture);public string Raw=>Wall.ProjectedRawMm.ToString(CultureInfo.InvariantCulture);public string Stored=>Wall.StoredMeasurementMm?.ToString(CultureInfo.InvariantCulture)??"";public string Layer=>Wall.Layer;public string Diagnostics=>Wall.Status==WallReviewStatus.Valid?"Valid":string.Join("; ",Wall.Diagnostics);public ImportDisplay(WallRow wall,string label){Wall=wall;Id=label;}
        }
        private sealed class ComparisonDisplay
        {
            private readonly Top50OfferPresentation offers;public Proposal Proposal{get;}public int RankNumber=>offers.RankNumber(Proposal);public string Rank=>offers.Rank(Proposal);public int Types=>Proposal.Stock.Count;public int Panels=>Proposal.PanelCount;public string Stock=>(Proposal.StockMicrometres/1000000m).ToString("F3",CultureInfo.InvariantCulture);public int Groups=>Proposal.Groups.Count;public string Reason=>offers.Reason(Proposal);public ComparisonDisplay(Proposal proposal,Top50OfferPresentation offers){Proposal=proposal;this.offers=offers;}
        }
        private sealed class GroupDisplay
        {
            public CastingGroup Group{get;}public int Index{get;}private readonly Proposal proposal;public string Id=>Top50OfferPresentation.CastingGroup(Index);public int Count=>Group.Walls.Count;public string Installed=>(Group.InstalledPanelMicrometres/1000000m).ToString("F3",CultureInfo.InvariantCulture);public string Use=>((decimal)Group.InstalledPanelMicrometres/proposal.StockMicrometres*100).ToString("F2",CultureInfo.InvariantCulture);public int Direct=>Group.Walls.Sum(a=>a.Pattern.DirectLocations);public int Fillers=>Group.Walls.Sum(a=>a.Pattern.FillerLocations);private readonly Top50WallLabels labels;public string Ids=>string.Join(", ",Group.Walls.Select(a=>labels[a.WallId]));public GroupDisplay(CastingGroup group,Proposal proposal,int index,Top50WallLabels labels){Group=group;this.proposal=proposal;Index=index;this.labels=labels;}
        }
    }
    public sealed class Top50Diagram : FrameworkElement
    {
        private readonly CastingGroup group;
        private readonly Top50WallLabels labels;
        public Top50Diagram(CastingGroup group,Top50WallLabels labels){this.group=group;this.labels=labels;MinHeight=group.Walls.Count*72+20;HorizontalAlignment=HorizontalAlignment.Stretch;System.Windows.Automation.AutomationProperties.SetName(this,string.Join("; ",group.Walls.Select(w=>labels[w.WallId])));}
        protected override Size MeasureOverride(Size available)=>new Size(double.IsInfinity(available.Width)?900:available.Width,MinHeight);
        protected override void OnRender(DrawingContext dc)
        {
            double start=245,width=Math.Max(200,ActualWidth-start-70),maximum=group.Walls.Max(w=>w.Pattern.LengthMicrometres);double scale=width/maximum;double y=20;
            foreach(var wall in group.Walls)
            {
                DrawText(dc,labels[wall.WallId],12,Top50Window.Navy,12,y+4);double x=start;
                for(int i=0;i<wall.Pattern.PanelsMm.Count;i++)
                {
                    int size=wall.Pattern.PanelsMm[i];double segment=size*1000*scale;dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(86,125,151)),null,new Rect(x,y,segment,24));DrawText(dc,size.ToString(CultureInfo.InvariantCulture),12,Brushes.White,x+Math.Max(0,(segment-35)/2),y+4);x+=segment;
                    if(i<wall.Pattern.InternalGapsMicrometres.Count){double gap=wall.Pattern.InternalGapsMicrometres[i]*scale;if(gap>0)dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(224,165,64)),null,new Rect(x,y,gap,24));x+=gap;}
                }
                DrawText(dc,string.Join(" + ",wall.Pattern.PanelsMm)+" mm | "+string.Join(", ",wall.Pattern.InternalGapsMicrometres.Select((gap,index)=>new{gap,index}).Where(v=>v.gap>0).Select(v=>"filler after panel "+(v.index+1)+": "+(v.gap/1000m).ToString(CultureInfo.InvariantCulture)+" mm")),12,Top50Window.Muted,start,y+31);y+=72;
            }
        }
        private void DrawText(DrawingContext dc,string text,double size,Brush brush,double x,double y)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
    }
}
