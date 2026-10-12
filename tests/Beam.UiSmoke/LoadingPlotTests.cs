using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EngineeringDashboard;

internal static class LoadingPlotTests
{
    public static void Run(string output)
    {
        int checks=0;
        foreach(int width in new[] {1040,600,320})
        foreach(int count in new[] {4,10})
        {
            var supports=count==4 ? new[] {300.0,1824,2074,3598} : Enumerable.Range(0,count).Select(i=>300+i*325.0).ToArray();
            var reactions=Enumerable.Range(0,count).Select(i=>i==2 ? -23.46 : 25.84-i).ToArray();
            var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=3825,KnPerM=24.026},new BeamDistributedLoad {StartMm=3825,EndMm=3900,KnPerM=9.916},new BeamDistributedLoad {StartMm=175,EndMm=825,KnPerM=5.681}};
            var plot=new TowerDiagramWindow.MemberLoadingPlot(3900,supports,reactions,uniform,null);
            plot.Measure(new Size(width,double.PositiveInfinity));plot.Arrange(new Rect(0,0,width,plot.DesiredSize.Height));plot.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,(int)Math.Ceiling(plot.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(plot);
            if(plot.DrawnSupportCount!=count || plot.DrawnReactionLabelCount!=count || plot.ReactionLabelBounds.Count!=count)throw new Exception("Every support must display its signed reaction, including close R2/R3 labels.");checks++;
            if(plot.ReactionLabelBounds.Where((r,i)=>plot.ReactionLabelBounds.Skip(i+1).Any(r.IntersectsWith)).Any())throw new Exception("Reaction label rows must not overlap.");checks++;
            if(plot.ReactionLabelBounds.Any(r=>r.Top<0 || r.Bottom>plot.ActualHeight || r.Left<0 || r.Right>width))throw new Exception("All reaction labels must stay inside the measured loading card.");checks++;
            if(output!=null && width==1040 && count==4)
            {
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(output,"close-reaction-labels.png")))png.Save(file);
            }
        }
        Console.WriteLine($"Loading plot: {checks} checks passed for close supports, signed reactions and narrow cards.");
    }
}
