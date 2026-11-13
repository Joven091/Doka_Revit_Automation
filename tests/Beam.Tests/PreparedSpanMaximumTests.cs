using System;
using System.Linq;
using EngineeringDashboard;

internal static class PreparedSpanMaximumTests
{
    static bool Reference(TowerInput input,TransferredSlabOptimizer.Geometry geometry,TransferredSlabOptimizer.SpanGeometryShape shape,double largest)
    {
        for(int run=0;run<geometry.Stock.Length;run++)
        {
            double q=largest+input.Primary.SelfWeightKnM(geometry.Stock[run].StockLengthMm),tail=shape.Tails[run],span=shape.ClearSpans[run];
            if(q*tail>input.Primary.ShearLimitKn+1e-7 || q*tail*tail/2>input.Primary.MomentLimitKnM+1e-7
                || q*span>2*input.Primary.ShearLimitKn+1e-7 || q*span*span>16*input.Primary.MomentLimitKnM+1e-7)return false;
        }
        return true;
    }
    internal static void Run()
    {
        int checks=0;var random=new Random(752091);
        foreach(var material in new[] {PrimaryBeamMaterial.DoubleH20,PrimaryBeamMaterial.WS10})for(int trial=0;trial<60;trial++)
        {
            var input=new TowerInput {PrimaryMaterial=material};double start=0;
            var stocks=Enumerable.Range(0,1+trial%4).Select(run=>
            {
                double length=material==PrimaryBeamMaterial.WS10 ? new[] {2500.0,3000,4500,5000}[run] : 1800+random.Next(3200);
                var piece=new TowerStockPiece {StartMm=start,StockLengthMm=length};start+=length;return piece;
            }).ToArray();
            var geometry=new TransferredSlabOptimizer.Geometry {Stock=stocks,MemberSupports=stocks.Select(p=>new[] {150+random.NextDouble()*500,p.StockLengthMm-150-random.NextDouble()*500}).ToArray()};
            var bands=Enumerable.Range(0,trial%4).Select(i=>new BeamDistributedLoad {StartMm=start*(i+.2)/(1+trial%4),EndMm=start*(i+.5)/(1+trial%4)}).ToArray();
            var context=new TransferredSlabOptimizer.SpanGeometryContext(trial%2==0 ? 0 : 25.125,bands);
            var shape=TransferredSlabOptimizer.PrepareSpanShape(geometry,context);
            if(shape.MaximumTail!=shape.Tails.Max() || shape.MaximumClearSpan!=shape.ClearSpans.Max())throw new Exception("Span maxima did not retain exact prepared dimensions");
            var boundaries=shape.Tails.SelectMany(t=>new[] {(input.Primary.ShearLimitKn+1e-7)/t,2*(input.Primary.MomentLimitKnM+1e-7)/(t*t)})
                .Concat(shape.ClearSpans.SelectMany(s=>new[] {(2*input.Primary.ShearLimitKn+1e-7)/s,(16*input.Primary.MomentLimitKnM+1e-7)/(s*s)})).Where(double.IsFinite)
                .SelectMany(q=>new[] {Math.BitDecrement(q),q,Math.BitIncrement(q)}).Concat(new[] {0.0,random.NextDouble()*100}).ToArray();
            foreach(double q in boundaries)
            {
                if(TransferredSlabOptimizer.PreparedSpanShapeFits(input,geometry,context,q)!=Reference(input,geometry,shape,q))throw new Exception("Span maxima changed an exact scalar acceptance boundary");
                checks++;
            }
        }
        Console.WriteLine("PASS "+checks+" span-maximum checks: exact per-run reference including adjacent floating-point boundaries and WS10 self-weight");
    }
}
