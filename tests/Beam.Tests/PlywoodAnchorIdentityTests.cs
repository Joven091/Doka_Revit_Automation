using System;
using System.Linq;
using EngineeringDashboard;

internal static class PlywoodAnchorIdentityTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Plywood anchor identity: "+message);}
    static void Rejected(Action action,string message)
    {bool failed=false;try{action();}catch(ArgumentException){failed=true;}Check(failed,message);}
    static void ExactSources(double start,double end,double[] required)
    {
        var rows=PlywoodSpacing.FillRequiredRows(start,end,488,required);
        Check(required.All(x=>Array.IndexOf(rows,x)>=0),"every exact mandatory/source coordinate survives coverage-anchor replacement");
        Check(rows.Zip(rows.Skip(1),(a,b)=>b-a).All(g=>g>=80-1e-7 && g<=488+1e-7),"all physical row spacing and flange limits remain enforced");
        var widths=PlywoodSpacing.RowTributaries(start,end,rows);
        Check(Math.Abs(widths.Sum()-(end-start))<1e-8,"tributary widths conserve the full supported interval");
    }
    internal static void Run()
    {
        const double start=2400,end=4920,first=2440,last=4880;
        foreach(int sign in new[] {-1,1})
        foreach(double delta in new[] {Math.BitIncrement(first)-first,Math.BitIncrement(last)-last,9e-8})
            ExactSources(start,end,new[] {first+sign*delta,last+sign*delta});
        foreach(int sign in new[] {-1,1})
        {
            Rejected(()=>PlywoodSpacing.FillRequiredRows(start,end,488,new[] {first+sign*1.1e-7,last}),"a mandatory sheet-joint displacement outside the existing identity tolerance is still rejected at the near end");
            Rejected(()=>PlywoodSpacing.FillRequiredRows(start,end,488,new[] {first,last+sign*1.1e-7}),"a mandatory sheet-joint displacement outside the existing identity tolerance is still rejected at the far end");
        }
        double near=first+(Math.BitIncrement(first)-first),far=last-(Math.BitIncrement(last)-last);
        ExactSources(start,end,new[] {near,near+80,far-80,far});
        Rejected(()=>PlywoodSpacing.FillRequiredRows(start,end,488,new[] {near,near+1e-8,far}),"distinct mandatory rows are not silently coalesced or dropped");
        foreach(double anchor in new[] {first,last})
        foreach(int sign in new[] {-1,1})
        {
            var pair=new[] {anchor,anchor+sign*1e-10};
            Rejected(()=>PlywoodSpacing.FillRequiredRows(start,end,488,pair),"an exact original anchor remains a distinct mandatory source when another near-identical source is present");
            Rejected(()=>PlywoodSpacing.FillRequiredRows(start,end,488,pair.Reverse().ToArray()),"reversing near-identical source order preserves distinct-mandatory rejection at both coverage ends");
        }
        Rejected(()=>PlywoodSpacing.FillRequiredRows(start,end,488,new[] {start-1e-8,far}),"row identity handling does not permit a source outside coverage");
        const double parentLength=6099.999812417978,beamLength=6099.999812417687,covered=5950;
        double sharedStart=(parentLength-covered)/2,beamStart=(beamLength-covered)/2;
        Check(sharedStart!=beamStart && Math.Abs(sharedStart-beamStart)<1e-7,"the actual Area2 parent/B2 span mismatch is reproduced exactly");
        var sources=PlywoodSpacing.SheetSupportRows(sharedStart,sharedStart+covered,250);
        var actual=PlywoodSpacing.FillRequiredRows(beamStart,beamStart+covered,250,sources);
        Check(actual.SequenceEqual(sources),"actual Area2 B2 retains the shared source grid exactly instead of adding microscopic endpoint duplicates");
        var sourceWidths=PlywoodSpacing.RowTributaries(sharedStart,sharedStart+covered,sources);
        var physicalLoads=new double[actual.Length];const double area=6.9;
        for(int i=0;i<sources.Length;i++)
        {
            int row=Array.IndexOf(actual,sources[i]);Check(row>=0,"every actual shared slab row retains its exact source index");
            physicalLoads[row]+=sourceWidths[i]*area/1000;
        }
        Check(Math.Abs(physicalLoads.Sum()-covered*area/1000)<1e-10,"the mapped shared slab force is preserved without removing any load source");
        Check(Math.Abs(actual.Zip(physicalLoads,(x,f)=>x*f).Sum()-sources.Zip(sourceWidths,(x,w)=>x*w*area/1000).Sum())<1e-7,"the mapped shared slab first moment is preserved");
        var local=actual.Select(x=>x-beamStart).ToArray();var tributaries=PlywoodSpacing.RowTributaries(0,covered,local);
        var strips=TransferredSlabOptimizer.ConcreteEndStripTributaries(covered,local,beamStart);
        for(int i=0;i<tributaries.Length;i++)tributaries[i]+=strips[i];
        Check(Math.Abs(tributaries.Sum()-beamLength)<1e-8,"the full actual beam length including both end strips remains loaded");
        double moment=actual.Zip(tributaries,(x,w)=>x*w).Sum();
        Check(Math.Abs(moment-beamLength*beamLength/2)<1e-5,"the full beam end-strip load retains its longitudinal first moment");
        Console.WriteLine("PASS "+checks+" plywood coverage-anchor identity checks: exact source rows, both-end ULP/tolerance boundaries and actual Area2 force/moment mapping");
    }
}
