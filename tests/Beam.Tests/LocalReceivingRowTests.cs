using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class LocalReceivingRowTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-7,message+": "+a+" / "+b);
    static void Spacing(double[] rows,double maximum)
        =>Check(rows.Zip(rows.Skip(1),(a,b)=>b-a).All(g=>g>=80-1e-7 && g<=maximum+1e-7),"Every receiving gap satisfies flange clearance and plywood span");
    static object Grid(bool both)
    {
        var shared=new[] {215.0,2440,4880,5100,5130,5390,5640,5960,7320,9760,10385};
        var left=shared.Select(x=>both && x==5100 ? 250.0 : 0).ToArray();
        var right=shared.Select(x=>x==5100 || x==5390 || x==5640 || x==5960 ? 0.0 : 250).ToArray();
        var input=new TowerInput {LengthMm=10600,TransferredSlab=new TransferredSlabInput {LocalReceivingRows=true,
            SharedRowsMm=shared,LeftBaselineWidthsMm=left,RightBaselineWidthsMm=right,LeftEffectiveWidthsMm=left,RightEffectiveWidthsMm=right,
            UpperSpacingMm=350,LeftSlabSpacingMm=488,RightSlabSpacingMm=488}};
        return typeof(TransferredSlabOptimizer).GetMethod("BuildIndividualRowGrid",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {input,10500.0,10500.0,50.0});
    }
    static T Field<T>(object grid,string field)=>(T)grid.GetType().GetField(field,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(grid);
    internal static void Run()
    {
        var requests=new[] {new ReceivingRowRequest {PositionMm=5100,Sides=1},new ReceivingRowRequest {PositionMm=5130,Sides=2}};
        var layout=ReceivingRows.Coordinate(50,10550,350,requests);
        Near(layout.ReceiversMm[0],layout.ReceiversMm[1],"Opposite slab transfers share one checked lower receiver");
        Check(requests.Select(x=>x.PositionMm).SequenceEqual(new[] {5100.0,5130}),"Upper slab row positions remain unchanged");
        Spacing(layout.RowsMm,350);
        requests[1].Sides=1;layout=ReceivingRows.Coordinate(50,10550,350,requests);
        Check(Math.Abs(layout.ReceiversMm[0]-layout.ReceiversMm[1])>=80-1e-7,"Two loaded rows of one slab remain separate receivers");Spacing(layout.RowsMm,350);
        var crossing=new[] {new ReceivingRowRequest {PositionMm=5368,Sides=1},new ReceivingRowRequest {PositionMm=5390,Crossing=true,MinimumMm=5390,MaximumMm=5960}};
        layout=ReceivingRows.Coordinate(50,10550,350,crossing);
        Check(layout.ReceiversMm.All(x=>x>=5390 && x<=5960),"Receiving row remains wholly inside the crossing footprint");Spacing(layout.RowsMm,350);
        var isolated=Grid(false);Check(isolated!=null,"B1 builds without foreign rows");
        var sources=Field<int[]>(isolated,"SlabSourceRowIndices");var rows=Field<double[]>(isolated,"Rows");
        Check(sources[3]==-1 && sources[5]==-1,"Remote slab and crossing rows do not become B1 requirements");
        Near(rows[sources[4]]+50,5130,"B1 retains its actual S1 receiving row");Spacing(rows,350);
        var joined=Grid(true);Check(joined!=null,"Adjacent slab rows are coordinated rather than rejected");
        sources=Field<int[]>(joined,"SlabSourceRowIndices");rows=Field<double[]>(joined,"Rows");
        Check(sources[3]==sources[4],"The 5100/5130 conflict has one explicit receiver mapping");
        Near(Field<double[]>(joined,"LeftScales").Sum(),.488,"Entire left slab design transfer retained");
        Near(Field<double[]>(joined,"RightScales").Sum(),7*.488,"Every right slab transfer retained exactly once");
        Spacing(rows,350);
        var random=new Random(10195130);
        for(int sample=0;sample<300;sample++)
        {
            double first=1000+sample*10,delta=1+random.Next(78);
            var pair=new[] {new ReceivingRowRequest {PositionMm=first,Sides=1},new ReceivingRowRequest {PositionMm=first+delta,Sides=2}};
            var result=ReceivingRows.Coordinate(0,10600,350,pair);Spacing(result.RowsMm,350);
            Check(result.ReceiversMm.All(result.RowsMm.Contains),"Every source has a physical receiving row");
            Check(result.ReceiversMm.Zip(pair,(x,p)=>Math.Abs(x-p.PositionMm)).All(d=>d<=80+1e-7),"Adjustments stay local to the original flange collision");
        }
        Console.WriteLine("Passed "+checks+" local receiving-row checks.");
    }
    internal static void Reported()
    {
        var input=ControlledPerformanceTests.Input("local-rows");
        input.Trace=new BeamSearchTrace("Reported local receiving rows");
        var result=TransferNetworkOptimizer.Optimize(input,input.Network);
        ControlledPerformanceTests.Validate(input,result);
        Check(!result.RequiresSeparateSlabFormwork,"All three slabs remain part of the calculated system");
        for(int beam=0;beam<result.Beams.Length;beam++)
        {
            var t=result.Beams[beam].Transfer;Spacing(t.RowsMm,result.Input.BeamSpacing(beam));
            Check(t.SharedSlabRowsMm!=null && t.SlabSourceRowIndices.Length==result.SlabRowsMm.Length,"Explicit source mapping survives final crossing rechecks");
            Near(t.LeftSlabSourceScales.Sum(),t.LeftUpperScales.Sum(),"Left design transfers conserved through adjustment");
            Near(t.RightSlabSourceScales.Sum(),t.RightUpperScales.Sum(),"Right design transfers conserved through adjustment");
        }
        for(int bay=0;bay<result.Bays.Length;bay++)foreach(int source in Enumerable.Range(0,result.SlabRowsMm.Length).Where(i=>result.SlabRowActive(bay,i)))
        {
            Check(result.SlabBeamRow(bay,source)>=0 && result.SlabBeamRow(bay+1,source)>=0,"Every physical slab H20 has both receivers");
            var left=result.Beams[bay].Transfer;var right=result.Beams[bay+1].Transfer;
            Near(left.RightSlabSourceScales[source],right.LeftSlabSourceScales[source],"Both ends use the same slab transfer");
        }
        Console.WriteLine("REPORTED local rows: "+result.LegCount+" legs, "+result.H20Count+" H20s, "+input.Trace.ElapsedSeconds.ToString("0.000")+" seconds.");
        Console.WriteLine("Passed "+checks+" reported local-row capacity, mapping and conservation checks.");
    }
}
