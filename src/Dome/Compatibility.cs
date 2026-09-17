using System;
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
namespace EngineeringDashboard.Dome
{
    internal static class Numeric
    {
        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
