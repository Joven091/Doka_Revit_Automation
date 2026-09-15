using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EngineeringDashboard;

internal static class Program
{
 private static int checks;
 private static BackproppingWindow window;
 private static T Get<T>(string name) => (T)window.FindName(name);
 private static void Assert(bool value) { checks++; if(!value) throw new Exception("UI assertion " + checks + " failed"); }
 private static void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
 private static IEnumerable<T> Children<T>(DependencyObject obj) where T:DependencyObject
 { foreach(var item in LogicalTreeHelper.GetChildren(obj)){ if(!(item is DependencyObject c)) continue; if(c is T value) yield return value; foreach(var descendant in Children<T>(c)) yield return descendant;} }
 private static void Layout() { window.Measure(new Size(940,860)); window.Arrange(new Rect(0,0,940,860));window.UpdateLayout(); }
 private static string Section() => string.Join(" ",Children<TextBlock>(Get<StackPanel>("SectionPanel")).Select(t=>t.Text));
 [STAThread] private static void Main()
 {
  window=new BackproppingWindow(); Layout();
  Assert(Get<DataGrid>("SlabGrid").Items.Count==0); Assert(Get<TextBox>("FloorName").Text==""); Assert(Get<ComboBox>("Temperature").SelectedIndex==0);
  Get<TextBox>("FloorName").Text="Level 5"; Assert(Get<Button>("CancelEdit").Visibility==Visibility.Visible);
  Get<TextBox>("Thickness").Text="200"; Get<TextBox>("ClearHeight").Text="3000";Get<TextBox>("Sidl").Text="3";Get<TextBox>("LiveLoad").Text="3";Click("SaveSlab");
  Assert(Get<TextBox>("FloorName").Text=="Level 4");Assert(Get<TextBox>("Sidl").Text=="3");
  Get<TextBox>("Age").Text="10";Click("SaveSlab");
  Get<TextBox>("Age").Text="9";Click("SaveSlab");Assert(Get<DataGrid>("SlabGrid").Items.Count==2);Assert(Get<TextBlock>("ErrorText").Text.Contains("younger"));
  Get<TextBox>("Age").Text="10";Click("SaveSlab");
  Get<TextBox>("Age").Text="35";Click("SaveSlab");Layout();
  Assert(Get<DataGrid>("SlabGrid").Items.Count==4);Assert(Get<StackPanel>("EntryForm").Visibility==Visibility.Collapsed);Assert(Section().Contains("28+ days"));
  var combos=Children<ComboBox>(Get<StackPanel>("StoreyPanel")).ToList();Assert(combos.Count==2);foreach(var combo in combos)combo.SelectedIndex=0;
  Click("CalculateButton");Layout();Assert(Get<TextBlock>("ErrorText").Text=="");Assert(Get<StackPanel>("ResultsPanel").Children.Count==2);
  Assert(Children<TextBlock>(Get<StackPanel>("ResultsPanel")).Any(t=>t.Text=="2750 × 2750 mm"&&t.FontSize>=30));
  var input=Children<TextBox>(Get<StackPanel>("StoreyPanel")).First(); input.Text="bad";
  Get<ComboBox>("Temperature").SelectedIndex=1;Layout();Assert(Get<StackPanel>("ResultsPanel").Children.Count==0);
  Assert(Children<TextBox>(Get<StackPanel>("StoreyPanel")).First().Text=="bad");
  Click("CalculateButton");Layout();Assert(Children<TextBlock>(Get<StackPanel>("ResultsPanel")).Any(t=>t.Text.Contains("Enter effective prop extension")));
  Children<TextBox>(Get<StackPanel>("StoreyPanel")).First().Text="3000";Get<TextBox>("ProjectMaximum").Text="2010";Click("CalculateButton");Layout();
  Assert(Children<TextBlock>(Get<StackPanel>("ResultsPanel")).Any(t=>t.Text=="2000 × 2000 mm"));
  Click("RemoveLowest");Assert(Get<DataGrid>("SlabGrid").Items.Count==3);Assert(Get<StackPanel>("EntryForm").Visibility==Visibility.Visible);
  Click("RemoveLowest");Click("RemoveLowest");Click("RemoveLowest");Assert(Get<DataGrid>("SlabGrid").Items.Count==0);
  window.Close();Console.WriteLine($"Passed {checks} WPF workflow checks.");
 }
}
