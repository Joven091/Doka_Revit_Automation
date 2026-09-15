using System;
using System.Linq;
namespace EngineeringDashboard
{
    /// <summary>Full uniform load on equal spans, constant EI and two equal cantilevers.</summary>
    public sealed class ContinuousBeamResponse
    {
        public int Count { get; private set; }
        public double Reaction { get; private set; }
        public double Shear { get; private set; }
        public double Moment { get; private set; }
        public double SpanDeflection { get; private set; }
        public double[] Reactions { get; private set; }
        public static double[] SupportMoments(int count, double length, double left, double right, double load)
        {
            if (count < 2 || count > 10000 || length <= 0 || double.IsInfinity(length) || double.IsNaN(length)) throw new ArgumentException("Invalid continuous beam geometry.");
            var m = new double[count]; m[0] = -load * left * left / 2; m[count - 1] = -load * right * right / 2;
            int k = count - 2; var diagonal = new double[k]; var rhs = new double[k];
            for (int i = 0; i < k; i++)
            {
                diagonal[i] = 4; rhs[i] = -load * length * length / 2;
                if (i == 0) rhs[i] -= m[0]; if (i == k - 1) rhs[i] -= m[count - 1];
                if (i > 0) { diagonal[i] -= 1 / diagonal[i - 1]; rhs[i] -= rhs[i - 1] / diagonal[i - 1]; }
            }
            for (int i = k - 1; i >= 0; i--) m[i + 1] = (rhs[i] - (i < k - 1 ? m[i + 2] : 0)) / diagonal[i];
            return m;
        }
        public static double[] SupportReactions(double[] m, double length, double left, double right, double load)
        {
            var reactions = new double[m.Length];
            for (int i = 0; i < m.Length - 1; i++)
            {
                double v = load * length / 2 + (m[i + 1] - m[i]) / length;
                reactions[i] += v; reactions[i + 1] += load * length - v;
            }
            reactions[0] += load * left; reactions[reactions.Length - 1] += load * right;
            return reactions;
        }
        public static ContinuousBeamResponse Analyze(int count, double length, double cantilever, double load, double ei)
        {
            var m = SupportMoments(count, length, cantilever, cantilever, load);
            var reactions = SupportReactions(m, length, cantilever, cantilever, load);
            double shear = Math.Abs(load * cantilever), moment = m.Max(x => Math.Abs(x));
            for (int i = 0; i < count - 1; i++)
            {
                double v = load * length / 2 + (m[i + 1] - m[i]) / length;
                shear = Math.Max(shear, Math.Max(Math.Abs(v), Math.Abs(v - load * length)));
                if (load > 0)
                {
                    double x = v / load;
                    if (x > 0 && x < length) moment = Math.Max(moment, Math.Abs(m[i] + v * x - load * x * x / 2));
                }
            }
            return new ContinuousBeamResponse { Count = count, Reaction = reactions.Max(), Reactions = reactions,
                Shear = shear, Moment = moment, SpanDeflection = ContinuousSpanDeflection.FromMoments(m, length, load, ei) };
        }
    }
    /// <summary>Component-wise envelope for all counts >=5 in the adopted model.
    /// Five/six support moment-pair convex hull and proof are documented in docs/slab-five-plus.md.</summary>
    public sealed class SlabSupportEnvelope
    {
        public double Reaction { get; private set; }
        public double Shear { get; private set; }
        public double Moment { get; private set; }
        public double SpanDeflection { get; private set; }
        public int ReactionCount { get; private set; }
        public static SlabSupportEnvelope ForSelection(int count, double length, double cantilever, double load, double ei)
        {
            if (count == 5) return Calculate(length, cantilever, load, ei); // Selector value 5 means 5+.
            var response = ContinuousBeamResponse.Analyze(count, length, cantilever, load, ei);
            return new SlabSupportEnvelope { Reaction = response.Reaction, Shear = response.Shear, Moment = response.Moment,
                SpanDeflection = response.SpanDeflection, ReactionCount = count };
        }
        public static SlabSupportEnvelope Calculate(double length, double cantilever, double load, double ei)
        {
            var five = ContinuousBeamResponse.Analyze(5, length, cantilever, load, ei);
            var six = ContinuousBeamResponse.Analyze(6, length, cantilever, load, ei);
            return new SlabSupportEnvelope { Reaction = Math.Max(five.Reaction, six.Reaction),
                Shear = Math.Max(five.Shear, six.Shear), Moment = Math.Max(five.Moment, six.Moment),
                SpanDeflection = Math.Max(five.SpanDeflection, six.SpanDeflection),
                ReactionCount = five.Reaction >= six.Reaction ? 5 : 6 };
        }
    }
}
