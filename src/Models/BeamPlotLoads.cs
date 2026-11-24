using System;
using System.Globalization;

namespace EngineeringDashboard
{
    public sealed class BeamPointLoad { public double PositionMm, Kn; }
    public sealed class BeamDistributedLoad { public double StartMm, EndMm, KnPerM; }

    internal static class LoadValue
    {
        internal static string Format(double value,int decimals=3)
        {
            double step=Math.Pow(10,-decimals);
            if(value!=0 && Math.Abs(value)<step/2)return (value<0 ? "−<" : "<")+step.ToString("0."+new string('0',decimals),CultureInfo.CurrentCulture);
            return value.ToString("0."+new string('#',decimals),CultureInfo.CurrentCulture);
        }
    }
}
