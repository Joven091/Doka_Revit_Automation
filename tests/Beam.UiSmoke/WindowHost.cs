using System.Windows;
namespace EngineeringDashboard
{
 // Exercise the production XAML and UI behavior without a Revit host. Import calls
 // the same validation guard; actual parameter writes are covered by host checks.
 public partial class BeamCalculatorWindow : Window
 {
  public int AcceptedImports {get;private set;}
  public BeamCalculatorWindow(){InitializeComponent();InitializeUi();}
  private void BtnApplyToBeams_Click(object s,RoutedEventArgs e){if(TryGetValidCalculation(out var vm)) AcceptedImports++;}
  private void BtnClose_Click(object s,RoutedEventArgs e)=>Close();
 }
}
