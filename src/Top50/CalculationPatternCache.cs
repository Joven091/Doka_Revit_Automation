using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard.Top50
{
    // Only FewestComponentPatterns is memoized: the key records every input that
    // enumerator reads. LongestSimultaneous and inventory quantities affect the
    // later allocation, not these immutable patterns. Lifetime is one search run.
    internal sealed class CalculationPatternCache : ITop50PatternSource
    {
        private readonly FewestComponentPatterns source=new FewestComponentPatterns();
        private readonly Dictionary<string,IReadOnlyList<PanelPattern>> items=new Dictionary<string,IReadOnlyList<PanelPattern>>(StringComparer.Ordinal);
        internal int Preparations {get;private set;}
        internal int Hits {get;private set;}
        public IReadOnlyList<PanelPattern> Enumerate(long length,IEnumerable<int> catalog,SearchLimits limits,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var sizes=catalog.Distinct().OrderByDescending(n=>n).ToArray();
            string key=FormattableString.Invariant($"{length}|{limits.MaxInternalFillers}|{limits.MaxFillerMm}|{limits.PatternsPerLength}|{limits.PatternVisitLimit}|")
                +string.Join(",",sizes.Select(n=>n.ToString(CultureInfo.InvariantCulture)));
            if(items.TryGetValue(key,out var found)){Hits++;return found;}
            var result=source.Enumerate(length,sizes,limits,cancellation);
            cancellation.ThrowIfCancellationRequested();
            // Eviction bounds memory, not the design space: evicted entries are
            // calculated again using the same node allowance and traversal.
            if(items.Count>=4096)items.Clear();
            items.Add(key,result);Preparations++;return result;
        }
    }
}
