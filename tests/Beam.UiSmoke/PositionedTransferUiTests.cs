using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EngineeringDashboard;

internal static class PositionedTransferUiTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception("Positioned UI: "+message);}
    static void Render(FrameworkElement element,double width,string path=null)
    {
        element.Measure(new Size(width,double.PositiveInfinity));element.Arrange(new Rect(0,0,width,element.DesiredSize.Height));element.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(element.DesiredSize.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        if(path!=null){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
    }
    internal static void Run(string preview)
    {
        var layout=PositionedTransferTests.Fixture();var model=EquivalentTransferredLoading.Create(layout,0,0);var view=new EquivalentLoadingView(model,false);
        Render(view,1100);Check(!view.IsDetailsOpen && model.HasPositionedLoads,"positioned diagram starts without cards");
        int foot=Array.FindIndex(model.Components,c=>c.Name.Contains("slab H20"));
        var target=view.Plot.Targets.Last(t=>t.Component==foot);view.Plot.SelectAt(new Point(target.Bounds.X+target.Bounds.Width/2,target.Bounds.Y+target.Bounds.Height/2));Render(view,1100);
        Check(view.VisibleCalculation.Any(t=>t.Contains("3.26838")) && view.VisibleCalculation.Any(t=>t.Contains("1.425")) && view.VisibleCalculation.Any(t=>t.Contains("2125 mm")),"clicked slab-foot source and physical stock coordinate are explained");
        Check(view.VisibleCalculation.Any(t=>t.Contains("2.04274")),"conversion retains separate physical foot load");
        foreach(double width in new[] {1100.0,760,520})
        {
            Render(view,width,preview==null ? null : Path.ChangeExtension(preview,"positioned-card-"+width+".png"));
            Check(view.Plot.TextBounds.All(r=>r.Left>=0 && r.Right<=view.Plot.ActualWidth),"reference labels stay inside narrow plot");
            for(int i=0;i<view.Plot.TextBounds.Count;i++)for(int j=i+1;j<view.Plot.TextBounds.Count;j++)Check(!view.Plot.TextBounds[i].IntersectsWith(view.Plot.TextBounds[j]),"positioned loads and dimensions have separate labels");
        }
        var network=layout.Transfer.PrimaryPlan.Network;
        var result=new TransferNetworkResult {Input=network,Beams=new[] {layout,PositionedTransferTests.Fixture(true)},BeamCentresMm=new[] {0.0,2925},SlabCoveredLengthMm=7000,
            Bays=new[] {new UpperTransferPlan {StockLengthMm=1425,EdgeMm=350,NearFootMm=500,FarFootMm=500,IsCustomLength=true}}};
        var section=new TransferNetworkPlan {SectionOnly=true,SectionPositionMm=2400};section.SetResult(result);
        foreach(double width in new[] {1200.0,760,520})
        {
            Render(section,width,preview==null ? null : Path.ChangeExtension(preview,"positioned-section-"+width+".png"));
            foreach(string label in new[] {"Beam secondary H20","Double primary H20","Slab H20","Concrete slab"})Check(section.SectionLabels.Contains(label),"physical section identifies "+label);
            Check(section.SectionLabels.Any(l=>l.Contains("D3 support")),"support context labelled");
            Check(section.SectionHorizontalScale==section.SectionVerticalScale,"section retains natural geometry");
            Check(section.AnnotationBounds.All(r=>!section.SectionGeometryBounds.Any(g=>g.IntersectsWith(r))),"labels do not cover physical members or transfer arrows");
            for(int i=0;i<section.AnnotationBounds.Count;i++)for(int j=i+1;j<section.AnnotationBounds.Count;j++)Check(!section.AnnotationBounds[i].IntersectsWith(section.AnnotationBounds[j]),"section labels and dimensions remain separate");
        }
        // A site bearer can offset the lower receiver longitudinally. The slab
        // section must intersect the original upper row, not the shifted receiver.
        for(int beam=0;beam<result.Beams.Length;beam++)
        {
            var t=result.Beams[beam].Transfer;
            t.SharedSlabRowsMm=new[] {2400.0,2905};t.SlabSourceRowIndices=new[] {0,2};
            t.LeftSlabSourceScales=beam==0 ? new double[2] : new[] {.488,.488};
            t.RightSlabSourceScales=beam==0 ? new[] {.488,.488} : new double[2];
        }
        result.Beams[0].Transfer.RowsMm=new[] {2480.0,2600,2905};
        section.SectionPositionMm=2400;section.InvalidateVisual();Render(section,1200);
        Check(section.SectionLabels.Contains("Slab H20") && section.SectionLabels.Any(l=>l.StartsWith("S1 · T1 · x 2400")),"upper section uses the physical source row after lower receiver adjustment");
        section.SectionPositionMm=2480;section.InvalidateVisual();Render(section,1200);
        Check(section.SectionLabels.Contains("Beam secondary H20") && !section.SectionLabels.Contains("Slab H20"),"lower receiver section does not invent a slab H20 at the shifted station");
        Console.WriteLine("Positioned load cards and labelled physical sections passed.");
    }
}
