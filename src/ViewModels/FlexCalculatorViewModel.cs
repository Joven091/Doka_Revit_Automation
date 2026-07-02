using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EngineeringDashboard;

public class FlexCalculatorViewModel : INotifyPropertyChanged
{
    // --- Constants ---
    private const double MAX_H20_SHEAR_KN = 11.00;
    private const double MAX_H20_MOMENT_KNM = 5.00;
    private const double SYSTEM_DEPTH_METERS = 0.418;
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
    private double _secondaryBeamSpacing = 500;
    private double _secondaryBeamCantilever = 500;
    private string _deflectionLimit = "L/500";

    // --- Inputs: Primary Beam & Prop Layout ---
    private string _propCount = "3";
    private double _propSpacing = 1000;
    private double _primaryBeamCantilever = 500;
    private string _propType = "300";
    private string _propOrientation = "at bottom";
    private string _selectedH20 = "2.90m";

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
    public List<string> PropTypeOptions => ["250", "300", "350", "400", "550"];
    public List<string> PropOrientationOptions => ["at bottom", "at top"];
    public List<string> DeflectionLimitOptions => ["L/500", "L/350", "L/270"];
    public List<string> PrimaryBeamCountOptions => ["2", "3", "4+"];
    public List<string> PropCountOptions => ["2", "3", "4+"];

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
    
    public string PropType { get => _propType; set { _propType = value; OnPropertyChanged(); Calculate(); } }
    public string PropOrientation { get => _propOrientation; set { _propOrientation = value; OnPropertyChanged(); Calculate(); } }

    public string SelectedH20
    {
        get => _selectedH20;
        set
        {
            _selectedH20 = value;
            OnPropertyChanged();
            if (FloorPropData.H20Data.TryGetValue(value, out double rec))
            {
                RecommendedPropSpacing = rec;
            }
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

    public FlexCalculatorViewModel()
    {
        SelectedH20 = "2.90m"; 
        Calculate();
    }

    private void Calculate()
    {
        WarningMessage = string.Empty;
        List<string> warnings = [];

        // 1. Get allowable capacities
        double extensionMeters = (FloorHeight / 1000.0) - (SlabThickness / 1000.0) - SYSTEM_DEPTH_METERS;
        double roundedExtension = Math.Ceiling(extensionMeters * 10) / 10.0;
        string propKey = $"{PropType} {PropOrientation}";

        double? capacity = PropDataHelper.GetCapacity(roundedExtension, propKey);

        if (!capacity.HasValue || capacity.Value <= 0)
        {
            PropCapacityLimit = 0;
            UtilizationRate = 0;
            SuggestedPrimaryBeamSpacing = 0;
            ActualPropLoad = 0;
            H20Shear = 0;
            H20Utilization = 0;
            WarningMessage = "Prop capacity data not found for this extension height.";
            return;
        }

        PropCapacityLimit = Math.Round(capacity.Value, 2);

        // 2. Convert inputs to working units
        int pbcInt = PrimaryBeamCount == "4+" ? 4 : (int.TryParse(PrimaryBeamCount, out int p) ? p : 3);
        int pcInt = PropCount == "4+" ? 4 : (int.TryParse(PropCount, out int c) ? c : 3);

        double w_slab;
        if (UseCustomLoads)
        {
            double h = SlabThickness / 1000.0;
            double concreteLoad = h * 25.0;
            w_slab = concreteLoad + CustomLiveLoad + CustomFormworkLoad;
            w_slab = Math.Round(w_slab, 2);
        }
        else
        {
            w_slab = SlabLoadHelper.CalculateTotalLoad((int)SlabThickness);
        }

        double a_s = SecondaryBeamCantilever / 1000.0;
        double L_p = PropSpacing / 1000.0;
        double a_p = PrimaryBeamCantilever / 1000.0;
        double c_sec = SecondaryBeamSpacing / 1000.0;

        double w_sb = w_slab * c_sec; 

        // Deflection Limit parsing
        double limit_denom = 350.0;
        if (DeflectionLimit == "L/500") limit_denom = 500.0;
        else if (DeflectionLimit == "L/270") limit_denom = 270.0;

        // 3. Initial Cantilever Absolute Limits
        double v_cantilever_sec = w_sb * a_s;
        double m_cantilever_sec = (w_sb * Math.Pow(a_s, 2)) / 2.0;
        double defl_cantilever_sec = (w_sb * Math.Pow(a_s, 4)) / (8.0 * H20_EI);
        double limit_cantilever = a_s / limit_denom;
        
        if (v_cantilever_sec > MAX_H20_SHEAR_KN)
        {
            warnings.Add($"Secondary beam cantilever shear exceeds limit ({Math.Round(v_cantilever_sec,2)} kN). Reduce a_s.");
        }
        if (m_cantilever_sec > MAX_H20_MOMENT_KNM)
        {
            warnings.Add($"Secondary beam cantilever moment exceeds limit ({Math.Round(m_cantilever_sec,2)} kNm). Reduce a_s.");
        }
        if (a_s > 0 && defl_cantilever_sec > limit_cantilever)
        {
            warnings.Add($"Secondary beam cantilever deflection ({Math.Round(defl_cantilever_sec * 1000.0, 2)} mm) exceeds limit ({DeflectionLimit}). Reduce a_s.");
        }

        // 4. Numerical Solver for L_s (Continuous Beam Analysis)
        double best_L_s = 0;
        
        if (warnings.Count == 0) 
        {
            // Iterate from 4.0m down to 0.1m in 10mm increments
            for (double test_L = 4.00; test_L >= 0.10; test_L -= 0.01) 
            {
                // Secondary Beam Internal Forces & Deflection
                double v_sec = w_sb * GetMaxShearMultiplier(pbcInt, test_L, a_s);
                double m_sec = w_sb * GetMaxMomentMultiplier(pbcInt, test_L, a_s);
                double defl_sec = (w_sb * Math.Pow(test_L, 4)) / H20_EI * GetMaxDeflectionMultiplier(pbcInt, test_L, a_s);
                
                // Load transferred to Primary Beam
                double R_sec_multiplier = GetMaxReactionMultiplier(pbcInt, test_L, a_s);
                double w_pb = w_slab * R_sec_multiplier;
                
                // Primary Beam Internal Forces & Prop Reaction
                double v_prim = w_pb * GetMaxShearMultiplier(pcInt, L_p, a_p);
                double m_prim = w_pb * GetMaxMomentMultiplier(pcInt, L_p, a_p);
                double p_prop = w_pb * GetMaxReactionMultiplier(pcInt, L_p, a_p);
                
                if (v_sec <= MAX_H20_SHEAR_KN && 
                    m_sec <= MAX_H20_MOMENT_KNM && 
                    defl_sec <= (test_L / limit_denom) &&
                    v_prim <= MAX_H20_SHEAR_KN && 
                    m_prim <= MAX_H20_MOMENT_KNM && 
                    p_prop <= capacity.Value)
                {
                    best_L_s = test_L;
                    break; 
                }
            }
        }

        // 5. Final Output Resolution
        if (best_L_s <= 0)
        {
            warnings.Add("Cannot find a valid primary beam spacing for the given parameters.");
            SuggestedPrimaryBeamSpacing = 0;
            ActualPropLoad = 0;
            H20Shear = 0;
            H20Utilization = 0;
            UtilizationRate = 0;
        }
        else
        {
            SuggestedPrimaryBeamSpacing = Math.Floor((best_L_s * 1000.0) / 25.0) * 25.0;
            
            // Convert back to meters for load calculation
            double final_L_s = SuggestedPrimaryBeamSpacing / 1000.0;

            // Resolve actual loads at the rounded span
            double final_w_pb = w_slab * GetMaxReactionMultiplier(pbcInt, final_L_s, a_s);
            double final_V_prim = final_w_pb * GetMaxShearMultiplier(pcInt, L_p, a_p);
            double final_P_prop = final_w_pb * GetMaxReactionMultiplier(pcInt, L_p, a_p);
            
            ActualPropLoad = Math.Round(final_P_prop, 2);
            H20Shear = Math.Round(final_V_prim, 2);
            H20Utilization = Math.Round((final_V_prim / MAX_H20_SHEAR_KN) * 100, 1);
            UtilizationRate = Math.Round((final_P_prop / capacity.Value) * 100, 1);
        }

        if (warnings.Count != 0)
        {
            WarningMessage = string.Join(" | ", warnings);
        }
    }

    // ====================================================================
    // CONTINUOUS BEAM HELPER METHODS (Calculates Multipliers)
    // ====================================================================

    private double GetMaxDeflectionMultiplier(int supportCount, double L, double a)
    {
        double r = a / L;
        double r2 = r * r;
        double m1, m2;

        if (supportCount <= 2)
        {
            m1 = 0.5 * r2;
            m2 = 0.5 * r2;
        }
        else if (supportCount == 3)
        {
            m1 = 0.5 * r2;
            m2 = 0.125 - 0.25 * r2;
        }
        else
        {
            m1 = 0.5 * r2;
            m2 = 0.1 - 0.1 * r2;
        }

        double maxAbsDefl = 0;
        for (double u = 0.1; u <= 0.95; u += 0.05)
        {
            double u2 = u * u;
            double u3 = u2 * u;
            double u4 = u3 * u;

            double term1 = (u - 2 * u3 + u4) / 24.0;
            double term2 = m1 * (2 * u - 3 * u2 + u3) / 6.0;
            double term3 = m2 * (u - u3) / 6.0;

            double defl = Math.Abs(term1 - term2 - term3);
            if (defl > maxAbsDefl)
            {
                maxAbsDefl = defl;
            }
        }

        return maxAbsDefl;
    }

    private double GetMaxReactionMultiplier(int supportCount, double L, double a)
    {
        if (supportCount <= 2)
        {
            return (L / 2.0) + a; 
        }
        else if (supportCount == 3)
        {
            double R1 = (3.0 / 8.0) * L + a + (3.0 / 4.0) * (Math.Pow(a, 2) / L);
            double R2 = (5.0 / 4.0) * L - (3.0 / 2.0) * (Math.Pow(a, 2) / L);
            return Math.Max(R1, R2);
        }
        else 
        {
            // Exact continuous boundary envelope for 4+ supports
            double R1 = 0.4 * L + a + 0.6 * (Math.Pow(a, 2) / L);
            double R2 = 1.1 * L - 0.6 * (Math.Pow(a, 2) / L);
            return Math.Max(R1, R2);
        }
    }

    private double GetMaxShearMultiplier(int supportCount, double L, double a)
    {
        double Va = a; 
        if (supportCount <= 2)
        {
            return Math.Max(Va, L / 2.0);
        }
        else if (supportCount == 3)
        {
            double V1 = Math.Abs((3.0 / 8.0) * L + (3.0 / 4.0) * (Math.Pow(a, 2) / L));
            double V2 = Math.Abs((5.0 / 8.0) * L - (3.0 / 4.0) * (Math.Pow(a, 2) / L));
            return Math.Max(Va, Math.Max(V1, V2));
        }
        else 
        {
            double V1 = Math.Abs(0.4 * L + 0.6 * (Math.Pow(a, 2) / L));
            double V2 = Math.Abs(0.6 * L - 0.6 * (Math.Pow(a, 2) / L));
            return Math.Max(Va, Math.Max(V1, V2));
        }
    }

    private double GetMaxMomentMultiplier(int supportCount, double L, double a)
    {
        double Ma = Math.Pow(a, 2) / 2.0; 
        
        if (supportCount <= 2)
        {
            double M_span = Math.Abs(Math.Pow(L, 2) / 8.0 - Ma);
            return Math.Max(Ma, M_span);
        }
        else if (supportCount == 3)
        {
            double R1 = (3.0 / 8.0) * L + a + (3.0 / 4.0) * (Math.Pow(a, 2) / L);
            double x_sup = R1 - a; 
            
            double M_span = (x_sup > 0 && x_sup < L) ? (R1 * x_sup - 0.5 * Math.Pow(x_sup + a, 2)) : 0;
            double M_sup = Math.Abs(Math.Pow(a, 2) / 4.0 - Math.Pow(L, 2) / 8.0);
            
            return Math.Max(Ma, Math.Max(M_sup, M_span));
        }
        else 
        {
            double R1 = 0.4 * L + a + 0.6 * (Math.Pow(a, 2) / L);
            double x_sup = R1 - a;
            
            double M_span = (x_sup > 0 && x_sup < L) ? (R1 * x_sup - 0.5 * Math.Pow(x_sup + a, 2)) : 0;
            double M_sup = Math.Abs(Math.Pow(a, 2) / 10.0 - Math.Pow(L, 2) / 10.0);
            double M_int = Math.Pow(L, 2) / 10.0; 
            
            return Math.Max(Ma, Math.Max(M_sup, Math.Max(M_span, M_int)));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}