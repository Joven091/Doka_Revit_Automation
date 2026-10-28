using System;
using System.Collections.Generic;
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
        private readonly Dictionary<string,bool> settingsExpanded=new Dictionary<string,bool>();
        private readonly ComboBox dimensionMode=new ComboBox {Width=105,ItemsSource=new[] {"None","Selected","All"},SelectedIndex=1,ToolTip="Dimensions to display"};
        private readonly StackPanel sectionControls=new StackPanel {Orientation=Orientation.Horizontal,Visibility=Visibility.Collapsed};
        internal readonly Slider SectionPosition=new Slider {Width=150,Minimum=0,TickFrequency=25,IsSnapToTickEnabled=true,VerticalAlignment=VerticalAlignment.Center};
        internal readonly TextBox SectionPositionInput=new TextBox {Width=80,ToolTip="Section A–A position from beam start (mm)"};
        internal readonly StackPanel GoverningMembers=new StackPanel {Margin=new Thickness(0,10,0,0)};
        private MenuItem addMenu;
        private TransferNetworkResult governingResult;
        private bool syncingSection;
        internal readonly ComboBox RevitPartSelector=new ComboBox {Width=225,Height=30,VerticalAlignment=VerticalAlignment.Center,DisplayMemberPath="Label",SelectedValuePath="Key",Margin=new Thickness(0,0,12,0),Visibility=Visibility.Collapsed,ToolTip="Active part for editing and section diagrams; the canvas always shows all parts."};
        private bool syncingRevitPart;
        private void RefreshCombinedRevitControls()
        {
            syncingRevitPart=true;
            try {RevitPartSelector.Visibility=Model.HasCombinedRevitDrawing ? Visibility.Visible : Visibility.Collapsed;RevitPartSelector.IsEnabled=!Model.IsOptimizing;
                RevitPartSelector.ItemsSource=Model.RevitDrawings;RevitPartSelector.SelectedValue=Model.ActiveRevitDrawingKey;
                ((Button)Buttons["Calculate"]).Content=Model.HasCombinedRevitDrawing ? "Calculate all" : "Calculate";}
            finally {syncingRevitPart=false;}
        }

        private FrameworkElement BuildToolbar(Action changeSystem)
        {
            var toolbar=new DockPanel {Margin=new Thickness(8),LastChildFill=true};
            var actions=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};
            actions.Children.Add(Button("Cancel","Cancel",()=> {Model.CancelOptimization();Refresh();}));
            var calculate=Button("Calculate","Calculate",async()=>await CalculateAsync());calculate.SetResourceReference(StyleProperty,"PrimaryAction");actions.Children.Add(calculate);
            DockPanel.SetDock(actions,Dock.Right);toolbar.Children.Add(actions);
            var edit=new WrapPanel();
            var menus=new Menu {Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Center};
            var system=new MenuItem {Header="D3 shoring ▾",FontSize=18,FontWeight=FontWeights.SemiBold};
            if(changeSystem!=null)system.Items.Add(MenuAction("ChangeSystem","Change system…",changeSystem));
            menus.Items.Add(system);
            var file=new MenuItem {Header="_File",Padding=new Thickness(10,6,10,6)};menus.Items.Add(file);
            file.Items.Add(MenuAction("NewSession","New session",StartNewSession));file.Items.Add(new Separator());
            file.Items.Add(MenuAction("Load","Open inputs…",()=> {if(!CommitFields())return;var old=Capture();load?.Invoke();if(closed)return;if(!Same(old,Capture())){undo.Push(old);redo.Clear();}BuildProperties();Refresh();Drawing.Fit();}));
            file.Items.Add(MenuAction("Save","Save inputs…",()=> {if(CommitFields())save?.Invoke();}));
            file.Items.Add(new Separator());file.Items.Add(MenuAction("Export","Export report…",()=> {if(CommitFields())export?.Invoke();}));
            if(checkForUpdates!=null){file.Items.Add(new Separator());file.Items.Add(MenuAction("CheckUpdates","Check for updates…",checkForUpdates));}
            var tools=new MenuItem {Header="_Tools",Padding=new Thickness(10,6,10,6)};tools.Items.Add(MenuAction("Diagnose","Diagnose…",ShowDiagnostics));menus.Items.Add(tools);
            addMenu=new MenuItem {Header="+ _Add",Padding=new Thickness(10,6,10,6)};menus.Items.Add(addMenu);
            addMenu.Items.Add(MenuAction("AddAbove","Bay above selected beam",()=>AddBay(true)));
            addMenu.Items.Add(MenuAction("AddBelow","Bay below selected beam",()=>AddBay(false)));
            addMenu.Items.Add(MenuAction("AddIndependent","Independent beam (two endpoints)",BeginIndependent));
            addMenu.Items.Add(MenuAction("AddCrossing","Crossing beam",()=> {if(CommitFields() && Model.IsNetworkMode){Geometry();Drawing.AddCrossing=true;Drawing.Cursor=Cursors.Cross;Drawing.InvalidateVisual();}}));
            edit.Children.Add(menus);
            if(readRevitSelection!=null || requestRevitSelection!=null)
            {
                var selection=Button("FromRevit","Use Revit selection",ImportRevitSelection);
                selection.ToolTip="Select beams, slabs, columns and walls in Revit, then click once to replace the canvas. Undo restores the previous drawing.";edit.Children.Add(selection);
            }
            edit.Children.Add(Button("Undo","Undo",()=>History(undo,redo)));edit.Children.Add(Button("Redo","Redo",()=>History(redo,undo)));
            edit.Children.Add(Button("Delete","Delete",RemoveSelected));toolbar.Children.Add(edit);return toolbar;
        }
        private FrameworkElement BuildViewToolbar()
        {
            var toolbar=new DockPanel {Margin=new Thickness(10,0,10,6),LastChildFill=true};
            var modes=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(modes,Dock.Left);toolbar.Children.Add(modes);
            modes.Children.Add(Button("Geometry","Geometry",Geometry));
            modes.Children.Add(Button("Formwork","Formwork",()=> {if(!CommitFields())return;CancelDrafting();sectionMode=false;Drawing.Formwork=true;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Visibility=Visibility.Visible;sectionHost.Visibility=Visibility.Collapsed;Drawing.InvalidateVisual();Drawing.Fit();Refresh();}));
            modes.Children.Add(Button("Section","Section",()=> {if(!CommitFields())return;CancelDrafting();sectionMode=true;Drawing.AddCrossing=false;Drawing.CancelSelectionGesture();Drawing.Visibility=Visibility.Collapsed;sectionHost.Visibility=Visibility.Visible;Refresh();}));
            RevitPartSelector.SelectionChanged+=(s,e)=> {if(syncingRevitPart || !(RevitPartSelector.SelectedValue is string key) || !CommitFields())return;Model.ActivateRevitDrawing(key);Drawing.ClearSelection();ClearDiagrams();refreshInputs?.Invoke();BuildProperties();Refresh();};
            foreach(var tab in modes.Children.OfType<Button>()){tab.Margin=new Thickness(0);tab.SetResourceReference(StyleProperty,"ViewTab");}
            var display=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};toolbar.Children.Add(display);
            display.Children.Add(new TextBlock {Text="Dimensions",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,6,0)});display.Children.Add(dimensionMode);
            dimensionMode.SelectionChanged+=(s,e)=> {bool all=dimensionMode.SelectedIndex==2;Drawing.HideDimensions=Section.HideDimensions=dimensionMode.SelectedIndex==0;dimensions.IsChecked=all;Drawing.AllDimensions=Section.ShowAllDimensions=all;Drawing.InvalidateVisual();Section.InvalidateVisual();};
            var secondary=new CheckBox {Content="Secondary H20s",Margin=new Thickness(10,0,8,0),VerticalAlignment=VerticalAlignment.Center};
            secondary.Click+=(s,e)=> {Drawing.ShowSecondaryH20s=secondary.IsChecked==true;Drawing.InvalidateVisual();Refresh();};Buttons["SecondaryH20s"]=secondary;display.Children.Add(secondary);
            display.Children.Add(Button("ZoomOut","−",()=>Zoom(1/1.25)));display.Children.Add(Button("ZoomIn","+",()=>Zoom(1.25)));display.Children.Add(Button("Fit","Fit",()=> {if(sectionMode)SectionViewport.Fit();else Drawing.Fit();}));
            // Section position also appears on the plan, so users can place the cut before switching views.
            var sectionBar=new WrapPanel {Margin=new Thickness(10,3,10,5)};
            sectionBar.Children.Add(RevitPartSelector);
            sectionControls.Children.Add(new TextBlock {Text="A–A position (mm)",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
            sectionControls.Children.Add(SectionPosition);sectionControls.Children.Add(SectionPositionInput);sectionBar.Children.Add(sectionControls);
            SectionPosition.ValueChanged+=(s,e)=> {if(!syncingSection)SetSectionPosition(e.NewValue);};
            Action commit=()=> {if(double.TryParse(SectionPositionInput.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var x) && TowerBeamAnalysis.Finite(x) && x>=0 && x<=Model.DrawingLengthMm){SectionPositionInput.ClearValue(BorderBrushProperty);SetSectionPosition(x);}else SectionPositionInput.BorderBrush=Brushes.Firebrick;};
            SectionPositionInput.KeyDown+=(s,e)=> {if(e.Key==Key.Enter){commit();e.Handled=true;}};SectionPositionInput.LostKeyboardFocus+=(s,e)=>commit();
            var container=new StackPanel();container.Children.Add(toolbar);container.Children.Add(sectionBar);return container;
        }
        private void SetSectionPosition(double x)
        {
            if(syncingSection || !TowerBeamAnalysis.Finite(x))return;
            syncingSection=true;
            try {x=Math.Max(0,Math.Min(Model.DrawingLengthMm,x));Section.SectionPositionMm=Drawing.SectionPositionMm=x;SectionPosition.Maximum=Model.DrawingLengthMm;SectionPosition.Value=x;SectionPositionInput.Text=x.ToString("0.###",CultureInfo.CurrentCulture);Section.InvalidateVisual();Drawing.InvalidateVisual();}
            finally {syncingSection=false;}
        }
        private void RefreshSectionControls()
        {
            if(Model.NetworkResult!=null && Model.IsCalculationValid)
            {
                SetSectionPosition(TowerBeamAnalysis.Finite(Drawing.SectionPositionMm) ? Drawing.SectionPositionMm : Model.DrawingLengthMm/2);
                if(!ReferenceEquals(governingResult,Model.NetworkResult))
                {
                    governingResult=Model.NetworkResult;GoverningMembers.Children.Clear();GoverningMembers.Children.Add(new TextBlock {Text="Governing members",FontWeight=FontWeights.SemiBold});
                    for(int i=0;i<governingResult.Beams.Length;i++)
                    {
                        int beam=i;var b=governingResult.Beams[i];var t=b.Transfer;
                        int row=Enumerable.Range(0,t.RowsMm.Length).OrderByDescending(r=>t.LowerCases.Max(c=>c[r].Utilization)).First();
                        var button=new Button {Content=$"B{i+1} · T{row+1} · {t.LowerCases.Max(c=>c[row].Utilization)*100:0.0}%",HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,0)};
                        button.Click+=(s,e)=>ShowMember(beam,b.StockPlan.Pieces.Length+row,0);GoverningMembers.Children.Add(button);
                    }
                }
            }
            else {governingResult=null;GoverningMembers.Children.Clear();}
            GoverningMembers.Visibility=sectionMode && governingResult!=null && InspectorTabs.SelectedIndex==0 ? Visibility.Visible : Visibility.Collapsed;
        }
        private void SettingsGroup(string title,string summary,bool expanded,Action build)
        {
            int first=properties.Children.Count;build();var body=new StackPanel {Margin=new Thickness(10,8,10,8)};
            while(properties.Children.Count>first){var child=properties.Children[first];properties.Children.RemoveAt(first);body.Children.Add(child);}
            var heading=new StackPanel();heading.Children.Add(new TextBlock {Text=title,FontWeight=FontWeights.SemiBold});var summaryText=new TextBlock {Text=summary,FontSize=12,Foreground=Brushes.SlateGray,MaxWidth=240};heading.Children.Add(summaryText);
            var group=new Expander {Header=heading,Tag=title,Content=body,IsExpanded=settingsExpanded.TryGetValue(title,out var open) ? open : expanded,Margin=new Thickness(0,0,0,10),Padding=new Thickness(6),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(223,227,230))};
            Action updateSummary=()=>summaryText.Text=title=="Available lengths" ? $"{TowerStockPlanner.ParseLengths(Model.StockLengthsText).Length} stock lengths"+(Model.AllowCustomH20Lengths ? " · custom H20s enabled" : " · catalogue only") : title=="Support layout" ? $"{Model.StockEndSetbackMm:0.###} mm inset · {Model.MaximumConcreteEndOverhangMm:0.###} mm end strip" : title=="Loads" ? (Model.UseCustomLoads ? "Custom allowances" : "Standard allowances") : summary;
            updateSummary();syncControls.Add(updateSummary);
            summaryText.Visibility=group.IsExpanded ? Visibility.Collapsed : Visibility.Visible;group.Expanded+=(s,e)=> {settingsExpanded[title]=true;summaryText.Visibility=Visibility.Collapsed;};group.Collapsed+=(s,e)=> {settingsExpanded[title]=false;summaryText.Visibility=Visibility.Visible;};properties.Children.Add(group);
        }
        private ComboBox SettingChoice<T>(string name,string label,System.Collections.IEnumerable values,object selected,string display,Action<T> set,Func<object> get)
        {
            Text(label);var choice=new ComboBox {Name=name,ItemsSource=values,SelectedItem=selected,DisplayMemberPath=display,SelectedValuePath=typeof(T)==typeof(string) ? "" : "Value",Margin=new Thickness(0,0,0,12)};
            choice.SelectionChanged+=(s,e)=> {if(changing || !(choice.SelectedItem is T value))return;if(CommitFields())Mutate(()=>set(value));else Refresh();};
            syncControls.Add(()=>choice.SelectedItem=get());properties.Children.Add(choice);return choice;
        }
        private void BuildSharedSettings()
        {
            Text("Applies to all beams and slab bays.");
            SettingsGroup("Materials and design limits","Plywood, primary material, tower capacity and deflection criteria",true,()=>
            {
                SettingChoice<PlywoodProfile>("WorkspacePlywoodSelector","Plywood",Model.PlywoodOptions,Model.SelectedPlywood,"Label",v=>Model.SelectedPlywood=v,()=>Model.SelectedPlywood);
                SettingChoice<PrimaryBeamMaterialOption>("PrimaryMaterialSelector","Primary beam material",Model.PrimaryMaterialOptions,Model.PrimaryMaterialOptions.First(v=>v.Value==Model.PrimaryMaterial),"Label",v=>Model.PrimaryMaterial=v.Value,()=>Model.PrimaryMaterialOptions.First(v=>v.Value==Model.PrimaryMaterial));
                if(Model.PrimaryMaterial==PrimaryBeamMaterial.WS10)Text("WS10 primaries use uncut catalogue lengths. Custom lengths apply only to H20s.");
                Number("TowerLegCapacityKn","Allowable load per tower leg (kN)",()=>Model.TowerLegCapacityKn,v=>Model.TowerLegCapacityKn=v);
                SettingChoice<string>("WorkspacePlywoodDeflectionSelector","Plywood deflection limit",Model.PlywoodDeflectionLimitOptions,Model.PlywoodDeflectionLimit,"",v=>Model.PlywoodDeflectionLimit=v,()=>Model.PlywoodDeflectionLimit);
                SettingChoice<BeamDeflectionOption>("MemberDeflectionSelector","Member deflection limit",Model.DeflectionOptions,Model.DeflectionLimit,"Label",v=>Model.DeflectionLimit=v,()=>Model.DeflectionLimit);
            });
            SettingsGroup("Available lengths","H20 stock lengths and custom-length options",false,()=>
            {
                BuildStockChoices();
                if(Model.PrimaryMaterial==PrimaryBeamMaterial.WS10)Text(Model.WS10CatalogueText);
                var custom=new CheckBox {Content="Allow custom H20 lengths",IsChecked=Model.AllowCustomH20Lengths};
                custom.Click+=(s,e)=> {bool value=custom.IsChecked==true;if(CommitFields())Mutate(()=>Model.AllowCustomH20Lengths=value);else Refresh();};syncControls.Add(()=>custom.IsChecked=Model.AllowCustomH20Lengths);properties.Children.Add(custom);Text("25 mm increments");
                int first=properties.Children.Count;
                var scope=SettingChoice<CustomH20ScopeOption>("CustomH20ScopeSelector","Apply custom lengths to",Model.CustomH20ScopeOptions,Model.CustomH20ScopeOptions.First(v=>v.Value==Model.CustomH20Scope),"Label",v=>Model.CustomH20Scope=v.Value,()=>Model.CustomH20ScopeOptions.First(v=>v.Value==Model.CustomH20Scope));
                var scopeLabel=(FrameworkElement)properties.Children[first];syncControls.Add(()=> {scope.Visibility=scopeLabel.Visibility=Model.AllowCustomH20Lengths ? Visibility.Visible : Visibility.Collapsed;scope.IsEnabled=Model.AllowCustomH20Lengths;});
            });
            SettingsGroup("Support layout","Member-end support distances and tower arrangement",false,()=>
            {
                Number("StockEndSetbackMm","Minimum support distance from member end (mm)",()=>Model.StockEndSetbackMm,v=>Model.StockEndSetbackMm=v);Fields["StockEndSetbackMm"].ToolTip="Minimum distance from each primary stock end to its nearest tower support.";
                Number("MaximumConcreteEndOverhangMm","Maximum concrete end strip (mm)",()=>Model.MaximumConcreteEndOverhangMm,v=>Model.MaximumConcreteEndOverhangMm=v,0);Fields["MaximumConcreteEndOverhangMm"].ToolTip="Maximum concrete length beyond primary coverage at either end, assigned to the external column-side support.";
                var allow=new CheckBox {Content="Allow standalone 600 mm towers",IsChecked=!Model.Exclude600Standalone};
                allow.Click+=(s,e)=> {bool exclude=allow.IsChecked!=true;if(CommitFields())Mutate(()=>Model.Exclude600Standalone=exclude);else Refresh();};syncControls.Add(()=>allow.IsChecked=!Model.Exclude600Standalone);properties.Children.Add(allow);
            });
            SettingsGroup("Loads",Model.UseCustomLoads ? "Custom allowances" : "Standard allowances",true,()=>
            {
                var mode=SettingChoice<string>("LoadAllowanceSelector","Load allowances",new[] {"Standard","Custom"},Model.UseCustomLoads ? "Custom" : "Standard","",v=>Model.UseCustomLoads=v=="Custom",()=>Model.UseCustomLoads ? "Custom" : "Standard");
                var standard=new TextBlock {Text="Standard allowances are calculated from each member’s concrete depth.",FontSize=12,Foreground=Brushes.SlateGray,Margin=new Thickness(0,0,0,8)};properties.Children.Add(standard);
                int first=properties.Children.Count;
                Number("CustomLiveLoad","Placing + working load (kN/m²)",()=>Model.CustomLiveLoad,v=>Model.CustomLiveLoad=v,0);
                Number("CustomFormworkLoad","Formwork load (kN/m²)",()=>Model.CustomFormworkLoad,v=>Model.CustomFormworkLoad=v,0);
                var customFields=properties.Children.Cast<FrameworkElement>().Skip(first).ToArray();syncControls.Add(()=> {standard.Visibility=Model.UseCustomLoads ? Visibility.Collapsed : Visibility.Visible;foreach(var field in customFields)field.Visibility=Model.UseCustomLoads ? Visibility.Visible : Visibility.Collapsed;Fields["CustomLiveLoad"].IsEnabled=Fields["CustomFormworkLoad"].IsEnabled=Model.UseCustomLoads;});
            });
        }
        private void BuildStockChoices()
        {
            Text("Available H20 lengths (mm)");
            var stock=new TextBox {Text=Model.StockLengthsText,Visibility=Visibility.Collapsed};Fields["StockLengthsText"]=stock;properties.Children.Add(stock);
            var editor=new StringEditor {Box=stock,Get=()=>Model.StockLengthsText,Set=v=>Model.StockLengthsText=v};stringEditors.Add(editor);
            stock.TextChanged+=(s,e)=> {if(changing)return;editor.Dirty=true;Model.SetAdditionalTextInputErrors(this,true);Refresh();};
            var chips=new WrapPanel();properties.Children.Add(chips);
            Action render=()=>
            {
                chips.Children.Clear();var selected=TowerStockPlanner.ParseLengths(Model.StockLengthsText);
                foreach(double length in selected.Concat(TowerStockPlanner.ParseLengths(TowerStockPlanner.DefaultLengths)).Distinct().OrderBy(x=>x))
                {
                    var chip=new ToggleButton {Content=length.ToString("0.###"),IsChecked=selected.Contains(length),MinWidth=64,Padding=new Thickness(6),Margin=new Thickness(0,0,6,6)};chip.SetResourceReference(StyleProperty,"StockChip");
                    chip.Click+=(s,e)=> {var values=chips.Children.OfType<ToggleButton>().Where(b=>b.IsChecked==true).Select(b=>b.Content.ToString()).ToArray();if(values.Length==0){chip.IsChecked=true;return;}stock.Text=string.Join("; ",values);CommitFields();};chips.Children.Add(chip);
                }
            };render();
            var add=new DockPanel();var input=new TextBox {MinWidth=90,ToolTip="Add an available H20 length in mm"};var button=new Button {Content="+ Add length",Margin=new Thickness(6,0,0,8)};DockPanel.SetDock(button,Dock.Right);add.Children.Add(button);add.Children.Add(input);properties.Children.Add(add);
            button.Click+=(s,e)=> {if(!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value) || !TowerBeamAnalysis.Finite(value) || value<300 || value>50000 || value!=Math.Round(value)){input.BorderBrush=Brushes.Firebrick;input.ToolTip="Enter a whole length from 300 to 50000 mm.";return;}stock.Text=Model.StockLengthsText+"; "+value.ToString("0",CultureInfo.CurrentCulture);if(CommitFields()){input.Clear();input.ClearValue(BorderBrushProperty);render();}};
        }
    }
}
