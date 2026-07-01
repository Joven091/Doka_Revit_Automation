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
    private const double SYSTEM_DEPTH_METERS = 0.418; // Depth of H20 beams + plywood

    // --- Inputs: Slab & Geometry ---
    private double _slabThickness = 300;
    private double _floorHeight = 3000;
    
    // --- Inputs: Secondary Beam Layout ---
    private int _primaryBeamCount = 3; // Number of primary beams supporting the secondary beam
    private double _secondaryBeamSpacing = 500; // Distance between secondary beams
    private double _secondaryBeamCantilever = 500; // a_s

    // --- Inputs: Primary Beam & Prop Layout ---
    private int _propCount = 3; // Number of props supporting the primary beam
    private double _propSpacing = 1000; // L_p
    private double _primaryBeamCantilever = 500; // a_p
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

    // --- Public Input Properties ---
    public double SlabThickness { get => _slabThickness; set { _slabThickness = value; OnPropertyChanged(); Calculate(); } }
    public double FloorHeight { get => _floorHeight; set { _floorHeight = value; OnPropertyChanged(); Calculate(); } }
    
    public double SecondaryBeamSpacing { get => _secondaryBeamSpacing; set { _secondaryBeamSpacing = value; OnPropertyChanged(); Calculate(); } }
    public int PrimaryBeamCount { get => _primaryBeamCount; set { _primaryBeamCount = value; OnPropertyChanged(); Calculate(); } }
    public double SecondaryBeamCantilever { get => _secondaryBeamCantilever; set { _secondaryBeamCantilever = value; OnPropertyChanged(); Calculate(); } }

    public int PropCount { get => _propCount; set { _propCount = value; OnPropertyChanged(); Calculate(); } }
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
        List<string> warnings = new();

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
        double w_slab = SlabLoadHelper.CalculateTotalLoad((int)SlabThickness); // kN/m²
        double a_s = SecondaryBeamCantilever / 1000.0;
        double L_p = PropSpacing / 1000.0;
        double a_p = PrimaryBeamCantilever / 1000.0;
        double c_sec = SecondaryBeamSpacing / 1000.0;

        // ====================================================================
        // LIMIT CHECK A: SECONDARY BEAM LIMITS (H20 Timber)
        // ====================================================================
        double w_sb = w_slab * c_sec; // Line load on a single secondary beam (kN/m)
        
        // A.1: Independent Cantilever Check
        double v_cantilever_sec = w_sb * a_s;
        double m_cantilever_sec = (w_sb * Math.Pow(a_s, 2)) / 2.0;
        
        double L_s_max_sec_shear = 0;
        double L_s_max_sec_moment = 0;

        if (v_cantilever_sec > MAX_H20_SHEAR_KN)
        {
            warnings.Add($"Secondary beam cantilever shear exceeds limit ({Math.Round(v_cantilever_sec,2)} kN). Reduce a_s.");
        }
        else if (m_cantilever_sec > MAX_H20_MOMENT_KNM)
        {
            warnings.Add($"Secondary beam cantilever moment exceeds limit ({Math.Round(m_cantilever_sec,2)} kNm). Reduce a_s.");
        }
        else
        {
            // A.2: Span calculation based on support conditions (PrimaryBeamCount)
            if (PrimaryBeamCount <= 2)
            {
                L_s_max_sec_shear = 2.0 * (MAX_H20_SHEAR_KN / w_sb);
                L_s_max_sec_moment = Math.Sqrt((8.0 * MAX_H20_MOMENT_KNM) / w_sb);
            }
            else if (PrimaryBeamCount == 3)
            {
                L_s_max_sec_shear = MAX_H20_SHEAR_KN / (0.625 * w_sb);
                L_s_max_sec_moment = Math.Sqrt((8.0 * MAX_H20_MOMENT_KNM) / w_sb);
            }
            else // 4+ Supports
            {
                L_s_max_sec_shear = MAX_H20_SHEAR_KN / (0.6 * w_sb);
                L_s_max_sec_moment = Math.Sqrt((10.0 * MAX_H20_MOMENT_KNM) / w_sb);
            }
        }

        // ====================================================================
        // LIMIT CHECK B: PRIMARY BEAM & PROP LIMITS
        // ====================================================================
        double max_w_pb_prop = 0;
        double max_w_pb_shear = 0;

        if (PropCount <= 2)
        {
            max_w_pb_prop = capacity.Value / ((L_p / 2.0) + a_p);
            max_w_pb_shear = MAX_H20_SHEAR_KN / Math.Max(a_p, L_p / 2.0);
        }
        else if (PropCount == 3)
        {
            max_w_pb_prop = capacity.Value / Math.Max(0.625 * L_p, a_p + 0.4 * L_p);
            max_w_pb_shear = MAX_H20_SHEAR_KN / Math.Max(a_p, 0.625 * L_p);
        }
        else
        {
            max_w_pb_prop = capacity.Value / Math.Max(1.1 * L_p, a_p + 0.4 * L_p);
            max_w_pb_shear = MAX_H20_SHEAR_KN / Math.Max(a_p, 0.6 * L_p);
        }

        double w_pb_allowable = Math.Min(max_w_pb_prop, max_w_pb_shear);
        double L_s_max_primary = 0;

        if (PrimaryBeamCount <= 2)
        {
            L_s_max_primary = 2.0 * ((w_pb_allowable / w_slab) - a_s);
        }
        else if (PrimaryBeamCount == 3)
        {
            double L_s_1 = (w_pb_allowable / w_slab) / 0.625;
            double L_s_2 = ((w_pb_allowable / w_slab) - a_s) / 0.4;
            L_s_max_primary = Math.Min(L_s_1, L_s_2);
        }
        else 
        {
            double L_s_1 = (w_pb_allowable / w_slab) / 1.1;
            double L_s_2 = ((w_pb_allowable / w_slab) - a_s) / 0.4;
            L_s_max_primary = Math.Min(L_s_1, L_s_2);
        }

        // ====================================================================
        // FINAL RESOLUTION: FIND THE WEAKEST LINK
        // ====================================================================
        double L_s_max = Math.Min(L_s_max_primary, Math.Min(L_s_max_sec_shear, L_s_max_sec_moment));

        if (L_s_max < 0 || (L_s_max_sec_shear == 0 && L_s_max_sec_moment == 0)) 
        {
            L_s_max = 0;
        }

        // Round down to the nearest 10 mm (0.01 m)
        double L_s = Math.Floor(L_s_max * 100) / 100.0;
        SuggestedPrimaryBeamSpacing = Math.Round(L_s * 1000); // in mm

        // ====================================================================
        // RECALCULATE ACTUAL FORCES FOR UI DISPLAY
        // ====================================================================
        double w_pb = 0;
        if (PrimaryBeamCount <= 2) 
        {
            w_pb = w_slab * ((L_s / 2.0) + a_s);
        }
        else if (PrimaryBeamCount == 3)
        {
            w_pb = w_slab * Math.Max(0.625 * L_s, a_s + 0.4 * L_s);
        }
        else 
        {
            w_pb = w_slab * Math.Max(1.1 * L_s, a_s + 0.4 * L_s);
        }

        double P_max = 0;
        double V_max = 0;

        if (PropCount <= 2)
        {
            P_max = w_pb * ((L_p / 2.0) + a_p);
            V_max = w_pb * Math.Max(a_p, L_p / 2.0);
        }
        else if (PropCount == 3)
        {
            P_max = w_pb * Math.Max(0.625 * L_p, a_p + 0.4 * L_p);
            V_max = w_pb * Math.Max(a_p, 0.625 * L_p);
        }
        else 
        {
            P_max = w_pb * Math.Max(1.1 * L_p, a_p + 0.4 * L_p);
            V_max = w_pb * Math.Max(a_p, 0.6 * L_p);
        }

        ActualPropLoad = Math.Round(P_max, 2);
        H20Shear = Math.Round(V_max, 2);
        H20Utilization = Math.Round((V_max / MAX_H20_SHEAR_KN) * 100, 1);
        UtilizationRate = Math.Round((P_max / capacity.Value) * 100, 1);

        if (SuggestedPrimaryBeamSpacing <= 0)
        {
            warnings.Add("Cannot find a valid primary beam spacing for the given parameters.");
        }
        
        if (warnings.Any())
        {
            WarningMessage = string.Join(" | ", warnings);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}