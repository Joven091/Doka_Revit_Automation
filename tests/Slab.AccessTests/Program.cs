using System;
using System.Linq;
using EngineeringDashboard;

internal static class Program
{
    private static int checks;
    private static void Near(double expected, double actual, string description, double tolerance = 2e-8)
    {
        checks++;
        if (Math.Abs(expected - actual) > tolerance * Math.Max(1, Math.Abs(expected)))
            throw new Exception(description + ": expected " + expected + ", got " + actual);
    }
    private static void Check(bool condition, string description) { checks++; if (!condition) throw new Exception(description); }
    private static int Main()
    {
        try
        {
            foreach (int count in new[] { 2, 3, 4, 5, 6, 9, 16, 32 })
            foreach (double span in new[] { .5, 1.0, 2.0 })
            foreach (double end in new[] { 0.0, .15, .5, 1.0 })
            {
                double length = (count - 1) * span + 2 * end;
                var actual = SlabBeamAnalysis.Analyze(count, span, end, end, new[] { new SlabBeamLoad { End = length, Load = 3.7 } }, 449.8756, 350);
                var reference = ContinuousBeamResponse.Analyze(count, span, end, 3.7, 449.8756);
                Near(reference.Moment, actual.Moment, "Full UDL moment parity");
                Near(reference.Shear, actual.Shear, "Full UDL shear parity");
                Near(reference.SpanDeflection, actual.SpanDeflection, "Full UDL span displacement parity");
                Near(reference.CantileverDeflection, actual.LeftTipDeflection, "Full UDL left tip includes rotation");
                Near(reference.CantileverDeflection, actual.RightTipDeflection, "Full UDL right tip includes rotation");
                for (int i = 0; i < count; i++) Near(reference.Reactions[i], actual.Reactions[i], "Full UDL individual reaction parity");
            }
            foreach (int count in new[] { 2, 3, 5, 8 })
            foreach (double left in new[] { .0, .15, .4 })
            foreach (double right in new[] { .0, .25, .75 })
            foreach (bool both in new[] { false, true })
            {
                const double span = 1.5;
                double length = left + (count - 1) * span + right;
                double accessLeft = both ? Math.Min(.25, left + .1) : 0, accessRight = Math.Min(.6, right + .15);
                var zones = SlabBeamAnalysis.Zones(length, accessLeft, accessRight, 9.4, .75, .75);
                var actual = SlabBeamAnalysis.Analyze(count, span, left, right, zones, 449.8756, 350);
                IndependentFiniteElement.Compare(actual, Near);
                Near(zones.Sum(q => q.Load * (q.End - q.Start)), actual.Reactions.Sum(), "Partial load vertical equilibrium");
                Near(zones.Sum(q => q.Load * (q.End - q.Start) * (q.Start + q.End) / 2), actual.Reactions.Select((r,i) => r * actual.Supports[i]).Sum(), "Partial load moment equilibrium");
                foreach (double support in actual.Supports) Near(0, actual.Response(support)[2], "Each support displacement is zero");
                var reflected = zones.Reverse().Select(q => new SlabBeamLoad { Start = length - q.End, End = length - q.Start, Load = q.Load, IsAccess = q.IsAccess }).ToArray();
                var mirror = SlabBeamAnalysis.Analyze(count, span, right, left, reflected, 449.8756, 350);
                Near(actual.Moment, mirror.Moment, "Left/right reflection moment");
                Near(actual.Shear, mirror.Shear, "Left/right reflection shear");
                Near(actual.LeftTipDeflection, mirror.RightTipDeflection, "Left/right reflection tip");
                Near(actual.DeflectionUtilization, mirror.DeflectionUtilization, "Left/right reflection criterion");
                for (int i = 0; i < count; i++) Near(actual.Reactions[i], mirror.Reactions[count - 1 - i], "Left/right reflection reactions");
                // Two supports are statically determinate, independent of the response implementation.
                if (count == 2)
                {
                    double expectedRight = zones.Sum(q => q.Load * (q.End - q.Start) * ((q.Start + q.End) / 2 - left)) / span;
                    Near(expectedRight, actual.Reactions[1], "Two-support partial UDL closed-form right reaction");
                }
                Near(0, actual.Response(0,false)[1], "Free left end has no moment");
                Near(0, actual.Response(length,true)[1], "Free right end has no moment");
            }
            var example = SlabBeamAnalysis.Analyze(2, 1.5, .15, .75,
                SlabBeamAnalysis.Zones(2.4, 0, .6, 10, .75, .75), 449.8756, 350);
            Near(0.6 * .75 + 1.8 * 10, example.TotalLoad, "Access total includes deck weight exactly once");
            Near(10, example.Loads[0].Load, "Concrete UDL continues beyond the last support to slab edge");
            Near(1.8, example.Loads[1].Start, "UDL transition is the slab edge, not the last support");
            Check(example.Loads[1].IsAccess, "Access diagram zone identity is retained");
            Console.WriteLine("Passed " + checks + " slab access beam checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
