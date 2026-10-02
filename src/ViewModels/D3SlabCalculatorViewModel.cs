using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EngineeringDashboard
{
    public sealed class D3SlabCalculatorViewModel : INotifyPropertyChanged
    {
        private readonly D3SlabInput values = new D3SlabInput();
        private int revision; private bool textErrors;
        private string stocks = TowerStockPlanner.DefaultLengths;
        public D3SlabLayout Result { get; private set; }
        public bool IsOptimizing { get; private set; }
        public string Warning { get; private set; } = "";
        public double LengthMm { get => values.LengthMm; set { values.LengthMm = value; Changed(); } }
        public double WidthMm { get => values.WidthMm; set { values.WidthMm = value; Changed(); } }
        public double ThicknessMm { get => values.ThicknessMm; set { values.ThicknessMm = value; Changed(); } }
        public double LegCapacityKn { get => values.LegCapacityKn; set { values.LegCapacityKn = value; Changed(); } }
        public double MinimumEdgeMm { get => values.MinimumEdgeMm; set { values.MinimumEdgeMm = value; Changed(); } }
        public double MaximumEdgeMm { get => values.MaximumEdgeMm; set { values.MaximumEdgeMm = value; Changed(); } }
        public double LiveLoadKnM2 { get => values.LiveLoadKnM2; set { values.LiveLoadKnM2 = value; Changed(); } }
        public double FormworkKnM2 { get => values.FormworkKnM2; set { values.FormworkKnM2 = value; Changed(); } }
        public bool UseCustomLoads { get => values.UseCustomLoads; set { values.UseCustomLoads = value; Changed(); } }
        public bool Exclude600Standalone { get => values.Exclude600Standalone; set { values.Exclude600Standalone = value; Changed(); } }
        public string StockLengthsText { get => stocks; set { stocks = value; Changed(); } }
        public string[] DeflectionOptions { get; } = { "L/500", "L/350", "L/270" };
        public string DeflectionCriterion { get => "L/" + values.DeflectionDenominator; set { values.DeflectionDenominator = value == "L/500" ? 500 : value == "L/350" ? 350 : value == "L/270" ? 270 : 0; Changed(); } }
        public bool CanOptimize => !IsOptimizing && !textErrors && Valid();
        public bool HasResult => Result != null && !textErrors;
        public string Status => IsOptimizing ? "Optimizing towers and H20 stock…" : Warning;
        public void SetTextErrors(bool value) { if (value == textErrors) return; textErrors = value; Changed(nameof(CanOptimize)); }
        public D3SlabInput CaptureInput()
        {
            if (textErrors) throw new ArgumentException("Correct the highlighted input.");
            var input = new D3SlabInput
            {
                LengthMm = LengthMm,
                WidthMm = WidthMm,
                ThicknessMm = ThicknessMm,
                LegCapacityKn = LegCapacityKn,
                MinimumEdgeMm = MinimumEdgeMm,
                MaximumEdgeMm = MaximumEdgeMm,
                LiveLoadKnM2 = LiveLoadKnM2,
                FormworkKnM2 = FormworkKnM2,
                UseCustomLoads = UseCustomLoads,
                Exclude600Standalone = Exclude600Standalone,
                StockLengthsMm = TowerStockPlanner.ParseLengths(stocks),
                DeflectionDenominator = values.DeflectionDenominator
            }; input.Validate(); return input;
        }
        private bool Valid() { try { CaptureInput(); return true; } catch (ArgumentException) { return false; } }
        private void Changed([CallerMemberName] string name = null)
        {
            Interlocked.Increment(ref revision); Result = null;
            try { CaptureInput(); Warning = ""; } catch (ArgumentException e) { Warning = e.Message; }
            Notify(name); Refresh();
        }
        private void Refresh() { foreach (string name in new[] { nameof(Result), nameof(CanOptimize), nameof(HasResult), nameof(IsOptimizing), nameof(Warning), nameof(Status) }) Notify(name); }
        public async Task OptimizeAsync()
        {
            if (IsOptimizing) return;
            D3SlabInput input; try { input = CaptureInput(); } catch (ArgumentException e) { Warning = e.Message; Refresh(); return; }
            int started = Volatile.Read(ref revision); Result = null; Warning = ""; IsOptimizing = true; Refresh();
            try { var result = await Task.Run(() => D3SlabOptimizer.Optimize(input, () => Volatile.Read(ref revision) != started)); if (started == revision) Result = result; }
            catch (OperationCanceledException) { }
            catch (ArgumentException e) { if (started == revision) Warning = e.Message; }
            finally { IsOptimizing = false; Refresh(); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
