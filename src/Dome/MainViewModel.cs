#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace EngineeringDashboard.Dome;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
}

public class ViewModelBase : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private Dictionary<string, string> errors = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => errors.Count > 0;
    public IEnumerable GetErrors(string? propertyName) => string.IsNullOrEmpty(propertyName)
        ? errors.Values.ToArray() : errors.TryGetValue(propertyName!, out var error) ? new[] { error } : Array.Empty<string>();
    protected void ReplaceErrors(Dictionary<string, string> next)
    {
        var changed = errors.Keys.Union(next.Keys).Where(k => (errors.TryGetValue(k, out var oldError) ? oldError : null) != (next.TryGetValue(k, out var newError) ? newError : null)).ToArray();
        errors = next;
        foreach (string key in changed) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(key));
        OnPropertyChanged(nameof(HasErrors));
    }
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    internal static bool ReadNumber(string text, string name, string label, double minimum, bool inclusiveMinimum,
        double maximum, Dictionary<string, string> errors, out double value)
    {
        if (!MathEvaluator.TryEvaluate(text, out value, out string error)) { errors[name] = $"{label}: {error}"; return false; }
        if ((inclusiveMinimum ? value < minimum : value <= minimum) || value > maximum)
        {
            errors[name] = $"{label}: enter a value {(inclusiveMinimum ? "at least" : "greater than")} {minimum} and at most {maximum}.";
            return false;
        }
        return true;
    }
}

public class MainViewModel : ViewModelBase
{
    internal Window? DialogOwner { get; set; }
    public const int MaxElements = 100;
    private string primaryBeamSize = "2", secondaryBeamSize = "1", combinedLoad = "5";
    private double? supportArea, loadTransferredToSecondary, totalLoad;
    private string validationMessage = "";
    public MainViewModel()
    {
        Elements.CollectionChanged += (_, _) => CalculateTotalLoad();
        AddSlabCommand = new RelayCommand(_ => Elements.Add(new SlabViewModel(this)), _ => Elements.Count < MaxElements);
        AddBeamCommand = new RelayCommand(_ => Elements.Add(new BeamViewModel(this)), _ => Elements.Count < MaxElements);
        CopyResultsCommand = new RelayCommand(_ => CopyResults(), _ => IsCalculationValid);
        ShowDiagramCommand = new RelayCommand(_ => ShowDiagram(), _ => IsCalculationValid);
        CalculateTotalLoad();
    }
    public ObservableCollection<ElementViewModel> Elements { get; } = new();
    public string PrimaryBeamSize { get => primaryBeamSize; set { if (SetProperty(ref primaryBeamSize, value)) CalculateTotalLoad(); } }
    public string SecondaryBeamSize { get => secondaryBeamSize; set { if (SetProperty(ref secondaryBeamSize, value)) CalculateTotalLoad(); } }
    public string CombinedLoad { get => combinedLoad; set { if (SetProperty(ref combinedLoad, value)) CalculateTotalLoad(); } }
    public double? SupportArea { get => supportArea; private set => SetProperty(ref supportArea, value); }
    public double? LoadTransferredToSecondary { get => loadTransferredToSecondary; private set => SetProperty(ref loadTransferredToSecondary, value); }
    public double? TotalLoad { get => totalLoad; private set => SetProperty(ref totalLoad, value); }
    public string ValidationMessage { get => validationMessage; private set => SetProperty(ref validationMessage, value); }
    public bool IsCalculationValid => TotalLoad.HasValue && !HasErrors;
    public ICommand AddSlabCommand { get; }
    public ICommand AddBeamCommand { get; }
    public ICommand CopyResultsCommand { get; }
    public ICommand ShowDiagramCommand { get; }

    public void CalculateTotalLoad()
    {
        var errors = new Dictionary<string, string>();
        bool pOk = ReadNumber(PrimaryBeamSize, nameof(PrimaryBeamSize), "Tributary width", 0, false, BeamDesign.MaxLength, errors, out double p);
        bool sOk = ReadNumber(SecondaryBeamSize, nameof(SecondaryBeamSize), "Secondary beam length", 0, false, BeamDesign.MaxLength, errors, out double s);
        bool qOk = ReadNumber(CombinedLoad, nameof(CombinedLoad), "Combined load", 0, true, BeamDesign.MaxPressure, errors, out double q);
        double area = p * s;
        bool geometryOk = pOk && sOk && Numeric.IsFinite(area) && area > 0;
        if (pOk && sOk && !geometryOk) errors[nameof(PrimaryBeamSize)] = "Support area is outside numeric range.";
        SupportArea = geometryOk ? area : null;
        if (Elements.Count > MaxElements) errors[nameof(Elements)] = $"At most {MaxElements} elements are supported.";
        for (int i = 0; i < Elements.Count; i++)
        {
            var element = Elements[i];
            element.Recalculate(geometryOk ? area : null, s);
            if (element.HasErrors) errors[$"Element{i}"] = $"Element {i + 1}: {element.ValidationMessage}";
        }
        double pressure = q + Elements.Sum(e => e.CalculatedLoad ?? 0);
        if (geometryOk && qOk && errors.Count == 0 && (!Numeric.IsFinite(pressure) || pressure > BeamDesign.MaxPressure))
            errors[nameof(CombinedLoad)] = $"Total pressure must be finite and at most {BeamDesign.MaxPressure} kN/m² (software range).";
        double total = pressure * area;
        if (errors.Count == 0 && !Numeric.IsFinite(total)) errors[nameof(CombinedLoad)] = "Total load is outside numeric range.";
        ReplaceErrors(errors);
        LoadTransferredToSecondary = errors.Count == 0 ? pressure : null;
        TotalLoad = errors.Count == 0 ? total : null;
        ValidationMessage = string.Join(Environment.NewLine, errors.Values);
        OnPropertyChanged(nameof(IsCalculationValid));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowDiagram()
    {
        if (!IsCalculationValid) return;
        var window = new DiagramWindow(new DiagramViewModel(LoadTransferredToSecondary!.Value,
            MathEvaluator.Evaluate(SecondaryBeamSize)));
        if (DialogOwner is { IsVisible: true } owner) window.Owner = owner;
        window.ShowDialog();
    }
    public string CreateResultsText()
    {
        if (!IsCalculationValid) throw new InvalidOperationException("Correct invalid inputs before copying results.");
        var text = new StringBuilder("DOME CALCULATOR — BETA — LOAD SUMMARY\n");
        text.AppendLine($"Tributary width: {MathEvaluator.Evaluate(PrimaryBeamSize):G17} m; secondary length: {MathEvaluator.Evaluate(SecondaryBeamSize):G17} m");
        text.AppendLine($"Combined load: {MathEvaluator.Evaluate(CombinedLoad):G17} kN/m²; concrete unit weight: 25 kN/m³");
        for (int i = 0; i < Elements.Count; i++) text.AppendLine($"Element {i + 1}: {Elements[i].Description}; contribution {Elements[i].CalculatedLoad:G17} kN/m²");
        text.AppendLine($"Support area: {SupportArea:G17} m²\nTransferred pressure: {LoadTransferredToSecondary:G17} kN/m²\nTotal load: {TotalLoad:G17} kN");
        text.AppendLine("Section areas are extruded concrete sections, not plan areas. Loads are averaged over the support area. This summary is not a whole-system design check.");
        return text.ToString();
    }
    private void CopyResults()
    {
        try { Clipboard.SetText(CreateResultsText()); }
        catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show("The clipboard is busy. Please try again.", "Copy results"); }
    }
}

public abstract class ElementViewModel : ViewModelBase
{
    protected readonly MainViewModel MainVM;
    private string surfaceArea = "10";
    private double? calculatedLoad;
    private string validationMessage = "";
    protected ElementViewModel(MainViewModel mainVM)
    {
        MainVM = mainVM;
        RemoveCommand = new RelayCommand(_ => MainVM.Elements.Remove(this));
    }
    // Property name retained for binding compatibility; this is a concrete SECTION area.
    public string SurfaceArea { get => surfaceArea; set { if (SetProperty(ref surfaceArea, value)) MainVM.CalculateTotalLoad(); } }
    public double? CalculatedLoad { get => calculatedLoad; private set => SetProperty(ref calculatedLoad, value); }
    public string ValidationMessage { get => validationMessage; private set => SetProperty(ref validationMessage, value); }
    public ICommand RemoveCommand { get; }
    public abstract string Description { get; }
    internal void Recalculate(double? supportArea, double secondaryLength)
    {
        var errors = new Dictionary<string, string>();
        ReadNumber(SurfaceArea, nameof(SurfaceArea), "Concrete section area", 0, false, 1000, errors, out double section);
        double extrusion = GetExtrusionLength(secondaryLength, errors);
        double? result = supportArea.HasValue && errors.Count == 0 ? section * extrusion * 25 / supportArea.Value : null;
        if (result.HasValue && !Numeric.IsFinite(result.Value)) { errors[nameof(SurfaceArea)] = "Element load is outside numeric range."; result = null; }
        ReplaceErrors(errors);
        ValidationMessage = string.Join(" ", errors.Values);
        CalculatedLoad = result;
    }
    protected virtual double GetExtrusionLength(double secondaryLength, Dictionary<string, string> errors) => secondaryLength;
}

public sealed class SlabViewModel(MainViewModel mainVM) : ElementViewModel(mainVM)
{
    public override string Description => $"Slab section {SurfaceArea} m², extruded along secondary length";
}

public sealed class BeamViewModel(MainViewModel mainVM) : ElementViewModel(mainVM)
{
    private bool isParallel = true;
    private string beamWidth = "0.5";
    public bool IsParallel { get => isParallel; set { if (value) SetOrientation(true); } }
    public bool IsPerpendicular { get => !isParallel; set { if (value) SetOrientation(false); } }
    private void SetOrientation(bool parallel)
    {
        if (isParallel == parallel) return;
        isParallel = parallel;
        OnPropertyChanged(nameof(IsParallel));
        OnPropertyChanged(nameof(IsPerpendicular));
        MainVM.CalculateTotalLoad();
    }
    public string BeamWidth { get => beamWidth; set { if (SetProperty(ref beamWidth, value)) MainVM.CalculateTotalLoad(); } }
    protected override double GetExtrusionLength(double secondaryLength, Dictionary<string, string> errors)
    {
        if (IsParallel) return secondaryLength;
        ReadNumber(BeamWidth, nameof(BeamWidth), "Extrusion width", 0, false, BeamDesign.MaxLength, errors, out double width);
        return width;
    }
    public override string Description => $"{(IsParallel ? "Parallel" : "Perpendicular")} beam section {SurfaceArea} m²" + (IsParallel ? "" : $", extrusion width {BeamWidth} m");
}
