using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace EngineeringDashboard
{
    public sealed class Top50DiagnosticWindow : Window
    {
        public Top50DiagnosticWindow(Func<string> capture)
        {
            if(capture==null)throw new ArgumentNullException(nameof(capture));
            Title="Top50 Planner — diagnosis";Width=1080;Height=760;MinWidth=640;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            Background=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=13;
            var root=new Grid{Margin=new Thickness(16),Background=Brushes.White};
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var title=new TextBlock{Text="Copy or save this report and send it with your comments.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
            root.Children.Add(title);
            var report=new TextBox{Text=capture(),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12};
            AutomationProperties.SetAutomationId(report,"Top50DiagnosticReport");
            var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};var message=new TextBlock{TextWrapping=TextWrapping.Wrap};footer.Children.Add(message);
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
            Action<string,Action> add=(label,action)=>{var button=new Button{Content=label,MinHeight=32,MinWidth=80,Padding=new Thickness(12,6,12,6),Margin=new Thickness(8,0,0,0)};button.Click+=(s,e)=>{try{action();}catch(Exception error){message.Text=error.Message;}};buttons.Children.Add(button);};
            add("Refresh",()=>{report.Text=capture();message.Text="Report refreshed.";});
            add("Copy report",()=>{Clipboard.SetText(report.Text);message.Text="Report copied.";});
            add("Save report…",()=>{var dialog=new SaveFileDialog{Title="Save Top50 diagnosis",Filter="Text report (*.txt)|*.txt",FileName="Top50-diagnosis-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"};if(dialog.ShowDialog(this)==true){File.WriteAllText(dialog.FileName,report.Text,new UTF8Encoding(false));message.Text="Report saved.";}});
            add("Close",Close);Grid.SetRow(footer,2);root.Children.Add(footer);Grid.SetRow(report,1);root.Children.Add(report);Content=root;
        }
    }
}
