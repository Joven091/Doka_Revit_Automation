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
            var root=new DockPanel{Margin=new Thickness(16)};
            var title=new TextBlock{Text="Copy or save this report and send it with your comments.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
            DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
            var report=new TextBox{Text=capture(),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12};
            AutomationProperties.SetAutomationId(report,"Top50DiagnosticReport");
            var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};var message=new TextBlock{TextWrapping=TextWrapping.Wrap};footer.Children.Add(message);
            var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
            Action<string,Action> add=(label,action)=>{var button=new Button{Content=label,Padding=new Thickness(12,6,12,6),Margin=new Thickness(8,0,0,0)};button.Click+=(s,e)=>{try{action();}catch(Exception error){message.Text=error.Message;}};buttons.Children.Add(button);};
            add("Refresh",()=>{report.Text=capture();message.Text="Report refreshed.";});
            add("Copy report",()=>{Clipboard.SetText(report.Text);message.Text="Report copied.";});
            add("Save report…",()=>{var dialog=new SaveFileDialog{Title="Save Top50 diagnosis",Filter="Text report (*.txt)|*.txt",FileName="Top50-diagnosis-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"};if(dialog.ShowDialog(this)==true){File.WriteAllText(dialog.FileName,report.Text,new UTF8Encoding(false));message.Text="Report saved.";}});
            add("Close",Close);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);root.Children.Add(report);Content=root;
        }
    }
}
