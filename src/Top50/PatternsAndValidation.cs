using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace EngineeringDashboard.Top50
{
    public sealed class FewestComponentPatterns : ITop50PatternSource
    {
        public IReadOnlyList<PanelPattern> Enumerate(long length,IEnumerable<int> catalog,SearchLimits limits,CancellationToken cancellation)
        {
            if(length<=0)throw new ArgumentOutOfRangeException(nameof(length));
            var sizes=catalog.Distinct().OrderByDescending(x=>x).ToArray();
            if(sizes.Length==0 || sizes.Any(x=>x<=0))throw new ArgumentException("A positive nominal panel catalog is required.");
            var result=new List<PanelPattern>();var panels=new List<int>();var clock=Stopwatch.StartNew();long visits=0;
            void Check(){cancellation.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>limits.Seconds)throw new TimeoutException("Retained pattern enumeration exceeded its explicit test/search budget; no infeasibility conclusion follows.");}
            void Find(int remaining,int start,long installed,int count)
            {
                if((++visits&1023)==0)Check();
                if(result.Count>=limits.PatternsPerLength)return;
                int maximumFillers=Math.Min(limits.MaxInternalFillers,count-1);
                long minimum=length-(long)maximumFillers*limits.MaxFillerMm*1000;
                if(remaining==0)
                {
                    long remainder=length-installed;
                    if(remainder<0 || installed<minimum || (remainder>0 && maximumFillers==0))return;
                    var gaps=new long[count-1];
                    for(int i=0;i<maximumFillers && remainder>0;i++){gaps[i]=Math.Min(remainder,(long)limits.MaxFillerMm*1000);remainder-=gaps[i];}
                    if(remainder==0)result.Add(new PanelPattern(panels,gaps));return;
                }
                if(start>=sizes.Length || installed+(long)remaining*sizes[start]*1000<minimum || installed+(long)remaining*sizes[sizes.Length-1]*1000>length)return;
                for(int i=start;i<sizes.Length && result.Count<limits.PatternsPerLength;i++)
                {
                    long total=installed+(long)sizes[i]*1000;if(total>length)continue;
                    panels.Add(sizes[i]);Find(remaining-1,i,total,count);panels.RemoveAt(panels.Count-1);
                }
            }
            Check();
            for(int count=1;count<=limits.MaxComponents && result.Count<limits.PatternsPerLength;count++)Find(count,0,0,count);
            return result.AsReadOnly();
        }
    }

    public static class Top50Validation
    {
        public static IReadOnlyList<string> Validate(IReadOnlyList<WallRow> walls,Proposal proposal,SearchLimits limits)
        {
            var errors=new List<string>();var included=walls.Where(w=>w.Included).ToDictionary(w=>w.Id);
            if(proposal.Stock.Count==0 || proposal.Stock.Any(x=>x.Key<=0 || x.Value<=0))errors.Add("Reusable stock must have positive nominal sizes and counts.");
            var seen=new HashSet<string>();
            foreach(var group in proposal.Groups)
            {
                if(group.Walls.Count==0)errors.Add("Empty casting group.");
                var used=new Dictionary<int,int>();
                foreach(var a in group.Walls)
                {
                    if(!included.TryGetValue(a.WallId,out var wall)){errors.Add("Unknown or excluded wall: "+a.WallId);continue;}
                    if(!seen.Add(a.WallId))errors.Add("Wall assigned more than once: "+a.WallId);
                    var p=a.Pattern;
                    if(p.PanelsMm.Count<1 || p.PanelsMm.Count>limits.MaxComponents || p.InternalGapsMicrometres.Count!=p.PanelsMm.Count-1)errors.Add("Invalid component/internal-gap count: "+a.WallId);
                    if(p.InternalGapsMicrometres.Any(g=>g<0 || g>(long)limits.MaxFillerMm*1000) || p.FillerLocations>limits.MaxInternalFillers)errors.Add("Invalid internal filler: "+a.WallId);
                    if(p.LengthMicrometres!=wall.LengthMicrometres)errors.Add("Arrangement does not close to the rounded-once source measurement: "+a.WallId);
                    foreach(var size in p.PanelsMm){used.TryGetValue(size,out var n);used[size]=n+1;}
                }
                foreach(var pair in used)if(!proposal.Stock.TryGetValue(pair.Key,out var stock) || pair.Value>stock)errors.Add("Casting exceeds reusable stock for "+pair.Key+"mm.");
            }
            foreach(var id in included.Keys)if(!seen.Contains(id))errors.Add("Included wall not assigned: "+id);
            var longest=included.Values.OrderByDescending(w=>w.LengthMicrometres).ThenBy(w=>w.Id,StringComparer.Ordinal).Take(limits.LongestSimultaneous).Select(w=>w.Id).ToArray();
            if(longest.Length>0 && !proposal.Groups.Any(g=>longest.All(id=>g.Walls.Any(a=>a.WallId==id))))errors.Add("Required longest walls are not simultaneous in one casting.");
            if(proposal.IdenticalKitCount>0)
            {
                int kits=proposal.IdenticalKitCount;
                if(proposal.Stock.Any(p=>p.Value%kits!=0))errors.Add("Identical kit stock is not an exact repeated inventory.");
                foreach(var a in proposal.Groups.SelectMany(g=>g.Walls).Where(a=>longest.Contains(a.WallId)))
                    if(a.Pattern.PanelsMm.GroupBy(p=>p).Any(g=>!proposal.Stock.TryGetValue(g.Key,out var stock)||g.Count()>stock/kits))errors.Add("Required wall does not fit one identical kit: "+a.WallId);
            }
            return errors.AsReadOnly();
        }
    }
}
