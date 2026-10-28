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
    public sealed class BackproppingDocument
    {
        public int Version { get; set; } = 1;
        public string Kind { get; set; } = "Backpropping";
        public TemperatureCurve Curve { get; set; }
        public double? ProjectMaximumMm { get; set; }
        public List<SlabInput> Slabs { get; set; } = new List<SlabInput>();
    }
    public partial class BackproppingWindow : Window
    {
        private readonly List<SlabInput> slabs = new List<SlabInput>();
        private readonly Dictionary<int, StoreyControls> storeyControls = new Dictionary<int, StoreyControls>();
        private readonly Dictionary<int, string> extensionDrafts = new Dictionary<int, string>();
        private readonly Dictionary<int, string> maximumDrafts = new Dictionary<int, string>();
        private sealed class SessionState
        {
            public List<SlabInput> Slabs;
            public Dictionary<int,string> Maximums;
            public string ProjectMaximum;
            public string[] Draft;
            public int Curve, Editing;
            public bool Dirty, HadResults;
        }
        private static SessionState retained;
        private bool updating;
        private bool ready;
        private bool draftDirty;
        private int editing = -1;
        private TemperatureCurve Curve => (TemperatureCurve)Math.Max(0, Temperature.SelectedIndex);
        private sealed class StoreyControls { public ComboBox Prop; public TextBox Maximum; }
        private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(255, 242, 191));

        public BackproppingWindow()
        {
            InitializeComponent();
            CalculatorToolbarHost.Content=new DashboardToolbar(this,"Backpropping",()=>LoadCalculation_Click(null,null),()=>SaveCalculation_Click(null,null));
            updating = true; Temperature.SelectedIndex = 0; updating = false;
            ready = true; NewDraft(); Refresh();
            if(retained != null) RestoreSession(retained);
            Closed += (sender,args) => retained = CaptureSession();
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
                FloorHeightBelowMm = previous.FloorHeightBelowMm, AgeDays = previous.AgeDays, Sidl = previous.Sidl, FinalLiveLoad = previous.FinalLiveLoad });
        }
        private void FillDraft(SlabInput input)
        {
            updating = true;
            FloorName.Text = input?.Name ?? ""; Thickness.Text = input?.ThicknessMm.ToString(CultureInfo.CurrentCulture) ?? "";
            FloorHeight.Text = input?.FloorHeightBelowMm.ToString(CultureInfo.CurrentCulture) ?? "";
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
                input.FloorHeightBelowMm = Number(FloorHeight, "floor-to-floor height", true); input.AgeDays = index == 0 ? 0 : Number(Age, "concrete age");
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
            maximumDrafts.Remove(slabs.Count);
            maximumDrafts.Remove(slabs.Count-1);
            if (slabs.Count > 0) { slabs.Last().PropId = null; slabs.Last().MaximumSpacingMm = null; }
            if (editing >= slabs.Count || editing < 0) NewDraft();
            ErrorText.Text = ""; InvalidateResults(); Refresh();
        }
        private void Refresh(bool rebuildStoreys = true)
        {
            if (!ready) return;
            var loads = BackproppingCalculator.CalculateLoads(slabs, Curve);
            bool complete = loads.Complete;
            EntryTitle.Text = editing >= 0 ? "2  Editing " + slabs[editing].Name : complete ? "2  Slab stack complete" : slabs.Count == 0 ? "2  Slab being cast" : "2  Add supporting slab below";
            EntryForm.Visibility = complete && editing < 0 && !draftDirty ? Visibility.Collapsed : Visibility.Visible;
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
            RemoveLowest.IsEnabled = slabs.Count > 0; DashboardToolbar.Icon(RemoveLowest,"Delete",slabs.Count == 0 ? "Remove lowest slab" : "Remove lowest slab: " + slabs.Last().Name);
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
                var panel = new StackPanel(); var grid = new Grid(); for (int c=0;c<2;c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                var controls = new StoreyControls { Prop = new ComboBox { ItemsSource = BackproppingPropCatalog.All, SelectedItem = BackproppingPropCatalog.All.FirstOrDefault(p => p.Name == slab.PropId) },
                    Maximum = new TextBox { Text = maximumDrafts.TryGetValue(index, out string max) ? max : slab.MaximumSpacingMm?.ToString(CultureInfo.CurrentCulture) ?? "" } };
                storeyControls[index] = controls;
                string[] labels = { "Selected prop type", "Maximum override (mm)" };
                Control[] inputs = { controls.Prop, controls.Maximum };
                for (int c=0;c<2;c++) { var field = new StackPanel(); field.Children.Add(Text(labels[c])); field.Children.Add(inputs[c]); Grid.SetColumn(field,c); grid.Children.Add(field); }
                panel.Children.Add(grid); panel.Children.Add(Text($"Prop extension: {slab.PropExtensionMm:0.##} mm (automatic). Blank maximum uses the project limit."));
                group.Content = panel; StoreyPanel.Children.Add(group);
                controls.Prop.SelectionChanged += (a,b) => { slabs[index].PropId = (controls.Prop.SelectedItem as BackproppingProp)?.Name; InvalidateResults(); };
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
                    try
                    {
                        string prop = (controls.Prop.SelectedItem as BackproppingProp)?.Name;
                        double extension = s.PropExtensionMm;
                        double? maximum = OptionalMaximum(controls.Maximum) ?? projectMax;
                        var result = BackproppingCalculator.CalculateSpacing(row.DownwardLoad, prop, extension, maximum);
                        panel.Children.Add(new TextBlock { Text = $"{result.SpacingMm:0} × {result.SpacingMm:0} mm", FontSize = 32, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                        panel.Children.Add(Text($"Load per prop: {F(result.PropLoadKn)} kN"));
                        panel.Children.Add(Text($"Utilization: {F(result.UtilizationPercent)}%"));
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
        private SessionState CaptureSession() => new SessionState {
            Slabs=slabs.Select(s=>s.Copy()).ToList(), Maximums=new Dictionary<int,string>(maximumDrafts),
            ProjectMaximum=ProjectMaximum.Text, Curve=Temperature.SelectedIndex, Editing=editing, Dirty=draftDirty,
            HadResults=ResultsPanel.Children.Count>0,
            Draft=new[]{FloorName.Text,Thickness.Text,FloorHeight.Text,Age.Text,Sidl.Text,LiveLoad.Text}
        };
        private void RestoreSession(SessionState state)
        {
            updating=true; slabs.Clear(); slabs.AddRange(state.Slabs.Select(s=>s.Copy()));
            maximumDrafts.Clear(); foreach(var pair in state.Maximums) maximumDrafts[pair.Key]=pair.Value;
            Temperature.SelectedIndex=state.Curve; ProjectMaximum.Text=state.ProjectMaximum;
            editing=state.Editing; draftDirty=state.Dirty;
            TextBox[] boxes={FloorName,Thickness,FloorHeight,Age,Sidl,LiveLoad};
            for(int i=0;i<boxes.Length;i++) boxes[i].Text=state.Draft[i];
            updating=false; Refresh();
            if(state.HadResults && !draftDirty && editing<0) Calculate_Click(this,new RoutedEventArgs());
        }
        internal void StartNewCalculation()
        {
            slabs.Clear(); maximumDrafts.Clear(); extensionDrafts.Clear(); retained=null;
            updating=true; Temperature.SelectedIndex=0; ProjectMaximum.Text=""; updating=false;
            ErrorText.Text=""; NewDraft(); Refresh(); InvalidateResults(); ResultState.Text="New calculation — enter the slab being cast.";
        }
        private void NewCalculation_Click(object sender,RoutedEventArgs e) => StartNewCalculation();
        internal BackproppingDocument CaptureCalculation()
        {
            if(editing>=0 || draftDirty) throw new ArgumentException("Save or cancel the current slab entry before saving or exporting the calculation.");
            var document=new BackproppingDocument { Curve=Curve, ProjectMaximumMm=OptionalMaximum(ProjectMaximum), Slabs=slabs.Select(s=>s.Copy()).ToList() };
            foreach(var pair in maximumDrafts)
                if(pair.Key>=0 && pair.Key<document.Slabs.Count)
                    document.Slabs[pair.Key].MaximumSpacingMm=OptionalMaximum(new TextBox{Text=pair.Value});
            ValidateDocument(document); return document;
        }
        private static void ValidateDocument(BackproppingDocument document)
        {
            if(document==null || document.Version!=1 || document.Kind!="Backpropping" || document.Slabs==null) throw new ArgumentException("This is not a supported backpropping calculation file.");
            if(!Enum.IsDefined(typeof(TemperatureCurve),document.Curve)) throw new ArgumentException("Invalid temperature curve.");
            Action<double?> validateMaximum = value => { if(value.HasValue) { BackproppingCalculator.RequireNumber(value.Value,"Maximum spacing",true); if(value.Value<25)throw new ArgumentException("Maximum spacing must be at least 25 mm."); } };
            validateMaximum(document.ProjectMaximumMm);
            BackproppingCalculator.CalculateLoads(document.Slabs,document.Curve);
            foreach(var slab in document.Slabs)
            {
                validateMaximum(slab.MaximumSpacingMm);
                if(slab.PropId!=null && !BackproppingPropCatalog.All.Any(p=>p.Name==slab.PropId))throw new ArgumentException("Unknown prop configuration: "+slab.PropId);
            }
        }
        internal void RestoreCalculation(BackproppingDocument document)
        {
            ValidateDocument(document); // Preserve the current calculation if the file is invalid.
            updating=true; slabs.Clear(); slabs.AddRange(document.Slabs.Select(s=>s.Copy()));
            maximumDrafts.Clear(); extensionDrafts.Clear(); Temperature.SelectedIndex=(int)document.Curve;
            ProjectMaximum.Text=document.ProjectMaximumMm?.ToString(CultureInfo.CurrentCulture) ?? "";
            updating=false; ErrorText.Text=""; NewDraft(); Refresh(); InvalidateResults();
            ResultState.Text="Calculation loaded — calculate spacing to review results.";
        }
        private void SaveCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.Save(this,CaptureCalculation(),"Backpropping"));
        private void LoadCalculation_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=> { var path=CalculationFiles.ChooseLoad(this); if(path!=null) RestoreCalculation(CalculationFiles.Read<BackproppingDocument>(path)); });
        internal List<CalculationField> ReportResults(BackproppingDocument document)
        {
            ValidateDocument(document);
            var loads=BackproppingCalculator.CalculateLoads(document.Slabs,document.Curve);
            if(!loads.Complete) throw new ArgumentException("Complete the supporting slab stack before exporting a report.");
            var results=new List<CalculationField>();
            foreach(var row in loads.Slabs)
            {
                var slab=document.Slabs[row.Index];
                results.Add(CalculationFiles.Field(slab.Name + " · load breakdown (kN/m²)", $"Incoming {row.Incoming:0.000}; self-weight {row.SelfWeight:0.000}; construction live {row.ConstructionLive:0.000}; demand {row.Demand:0.000}; adopted capacity {row.Capacity:0.000}; transfer {row.Transfer:0.000}; downward {row.DownwardLoad:0.000}"));
                if(row.Index==0) results.Add(CalculationFiles.Field("Pour components (kN/m²)",$"Concrete {row.SelfWeight:0.000} + formwork 0.50 + working 0.75 + heaping {loads.Heaping:0.000} = {loads.PourLoad:0.000}"));
                else results.Add(CalculationFiles.Field(slab.Name+" · strength (%)",row.StrengthPercent));
                if(row.Index>0 && row.Transfer>0)
                {
                    var spacing=BackproppingCalculator.CalculateSpacing(row.DownwardLoad,slab.PropId,slab.PropExtensionMm,slab.MaximumSpacingMm ?? document.ProjectMaximumMm);
                    results.Add(CalculationFiles.Field(slab.Name+" → "+document.Slabs[row.Index+1].Name,
                        $"Spacing {spacing.SpacingMm:0} × {spacing.SpacingMm:0} mm; load/prop {spacing.PropLoadKn:0.000} kN; capacity {spacing.CapacityKn:0.000} kN; utilization {spacing.UtilizationPercent:0.00}%; extension {slab.PropExtensionMm:0.##} mm; table row {spacing.LookupExtensionMm:0} mm; governing: {spacing.GoverningLimit}"));
                }
            }
            results.Add(CalculationFiles.Field("Stack check","Complete under the adopted load-transfer stopping rule.")); return results;
        }
        private void ExportReport_Click(object sender,RoutedEventArgs e) => CalculationFiles.Run(this,()=>CalculationFiles.ExportHtml(this,"Backpropping",BuildReport()));
        internal string BuildReport()
        {
            var document=CaptureCalculation(); var results=ReportResults(document);
            var inputs=new List<CalculationField> { CalculationFiles.Field("Temperature curve",document.Curve), CalculationFiles.Field("Project maximum spacing (mm)",document.ProjectMaximumMm?.ToString(CultureInfo.InvariantCulture) ?? "Not supplied") };
            foreach(var slab in document.Slabs)
                inputs.Add(CalculationFiles.Field(slab.Name,$"Thickness {slab.ThicknessMm:0.##} mm; height below {slab.FloorHeightBelowMm:0.##} mm; age {slab.AgeDays:0.##} days; SIDL {slab.Sidl:0.###} kN/m²; final live {slab.FinalLiveLoad:0.###} kN/m²; prop {slab.PropId ?? "Not selected"}; maximum override {slab.MaximumSpacingMm?.ToString(CultureInfo.InvariantCulture) ?? "None"} mm"));
            return CalculationFiles.Report("Backpropping",inputs,results,
                "Adopted capacity approximation, not an official Doka load-distribution formula or structural verification. Repeated aligned uniform slab zone. Capacity = strength fraction × (self-weight + SIDL + final live). Transfer = max(0, demand - capacity). Add 0.40 kN/m² per required reprop storey.\n"+
                "Concrete 25 kN/m³; main formwork 0.50; working 0.75; heaping min(1.75,max(0.75,10% concrete weight)); construction live 0.75 at the first supporting slab and zero below (area loads in kN/m²).\n"+
                "Strength anchors day/%: A 0/0,2/50,4/70,12/80,20/90,28/100; B 0/0,4/50,8/70,28/100; C 0/0,6/50,12/70,28/100. Linear interpolation, cap 100%.\n"+
                "Square spacing = floor(min(1000 sqrt(prop capacity/downward load), supplied maximum)/25) × 25 mm. Storey maximum overrides project maximum. Without a maximum, spacing checks prop capacity only.\n"+
                "Prop extension = height below - upper slab thickness, direct head/base contact and restrained temporary reshoring table conditions. Main shores are not sized. Slab punching, local bearing, edges, offsets, stiffness/settlement, lateral stability and stripping sequence are excluded.");
        }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
