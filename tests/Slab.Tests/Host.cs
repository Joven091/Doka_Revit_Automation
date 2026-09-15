using System.Windows;
namespace EngineeringDashboard { public partial class FlexCalculatorWindow:Window {
public FlexCalculatorWindow(){InitializeComponent();InitializeUi();DataContext=new FlexCalculatorViewModel();}
private void BtnImportValues_Click(object sender,RoutedEventArgs e){}
private void BtnClose_Click(object sender,RoutedEventArgs e){}
}}
