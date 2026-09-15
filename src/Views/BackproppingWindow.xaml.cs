using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using EngineeringDashboard.Backpropping;

namespace EngineeringDashboard
{
    public partial class BackproppingWindow : Window
    {
        private readonly List<SlabInput> slabs = new List<SlabInput>();
        private readonly Dictionary<int, StoreyControls> storeyControls = new Dictionary<int, StoreyControls>();
        private readonly Dictionary<int, string> extensionDrafts = new Dictionary<int, string>();
        private readonly Dictionary<int, string> maximumDrafts = new Dictionary<int, string>();
        private bool updating;
        private bool ready;
        private bool draftDirty;
        private int editing = -1;
        private TemperatureCurve Curve => (TemperatureCurve)Math.Max(0, Temperature.SelectedIndex);
        private sealed class StoreyControls { public ComboBox Prop; public TextBox Extension; public TextBox Maximum; }
        private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(255, 242, 191));

        public BackproppingWindow()
        {
            InitializeComponent();
            updating = true; Temperature.SelectedIndex = 0; updating = false;
            ready = true; NewDraft(); Refresh();
        }

        private static TextBlock Text(string value, bool bold = false) => new TextBlock {
            Text = value, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
        private static string F(double value) => value.ToString("0.00", CultureInfo.CurrentCulture);
        private static double Number(TextBox box, string label, bool positive = false)
        {
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value))
                throw new ArgumentException("Enter " + label + ".");
            BackproppingCalculator.RequireNumber(value, label, positive); return value;
        }
        private static double? OptionalMaximum(TextBox box)
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return null;
            double value = Number(box, "maximum spacing", true);
            if (value < 25) throw new ArgumentException("Maximum spacing must be at least 25 mm.");
            return value;
        }
        private void InvalidateResults()
        {
            ResultsPanel.Children.Clear(); ResultState.Text = "Inputs changed — calculate spacing.";
        }
        private void Draft_Changed(object sender, TextChangedEventArgs e)
        {
            if (!ready || updating) return;
            draftDirty = true; CancelEdit.Visibility = Visibility.Visible; InvalidateResults();
        }
        private void Age_Changed(object sender, TextChangedEventArgs e)
        {
            Draft_Changed(sender, e); if (ready && !updating) UpdateStrength();
        }
        private void UpdateStrength()
        {
            if (double.TryParse(Age.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double days) &&
                days >= 0 && !double.IsInfinity(days))
                StrengthText.Text = $"{F(BackproppingCalculator.Strength(days, Curve))}% strength · Curve {Curve}";
            else StrengthText.Text = "Enter a valid concrete age.";
        }
        private void Temperature_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || updating) return; InvalidateResults(); Refresh();
        }
        private void ProjectMaximum_Changed(object sender, TextChangedEventArgs e)
        {
            if (!ready || updating) return; InvalidateResults(); UpdateLimitHints();
        }
        private string SuggestedName()
        {
            if (slabs.Count == 0) return "";
            var match = Regex.Match(slabs[0].Name, @"^(.*?)(\d+)(\D*)$");
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out int n)) return slabs[0].Name + " · " + slabs.Count + " below";
            int value = n - slabs.Count;
            return match.Groups[1].Value + (value >= 0 ? value.ToString().PadLeft(match.Groups[2].Length, '0') : value.ToString()) + match.Groups[3].Value;
        }
        private void NewDraft()
        {
            editing = -1;
            var previous = slabs.LastOrDefault();
            FillDraft(previous == null ? null : new SlabInput { Name = SuggestedName(), ThicknessMm = previous.ThicknessMm,
                ClearHeightBelowMm = previous.ClearHeightBelowMm, AgeDays = previous.AgeDays, Sidl = previous.Sidl, FinalLiveLoad = previous.FinalLiveLoad });
        }
        private void FillDraft(SlabInput input)
        {
            updating = true;
            FloorName.Text = input?.Name ?? ""; Thickness.Text = input?.ThicknessMm.ToString(CultureInfo.CurrentCulture) ?? "";
            ClearHeight.Text = input?.ClearHeightBelowMm.ToString(CultureInfo.CurrentCulture) ?? "";
            Age.Text = input?.AgeDays.ToString(CultureInfo.CurrentCulture) ?? "0";
            Sidl.Text = input?.Sidl.ToString(CultureInfo.CurrentCulture) ?? ""; LiveLoad.Text = input?.FinalLiveLoad.ToString(CultureInfo.CurrentCulture) ?? "";
            updating = false; draftDirty = false; UpdateStrength();
        }
        private void SaveSlab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (editing < 0 && BackproppingCalculator.CalculateLoads(slabs, Curve).Complete) return;
                int index = editing < 0 ? slabs.Count : editing;
                if (string.IsNullOrWhiteSpace(FloorName.Text)) throw new ArgumentException("Enter a floor name.");
                var input = editing >= 0 ? slabs[editing].Copy() : new SlabInput();
                input.Name = FloorName.Text.Trim(); input.ThicknessMm = Number(Thickness, "slab thickness", true);
                input.ClearHeightBelowMm = Number(ClearHeight, "clear height", true); input.AgeDays = index == 0 ? 0 : Number(Age, "concrete age");
                input.Sidl = Number(Sidl, "SIDL"); input.FinalLiveLoad = Number(LiveLoad, "final live load");
                var proposed = slabs.Select(s => s.Copy()).ToList();
                if (editing < 0) proposed.Add(input); else proposed[index] = input;
                BackproppingCalculator.CalculateLoads(proposed, Curve); // Validate age order before changing saved state.
                if (editing < 0)
                {
                    // New backpropping storey is created below the former lowest slab.
                    if (slabs.Count >= 2 && string.IsNullOrEmpty(slabs.Last().PropId)) slabs.Last().PropId = slabs[slabs.Count-2].PropId;
                    slabs.Add(input);
                }
                else slabs[index] = input;
                ErrorText.Text = ""; InvalidateResults(); NewDraft(); Refresh();
            }
            catch (ArgumentException ex) { ErrorText.Text = ex.Message; }
        }
        private void Edit(int index)
        {
            if (draftDirty) { ErrorText.Text = "Save the current entry or cancel it before editing another slab."; return; }
            editing = index; FillDraft(slabs[index]); Refresh(false);
        }
        private void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            if (SlabGrid.SelectedIndex >= 0) Edit(SlabGrid.SelectedIndex);
            else ErrorText.Text = "Select a slab in the review table.";
        }
        private void CancelEdit_Click(object sender, RoutedEventArgs e) { ErrorText.Text = ""; NewDraft(); Refresh(); }
        private void RemoveLowest_Click(object sender, RoutedEventArgs e)
        {
            if (slabs.Count == 0) return;
            slabs.RemoveAt(slabs.Count-1);
            extensionDrafts.Remove(slabs.Count); maximumDrafts.Remove(slabs.Count);
            extensionDrafts.Remove(slabs.Count-1); maximumDrafts.Remove(slabs.Count-1);
            if (slabs.Count > 0) { slabs.Last().PropId = null; slabs.Last().PropExtensionMm = null; slabs.Last().MaximumSpacingMm = null; }
            if (editing >= slabs.Count || editing < 0) NewDraft();
            ErrorText.Text = ""; InvalidateResults(); Refresh();
        }
        private void Refresh(bool rebuildStoreys = true)
        {
            if (!ready) return;
            var loads = BackproppingCalculator.CalculateLoads(slabs, Curve);
            bool complete = loads.Complete;
            EntryTitle.Text = editing >= 0 ? "2  Editing " + slabs[editing].Name : complete ? "2  Slab stack complete" : slabs.Count == 0 ? "2  Slab being cast" : "2  Add supporting slab below";
            EntryForm.Visibility = complete && editing < 0 ? Visibility.Collapsed : Visibility.Visible;
            SaveSlab.Content = editing >= 0 ? "Save changes" : slabs.Count == 0 ? "Add slab being cast" : "Add supporting slab";
            CancelEdit.Visibility = editing >= 0 || draftDirty ? Visibility.Visible : Visibility.Collapsed;
            Age.IsReadOnly = editing == 0 || editing < 0 && slabs.Count == 0;
            EntryHint.Text = slabs.Count == 0 ? "Enter the active pour first." : "Suggested name is editable. Loads and dimensions carry forward. Age cannot decrease down the stack.";
            UpdateStrength(); ProgressText.Inlines.Clear();
            if (slabs.Count == 0) ProgressText.Text = "Start with the slab being cast.";
            else if (slabs.Count == 1) ProgressText.Text = "Add the first supporting slab to check load transfer.";
            else if (complete) ProgressText.Text = "No further backpropping required below " + slabs[loads.Slabs.Last().Index].Name + " under the adopted assumptions.";
            else
            {
                ProgressText.Inlines.Add(new Run("Add a supporting slab below " + slabs.Last().Name + " · Residual support load: "));
                ProgressText.Inlines.Add(new Run(F(loads.Slabs.Last().Transfer) + " kN/m²") { Foreground = Brushes.Firebrick, FontWeight = FontWeights.Bold });
            }
            ProgressBorder.Background = complete ? new SolidColorBrush(Color.FromRgb(232,245,233)) : Yellow;
            SlabGrid.ItemsSource = null; SlabGrid.ItemsSource = slabs;
            RemoveLowest.IsEnabled = slabs.Count > 0; RemoveLowest.Content = slabs.Count == 0 ? "Remove lowest slab" : "Remove lowest: " + slabs.Last().Name;
            CalculateButton.IsEnabled = slabs.Count >= 2;
            RenderSection(loads); RenderBreakdown(loads);
            if (rebuildStoreys) RenderStoreys(loads);
        }

        private void RenderSection(LoadResult loads)
        {
            SectionPanel.Children.Clear();
            if (slabs.Count == 0) { SectionPanel.Children.Add(Text("Your floor stack will appear here.")); return; }
            for (int i = 0; i < slabs.Count; i++)
            {
                int index = i; var s = slabs[i]; var row = loads.Slabs.FirstOrDefault(r => r.Index == i);
                var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var info = new StackPanel { Margin = new Thickness(0,0,10,14) }; Grid.SetRowSpan(info,2);
                var select = new Button { Content = s.Name, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0,0,0,4) };
                select.Click += (a,b) => Edit(index); info.Children.Add(select);
                info.Children.Add(Text($"{s.ThicknessMm:0.##} mm · {(s.AgeDays > 28 ? "28+" : s.AgeDays.ToString("0.##"))} days · {F(BackproppingCalculator.Strength(s.AgeDays, Curve))}%"));
                info.Children.Add(Text($"SIDL {s.Sidl:0.##} · LL {s.FinalLiveLoad:0.##} kN/m²"));
                info.Children.Add(new Border { Background = Yellow, Padding = new Thickness(5), Margin = new Thickness(0,6,0,0), Child = Text(row == null ? "Outside required support stack" : "↓ " + F(row.DownwardLoad) + " kN/m²") });
                grid.Children.Add(info);
                // The final floor's spanning label can enlarge its row. Keep the slab's
                // top face at the storey boundary instead of centering it below the props.
                var slab = new Rectangle { Height = 16, VerticalAlignment = VerticalAlignment.Top,
                    Fill = editing == i ? Brushes.Goldenrod : Brushes.DarkGray }; Grid.SetColumn(slab,1); grid.Children.Add(slab);
                if (i < slabs.Count-1)
                {
                    var support = new Grid { MinHeight = 112 }; Grid.SetColumn(support,1); Grid.SetRow(support,1);
                    support.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); support.RowDefinitions.Add(new RowDefinition());
                    if (i == 0)
                    {
                        var beams = new StackPanel(); var ends = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Height = 22 };
                        for (int b = 0; b < 4; b++)
                        {
                            var end = new Grid { Width = 16, Height = 22 };
                            end.Children.Add(new Rectangle { Width = 5, Fill = Brushes.Goldenrod });
                            end.Children.Add(new Rectangle { Height = 5, VerticalAlignment = VerticalAlignment.Top, Fill = Brushes.Goldenrod });
                            end.Children.Add(new Rectangle { Height = 5, VerticalAlignment = VerticalAlignment.Bottom, Fill = Brushes.Goldenrod }); ends.Children.Add(end);
                        }
                        beams.Children.Add(ends); beams.Children.Add(new Border { Background = Brushes.Goldenrod, BorderBrush = Brushes.DarkGoldenrod, BorderThickness = new Thickness(0,4,0,4), Child = new TextBlock { Text = "Primary H20", TextAlignment = TextAlignment.Center, FontSize = 11 } }); support.Children.Add(beams);
                    }
                    var props = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, MinHeight = 76 }; Grid.SetRow(props,1);
                    for (int p = 0; p < 3; p++)
                    {
                        var prop = new Grid { Width = 20 }; prop.Children.Add(new Rectangle { Width = 7, Fill = Brushes.SlateGray });
                        prop.Children.Add(new Rectangle { Height = 4, Fill = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Top });
                        prop.Children.Add(new Rectangle { Height = 4, Fill = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Bottom }); props.Children.Add(prop);
                    }
                    support.Children.Add(props); grid.Children.Add(support);
                }
                SectionPanel.Children.Add(grid);
            }
        }

        private void RenderStoreys(LoadResult loads)
        {
            StoreyPanel.Children.Clear(); storeyControls.Clear();
            foreach (var row in loads.Slabs.Where(r => r.Index > 0 && r.Transfer > 0 && r.Index < slabs.Count-1))
            {
                int index = row.Index; var slab = slabs[index];
                var group = new GroupBox { Header = slab.Name + " → " + slabs[index+1].Name, Padding = new Thickness(8), Margin = new Thickness(0,0,0,8) };
                var panel = new StackPanel(); var grid = new Grid(); for (int c=0;c<3;c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                var controls = new StoreyControls { Prop = new ComboBox { ItemsSource = BackproppingPropCatalog.All, SelectedItem = BackproppingPropCatalog.All.FirstOrDefault(p => p.Name == slab.PropId) },
                    Extension = new TextBox { Text = extensionDrafts.TryGetValue(index, out string ext) ? ext : (slab.PropExtensionMm ?? slab.ClearHeightBelowMm).ToString(CultureInfo.CurrentCulture) },
                    Maximum = new TextBox { Text = maximumDrafts.TryGetValue(index, out string max) ? max : slab.MaximumSpacingMm?.ToString(CultureInfo.CurrentCulture) ?? "" } };
                storeyControls[index] = controls;
                string[] labels = { "Selected prop type", "Effective prop extension (mm)", "Maximum override (mm)" };
                Control[] inputs = { controls.Prop, controls.Extension, controls.Maximum };
                for (int c=0;c<3;c++) { var field = new StackPanel(); field.Children.Add(Text(labels[c])); field.Children.Add(inputs[c]); Grid.SetColumn(field,c); grid.Children.Add(field); }
                panel.Children.Add(grid); panel.Children.Add(Text("Extension defaults to clear height. Adjust for head/base packers. Blank maximum uses the project limit."));
                group.Content = panel; StoreyPanel.Children.Add(group);
                controls.Prop.SelectionChanged += (a,b) => { slabs[index].PropId = (controls.Prop.SelectedItem as BackproppingProp)?.Name; InvalidateResults(); };
                controls.Extension.TextChanged += (a,b) => { extensionDrafts[index] = controls.Extension.Text; InvalidateResults(); };
                controls.Maximum.TextChanged += (a,b) => { maximumDrafts[index] = controls.Maximum.Text; InvalidateResults(); };
            }
            if (storeyControls.Count == 0) StoreyPanel.Children.Add(Text(loads.Complete ? "No backpropping storey required." : "Add the next supporting slab to configure backpropping."));
        }
        private void UpdateLimitHints() { ResultState.Text = "Spacing limit changed — calculate spacing."; }
        private void RenderBreakdown(LoadResult loads)
        {
            BreakdownPanel.Children.Clear();
            foreach (var row in loads.Slabs)
            {
                string detail = row.Index == 0 ? $"Concrete {F(row.SelfWeight)} + formwork 0.50 + working 0.75 + heaping {F(loads.Heaping)} = {F(loads.PourLoad)}"
                    : $"Incoming {F(row.Incoming)} + self-weight {F(row.SelfWeight)} + construction live {F(row.ConstructionLive)} = demand {F(row.Demand)}\nAdopted capacity {F(row.Capacity)}; required transfer {F(row.Transfer)}; downward load {F(row.DownwardLoad)}";
                BreakdownPanel.Children.Add(new TextBlock { Text = slabs[row.Index].Name + "\n" + detail, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap });
            }
        }
        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            ResultsPanel.Children.Clear(); ErrorText.Text = "";
            try
            {
                if (editing >= 0 || draftDirty) throw new ArgumentException("Save or cancel the current slab entry before calculating.");
                double? projectMax = OptionalMaximum(ProjectMaximum);
                var loads = BackproppingCalculator.CalculateLoads(slabs, Curve);
                if (!loads.Complete) throw new ArgumentException("Add the next supporting slab before calculating final spacing.");
                foreach (var row in loads.Slabs.Where(r => r.Index > 0 && r.Transfer > 0))
                {
                    var s = slabs[row.Index]; var controls = storeyControls[row.Index];
                    var panel = new StackPanel(); panel.Children.Add(Text(s.Name + " → " + slabs[row.Index+1].Name, true));
                    panel.Children.Add(Text("RECOMMENDED SQUARE GRID"));
                    try
                    {
                        string prop = (controls.Prop.SelectedItem as BackproppingProp)?.Name;
                        double extension = Number(controls.Extension, "effective prop extension", true);
                        if (extension > s.ClearHeightBelowMm) throw new ArgumentException("Prop extension cannot exceed the clear height.");
                        double? maximum = OptionalMaximum(controls.Maximum) ?? projectMax;
                        var result = BackproppingCalculator.CalculateSpacing(row.DownwardLoad, prop, extension, maximum);
                        panel.Children.Add(new TextBlock { Text = $"{result.SpacingMm:0} × {result.SpacingMm:0} mm", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                        panel.Children.Add(Text(result.GoverningLimit + (result.HasProjectLimit ? "" : " · Capacity-based spacing only — slab support limit not checked"), true));
                        panel.Children.Add(Text($"{prop} · Table extension {result.LookupExtensionMm:0} mm · Capacity {F(result.CapacityKn)} kN\nLoad down {F(row.DownwardLoad)} kN/m² · Per prop {F(result.PropLoadKn)} kN · Utilization {F(result.UtilizationPercent)}%"));
                    }
                    catch (ArgumentException ex) { panel.Children.Add(new TextBlock { Text = "— × — mm", FontSize = 32, FontWeight = FontWeights.Bold }); panel.Children.Add(new TextBlock { Text = ex.Message, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }); }
                    ResultsPanel.Children.Add(new Border { Background = Yellow, BorderBrush = Brushes.Goldenrod, BorderThickness = new Thickness(6,0,0,0), Padding = new Thickness(14), Margin = new Thickness(0,6,0,6), Child = panel });
                }
                if (ResultsPanel.Children.Count == 0) ResultsPanel.Children.Add(Text("No backpropping required under the adopted assumptions.", true));
                ResultState.Text = "Results use the adopted capacity-based assumptions; review any incomplete storeys.";
                ResultsPanel.UpdateLayout(); ResultsPanel.BringIntoView();
            }
            catch (ArgumentException ex) { ErrorText.Text = ex.Message; ResultState.Text = "Calculation incomplete."; }
        }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
