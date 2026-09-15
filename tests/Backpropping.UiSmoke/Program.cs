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
 private static void CheckPropBearing()
 {
  var section=Get<StackPanel>("SectionPanel");
  foreach(double width in new[]{260.0,340.0,460.0})
  {
   section.Measure(new Size(width,double.PositiveInfinity));
   section.Arrange(new Rect(0,0,width,section.DesiredSize.Height)); section.UpdateLayout();
   var floors=section.Children.Cast<Grid>().ToList();
   for(int i=0;i<floors.Count-1;i++)
   {
    var slab=floors[i+1].Children.OfType<System.Windows.Shapes.Rectangle>().Single();
    double slabTop=slab.TranslatePoint(new Point(0,0),section).Y;
    var props=Children<System.Windows.Controls.Primitives.UniformGrid>(floors[i]).Single(g=>g.Columns==3);
    foreach(Grid prop in props.Children)
    {
     var plate=prop.Children.OfType<System.Windows.Shapes.Rectangle>().Single(r=>r.VerticalAlignment==VerticalAlignment.Bottom);
     double plateBottom=plate.TranslatePoint(new Point(0,plate.ActualHeight),section).Y;
     Assert(Math.Abs(plateBottom-slabTop)<0.01);
    }
   }
  }
 }
 [STAThread] private static void Main()
 {
  window=new BackproppingWindow(); Layout();
  Assert(Get<DataGrid>("SlabGrid").Items.Count==0); Assert(Get<TextBox>("FloorName").Text==""); Assert(Get<ComboBox>("Temperature").SelectedIndex==0);
  Get<TextBox>("FloorName").Text="Level 5"; Assert(Get<Button>("CancelEdit").Visibility==Visibility.Visible);
  Get<TextBox>("Thickness").Text="200"; Get<TextBox>("FloorHeight").Text="3200";Get<TextBox>("Sidl").Text="3";Get<TextBox>("LiveLoad").Text="3";Click("SaveSlab");
  Assert(Get<TextBox>("FloorName").Text=="Level 4");Assert(Get<TextBox>("Sidl").Text=="3");
  Get<TextBox>("Age").Text="10";Click("SaveSlab");
  Get<TextBox>("Age").Text="9";Click("SaveSlab");Assert(Get<DataGrid>("SlabGrid").Items.Count==2);Assert(Get<TextBlock>("ErrorText").Text.Contains("younger"));
  Get<TextBox>("Age").Text="10";Click("SaveSlab");
  Get<TextBox>("Age").Text="35";Click("SaveSlab");Layout();
  Assert(Get<DataGrid>("SlabGrid").Items.Count==4);Assert(Get<StackPanel>("EntryForm").Visibility==Visibility.Collapsed);Assert(Section().Contains("28+ days"));
  CheckPropBearing();
  var combos=Children<ComboBox>(Get<StackPanel>("StoreyPanel")).ToList();Assert(combos.Count==2);foreach(var combo in combos)combo.SelectedIndex=0;
  Assert(Children<TextBox>(Get<StackPanel>("StoreyPanel")).Count()==2);
  Assert(Children<TextBlock>(Get<StackPanel>("StoreyPanel")).Count(t=>t.Text.Contains("Prop extension: 3000 mm (automatic)"))==2);
  Click("CalculateButton");Layout();Assert(Get<TextBlock>("ErrorText").Text=="");Assert(Get<StackPanel>("ResultsPanel").Children.Count==2);
  Assert(Children<TextBlock>(Get<StackPanel>("ResultsPanel")).Any(t=>t.Text=="2750 × 2750 mm"&&t.FontSize>=30));
  var firstCard=(StackPanel)((Border)Get<StackPanel>("ResultsPanel").Children[0]).Child;
  Assert(firstCard.Children.Count==4); // Storey identity plus spacing, force and utilization only.
  Assert(((TextBlock)firstCard.Children[2]).Text.StartsWith("Load per prop:"));
  Assert(((TextBlock)firstCard.Children[3]).Text.StartsWith("Utilization:"));
  var input=Children<TextBox>(Get<StackPanel>("StoreyPanel")).First(); input.Text="bad";
  Get<ComboBox>("Temperature").SelectedIndex=1;Layout();Assert(Get<StackPanel>("ResultsPanel").Children.Count==0);
  Assert(Children<TextBox>(Get<StackPanel>("StoreyPanel")).First().Text=="bad");
  Click("CalculateButton");Layout();Assert(Children<TextBlock>(Get<StackPanel>("ResultsPanel")).Any(t=>t.Text.Contains("Enter maximum spacing")));
  Children<TextBox>(Get<StackPanel>("StoreyPanel")).First().Text="";Get<TextBox>("ProjectMaximum").Text="2010";Click("CalculateButton");Layout();
  Assert(Children<TextBlock>(Get<StackPanel>("ResultsPanel")).Any(t=>t.Text=="2000 × 2000 mm"));
  Click("RemoveLowest");Assert(Get<DataGrid>("SlabGrid").Items.Count==3);Assert(Get<StackPanel>("EntryForm").Visibility==Visibility.Visible);
  CheckPropBearing();
  Click("RemoveLowest");Click("RemoveLowest");Click("RemoveLowest");Assert(Get<DataGrid>("SlabGrid").Items.Count==0);
  window.Close();
  window=new BackproppingWindow();
  Get<ComboBox>("Temperature").SelectedIndex=1;
  Get<TextBox>("FloorName").Text="Level 5";Get<TextBox>("Thickness").Text="200";
  Get<TextBox>("FloorHeight").Text="3200";Get<TextBox>("Sidl").Text="6";Get<TextBox>("LiveLoad").Text="6";Click("SaveSlab");
  Get<TextBox>("Age").Text="10";Click("SaveSlab");
  Get<TextBox>("FloorName").Text="Unfinished floor";
  Get<ComboBox>("Temperature").SelectedIndex=0;
  Assert(Get<StackPanel>("EntryForm").Visibility==Visibility.Visible);
  Assert(Get<Button>("CancelEdit").Visibility==Visibility.Visible);
  Assert(Get<TextBox>("FloorName").Text=="Unfinished floor");
  Click("CancelEdit");Assert(Get<StackPanel>("EntryForm").Visibility==Visibility.Collapsed);
  Click("CalculateButton");Assert(Get<TextBlock>("ErrorText").Text=="");
  Assert(Get<DataGrid>("SlabGrid").Items.Count==2);
  window.Close();Console.WriteLine($"Passed {checks} WPF workflow checks.");
 }
}
