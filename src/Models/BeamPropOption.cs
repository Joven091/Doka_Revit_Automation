using System;
using System.Collections.Generic;
using System.Linq;
namespace EngineeringDashboard
{
    /// <summary>Selections reference the same capacity keys and rows as the slab calculator.</summary>
    public sealed class BeamPropOption
    {
        public string Label { get; }
        public string CapacityKey { get; }
        public string Family => CapacityKey.StartsWith("DokaFit Prop", StringComparison.Ordinal) ? "DokaFit Prop" : "Prop Type Eurex eco 20";
        public string Size => CapacityKey.Replace("DokaFit Prop ", "").Split(' ')[0];
        public string Orientation => CapacityKey.Substring(CapacityKey.IndexOf("at ", StringComparison.Ordinal));
        public double MinimumMm { get; }
        public double MaximumMm { get; }
        private BeamPropOption(string label,string key,double min,double max)
        {Label=label;CapacityKey=key;MinimumMm=min;MaximumMm=max;}
        public static IReadOnlyList<BeamPropOption> All { get; } = Create().AsReadOnly();
        private static List<BeamPropOption> Create()
        {
            var options=new List<BeamPropOption>();
            foreach(var table in new[]{PropDataHelper.PropCapacities,PropDataHelper.DokaFitPropCapacities})
                foreach(string key in table.Values.SelectMany(row=>row.Keys).Distinct())
                {
                    var lengths=table.Where(row=>row.Value.TryGetValue(key,out var value)&&value.HasValue&&value.Value>0).Select(row=>row.Key*1000).ToList();
                    if(lengths.Count>0) options.Add(new BeamPropOption(key.StartsWith("DokaFit Prop",StringComparison.Ordinal)?key:"Eurex eco 20 · "+key,key,lengths.Min(),lengths.Max()));
                }
            return options;
        }
        public double Capacity(double extensionMm,out double lookupMeters)
        {
            double capacity = PropDataHelper.CapacityAtExtensionMm(extensionMm, CapacityKey, out double lookupMm);
            lookupMeters = lookupMm / 1000;
            return capacity;
        }
    }
}
