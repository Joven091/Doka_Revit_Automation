using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using OxyPlot;
using MyRevitAddin.Models;
using MyRevitAddin.ViewModels;

namespace EngineeringDashboard;

public class FlexCalculatorViewModel : INotifyPropertyChanged
{
    // --- Constants ---
    private const double MAX_H20_SHEAR_KN = 11.00;
    private const double MAX_H20_MOMENT_KNM = 5.00;
    private const double H20_E_KN_M2 = 10760000; // 1076 kN/cm2 = 10760000 kN/m2
    private const double H20_I_M4 = 0.00004181;  // 4181 cm4 = 0.00004181 m4
    private const double H20_EI = H20_E_KN_M2 * H20_I_M4;

    // --- Inputs: Slab & Geometry ---
    private double _slabThickness = 300;
    private double _floorHeight = 3000;
    
    // --- Custom Loads ---
    private bool _useCustomLoads = false;
    private double _customLiveLoad = 2.25; // Default from standard: placing (1.5) + working (0.75)
    private double _customFormworkLoad = 0.4;

    // --- Inputs: Secondary Beam Layout ---
    private string _primaryBeamCount = "3";
    private double _secondaryBeamSpacing = 488;
    private double _secondaryBeamCantilever = 500;
    private string _deflectionLimit = "L/350";

    // --- Inputs: Primary Beam & Prop Layout ---
    private string _propCount = "3";
    private double _propSpacing = 1000;
    private double _primaryBeamCantilever = 500;
    private string _propModel = "Prop Type Eurex eco 20";
    private string _propType = "300";
    private string _propOrientation = "at bottom";
    private string _selectedH20 = "3.30m";

    public bool IsCalculationValid { get; private set; }
    private bool textErrors;
    public double PropExtensionMm => FloorHeight - SlabThickness - 418;
    public string MemberFitHint
    {
        get
        {
            if (PropCount != "5+" || SelectedH20 == null || !FloorPropData.H20Data.ContainsKey(SelectedH20) || PropSpacing < 1 || double.IsNaN(PropSpacing) || double.IsInfinity(PropSpacing)) return "";
            double length = double.Parse(SelectedH20.Replace("m", ""), System.Globalization.CultureInfo.InvariantCulture) * 1000;
            double count = Math.Max(0, Math.Floor((length - 2 * PrimaryBeamCantilever + 1e-7) / PropSpacing) + 1);
            return $"Selected H20 fits at most {count:0} props at this spacing.";
        }
    }
    public double PropLookupExtensionMm { get; private set; }
    public double PrimaryDeflectionMm { get; private set; }
    public void SetTextInputErrors(bool value) { if (textErrors != value) { textErrors = value; Calculate(); } }
    // --- Outputs ---
    private double _recommendedPropSpacing;
    private double _suggestedPrimaryBeamSpacing;
    private double _actualPropLoad;
    private double _propCapacityLimit;
    private double _utilizationRate;
    private double _h20Shear;
    private double _h20Utilization;
    private string _warningMessage = string.Empty;

    // --- UI Collections ---
    public List<string> H20Options => FloorPropData.H20Data.Keys.ToList();
    public List<string> PropModelOptions => ["Prop Type Eurex eco 20", "DokaFit Prop"];

    private List<string> _propTypeOptions;
    public List<string> PropTypeOptions { get => _propTypeOptions; set { _propTypeOptions = value; OnPropertyChanged(); } }

    private List<string> _propOrientationOptions;
    public List<string> PropOrientationOptions { get => _propOrientationOptions; set { _propOrientationOptions = value; OnPropertyChanged(); } }

    public List<string> DeflectionLimitOptions => ["L/500", "L/350", "L/270"];
    public List<string> PrimaryBeamCountOptions => ["2", "3", "4", "5+"];
    public List<string> PropCountOptions => ["2", "3", "4", "5+"];

    // --- Public Input Properties ---
    public double SlabThickness { get => _slabThickness; set { _slabThickness = value; OnPropertyChanged(); Calculate(); } }
    public double FloorHeight { get => _floorHeight; set { _floorHeight = value; OnPropertyChanged(); Calculate(); } }
    
    public bool UseCustomLoads { get => _useCustomLoads; set { _useCustomLoads = value; OnPropertyChanged(); Calculate(); } }
    public double CustomLiveLoad { get => _customLiveLoad; set { _customLiveLoad = value; OnPropertyChanged(); Calculate(); } }
    public double CustomFormworkLoad { get => _customFormworkLoad; set { _customFormworkLoad = value; OnPropertyChanged(); Calculate(); } }

    public double SecondaryBeamSpacing { get => _secondaryBeamSpacing; set { _secondaryBeamSpacing = value; OnPropertyChanged(); Calculate(); } }
    public string PrimaryBeamCount { get => _primaryBeamCount; set { _primaryBeamCount = value; OnPropertyChanged(); Calculate(); } }
    public double SecondaryBeamCantilever { get => _secondaryBeamCantilever; set { _secondaryBeamCantilever = value; OnPropertyChanged(); Calculate(); } }
    public string DeflectionLimit { get => _deflectionLimit; set { _deflectionLimit = value; OnPropertyChanged(); Calculate(); } }

    public string PropCount { get => _propCount; set { _propCount = value; OnPropertyChanged(); Calculate(); } }
    public double PropSpacing { get => _propSpacing; set { _propSpacing = value; OnPropertyChanged(); Calculate(); } }
    public double PrimaryBeamCantilever { get => _primaryBeamCantilever; set { _primaryBeamCantilever = value; OnPropertyChanged(); Calculate(); } }
    
    public string PropModel
    {
        get => _propModel;
        set
        {
            _propModel = value;
            OnPropertyChanged();
            UpdatePropOptions();
            Calculate();
        }
    }

    public string PropType { get => _propType; set { _propType = value; OnPropertyChanged(); Calculate(); } }
    public string PropOrientation { get => _propOrientation; set { _propOrientation = value; OnPropertyChanged(); Calculate(); } }

    public string SelectedH20
    {
        get => _selectedH20;
        set
        {
            _selectedH20 = value;
            OnPropertyChanged();
            if (value != null && FloorPropData.H20Data.TryGetValue(value, out double rec))
            {
                RecommendedPropSpacing = rec;
            }
            Calculate();
        }
    }

    // --- Public Output Properties ---
    public double RecommendedPropSpacing { get => _recommendedPropSpacing; set { _recommendedPropSpacing = value; OnPropertyChanged(); } }
    public double SuggestedPrimaryBeamSpacing { get => _suggestedPrimaryBeamSpacing; private set { _suggestedPrimaryBeamSpacing = value; OnPropertyChanged(); } }
    public double ActualPropLoad { get => _actualPropLoad; private set { _actualPropLoad = value; OnPropertyChanged(); } }
    public double PropCapacityLimit { get => _propCapacityLimit; private set { _propCapacityLimit = value; OnPropertyChanged(); } }
    public double UtilizationRate { get => _utilizationRate; private set { _utilizationRate = value; OnPropertyChanged(); } }
    public double H20Shear { get => _h20Shear; private set { _h20Shear = value; OnPropertyChanged(); } }
    public double H20Utilization { get => _h20Utilization; private set { _h20Utilization = value; OnPropertyChanged(); } }
    public string WarningMessage { get => _warningMessage; private set { _warningMessage = value; OnPropertyChanged(); } }

    // --- Commands & Diagram Properties ---
    public ICommand ViewPrimaryBeamDiagramCommand { get; }
    public ICommand ViewSecondaryBeamDiagramCommand { get; }

    public event EventHandler<EventArgs> PrimaryBeamDiagramRequested;
    public event EventHandler<EventArgs> SecondaryBeamDiagramRequested;

    private DiagramWindowViewModel _primaryDiagramViewModel;
    public DiagramWindowViewModel PrimaryDiagramViewModel { get => _primaryDiagramViewModel; set { _primaryDiagramViewModel = value; OnPropertyChanged(); } }

    private DiagramWindowViewModel _secondaryDiagramViewModel;
    public DiagramWindowViewModel SecondaryDiagramViewModel { get => _secondaryDiagramViewModel; set { _secondaryDiagramViewModel = value; OnPropertyChanged(); } }


    public FlexCalculatorViewModel()
    {
        ViewPrimaryBeamDiagramCommand = new RelayCommand(_ => PrimaryBeamDiagramRequested?.Invoke(this, EventArgs.Empty), _ => IsCalculationValid);
        ViewSecondaryBeamDiagramCommand = new RelayCommand(_ => SecondaryBeamDiagramRequested?.Invoke(this, EventArgs.Empty), _ => IsCalculationValid);

        // The default three-prop layout occupies 3000 mm and fits a 3.30 m member. 
        UpdatePropOptions();
        RecommendedPropSpacing = FloorPropData.H20Data[SelectedH20];
        Calculate();
    }

    private void UpdatePropOptions()
    {
        if (PropModel == "Prop Type Eurex eco 20")
        {
            PropTypeOptions = new List<string> { "250", "300", "350", "400", "550" };
            PropOrientationOptions = new List<string> { "at bottom", "at top" };
            if (!PropTypeOptions.Contains(PropType)) PropType = "300";
            if (!PropOrientationOptions.Contains(PropOrientation)) PropOrientation = "at bottom";
        }
        else if (PropModel == "DokaFit Prop")
        {
            PropTypeOptions = new List<string> { "300", "350", "400" };
            PropOrientationOptions = new List<string> { "at bottom", "at top" };
            if (!PropTypeOptions.Contains(PropType)) PropType = "300";
            if (!PropOrientationOptions.Contains(PropOrientation)) PropOrientation = "at bottom";
        }
    }

    private static void CheckNumber(double value, string name, bool positive = true)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || positive && value == 0)
            throw new ArgumentException(name + (positive ? " must be finite and greater than zero." : " must be finite and zero or greater."));
    }
    private void ValidateInputs()
    {
        if (textErrors) throw new ArgumentException("Correct the highlighted input before calculating or importing.");
        CheckNumber(SlabThickness, "Slab thickness"); CheckNumber(FloorHeight, "Floor height");
        CheckNumber(SecondaryBeamSpacing, "Secondary spacing"); CheckNumber(PropSpacing, "Prop spacing");
        if (PropSpacing < 1) throw new ArgumentException("Prop spacing must be at least 1 mm.");
        CheckNumber(SecondaryBeamCantilever, "Secondary cantilever", false); CheckNumber(PrimaryBeamCantilever, "Primary cantilever", false);
        if (UseCustomLoads) { CheckNumber(CustomLiveLoad, "Live load", false); CheckNumber(CustomFormworkLoad, "Formwork load", false); }
        if (!PrimaryBeamCountOptions.Contains(PrimaryBeamCount) || !PropCountOptions.Contains(PropCount) || !DeflectionLimitOptions.Contains(DeflectionLimit)) throw new ArgumentException("Select valid support counts and a deflection limit.");
        if (!PropModelOptions.Contains(PropModel) || PropTypeOptions == null || !PropTypeOptions.Contains(PropType) || PropOrientationOptions == null || !PropOrientationOptions.Contains(PropOrientation)) throw new ArgumentException("Select a supported prop and outer tube orientation.");
        if (SelectedH20 == null || !FloorPropData.H20Data.ContainsKey(SelectedH20)) throw new ArgumentException("Select an H20 length.");
        int count = PropCount == "5+" ? 5 : int.Parse(PropCount);
        double available = double.Parse(SelectedH20.Replace("m", ""), System.Globalization.CultureInfo.InvariantCulture) * 1000;
        if ((count - 1) * PropSpacing + 2 * PrimaryBeamCantilever > available + 1e-7) throw new ArgumentException("The primary spans and cantilevers exceed the selected H20 length. Choose a longer member or reduce the layout.");
    }
    private void ClearResults()
    {
        IsCalculationValid = false; SuggestedPrimaryBeamSpacing = ActualPropLoad = H20Shear = H20Utilization = UtilizationRate = PropCapacityLimit = 0;
        PrimaryDeflectionMm = PropLookupExtensionMm = 0;
        PrimaryDiagramViewModel = SecondaryDiagramViewModel = null;
    }
    private void Calculate()
    {
        ClearResults();
        try { ValidateInputs(); CalculateValidInputs(); IsCalculationValid = SuggestedPrimaryBeamSpacing > 0 && string.IsNullOrEmpty(WarningMessage); }
        catch (ArgumentException ex) { ClearResults(); WarningMessage = ex.Message; }
        OnPropertyChanged(nameof(IsCalculationValid)); OnPropertyChanged(nameof(PropExtensionMm)); OnPropertyChanged(nameof(PropLookupExtensionMm)); OnPropertyChanged(nameof(PrimaryDeflectionMm)); OnPropertyChanged(nameof(MemberFitHint));
        CommandManager.InvalidateRequerySuggested();
    }
    private void CalculateValidInputs()
    {
        WarningMessage = string.Empty;
        List<string> warnings = [];

        // 1. Get allowable capacities
        string propKey = PropModel == "DokaFit Prop" ? $"DokaFit Prop {PropType} {PropOrientation}" : $"{PropType} {PropOrientation}";
        double capacity = PropDataHelper.CapacityAtExtensionMm(PropExtensionMm, propKey, out double lookupMm);
        PropLookupExtensionMm = lookupMm;
        PropCapacityLimit = capacity;

        // 2. Convert inputs to working units
        int pbcInt = PrimaryBeamCount == "5+" ? 5 : (int.TryParse(PrimaryBeamCount, out int p) ? p : 3);
        int pcInt = PropCount == "5+" ? 5 : (int.TryParse(PropCount, out int c) ? c : 3);

        double w_slab;
        if (UseCustomLoads)
        {
            double h = SlabThickness / 1000.0;
            double concreteLoad = h * 25.0;
            w_slab = concreteLoad + CustomLiveLoad + CustomFormworkLoad;

        }
        else
        {
            w_slab = SlabLoadHelper.CalculateTotalLoad(SlabThickness);
        }

        CheckNumber(w_slab, "Total slab load");
        double a_s = SecondaryBeamCantilever / 1000.0;
        double L_p = PropSpacing / 1000.0;
        double a_p = PrimaryBeamCantilever / 1000.0;
        double c_sec = SecondaryBeamSpacing / 1000.0;

        double w_sb = w_slab * c_sec;
        CheckNumber(w_sb, "Secondary beam line load"); 

        // Deflection Limit parsing
        double limit_denom = 350.0;
        if (DeflectionLimit == "L/500") limit_denom = 500.0;
        else if (DeflectionLimit == "L/270") limit_denom = 270.0;

        // 3. Initial Cantilever Absolute Limits
        double v_cantilever_sec = w_sb * a_s;
        double m_cantilever_sec = (w_sb * Math.Pow(a_s, 2)) / 2.0;
        
        if (v_cantilever_sec > MAX_H20_SHEAR_KN)
        {
            warnings.Add($"Secondary beam cantilever shear exceeds limit ({Math.Round(v_cantilever_sec,2)} kN). Reduce a_s.");
        }
        if (m_cantilever_sec > MAX_H20_MOMENT_KNM)
        {
            warnings.Add($"Secondary beam cantilever moment exceeds limit ({Math.Round(m_cantilever_sec,2)} kNm). Reduce a_s.");
        }

        // Primary support response is linear in load; reuse the unit-load envelope for each spacing candidate.
        var primaryEnvelope = SlabSupportEnvelope.ForSelection(pcInt, L_p, a_p, 1, H20_EI);
        if (primaryEnvelope.HasUplift)
        {
            WarningMessage = UpliftLocation("Primary H20", "prop", primaryEnvelope, pcInt == 5) +
                " Shorten the primary overhang or revise prop spacing. Changing primary beam spacing cannot resolve this geometry.";
            return;
        }
        // 4. Numerical Solver for L_s (Continuous Beam Analysis)
        double best_L_s = 0;
        string secondaryUplift = null;
        string tipFailure = null;
        
        if (warnings.Count == 0) 
        {
            // Evaluate the actual 25 mm output grid, including the primary deflection check.
            for (int spacingMm = 4000; spacingMm >= 100; spacingMm -= 25) 
            {
                double test_L = spacingMm / 1000.0;
                var secondaryEnvelope = SlabSupportEnvelope.ForSelection(pbcInt, test_L, a_s, 1, H20_EI);
                // Secondary Beam Internal Forces & Deflection
                double v_sec = w_sb * secondaryEnvelope.Shear;
                double m_sec = w_sb * secondaryEnvelope.Moment;
                double defl_sec = w_sb * secondaryEnvelope.SpanDeflection;
                
                // Load transferred to Primary Beam
                double R_sec_multiplier = secondaryEnvelope.Reaction;
                double w_pb = w_slab * R_sec_multiplier;
                
                // Primary Beam Internal Forces & Prop Reaction
                double v_prim = w_pb * primaryEnvelope.Shear;
                double m_prim = w_pb * primaryEnvelope.Moment;
                double defl_prim = w_pb * primaryEnvelope.SpanDeflection;
                double p_prop = w_pb * primaryEnvelope.Reaction;
                
                if (v_sec <= MAX_H20_SHEAR_KN && 
                    m_sec <= MAX_H20_MOMENT_KNM && 
                    defl_sec <= (test_L / limit_denom) &&
                    v_prim <= MAX_H20_SHEAR_KN && 
                    m_prim <= MAX_H20_MOMENT_KNM && 
                    p_prop <= capacity && defl_prim <= L_p / limit_denom)
                {
                    if (secondaryEnvelope.HasUplift)
                    {
                        if (secondaryUplift == null)
                            secondaryUplift = UpliftLocation("Secondary H20", "primary beam support", secondaryEnvelope, pbcInt == 5) +
                                $" At {spacingMm} mm primary beam spacing, strength and span-deflection checks pass but contact is lost. Shorten the secondary overhang or revise the support arrangement.";
                        continue;
                    }
                    double secondaryTip = w_sb * secondaryEnvelope.CantileverDeflection;
                    double primaryTip = w_pb * primaryEnvelope.CantileverDeflection;
                    if (secondaryTip > a_s / limit_denom || primaryTip > a_p / limit_denom)
                    {
                        if (tipFailure == null)
                        {
                            bool secondary = secondaryTip > a_s / limit_denom;
                            tipFailure = $"{(secondary ? "Secondary" : "Primary")} H20 end deflection including support rotation exceeds the overhang limit at {spacingMm} mm primary beam spacing " +
                                $"({(secondary ? secondaryTip : primaryTip) * 1000:0.00} mm versus {(secondary ? a_s : a_p) * 1000 / limit_denom:0.00} mm). Shorten the overhang or revise the support arrangement.";
                        }
                        continue;
                    }
                    best_L_s = test_L;
                    break; 
                }
            }
        }

        // 5. Final Output Resolution
        if (best_L_s <= 0)
        {
            if (secondaryUplift != null) warnings.Add(secondaryUplift);
            if (tipFailure != null) warnings.Add(tipFailure);
            warnings.Add("Cannot find a valid primary beam spacing for the given parameters.");
            SuggestedPrimaryBeamSpacing = 0;
            ActualPropLoad = 0;
            H20Shear = 0;
            H20Utilization = 0;
            UtilizationRate = 0;
        }
        else
        {
            SuggestedPrimaryBeamSpacing = Math.Round(best_L_s * 1000.0 / 25) * 25;
            
            // Convert back to meters for load calculation
            double final_L_s = SuggestedPrimaryBeamSpacing / 1000.0;

            // Resolve actual loads at the rounded span
            double final_w_pb = w_slab * SlabSupportEnvelope.ForSelection(pbcInt, final_L_s, a_s, 1, H20_EI).Reaction;
            double final_V_prim = final_w_pb * primaryEnvelope.Shear;
            double final_P_prop = final_w_pb * primaryEnvelope.Reaction;
            
            PrimaryDeflectionMm = final_w_pb * primaryEnvelope.SpanDeflection * 1000;
            ActualPropLoad = Math.Round(final_P_prop, 2);
            H20Shear = Math.Round(final_V_prim, 2);
            H20Utilization = Math.Round((final_V_prim / MAX_H20_SHEAR_KN) * 100, 1);
            UtilizationRate = Math.Round((final_P_prop / capacity) * 100, 1);
        }

        if (warnings.Count != 0)
        {
            WarningMessage = string.Join(" | ", warnings);
        }

        UpdateDiagrams(pbcInt, pcInt, w_sb, w_slab, a_s, a_p, SuggestedPrimaryBeamSpacing / 1000.0, L_p);
    }

    private static string UpliftLocation(string beam, string support, SlabSupportEnvelope response, bool envelope)
    {
        string caseLabel = envelope ? $" in the {response.MinimumReactionCount}-support case of 5+" : "";
        return $"Uplift: {beam} loses contact at {support} {response.MinimumReactionSupport} (numbered from the left){caseLabel}.";
    }

    private void UpdateDiagrams(int pbcInt, int pcInt, double w_sb, double w_slab, double a_s, double a_p, double final_L_s, double L_p)
    {
        if (SuggestedPrimaryBeamSpacing <= 0)
        {
            PrimaryDiagramViewModel = null;
            SecondaryDiagramViewModel = null;
            return;
        }

        var secondaryEnvelope = SlabSupportEnvelope.ForSelection(pbcInt, final_L_s, a_s, 1, H20_EI);
        SecondaryDiagramViewModel = DiagramWindowViewModel.ForSelection(pbcInt, final_L_s, a_s, w_sb, H20_EI, "Secondary beam");
        PrimaryDiagramViewModel = DiagramWindowViewModel.ForSelection(pcInt, L_p, a_p, w_slab * secondaryEnvelope.Reaction, H20_EI, "Primary beam");
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
