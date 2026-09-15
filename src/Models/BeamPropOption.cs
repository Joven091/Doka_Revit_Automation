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
            if(double.IsNaN(extensionMm)||double.IsInfinity(extensionMm)||extensionMm<MinimumMm||extensionMm>MaximumMm)
                throw new ArgumentException($"{Label}: prop extension must be within the available table range {MinimumMm:0}–{MaximumMm:0} mm.");
            lookupMeters=Math.Ceiling(extensionMm/100)/10;
            double? capacity=PropDataHelper.GetCapacity(lookupMeters,CapacityKey);
            if(!capacity.HasValue||capacity.Value<=0)throw new ArgumentException("Prop capacity data not found for this extension and configuration.");
            return capacity.Value;
        }
    }
}
