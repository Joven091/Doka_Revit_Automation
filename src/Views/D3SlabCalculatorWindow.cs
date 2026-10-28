using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace EngineeringDashboard
{
    public sealed class D3SlabCalculatorWindow : Window
    {
        public D3SlabCalculatorViewModel Model { get; }
        public bool ChangeSystemRequested { get; private set; }
        internal D3SlabPlan Plan { get; } = new D3SlabPlan();
        internal ScrollViewer PlanScroll { get; }
        internal Button OptimizeButton { get; }
        internal Button SaveButton { get; }
        internal readonly Dictionary<string, TextBox> Inputs = new Dictionary<string, TextBox>();
        private readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        private readonly TextBlock legs = new TextBlock(), stock = new TextBlock(), status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly Border results;
        private const string SavedFields = "LengthMm,WidthMm,ThicknessMm,LegCapacityKn,MinimumEdgeMm,MaximumEdgeMm,UseCustomLoads,LiveLoadKnM2,FormworkKnM2,Exclude600Standalone,StockLengthsText,DeflectionCriterion";
        public D3SlabCalculatorWindow(D3SlabCalculatorViewModel model = null)
        {
            Model = model ?? new D3SlabCalculatorViewModel(); DataContext = Model;
            Title = "Slab Calculator · D3 Shoring System"; Width = 1160; Height = 860; MinWidth = 900; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)); FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MyRevitAddin;component/Views/CalculatorStyles.xaml", UriKind.Relative) });
            var root = new Grid { Margin = new Thickness(16), Background = Background }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; var change = new Button { Content = "Change system", Padding = new Thickness(12, 5, 12, 5) }; DockPanel.SetDock(change, Dock.Right); change.Click += (s, e) => { ChangeSystemRequested = true; Close(); }; header.Children.Add(change); header.Children.Add(new TextBlock { Text = "Slab calculator · D3 Shoring System", FontSize = 22, FontWeight = FontWeights.SemiBold }); root.Children.Add(header);
            var workspace = new Grid(); Grid.SetRow(workspace, 1); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); workspace.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(workspace);
            var inputPanel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            inputPanel.Children.Add(Heading("Slab and tower loads"));
            AddInput(inputPanel, "Slab length (mm)", nameof(Model.LengthMm)); AddInput(inputPanel, "Slab width (mm)", nameof(Model.WidthMm)); AddInput(inputPanel, "Slab thickness (mm)", nameof(Model.ThicknessMm)); AddInput(inputPanel, "Permissible leg load (kN)", nameof(Model.LegCapacityKn));
            inputPanel.Children.Add(new TextBlock { Text = "Deflection criterion", Margin = new Thickness(0, 8, 0, 0) });
            var criterion = new ComboBox { ItemsSource = Model.DeflectionOptions }; criterion.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(Model.DeflectionCriterion)) { Mode = BindingMode.TwoWay }); inputPanel.Children.Add(criterion);
            var advanced = new StackPanel(); AddInput(advanced, "Available H20 lengths — both layers (mm)", nameof(Model.StockLengthsText));
            AddInput(advanced, "Minimum beam-shutter setback (mm)", nameof(Model.MinimumEdgeMm)); AddInput(advanced, "Maximum beam-shutter setback (mm)", nameof(Model.MaximumEdgeMm));
            var exclusion = new CheckBox { Content = "Exclude standalone 600 mm towers", Margin = new Thickness(0, 8, 0, 0) }; exclusion.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(Model.Exclude600Standalone)) { Mode = BindingMode.TwoWay }); advanced.Children.Add(exclusion);
            var custom = new CheckBox { Content = "Custom loads", Margin = new Thickness(0, 8, 0, 0) }; custom.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(Model.UseCustomLoads)) { Mode = BindingMode.TwoWay }); advanced.Children.Add(custom);
            var loadFields = new StackPanel(); loadFields.SetBinding(IsEnabledProperty, new Binding(nameof(Model.UseCustomLoads))); AddInput(loadFields, "Placing + working load (kN/m²)", nameof(Model.LiveLoadKnM2)); AddInput(loadFields, "Formwork load (kN/m²)", nameof(Model.FormworkKnM2)); advanced.Children.Add(loadFields);
            inputPanel.Children.Add(new Expander { Header = "Stock, edges and loads", Content = advanced, Margin = new Thickness(0, 12, 0, 0) });
            OptimizeButton = new Button { Content = "Optimize slab layout", Margin = new Thickness(0, 14, 0, 8), Padding = new Thickness(10) }; OptimizeButton.SetBinding(IsEnabledProperty, new Binding(nameof(Model.CanOptimize))); OptimizeButton.Click += async (s, e) => { RefreshValidation(); await Model.OptimizeAsync(); }; inputPanel.Children.Add(OptimizeButton);
            status.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Status))); inputPanel.Children.Add(status);
            inputPanel.Children.Add(new Expander { Header = "Calculation scope", Margin = new Thickness(0, 14, 0, 0), Content = new TextBlock { Text = "Rectangular slab surrounded by beams. Only the H20-covered rectangle is loaded; beam shutters and edge strips are excluded. Single secondary H20s; double primary pairs. Secondary overlap ends extend at least 150 mm beyond the shared primary centre, with equal sharing where stock overlaps. The largest passing nominal secondary spacing is selected, then the search ranks legs, timber length, piece count and edge setback. The best evaluated layout is returned; a global minimum is not proven. Sheathing, local bearing, height/bracing and foundations are not checked; enter a permissible leg capacity for the actual system.", TextWrapping = TextWrapping.Wrap } });
            workspace.Children.Add(new ScrollViewer { Content = inputPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var review = new Grid(); review.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); review.RowDefinitions.Add(new RowDefinition()); Grid.SetColumn(review, 2); workspace.Children.Add(review);
            var resultPanel = new StackPanel(); resultPanel.Children.Add(summary);
            var loadsToggle = new CheckBox { Content = "Show leg loads in plan", Margin = new Thickness(0, 8, 0, 0) }; loadsToggle.Checked += (s, e) => { Plan.ShowLegLoads = true; Plan.InvalidateVisual(); }; loadsToggle.Unchecked += (s, e) => { Plan.ShowLegLoads = false; Plan.InvalidateVisual(); }; resultPanel.Children.Add(loadsToggle);
            resultPanel.Children.Add(new Expander { Header = "Leg loads and coordinates", Content = new ScrollViewer { Content = legs, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Margin = new Thickness(0, 8, 0, 0) });
            resultPanel.Children.Add(new Expander { Header = "H20 stock and checks", Content = new ScrollViewer { Content = stock, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }, Margin = new Thickness(0, 8, 0, 0) });
            results = new Border { Child = resultPanel, Padding = new Thickness(14), Background = new SolidColorBrush(Color.FromRgb(255, 246, 214)), Margin = new Thickness(0, 0, 0, 12) }; review.Children.Add(results);
            PlanScroll = new ScrollViewer { Content = Plan, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(PlanScroll, 1); review.Children.Add(PlanScroll);
            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
            var close = new Button { Content = "Close", MinWidth = 80 }; DockPanel.SetDock(close, Dock.Right); close.Click += (s, e) => Close(); footer.Children.Add(close);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; footer.Children.Add(actions);
            SaveButton = ActionButton("Save calculation", () => CalculationFiles.Save(this, CaptureCalculation(), "D3 slab")); SaveButton.SetBinding(IsEnabledProperty, new Binding(nameof(Model.HasResult))); actions.Children.Add(SaveButton);
            actions.Children.Add(ActionButton("Load calculation", () => { var path = CalculationFiles.ChooseLoad(this); if (path != null) RestoreCalculation(CalculationFiles.Read<CalculationRecord>(path)); }));
            Content = root; AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>((s, e) => { RefreshValidation(); Dispatcher.BeginInvoke(new Action(RefreshValidation), System.Windows.Threading.DispatcherPriority.DataBind); }));
            Model.PropertyChanged += ModelChanged; Closed += (s, e) => Model.PropertyChanged -= ModelChanged; UpdateResult();
        }
        private Button ActionButton(string text, Action action) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) }; b.Click += (s, e) => CalculationFiles.Run(this, action); return b; }
        private static TextBlock Heading(string text) => new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        private void AddInput(Panel panel, string label, string name)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 0) }); var box = new TextBox(); box.SetBinding(TextBox.TextProperty, new Binding(name) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true, NotifyOnValidationError = true });
            System.Windows.Automation.AutomationProperties.SetName(box, label); Inputs.Add(name, box); panel.Children.Add(box);
        }
        private void RefreshValidation() => Model.SetTextErrors(Inputs.Values.Any(b => b.IsEnabled && Validation.GetHasError(b)));
        private void ModelChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Model.Result)) UpdateResult(); if (e.PropertyName == nameof(Model.UseCustomLoads)) Dispatcher.BeginInvoke(new Action(RefreshValidation), System.Windows.Threading.DispatcherPriority.DataBind); }
        private void UpdateResult()
        {
            results.Visibility = Model.HasResult ? Visibility.Visible : Visibility.Collapsed; summary.Text = Model.Result?.Summary ?? ""; legs.Text = Model.Result?.LegSchedule ?? ""; stock.Text = Model.Result?.Schedule ?? "";
            summary.ToolTip = Model.Result?.SearchNote;
            Plan.SetLayout(Model.Result); PlanScroll.ScrollToHome();
        }
        internal CalculationRecord CaptureCalculation() { RefreshValidation(); if (!Model.HasResult) throw new ArgumentException("Optimize the current valid inputs before saving."); return CalculationFiles.Capture("D3Slab", Model, SavedFields); }
        internal void RestoreCalculation(CalculationRecord record)
        {
            var candidate = new D3SlabCalculatorViewModel(); CalculationFiles.Apply(record, "D3Slab", candidate, SavedFields); candidate.CaptureInput();
            CalculationFiles.Apply(record, "D3Slab", Model, SavedFields); foreach (var box in Inputs.Values) box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); RefreshValidation();
        }
        internal string BuildReport()
        {
            var record = CaptureCalculation(); var result = Model.Result;
            var outputs = new[] { CalculationFiles.Field("Summary", result.Summary), CalculationFiles.Field("CoveredAreaM2", result.CoveredAreaM2), CalculationFiles.Field("SetbackAlongPrimaryMm", result.EdgeXmm), CalculationFiles.Field("SetbackAcrossPrimaryMm", result.EdgeYmm), CalculationFiles.Field("PrimaryDirection", result.SwapAxes ? "Slab width" : "Slab length"), CalculationFiles.Field("Search", result.SearchNote), CalculationFiles.Field("LegSchedule", result.LegSchedule), CalculationFiles.Field("StockSchedule", result.Schedule) };
            return CalculationFiles.Report("Slab — D3 Shoring System", record.Inputs, outputs, "Only H20-covered area is loaded. Secondary overlaps share the distributed load equally, counted once. Each primary receives the actual secondary reactions as point loads. All stock, support spans, overhangs, individual leg reactions and deflection criteria are checked. Sheathing and system height/bracing/bearing/foundations are outside this optimizer.");
        }
    }
}
