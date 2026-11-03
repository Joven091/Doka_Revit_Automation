using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50Window
    {
        private DataGrid pourTable;
        private Button PourAction(string text,Action handler)
            =>Action(text,()=>{try{handler();}catch(Exception error){Error(error);}});
        private UIElement PourPage()
        {
            pourTable=null;
            var pours=model.Pours;var current=pours.FirstOrDefault(p=>p.Id==model.SelectedPourId)??pours.FirstOrDefault();
            model.SelectedPourId=current?.Id;
            var root=new Grid();root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)});root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});root.ColumnDefinitions.Add(new ColumnDefinition());
            var left=new DockPanel();var heading=new StackPanel();heading.Children.Add(Label("Pour order",19,null,FontWeights.SemiBold));
            var add=PourAction("+ Add pour",()=>{model.AddPour();ShowPage(4);});add.IsEnabled=model.Input!=null;add.Margin=new Thickness(0,8,0,8);System.Windows.Automation.AutomationProperties.SetAutomationId(add,"Top50AddPour");heading.Children.Add(add);
            heading.Children.Add(Label("You assign the walls and set the order.",12,Muted,null,new Thickness(0,0,0,10)));DockPanel.SetDock(heading,Dock.Top);left.Children.Add(heading);
            var bottom=new StackPanel{Margin=new Thickness(0,10,0,0)};bottom.Children.Add(Label("Unassigned walls: "+model.UnassignedWallCount,13,null,FontWeights.SemiBold));
            DockPanel.SetDock(bottom,Dock.Bottom);left.Children.Add(bottom);
            var list=new StackPanel();
            for(int i=0;i<pours.Count;i++)
            {
                var p=pours[i];var check=model.PourChecks.FirstOrDefault(c=>c.PourId==p.Id);var row=new DockPanel();
                var arrows=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
                var up=PourAction("↑",()=>model.MovePour(p.Id,-1));up.IsEnabled=i>0;up.ToolTip="Move pour earlier";up.MinWidth=29;up.Padding=new Thickness(5);arrows.Children.Add(up);
                var down=PourAction("↓",()=>model.MovePour(p.Id,1));down.IsEnabled=i<pours.Count-1;down.ToolTip="Move pour later";down.MinWidth=29;down.Padding=new Thickness(5);arrows.Children.Add(down);DockPanel.SetDock(arrows,Dock.Right);row.Children.Add(arrows);
                var select=PourAction((i+1).ToString("00")+"  "+p.Name+"\n"+p.WallIds.Length+" walls · "+(check?.Status??"Not checked"),()=>{model.SelectedPourId=p.Id;ShowPage(4);HighlightWallIds(p.WallIds,false,"Highlight pour");});
                var caption=new StackPanel();caption.Children.Add(Label((i+1).ToString("00")+"  "+p.Name,13,null,FontWeights.SemiBold));caption.Children.Add(Label(p.WallIds.Length+" walls · "+(check?.Status??"Not checked"),12,Muted));select.Content=caption;select.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                select.HorizontalContentAlignment=HorizontalAlignment.Left;select.MinHeight=58;select.Background=p.Id==current?.Id?Color("#FFF2BF"):Brushes.White;select.BorderThickness=new Thickness(0);row.Children.Add(select);
                list.Children.Add(new Border{Child=row,BorderBrush=Line,BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,7)});
            }
            left.Children.Add(Scroll(list));root.Children.Add(left);
            if(current==null){var message=Notice(model.Input==null?"Import walls, then add your first pour.":"Add a pour, assign its walls and set your preferred order. Check pours compares each pour with the selected equipment offer.");Grid.SetColumn(message,2);root.Children.Add(message);return root;}
            var right=new Grid();right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition());right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetColumn(right,2);root.Children.Add(right);
            var top=new StackPanel();var title=new DockPanel();var edits=new WrapPanel();
            edits.Children.Add(PourAction("Rename",()=>{string name=AskText("Rename pour","Pour name",current.Name);if(name!=null)model.RenamePour(current.Id,name);}));
            var remove=PourAction("Delete",()=>model.DeletePour(current.Id));DashboardToolbar.Icon(remove,"Delete");remove.ToolTip="Delete this pour; its walls become unassigned";edits.Children.Add(remove);DockPanel.SetDock(edits,Dock.Right);title.Children.Add(edits);title.Children.Add(Label(current.Name,20,null,FontWeights.SemiBold));top.Children.Add(title);
            var actions=new WrapPanel{Margin=new Thickness(0,10,0,8)};var assign=PourAction("Assign walls",()=>AssignWallsDialog(current.Id));System.Windows.Automation.AutomationProperties.SetAutomationId(assign,"Top50AssignPourWalls");actions.Children.Add(assign);
            var fromRevit=Action("Assign Revit selection",async()=>await AssignCurrentRevitSelection(current.Id));fromRevit.IsEnabled=revit?.ReadSelection!=null&&model.Input?.RevitSource!=null;fromRevit.Margin=new Thickness(6,0,0,0);System.Windows.Automation.AutomationProperties.SetAutomationId(fromRevit,"Top50AssignRevitPour");actions.Children.Add(fromRevit);
            var show=PourAction("Show in Revit",()=>HighlightWallIds(current.WallIds,true,"Locate pour"));show.IsEnabled=revit?.Highlight!=null&&model.Input?.RevitSource!=null&&current.WallIds.Length>0;show.Margin=new Thickness(6,0,0,0);actions.Children.Add(show);top.Children.Add(actions);
            var result=model.PourChecks.FirstOrDefault(c=>c.PourId==current.Id);var status=Label(result?.Status??"Click Check pours to check the selected equipment offer.",13,result?.WithinStock==true?Brushes.DarkGreen:Muted,null,new Thickness(0,0,0,8));status.ToolTip=result?.Detail;top.Children.Add(status);right.Children.Add(top);
            var labels=new Top50WallLabels(model.Input.AllWalls);var walls=model.Input.Walls.ToDictionary(w=>w.Id,StringComparer.Ordinal);
            var rows=current.WallIds.Where(walls.ContainsKey).Select(id=>new PourWallDisplay(walls[id],labels[id],current.Id)).ToArray();
            pourTable=Table(rows,"Wall|Name|*","Length|Length|110");pourTable.SelectionMode=DataGridSelectionMode.Extended;pourTable.ColumnHeaderHeight=32;pourTable.Height=Math.Min(220,rows.Length*30+34);pourTable.SelectionChanged+=(s,e)=>UpdateBusy();System.Windows.Automation.AutomationProperties.SetAutomationId(pourTable,"Top50PourWalls");
            var choices=new[]{new PourChoice{Id=null,Name="Unassigned"}}.Concat(pours.Select(p=>new PourChoice{Id=p.Id,Name=p.Name})).ToArray();
            var factory=new FrameworkElementFactory(typeof(ComboBox));factory.SetValue(ComboBox.ItemsSourceProperty,choices);factory.SetValue(ComboBox.DisplayMemberPathProperty,"Name");factory.SetValue(ComboBox.SelectedValuePathProperty,"Id");factory.SetBinding(ComboBox.SelectedValueProperty,new Binding("Assignment"){Mode=BindingMode.OneWay});factory.SetBinding(FrameworkElement.TagProperty,new Binding("WallId"));
            factory.SetValue(FrameworkElement.MinHeightProperty,24d);factory.SetValue(FrameworkElement.HeightProperty,26d);factory.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);factory.SetValue(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center);factory.SetValue(Control.PaddingProperty,new Thickness(4,0,4,0));
            factory.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,new SelectionChangedEventHandler((s,e)=>
            {
                if(rendering)return;var combo=(ComboBox)s;if(!(combo.Tag is string id)||!(combo.SelectedItem is PourChoice choice))return;
                try{if(choice.Id==current.Id)return;if(choice.Id==null)model.UnassignWalls(current.Id,new[]{id});else model.AssignWalls(choice.Id,new[]{id});}catch(Exception error){Error(error);}
            }));
            pourTable.Columns.Add(new DataGridTemplateColumn{Header="Assignment",CellTemplate=new DataTemplate{VisualTree=factory},Width=180});Grid.SetRow(pourTable,1);right.Children.Add(pourTable);
            var diagram=new ScrollViewer{Margin=new Thickness(0,12,0,0),HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.White};
            var arrangement=new DockPanel();var legend=Label("Panel arrangement · dimensions in mm · same on both faces",12,Muted,null,new Thickness(10,8,0,0));DockPanel.SetDock(legend,Dock.Top);arrangement.Children.Add(legend);
            arrangement.Children.Add(result?.Group!=null?(UIElement)new Top50Diagram(result.Group,labels):Notice(current.WallIds.Length==0?"Assign walls to this pour.":"Panel arrangements appear here after you check the pours."));diagram.Content=arrangement;Grid.SetRow(diagram,2);right.Children.Add(diagram);
            var summary=new StackPanel{Margin=new Thickness(0,10,0,0)};
            if(result?.Group!=null)
            {
                int panels=result.Group.Walls.Sum(w=>w.Pattern.PanelsMm.Count)*Top50PlannerPolicy.WallFaces;
                summary.Children.Add(Label("Panels used: "+panels+" of "+Top50PlannerPolicy.TotalPanels(model.Selected)+" · both faces",13,null,FontWeights.SemiBold));
                foreach(var extra in result.AdditionalPanels.OrderBy(p=>p.Key))summary.Children.Add(Label("Add "+extra.Value*Top50PlannerPolicy.WallFaces+" × "+(extra.Key/1000m).ToString("0.00",CultureInfo.InvariantCulture)+" m panels for this arrangement",13));
                summary.Children.Add(Label(result.AdditionalStandard+result.AdditionalAdjustable==0?"Connectors: available · one level, both faces":"Connector shortage for this arrangement:",12));
                if(result.AdditionalStandard>0)summary.Children.Add(Label(result.AdditionalStandard+" × "+ConnectorEquipment.DirectName,12));
                if(result.AdditionalAdjustable>0)summary.Children.Add(Label(result.AdditionalAdjustable+" × "+ConnectorEquipment.FillerName,12));
            }
            summary.Children.Add(Label("Equipment can be reused after the previous pour is stripped.",12,Muted,null,new Thickness(0,6,0,0)));Grid.SetRow(summary,3);right.Children.Add(summary);
            return root;
        }
        private void AssignWallsDialog(string pourId)
        {
            var labels=new Top50WallLabels(model.Input.AllWalls);var dialog=new Window{Title="Assign walls to "+model.Pours.Single(p=>p.Id==pourId).Name,Owner=this,Width=650,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var root=new DockPanel{Margin=new Thickness(12)};var apply=PourAction("Assign selected walls",()=>{});DockPanel.SetDock(apply,Dock.Bottom);root.Children.Add(apply);
            var list=new ListBox{SelectionMode=SelectionMode.Extended};foreach(var wall in model.Input.Walls.Where(w=>w.Included)){var assigned=model.Pours.FirstOrDefault(p=>p.WallIds.Contains(wall.Id));list.Items.Add(new ListBoxItem{Content=labels[wall.Id]+" · "+(assigned?.Name??"Unassigned"),Tag=wall.Id,Padding=new Thickness(8)});}root.Children.Add(list);
            apply.Click+=(s,e)=>{var ids=list.SelectedItems.OfType<ListBoxItem>().Select(i=>(string)i.Tag).ToArray();if(ids.Length>0){try{model.AssignWalls(pourId,ids);dialog.DialogResult=true;}catch(Exception error){Error(error);}}};dialog.Content=root;dialog.ShowDialog();
        }
        private async Task AssignCurrentRevitSelection(string pourId)
        {
            if(revit?.ReadSelection==null||revitBusy||highlighting||model.IsBusy)return;revitBusy=true;UpdateBusy();
            try{var snapshot=await revit.ReadSelection();if(!revitClosed)model.AssignRevitSelection(pourId,snapshot);}
            catch(Exception error){if(!revitClosed)Error(error);}
            finally{revitBusy=false;if(!revitClosed)UpdateBusy();}
        }
        private void UnassignSelectedPourWalls()
        {if(pourTable==null||model.SelectedPourId==null)return;var ids=pourTable.SelectedItems.OfType<PourWallDisplay>().Select(r=>r.WallId).ToArray();if(ids.Length>0)model.UnassignWalls(model.SelectedPourId,ids);}
        private sealed class PourChoice{public string Id{get;set;}public string Name{get;set;}}
        private sealed class PourWallDisplay
        {
            public string WallId{get;}public string Name{get;}public string Assignment{get;}public string Length{get;}
            public PourWallDisplay(WallRow wall,string label,string pour){WallId=wall.Id;Name=label;Assignment=pour;Length=(wall.LengthMm/1000m).ToString("0.00",CultureInfo.InvariantCulture)+" m";}
        }
    }
}
