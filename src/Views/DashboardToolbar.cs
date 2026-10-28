using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EngineeringDashboard
{
    internal sealed class DashboardToolbar : DockPanel
    {
        internal MenuItem FileMenu {get;}=new MenuItem{Header="_File",Padding=new Thickness(10,6,10,6)};
        internal MenuItem ToolsMenu {get;}=new MenuItem{Header="_Tools",Padding=new Thickness(10,6,10,6)};
        internal MenuItem SaveItem {get;}
        internal DashboardToolbar(Window owner,string title,Action open,Action save,Action diagnose=null,Action changeSystem=null)
        {
            Margin=new Thickness(0,0,0,12);LastChildFill=true;
            Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/"+typeof(DashboardToolbar).Assembly.GetName().Name+";component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            var commands=new StackPanel{Orientation=Orientation.Horizontal};var menus=new Menu{Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Center};
            var name=new MenuItem{Header=title+(changeSystem==null?"":" ▾"),FontSize=18,FontWeight=FontWeights.SemiBold,Padding=new Thickness(3,6,12,6)};
            if(changeSystem!=null)name.Items.Add(Item("Change system…",changeSystem));menus.Items.Add(name);
            if(open!=null)FileMenu.Items.Add(Item("Open inputs…",open));
            if(save!=null){SaveItem=Item("Save inputs…",save);FileMenu.Items.Add(SaveItem);}
            if(FileMenu.Items.Count>0)FileMenu.Items.Add(new Separator());FileMenu.Items.Add(Item("Close",owner.Close));
            ToolsMenu.Items.Add(Item("Diagnose…",diagnose??(()=>Diagnose(owner))));
            menus.Items.Add(FileMenu);menus.Items.Add(ToolsMenu);commands.Children.Add(menus);Children.Add(commands);
        }
        internal static MenuItem Item(string label,Action action)
        {var item=new MenuItem{Header=label};item.Click+=(s,e)=>action();return item;}
        internal static void Icon(Button button,string command,string hint=null)
        {
            string path=command=="Undo"?"M9 4 L3 10 L9 16 M3 10 H14 C19 10 21 13 21 19":command=="Redo"?"M15 4 L21 10 L15 16 M21 10 H10 C5 10 3 13 3 19":"M4 7 H20 M9 7 V3 H15 V7 M6 7 L7 21 H17 L18 7 M10 11 V17 M14 11 V17";
            var drawing=new Path{Data=Geometry.Parse(path),StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};drawing.SetBinding(Shape.StrokeProperty,new Binding(nameof(Button.Foreground)){Source=button});
            var canvas=new Canvas{Width=24,Height=24,IsHitTestVisible=false};canvas.Children.Add(drawing);button.Content=new Viewbox{Width=18,Height=18,Child=canvas,IsHitTestVisible=false};
            button.MinWidth=34;button.MinHeight=30;button.Padding=new Thickness(8,5,8,5);
            button.ToolTip=hint??(command=="Undo"?"Undo (Ctrl+Z)":command=="Redo"?"Redo (Ctrl+Y)":"Delete (Delete)");AutomationProperties.SetName(button,command);AutomationProperties.SetHelpText(button,(string)button.ToolTip);
        }
        internal static void Diagnose(Window owner)
        {
            var text=new StringBuilder();var assembly=owner.GetType().Assembly;text.AppendLine("=== CALCULATOR DIAGNOSTIC REPORT ===\nCaptured UTC: "+DateTime.UtcNow.ToString("O"));text.AppendLine(owner.Title+" | "+assembly.GetName().Version+" | MVID "+assembly.ManifestModule.ModuleVersionId);
            var model=owner.DataContext;
            if(model!=null)foreach(var property in model.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0&&(p.PropertyType.IsPrimitive||p.PropertyType.IsEnum||p.PropertyType==typeof(string)||p.PropertyType==typeof(decimal))))
                try{text.AppendLine(property.Name+" = "+property.GetValue(model));}catch(Exception error){text.AppendLine(property.Name+": "+error);}
            var seen=new HashSet<DependencyObject>();Action<DependencyObject> visit=null;
            visit=node=>{if(node==null||!seen.Add(node))return;if(node is TextBox box){text.AppendLine((box.Name.Length>0?box.Name:box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path??"Input")+" = "+box.Text);foreach(var error in Validation.GetErrors(box))text.AppendLine("Validation: "+error.ErrorContent);}foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())visit(child);};visit(owner);
            var dialog=new Window{Title=owner.Title+" · diagnosis",Owner=owner,Width=850,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=new FontFamily("Segoe UI")};var panel=new DockPanel{Margin=new Thickness(16)};var copy=new Button{Content="Copy report",Padding=new Thickness(12,7,12,7)};DockPanel.SetDock(copy,Dock.Bottom);panel.Children.Add(copy);var report=new TextBox{Text=text.ToString(),IsReadOnly=true,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas")};panel.Children.Add(report);copy.Click+=(s,e)=>{try{Clipboard.SetText(report.Text);}catch(System.Runtime.InteropServices.ExternalException){report.SelectAll();report.Focus();}};dialog.Content=panel;dialog.Show();
        }
    }
}
