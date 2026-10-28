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
        internal static readonly Brush Navy=Color("#202A33"), MainBackground=Color("#F5F5F5"), Amber=Color("#F2CD24"), Line=Color("#DFE3E6"), Muted=Color("#586772");
        public const string Disclaimer="Planning proposal only — engineering verification required for Doka assembly, connector geometry, walings, ties, plywood/filler backing, pour pressure, wall height, corners, column clearance, collisions, two-face configuration. Geometric fit is not structural approval. Casting groups are mathematical equipment allocations, not site-approved pour sequences.";
        private readonly Top50ViewModel model;
        private readonly ContentControl content=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        private readonly TextBlock progress=new TextBlock();
        private readonly List<Button> navigation=new List<Button>();
        private readonly Button optimize,cancel,undo,redo,delete;
        private DataGrid importTable;
        private bool alternativesExpanded;
        private readonly MenuItem open,save,diagnose;
        private bool renderQueued,rendering;
        private int page,castingIndex,unitsChoice;
        public Top50ViewModel Model=>model;
        public int CurrentPage=>page;
        public Top50Window(Top50ViewModel model=null)
        {
            this.model=model??new Top50ViewModel();Title="Top50 Planner";Width=1500;Height=960;MinWidth=1000;MinHeight=680;Background=MainBackground;FontFamily=new FontFamily("Segoe UI");FontSize=13;Foreground=Navy;
            SetLocalStyles();
            var root=new Grid{Background=MainBackground};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
            var toolbar=new DockPanel{Margin=new Thickness(8),LastChildFill=true};
            System.Windows.Automation.AutomationProperties.SetAutomationId(toolbar,"Top50Toolbar");
            optimize=Action("Optimize",async()=>{try{await this.model.OptimizeAsync();if(this.model.Selected!=null)ShowPage(2);}catch(Exception e){Error(e);}},true);cancel=Action("Cancel",()=>this.model.Cancel());
            var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
            foreach(var button in new[]{cancel,optimize}){button.Padding=new Thickness(10,5,10,5);button.Margin=new Thickness(3);button.MinHeight=30;actions.Children.Add(button);}
            DockPanel.SetDock(actions,Dock.Right);toolbar.Children.Add(actions);
            var commands=new StackPanel{Orientation=Orientation.Horizontal};
            var title=Label("Top50 Planner",18,Navy,FontWeights.SemiBold,new Thickness(3,0,10,0));title.VerticalAlignment=VerticalAlignment.Center;commands.Children.Add(title);
            var menus=new Menu{Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Center};
            var file=new MenuItem{Header="_File",Padding=new Thickness(10,6,10,6)};System.Windows.Automation.AutomationProperties.SetAutomationId(file,"Top50File");
            open=MenuCommand("Open project…",OpenProject,"Top50Open");save=MenuCommand("Save project…",SaveProject,"Top50Save");file.Items.Add(open);file.Items.Add(save);menus.Items.Add(file);
            var tools=new MenuItem{Header="_Tools",Padding=new Thickness(10,6,10,6)};System.Windows.Automation.AutomationProperties.SetAutomationId(tools,"Top50Tools");
            diagnose=MenuCommand("Diagnose…",()=>new Top50DiagnosticWindow(this.model.BuildDiagnosticReport){Owner=this}.ShowDialog(),"Top50Diagnose");tools.Items.Add(diagnose);menus.Items.Add(tools);commands.Children.Add(menus);
            undo=Action("Undo",()=>this.model.Undo());redo=Action("Redo",()=>this.model.Redo());delete=Action("Delete",DeleteSelected);
            foreach(var button in new[]{undo,redo,delete}){button.Padding=new Thickness(10,5,10,5);button.Margin=new Thickness(3);button.MinHeight=30;commands.Children.Add(button);System.Windows.Automation.AutomationProperties.SetAutomationId(button,"Top50"+button.Content);}
            undo.ToolTip="Undo (Ctrl+Z)";redo.ToolTip="Redo (Ctrl+Y)";delete.ToolTip="Delete selected walls (Delete)";
            PreviewKeyDown+=PlannerShortcut;toolbar.Children.Add(commands);root.Children.Add(toolbar);
            var body=new Grid{Margin=new Thickness(20,8,20,20)};body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetRow(body,1);root.Children.Add(body);
            var tabs=new UniformGridProxy(4){Margin=new Thickness(0,0,0,14)};string[] labels={"Import","Settings","Results","Casting groups"};
            for(int i=0;i<labels.Length;i++){int index=i;var button=new Button{Content=labels[i],MinHeight=40,BorderThickness=new Thickness(0,0,0,1)};button.SetResourceReference(StyleProperty,"ViewTab");System.Windows.Automation.AutomationProperties.SetAutomationId(button,"Top50Page"+i);button.Click+=(sender,args)=>ShowPage(index);navigation.Add(button);tabs.Children.Add(button);}body.Children.Add(tabs);
            Grid.SetRow(content,1);body.Children.Add(content);progress.FontSize=12;progress.Foreground=Muted;progress.Visibility=Visibility.Collapsed;progress.Margin=new Thickness(0,10,0,0);Grid.SetRow(progress,2);body.Children.Add(progress);
            Content=root;
            this.model.PropertyChanged+=ModelChanged;Closing+=OnClosing;castingIndex=this.model.SelectedGroupIndex;ShowPage(this.model.CurrentPage);
        }
        private void SetLocalStyles()
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/"+typeof(Top50Window).Assembly.GetName().Name+";component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            var table=new Style(typeof(DataGrid));table.Setters.Add(new Setter(Control.FontSizeProperty,12d));table.Setters.Add(new Setter(Control.BorderBrushProperty,Line));table.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));table.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.White));table.Setters.Add(new Setter(DataGrid.RowBackgroundProperty,Brushes.White));table.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty,Color("#FAFAFA")));table.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty,Line));table.Setters.Add(new Setter(DataGrid.VerticalGridLinesBrushProperty,Line));table.Setters.Add(new Setter(DataGrid.RowHeightProperty,30d));table.Setters.Add(new Setter(DataGrid.ColumnHeaderHeightProperty,42d));Resources[typeof(DataGrid)]=table;
            var column=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));column.Setters.Add(new Setter(Control.BackgroundProperty,Color("#ECEFF1")));column.Setters.Add(new Setter(Control.ForegroundProperty,Navy));column.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));column.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,6,12,6)));column.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));Resources[typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)]=column;
            column.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Center));
            var row=new Style(typeof(DataGridRow));var selectedRow=new Trigger{Property=DataGridRow.IsSelectedProperty,Value=true};selectedRow.Setters.Add(new Setter(Control.BackgroundProperty,Color("#FFF2BF")));selectedRow.Setters.Add(new Setter(Control.ForegroundProperty,Navy));row.Triggers.Add(selectedRow);Resources[typeof(DataGridRow)]=row;
            var cell=new Style(typeof(DataGridCell));cell.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,2,5,2)));cell.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));var selectedCell=new Trigger{Property=DataGridCell.IsSelectedProperty,Value=true};selectedCell.Setters.Add(new Setter(Control.BackgroundProperty,Color("#FFF2BF")));selectedCell.Setters.Add(new Setter(Control.ForegroundProperty,Navy));cell.Triggers.Add(selectedCell);Resources[typeof(DataGridCell)]=cell;
        }
        private static TextBlock Label(string text,double size=13,Brush brush=null,FontWeight? weight=null,Thickness? margin=null)=>new TextBlock{Text=text,FontSize=size,Foreground=brush??Navy,FontWeight=weight??FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0)};
        private static Border Card(UIElement child,Thickness? padding=null){var card=new Border{Child=child,Padding=padding??new Thickness(12)};card.SetResourceReference(StyleProperty,"Card");return card;}
        private static Border Notice(string text){var notice=new Border{Child=Label(text,12),Margin=new Thickness(0)};notice.SetResourceReference(StyleProperty,"Step");return notice;}
        private static Border Group(string name,UIElement child)
        {
            var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());var heading=new Border{Child=Label(name,13,null,FontWeights.SemiBold),Margin=new Thickness(0)};heading.SetResourceReference(StyleProperty,"Step");grid.Children.Add(heading);var body=Card(child);body.Margin=new Thickness(0);Grid.SetRow(body,1);grid.Children.Add(body);return new Border{Child=grid,Margin=new Thickness(0,0,0,12)};
        }
        private static MenuItem MenuCommand(string text,Action handler,string automationId)
        {var item=new MenuItem{Header=text};System.Windows.Automation.AutomationProperties.SetAutomationId(item,automationId);item.Click+=(sender,args)=>handler();return item;}
        private Button Action(string text,Action handler,bool primary=false){var button=new Button{Content=text};if(primary)button.SetResourceReference(StyleProperty,"PrimaryAction");button.Click+=(sender,args)=>handler();return button;}
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
            page=Math.Max(0,Math.Min(3,index));importTable=null;model.CurrentPage=page;
            for(int i=0;i<navigation.Count;i++){navigation[i].Background=i==page?Color("#FFF2BF"):Brushes.White;navigation[i].BorderBrush=i==page?Color("#D4AA00"):Line;navigation[i].BorderThickness=new Thickness(0,0,0,i==page?3:1);}
            rendering=true;try{content.Content=page==0?ImportPage():page==1?SettingsPage():page==2?ResultsPage():CastingPage();}finally{rendering=false;}
            UpdateBusy();
        }
        private UIElement ImportPage()
        {
            var root=new DockPanel{LastChildFill=true};var top=new StackPanel();
            var commands=new WrapPanel{Margin=new Thickness(0,0,0,6)};commands.Children.Add(Action("Import DXF…",ImportDxf));var manual=Action("Add manual wall",AddManual);manual.Margin=new Thickness(8,0,0,0);commands.Children.Add(manual);commands.Children.Add(Label("Units override:",13,null,null,new Thickness(8,8,5,0)));var units=new ComboBox{ItemsSource=new[]{"Use DXF INSUNITS","Reviewed millimetres","Reviewed metres"},SelectedIndex=unitsChoice,MinWidth=145,VerticalContentAlignment=VerticalAlignment.Center,ToolTip="Applies to the next DXF import after an explicit review reason."};units.SelectionChanged+=(s,e)=>{if(!rendering)unitsChoice=units.SelectedIndex;};commands.Children.Add(units);top.Children.Add(commands);
            DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
            var labels=new Top50WallLabels(model.Input?.AllWalls??Array.Empty<WallRow>());
            var rows=model.Input?.Walls.Select(w=>new ImportDisplay(w,labels[w.Id])).ToArray()??Array.Empty<ImportDisplay>();
            var table=Table(rows,"Wall|Id|245","Length (mm)|Length|160","Status|Diagnostics|*");
            importTable=table;table.SelectionMode=DataGridSelectionMode.Extended;table.SelectionChanged+=(sender,args)=>UpdateBusy();System.Windows.Automation.AutomationProperties.SetAutomationId(table,"Top50ImportWalls");
            var checkbox=new Style(typeof(CheckBox));checkbox.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left));checkbox.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));checkbox.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(6,0,0,0)));
            table.Columns.Insert(0,new DataGridCheckBoxColumn{Header="Include",Binding=new Binding(nameof(ImportDisplay.Included)){Mode=BindingMode.OneWay},Width=70,ElementStyle=checkbox});
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
            var method=new ComboBox{ItemsSource=new[]{"Fixed longest-wall sets","Percentage reference"},SelectedIndex=model.Settings.OfferingMode};System.Windows.Automation.AutomationProperties.SetAutomationId(method,"Top50OfferingMethod");method.SelectionChanged+=(s,e)=>{if(!rendering){model.EditSettings(settings=>settings.OfferingMode=method.SelectedIndex);showMethod();}};form.Children.Add(FormRow("Method",method));
            fixedInputs.Children.Add(FormRow("Longest walls required simultaneously",Number(model.Settings.LongestCount,v=>model.Settings.LongestCount=(int)v)));
            var identical=new CheckBox{Content="Use identical panel sets",IsChecked=model.Settings.RequireIdenticalKits,ToolTip="Every set contains the same panel sizes and quantities. For example: 3 sets, each with two 4.5 m panels."};identical.Click+=(s,e)=>{model.EditSettings(settings=>settings.RequireIdenticalKits=identical.IsChecked==true);};fixedInputs.Children.Add(identical);form.Children.Add(fixedInputs);
            percentageInputs.Children.Add(FormRow("Target stock / total wall length",Number(model.Settings.Percentage,v=>model.Settings.Percentage=v," %")));
            percentageInputs.Children.Add(FormRow("Search above / below target",Number(model.Settings.BandPoints,v=>model.Settings.BandPoints=v," percentage points")));
            var expansion=new CheckBox{Content="Allow search beyond this percentage range",IsChecked=model.Settings.AllowExpansion};expansion.Click+=(s,e)=>{model.EditSettings(settings=>settings.AllowExpansion=expansion.IsChecked==true);};percentageInputs.Children.Add(expansion);form.Children.Add(percentageInputs);showMethod();
            var preset=new ComboBox{ItemsSource=new[]{"Economical Offer","Balanced Optimization","Fastest Construction"},SelectedIndex=model.Settings.Preset};preset.SelectionChanged+=(s,e)=>{if(!rendering){model.EditSettings(settings=>settings.Preset=preset.SelectedIndex);}};form.Children.Add(FormRow("Planning preset",preset));stack.Children.Add(Group("Offering method",form));
            var panels=new UniformGridProxy(6);foreach(var size in Precision.CatalogMm){var check=new CheckBox{Content=(size/1000m).ToString("F2",CultureInfo.InvariantCulture)+" m",IsChecked=model.Settings.CatalogMm.Contains(size),Margin=new Thickness(0,3,0,3)};check.Click+=(s,e)=>{model.EditSettings(settings=>settings.CatalogMm=Precision.CatalogMm.Where(n=>n==size?check.IsChecked==true:settings.CatalogMm.Contains(n)).ToArray());};panels.Children.Add(check);}stack.Children.Add(Group("Permitted nominal panel lengths — user-supplied catalog",panels));
            var fillers=new StackPanel();fillers.Children.Add(FormRow("Maximum internal fillers per wall",Number(model.Settings.MaxFillers,v=>model.Settings.MaxFillers=(int)v)));fillers.Children.Add(FormRow("Maximum filler width (mm)",Number(model.Settings.MaxFillerMm,v=>model.Settings.MaxFillerMm=(int)v)));stack.Children.Add(Group("Filler settings",fillers));return Scroll(stack);
        }
        private static Grid FormRow(string label,UIElement editor){var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(Label(label,13,null,null,new Thickness(0,5,10,5)));Grid.SetColumn(editor,1);row.Children.Add(editor);return row;}
        private TextBox Number(decimal initial,Action<decimal> apply,string suffix="")
        {
            var box=new TextBox{Text=initial.ToString(CultureInfo.InvariantCulture)+suffix,Padding=new Thickness(10,5,10,5),BorderBrush=Line,BorderThickness=new Thickness(1)};
            box.LostKeyboardFocus+=(s,e)=>{var text=box.Text.Replace(suffix.Length==0?"\0":suffix,"").Trim();if(decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out var value)){if(value!=initial){try{model.EditSettings(settings=>apply(value));}catch(OverflowException){box.BorderBrush=Brushes.Firebrick;Error("Value is outside the supported numeric range.");return;}initial=value;}box.ClearValue(Border.BorderBrushProperty);}else{box.BorderBrush=Brushes.Firebrick;Error("Enter a valid numeric value.");}};return box;
        }
        private UIElement ResultsPage()
        {
            var selected=model.Selected;if(selected==null)return Notice("Optimize reviewed walls to compare equipment proposals.");
            var root=new StackPanel();var kpis=new Grid{Height=76};for(int i=0;i<4;i++)kpis.ColumnDefinitions.Add(new ColumnDefinition());
            string[] captions={"Panel types","Panels — both faces","Offered length — per face","Casting groups"},values={selected.Stock.Count.ToString(),Top50PlannerPolicy.TotalPanels(selected).ToString(),Top50PlannerPolicy.OfferedLmPerFace(selected).ToString("0.00",CultureInfo.InvariantCulture)+" LM",selected.Groups.Count.ToString()};
            for(int i=0;i<4;i++){var stack=new StackPanel();stack.Children.Add(Label(captions[i],12,Muted));stack.Children.Add(Label(values[i],22,null,FontWeights.SemiBold,new Thickness(0,4,0,0)));var card=Card(stack,new Thickness(12));card.Margin=new Thickness(i==0?0:6,0,i==3?0:6,0);Grid.SetColumn(card,i);kpis.Children.Add(card);}root.Children.Add(kpis);
            bool identicalSets=model.Settings.OfferingMode==0&&selected.IdenticalKitCount>1;
            var stockRows=Top50PlannerPolicy.Inventory(selected);var columns=new List<string>{"Panel length (m)|Length|*","Qty|Quantity|75"};if(identicalSets)columns.Add("Qty per set (both faces)|QuantityPerSet|170");
            var stock=Table(stockRows,columns.ToArray());stock.ColumnHeaderHeight=30;stock.RowHeight=30;stock.Height=Math.Min(360,stockRows.Count*30+32);System.Windows.Automation.AutomationProperties.SetAutomationId(stock,"Top50PanelInventory");
            Emphasize(stock,0,false);Emphasize(stock,1,true);if(identicalSets)stock.ToolTip=selected.IdenticalKitCount+" identical sets; quantities include both faces.";
            var inventoryBody=new StackPanel();inventoryBody.Children.Add(stock);inventoryBody.Children.Add(Label("Total panels: "+Top50PlannerPolicy.TotalPanels(selected),13,null,FontWeights.SemiBold,new Thickness(0,8,0,0)));
            var schedules=new Grid{Margin=new Thickness(0,14,0,14)};schedules.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});schedules.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.45,GridUnitType.Star)});
            var inventory=Group("Panel inventory — both faces",inventoryBody);inventory.Margin=new Thickness(0,0,12,0);inventory.VerticalAlignment=VerticalAlignment.Top;schedules.Children.Add(inventory);
            var connectors=ConnectorResults(selected);connectors.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(connectors,1);schedules.Children.Add(connectors);root.Children.Add(schedules);
            var offers=new Top50OfferPresentation(model.Input,model.Settings,model.Proposals,model.IsStale);var rows=model.Proposals.OrderBy(p=>p.Rank).Select(p=>new ComparisonDisplay(p,offers)).ToArray();
            var comparison=Table(rows,"Rank|Rank|65","Types|Types|55","Panels (both faces)|Panels|115","LM per face|Stock|95","Casting groups|Groups|105","Status|Reason|*");comparison.Columns[0].SortMemberPath="RankNumber";comparison.ColumnHeaderHeight=34;comparison.RowHeight=double.NaN;comparison.MinRowHeight=42;comparison.Height=180;
            var reasonStyle=new Style(typeof(TextBlock));reasonStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));reasonStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(5)));((DataGridTextColumn)comparison.Columns.Last()).ElementStyle=reasonStyle;
            comparison.SelectedItem=rows.FirstOrDefault(r=>r.Proposal==selected);comparison.SelectionChanged+=(sender,args)=>{if(!rendering&&comparison.SelectedItem is ComparisonDisplay row)model.Selected=row.Proposal;};
            var alternatives=new Expander{Header="Compare alternatives · "+rows.Length+" ranked option"+(rows.Length==1?"":"s")+" · "+offers.Rank(selected)+" selected",Content=comparison,IsExpanded=alternativesExpanded,Padding=new Thickness(10),Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1)};
            System.Windows.Automation.AutomationProperties.SetAutomationId(alternatives,"Top50Alternatives");alternatives.Expanded+=(sender,args)=>alternativesExpanded=true;alternatives.Collapsed+=(sender,args)=>alternativesExpanded=false;root.Children.Add(alternatives);return Scroll(root);
        }
        private static void Emphasize(DataGrid table,int index,bool quantity)
        {
            var text=new Style(typeof(TextBlock));text.Setters.Add(new Setter(TextBlock.FontWeightProperty,FontWeights.SemiBold));text.Setters.Add(new Setter(TextBlock.FontSizeProperty,quantity?15d:14d));text.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));text.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(6,2,6,2)));
            if(quantity){text.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Center));var cell=new Style(typeof(DataGridCell));cell.Setters.Add(new Setter(Control.BackgroundProperty,Color("#FFF8DD")));cell.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));table.Columns[index].CellStyle=cell;}
            ((DataGridTextColumn)table.Columns[index]).ElementStyle=text;
        }
        private Border ConnectorResults(Proposal proposal)
        {
            var allocation=ConnectorAllocation.Calculate(proposal);
            var table=Table(new[]{new{Type=ConnectorEquipment.DirectName,Article=ConnectorEquipment.DirectArticle,Quantity=allocation.StandardStock.ToString("0",CultureInfo.InvariantCulture)},new{Type=ConnectorEquipment.FillerName,Article=ConnectorEquipment.FillerArticle,Quantity=allocation.AdjustableStock.ToString("0",CultureInfo.InvariantCulture)}},"Equipment|Type|*","Art. no.|Article|85","Qty|Quantity|65");
            table.RowHeight=40;table.ColumnHeaderHeight=30;table.Height=112;System.Windows.Automation.AutomationProperties.SetAutomationId(table,"Top50ConnectorInventory");
            var names=new Style(typeof(TextBlock));names.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));names.Setters.Add(new Setter(TextBlock.FontWeightProperty,FontWeights.SemiBold));names.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));names.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(4)));((DataGridTextColumn)table.Columns[0]).ElementStyle=names;
            var articles=new Style(typeof(TextBlock));articles.Setters.Add(new Setter(TextBlock.ForegroundProperty,Muted));articles.Setters.Add(new Setter(TextBlock.FontSizeProperty,12d));articles.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));((DataGridTextColumn)table.Columns[1]).ElementStyle=articles;Emphasize(table,2,true);
            var section=Group("Connectors per level — both faces",table);section.Margin=new Thickness(0);return section;
        }
        private UIElement CastingPage()
        {
            var proposal=model.Selected;if(proposal==null)return Notice("Select a validated proposal to inspect casting groups.");
            var labels=new Top50WallLabels(model.Input.AllWalls);var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());
            var rows=proposal.Groups.Select((group,index)=>new GroupDisplay(group,proposal,index,labels)).ToArray();var header=new WrapPanel{Margin=new Thickness(0,0,0,8)};
            header.Children.Add(Label("Casting group:",13,null,null,new Thickness(0,9,10,0)));var groups=new ComboBox{ItemsSource=rows,DisplayMemberPath="Choice",Width=260,MinHeight=30};System.Windows.Automation.AutomationProperties.SetAutomationId(groups,"Top50CastingGroupSelector");header.Children.Add(groups);header.Children.Add(Label("Same arrangement on both faces",12,Muted,null,new Thickness(12,9,0,0)));grid.Children.Add(header);
            var viewport=new Grid{Background=Brushes.White};viewport.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});viewport.RowDefinitions.Add(new RowDefinition());var legend=new DockPanel{Margin=new Thickness(12,8,12,0)};var key=Label("■ Panel    ▨ Filler",12,Muted);DockPanel.SetDock(key,Dock.Right);legend.Children.Add(key);legend.Children.Add(Label("Panel arrangement · dimensions in mm",12,Muted));viewport.Children.Add(legend);
            var diagrams=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(diagrams,1);viewport.Children.Add(diagrams);var frame=new Border{Child=viewport,BorderBrush=Line,BorderThickness=new Thickness(1)};Grid.SetRow(frame,1);grid.Children.Add(frame);
            groups.SelectionChanged+=(sender,args)=>{if(groups.SelectedItem is GroupDisplay row){castingIndex=row.Index;model.SelectedGroupIndex=castingIndex;diagrams.Content=new Top50Diagram(row.Group,labels);}};groups.SelectedIndex=Math.Max(0,Math.Min(castingIndex,rows.Length-1));return grid;
        }
        private void DeleteSelected()
        {if(model.IsBusy||page!=0||importTable==null)return;var ids=importTable.SelectedItems.OfType<ImportDisplay>().Select(row=>row.Wall.Id).ToArray();if(ids.Length>0)model.DeleteWalls(ids);}
        private void PlannerShortcut(object sender,KeyEventArgs args)
        {if(TryEditShortcut(args.Key,Keyboard.Modifiers,Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase))args.Handled=true;}
        public bool TryEditShortcut(Key key,ModifierKeys modifiers,bool textEditing)
        {
            if(model.IsBusy||textEditing)return false;
            if(modifiers==ModifierKeys.Control&&key==Key.Z&&model.CanUndo){model.Undo();return true;}
            if(modifiers==ModifierKeys.Control&&key==Key.Y&&model.CanRedo){model.Redo();return true;}
            if(modifiers==ModifierKeys.None&&key==Key.Delete&&page==0&&importTable?.SelectedItems.Count>0){DeleteSelected();return true;}
            return false;
        }
        private void ImportDxf(){if(!ConfirmDiscard())return;var dialog=new OpenFileDialog{Title="Import DXF wall dimensions",Filter="DXF drawings (*.dxf)|*.dxf"};if(dialog.ShowDialog(this)==true)try{var options=new DxfImportOptions();if(unitsChoice!=0){string reason=AskText("Reviewed source units","Reason for the explicit units conversion","");if(reason==null)return;options.MmPerUnitOverride=unitsChoice==1?1:1000;options.OverrideReason=reason;}model.Import(dialog.FileName,options);ShowPage(0);}catch(Exception e){Error(e);}}
        private void AddManual(){string length=AskText("Add manual wall","Reviewed wall length in millimetres","");if(length==null)return;string reason=AskText("Manual wall source","Source / engineering review reason","");if(reason==null)return;try{model.AddManual(decimal.Parse(length,CultureInfo.InvariantCulture),reason);ShowPage(0);}catch(Exception e){Error(e);}}
        private string AskText(string title,string label,string initial){var dialog=new Window{Title=title,Owner=this,Width=570,Height=190,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};var stack=new StackPanel{Margin=new Thickness(20)};stack.Children.Add(Label(label));var text=new TextBox{Text=initial,Margin=new Thickness(0,10,0,10)};stack.Children.Add(text);var ok=new Button{Content="OK",IsDefault=true};ok.Click+=(s,e)=>dialog.DialogResult=true;stack.Children.Add(ok);dialog.Content=stack;return dialog.ShowDialog()==true?text.Text:null;}
        private void OpenProject(){if(!ConfirmDiscard())return;var dialog=new OpenFileDialog{Title="Open Top50 project",Filter="Top50 projects (*.top50)|*.top50"};if(dialog.ShowDialog(this)==true)try{model.Open(dialog.FileName,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DokaTop50Native","sources"));ShowPage(0);}catch(Exception e){Error(e);}}
        private void SaveProject(){var dialog=new SaveFileDialog{Title="Save Top50 project",Filter="Top50 projects (*.top50)|*.top50",FileName=model.ProjectPath??"Top50 project.top50"};if(dialog.ShowDialog(this)==true)try{model.Save(dialog.FileName);}catch(Exception e){Error(e);}}
        private bool ConfirmDiscard()=>!model.Dirty||MessageBox.Show(this,"Discard unsaved changes? Save the project first to keep them.","Top50 project",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK;
        private void Review(WallRow wall)
        {
            var dialog=new Window{Title="Review "+new Top50WallLabels(model.Input.AllWalls)[wall.Id],Owner=this,Width=650,Height=370,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};var stack=new StackPanel{Margin=new Thickness(20)};stack.Children.Add(Label(string.Join("\n",wall.Diagnostics)+"\nProjected raw mm: "+wall.ProjectedRawMm+"; aligned endpoint distance mm: "+wall.EndpointDistanceMm+"; stored: "+wall.StoredMeasurementMm,12,Muted));var length=new TextBox{Text=wall.LengthMm.ToString(CultureInfo.InvariantCulture),Margin=new Thickness(0,12,0,6)};stack.Children.Add(FormRow("Reviewed length mm",length));var include=new CheckBox{Content="Include this reviewed wall",IsChecked=wall.Included};stack.Children.Add(include);var reason=new TextBox{Margin=new Thickness(0,8,0,8)};stack.Children.Add(FormRow("Explicit review reason",reason));var ok=new Button{Content="Apply review",IsDefault=true};ok.Click+=(s,e)=>{try{model.ReviewWall(wall.Id,decimal.Parse(length.Text,CultureInfo.InvariantCulture),include.IsChecked==true,reason.Text);dialog.DialogResult=true;}catch(Exception ex){model.RecordDiagnosticError("Review wall "+wall.Id,ex);MessageBox.Show(dialog,ex.Message);}};stack.Children.Add(ok);dialog.Content=stack;if(dialog.ShowDialog()==true){ShowPage(0);}
        }
        private void ModelChanged(object sender,PropertyChangedEventArgs e){if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>ModelChanged(sender,e)));return;}if(e.PropertyName==nameof(Top50ViewModel.Status)||e.PropertyName==nameof(Top50ViewModel.IsBusy)||e.PropertyName==nameof(Top50ViewModel.CanUndo)||e.PropertyName==nameof(Top50ViewModel.CanRedo))UpdateBusy();if(e.PropertyName==nameof(Top50ViewModel.Input)||e.PropertyName==nameof(Top50ViewModel.Proposals)||e.PropertyName==nameof(Top50ViewModel.Selected)||e.PropertyName==nameof(Top50ViewModel.Settings)){if(renderQueued)return;renderQueued=true;Dispatcher.BeginInvoke(new Action(()=>{renderQueued=false;ShowPage(page);}));}}
        private void UpdateBusy(){bool busy=model.IsBusy;cancel.IsEnabled=busy;cancel.Visibility=busy?Visibility.Visible:Visibility.Collapsed;optimize.IsEnabled=!busy;open.IsEnabled=!busy;save.IsEnabled=!busy;content.IsEnabled=!busy;undo.IsEnabled=model.CanUndo;redo.IsEnabled=model.CanRedo;delete.IsEnabled=!busy&&page==0&&importTable?.SelectedItems.Count>0;progress.Text=model.Status;progress.Visibility=busy||model.IsStale?Visibility.Visible:Visibility.Collapsed;}
        private void OnClosing(object sender,CancelEventArgs e){if(model.IsBusy){model.Cancel();e.Cancel=true;return;}if(!ConfirmDiscard())e.Cancel=true;else model.PropertyChanged-=ModelChanged;}
        private void Error(Exception error){model.RecordDiagnosticError("Planner action",error);ShowError(error.Message);}
        private void Error(string message){model.RecordDiagnosticError("Edit planner settings",new ArgumentException(message));ShowError(message);}
        private void ShowError(string message)=>MessageBox.Show(this,message,"Top50 Planner",MessageBoxButton.OK,MessageBoxImage.Information);
        private sealed class UniformGridProxy : System.Windows.Controls.Primitives.UniformGrid{public UniformGridProxy(int columns){Columns=columns;}}
        private sealed class ImportDisplay
        {
            public WallRow Wall{get;}public string Id{get;}public bool Included=>Wall.Included;public bool Acknowledged=>Wall.Acknowledged;public string Length=>Wall.LengthMm.ToString("F3",CultureInfo.InvariantCulture);public string Raw=>Wall.ProjectedRawMm.ToString(CultureInfo.InvariantCulture);public string Stored=>Wall.StoredMeasurementMm?.ToString(CultureInfo.InvariantCulture)??"";public string Layer=>Wall.Layer;public string Diagnostics=>Wall.Status==WallReviewStatus.Valid?"Valid":string.Join("; ",Wall.Diagnostics);public ImportDisplay(WallRow wall,string label){Wall=wall;Id=label;}
        }
        private sealed class ComparisonDisplay
        {
            private readonly Top50OfferPresentation offers;public Proposal Proposal{get;}public int RankNumber=>offers.RankNumber(Proposal);public string Rank=>offers.Rank(Proposal);public int Types=>Proposal.Stock.Count;public int Panels=>Top50PlannerPolicy.TotalPanels(Proposal);public string Stock=>(Proposal.StockMicrometres/1000000m).ToString("F3",CultureInfo.InvariantCulture);public int Groups=>Proposal.Groups.Count;public string Reason=>offers.Reason(Proposal);public ComparisonDisplay(Proposal proposal,Top50OfferPresentation offers){Proposal=proposal;this.offers=offers;}
        }
        private sealed class GroupDisplay
        {
            public CastingGroup Group{get;}public int Index{get;}private readonly Proposal proposal;public string Id=>Top50OfferPresentation.CastingGroup(Index);public string Choice=>Id+" · "+Count+" wall"+(Count==1?"":"s");public int Count=>Group.Walls.Count;public string Installed=>(Group.InstalledPanelMicrometres/1000000m).ToString("F3",CultureInfo.InvariantCulture);public string Use=>((decimal)Group.InstalledPanelMicrometres/proposal.StockMicrometres*100).ToString("F2",CultureInfo.InvariantCulture);public int Direct=>Group.Walls.Sum(a=>a.Pattern.DirectLocations);public int Fillers=>Group.Walls.Sum(a=>a.Pattern.FillerLocations);private readonly Top50WallLabels labels;public string Ids=>string.Join(", ",Group.Walls.Select(a=>labels[a.WallId]));public GroupDisplay(CastingGroup group,Proposal proposal,int index,Top50WallLabels labels){Group=group;this.proposal=proposal;Index=index;this.labels=labels;}
        }
    }
    public sealed class Top50Diagram : FrameworkElement
    {
        private readonly CastingGroup group;
        private readonly Top50WallLabels labels;
        public Top50Diagram(CastingGroup group,Top50WallLabels labels)
        {
            this.group=group;this.labels=labels;MinHeight=group.Walls.Sum(w=>Math.Max(80,48+w.Pattern.FillerLocations*17))+20;
            long longest=group.Walls.Max(w=>w.Pattern.LengthMicrometres);int shortest=group.Walls.SelectMany(w=>w.Pattern.PanelsMm).Min();
            MinWidth=Math.Max(900,315+longest/(shortest*1000d)*30);HorizontalAlignment=HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(this,"Same arrangement on both faces. "+string.Join("; ",group.Walls.Select(w=>labels[w.WallId])));
        }
        protected override Size MeasureOverride(Size available)=>new Size(double.IsInfinity(available.Width)?MinWidth:Math.Max(MinWidth,available.Width),MinHeight);
        protected override void OnRender(DrawingContext dc)
        {
            double start=245,width=Math.Max(200,ActualWidth-start-70),maximum=group.Walls.Max(w=>w.Pattern.LengthMicrometres),scale=width/maximum,y=20;
            foreach(var wall in group.Walls)
            {
                DrawText(dc,labels[wall.WallId],12,Top50Window.Navy,12,y+4);double x=start;int fillerIndex=0;
                for(int i=0;i<wall.Pattern.PanelsMm.Count;i++)
                {
                    int size=wall.Pattern.PanelsMm[i];double segment=size*1000*scale;dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(86,125,151)),new Pen(Brushes.White,.5),new Rect(x,y,segment,26));
                    var number=size.ToString(CultureInfo.InvariantCulture);DrawText(dc,number,11,Brushes.White,x+Math.Max(2,(segment-number.Length*6)/2),y+5);x+=segment;
                    if(i<wall.Pattern.InternalGapsMicrometres.Count)
                    {
                        long gapMm=wall.Pattern.InternalGapsMicrometres[i];double gap=gapMm*scale;
                        if(gap>0)
                        {
                            dc.DrawRectangle(Top50Window.Amber,null,new Rect(x,y,gap,26));double anchor=x+gap/2,baseline=y+34+fillerIndex*17;
                            dc.DrawLine(new Pen(Top50Window.Navy,.7),new Point(anchor,y+23),new Point(anchor,baseline));
                            string text="Filler "+(gapMm/1000m).ToString("0.###",CultureInfo.InvariantCulture)+" mm";double labelX=Math.Min(anchor+4,ActualWidth-text.Length*6.3-8);DrawText(dc,text,11,Top50Window.Navy,Math.Max(start,labelX),baseline-1);fillerIndex++;
                        }
                        x+=gap;
                    }
                }
                y+=Math.Max(80,48+wall.Pattern.FillerLocations*17);
            }
        }
        private void DrawText(DrawingContext dc,string text,double size,Brush brush,double x,double y)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
    }
}
