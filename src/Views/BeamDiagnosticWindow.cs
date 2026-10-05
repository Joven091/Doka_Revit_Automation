using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal sealed class BeamDiagnosticWindow : Window
    {
        private readonly Func<string,string> buildReport;
        private readonly Action<string> copyText;
        internal TextBox RemarksBox {get;}=new TextBox {Name="DiagnosticRemarks",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=60,MaxHeight=110,MaxLength=4000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        internal TextBox ReportBox {get;}=new TextBox {Name="DiagnosticReport",IsReadOnly=true,AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12};
        internal Button CopyButton {get;}=new Button {Name="CopyDiagnosticReport",Content="Copy report",Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,0,8,0)};
        private readonly TextBlock status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
        internal BeamDiagnosticWindow(Func<string,string> buildReport,Action<string> copyText=null)
        {
            this.buildReport=buildReport;this.copyText=copyText??new Action<string>(Clipboard.SetText);Title="Beam formwork diagnosis";Width=900;Height=720;MinWidth=600;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            FontFamily=new FontFamily("Segoe UI");FontSize=13;Background=Brushes.WhiteSmoke;
            var root=new Grid {Margin=new Thickness(16)};
            root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.Children.Add(new TextBlock {Text="Add what happened, then copy the report for review. You can refresh it while the search is running.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
            var label=new TextBlock {Text="Remarks (optional)",Margin=new Thickness(0,0,0,5)};Grid.SetRow(label,1);root.Children.Add(label);
            Grid.SetRow(RemarksBox,2);root.Children.Add(RemarksBox);ReportBox.Margin=new Thickness(0,12,0,12);Grid.SetRow(ReportBox,3);root.Children.Add(ReportBox);
            var footer=new StackPanel();var buttons=new StackPanel {Orientation=Orientation.Horizontal};
            var refresh=new Button {Name="RefreshDiagnosticReport",Content="Refresh report",Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,0,8,0)};
            refresh.Click+=(s,e)=>RefreshReport();CopyButton.Click+=(s,e)=>CopyReport();buttons.Children.Add(CopyButton);buttons.Children.Add(refresh);footer.Children.Add(buttons);footer.Children.Add(status);Grid.SetRow(footer,4);root.Children.Add(footer);Content=root;
            RefreshReport();
        }
        internal void RefreshReport()
        {ReportBox.Text=buildReport(RemarksBox.Text);status.Text="Report ready. Copy it and paste it into the chat for review.";}
        internal void CopyReport()
        {
            RefreshReport();
            try {copyText(ReportBox.Text);status.Text="Report copied. Paste it into the chat for review.";}
            catch(ExternalException) {ReportBox.Focus();ReportBox.SelectAll();status.Text="Clipboard is busy. The report is selected; press Ctrl+C to copy it.";}
        }
    }
}
