using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EngineeringDashboard;

internal static class CaseDiagramUiTests
{
    static int checks;
    static void Check(bool value,string name) {checks++;if(!value)throw new Exception("Case diagrams: "+name);}
    static void Near(double actual,double expected,string name)=>Check(Math.Abs(actual-expected)<1e-8,name);
    static TransferMember Member(double q,string name,int count=2)
    {
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=q}};
        var supports=new[] {250.0,1750};var points=new BeamPointLoad[0];
        return new TransferMember {Name=name,LoadCase="Beam",H20Count=count,LengthMm=2000,SupportsMm=supports,Uniform=uniform,Points=points,Analysis=LoadedBeamAnalysis.AnalyzeSigned(2000,supports,uniform,points,TowerOptimizer.PairEI*count/2,270)};
    }
    static System.Collections.Generic.IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var leaf in Drawings(child))yield return leaf;
        else yield return drawing;
    }
    static void Render(FrameworkElement element)
    {element.Measure(new Size(900,element.Height));element.Arrange(new Rect(0,0,900,element.Height));element.UpdateLayout();new System.Windows.Media.Imaging.RenderTargetBitmap(900,(int)element.Height,96,96,PixelFormats.Pbgra32).Render(element);}
    public static int Run()
    {
        var primary=new[] {new[] {Member(8,"P1 L"),Member(2,"P1 R")},new[] {Member(2,"P1 L"),Member(9,"P1 R")},new[] {Member(3,"P1 L"),Member(3,"P1 R")}};
        var lower=new[] {new[] {Member(1,"T1",1)},new[] {Member(6,"T1",1)},new[] {Member(2,"T1",1)}};
        var transfer=new TransferredSlabResult {RowsMm=new[] {1000.0},CaseNames=new[] {"Beam","Beam + left slab","Beam + both slabs"},PrimaryCases=primary,LowerCases=lower,EnteredUpperSpacingMm=250};
        var layout=new TowerLayout {DeflectionDenominator=270,StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StockLengthMm=2000}}},Transfer=transfer};
        var vm=new BeamCalculatorViewModel {UseTowers=true};typeof(BeamCalculatorViewModel).GetProperty("TowerResult").SetValue(vm,layout);
        var window=new TowerDiagramWindow(vm);window.SelectMember(0,0,-1);
        Check(window.SelectedLoadCase==0 && window.SelectedTransferMember==primary[0][0],"default uses the governing member case, even when it is not last");
        Check(window.LoadCaseSelector.Items.Count==3 && !window.CheckedCasesDetails.IsExpanded && window.CheckedCasesTable.Items.Count==3,"one selector retains all cases in collapsed details");
        var row=(TowerDiagramWindow.CheckedCaseRow)window.CheckedCasesTable.Items[0];
        Near(row.Shear,primary[0][0].Analysis.ShearKn/22,"details shear utilization");Near(row.Moment,primary[0][0].Analysis.MomentKnM/10,"details moment utilization");Near(row.Deflection,primary[0][0].Analysis.DeflectionUtilization,"details deflection utilization");
        window.CheckedCasesDetails.IsExpanded=true;window.LoadCaseSelector.SelectedIndex=2;Check(window.SelectedTransferMember==primary[2][0] && window.CheckedCasesDetails.IsExpanded,"manual selection changes the actual analysis while preserving expanded details");
        window.SelectMember(0,0,-1);Check(window.SelectedLoadCase==2,"refreshing the same member retains the manual choice");
        window.SelectMember(0,1,-1);Check(window.SelectedLoadCase==1 && window.SelectedTransferMember==primary[1][1],"changing primary line resets to that line's governing case");
        window.SelectMember(1,0,-1);Check(window.SelectedLoadCase==1 && window.SelectedTransferMember==lower[1][0],"lower secondary has its own governing case");
        primary[2][0].SubsetShearBoundKn=21;window.SelectMember(0,0,-1);Check(window.SelectedLoadCase==2,"conservative subset bounds participate in governing selection");
        Near(((TowerDiagramWindow.CheckedCaseRow)window.CheckedCasesTable.Items[2]).Shear,21.0/22,"case details retain subset bounds");window.Close();

        var raw=new[] {new BeamDistributedLoad {StartMm=0,EndMm=3050,KnPerM=20.97},new BeamDistributedLoad {StartMm=3050,EndMm=3700,KnPerM=10.28625},new BeamDistributedLoad {StartMm=3700,EndMm=6750,KnPerM=20.97}}
            .Concat(new[] {6.212291763,6.690160360,6.212291763,.493513048,.531475590,.493513048}.Select(q=>new BeamDistributedLoad {StartMm=3050,EndMm=3700,KnPerM=q})).ToArray();
        var plot=new TowerDiagramWindow.MemberLoadingPlot(6750,new[] {375.0,1875,3375,4875,6375},new[] {21.47,34.38,36.32,34.38,21.47},raw,new BeamPointLoad[0]);
        Check(plot.CombinedUniform.Length==3 && plot.SourceUniform==raw && raw.Length==9,"nine raw contributions draw as three intervals without mutating the source");
        Near(plot.CombinedUniform[0].KnPerM,plot.CombinedUniform[2].KnPerM,"equal outer intensities");Near(plot.CombinedUniform[1].KnPerM,raw.Skip(3).Sum(q=>q.KnPerM)+10.28625,"centre combines its contributions");
        var a=LoadedBeamAnalysis.AnalyzeSigned(6750,new[] {375.0,1875,3375,4875,6375},raw,new BeamPointLoad[0],TowerOptimizer.PairEI,270);
        var b=LoadedBeamAnalysis.AnalyzeSigned(6750,new[] {375.0,1875,3375,4875,6375},plot.CombinedUniform,new BeamPointLoad[0],TowerOptimizer.PairEI,270);
        Near(a.TotalLoadKn,b.TotalLoadKn,"combined loads conserve force");Near(a.MomentKnM,b.MomentKnM,"combined loads preserve moment response");Near(a.ShearKn,b.ShearKn,"combined loads preserve shear response");Near(a.DeflectionMm,b.DeflectionMm,"combined loads preserve deflection response");
        for(int i=0;i<a.Reactions.Length;i++)Near(a.Reactions[i],b.Reactions[i],"combined loads preserve every reaction");
        Render(plot);var primitives=Drawings(VisualTreeHelper.GetDrawing(plot)).ToArray();
        var bands=primitives.OfType<GeometryDrawing>().Where(d=>d.Pen?.Brush is SolidColorBrush brush && brush.Color==Colors.Firebrick && d.Geometry is LineGeometry l && Math.Abs(l.StartPoint.Y-l.EndPoint.Y)<1e-8).Select(d=>(LineGeometry)d.Geometry).ToArray();
        Check(bands.Length==3 && Math.Abs(bands[0].StartPoint.Y-bands[2].StartPoint.Y)<1e-8,"equal outer loads render at equal heights");
        Near((90-bands[0].StartPoint.Y)/(90-bands[1].StartPoint.Y),plot.CombinedUniform[0].KnPerM/plot.CombinedUniform[1].KnPerM,"arrow heights share one intensity scale");
        Check(primitives.OfType<GlyphRunDrawing>().Count(d=>new string(d.GlyphRun.Characters.ToArray()).StartsWith("R"))==5,"all reaction labels remain visible, including the centre");
        var signed=new TowerDiagramWindow.MemberLoadingPlot(2000,new[] {250.0,1750},new[] {-1.0,1},new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=2},new BeamDistributedLoad {StartMm=1000,EndMm=2000,KnPerM=-5}},new BeamPointLoad[0]);Render(signed);
        Check(signed.CombinedUniform.Select(q=>q.KnPerM).SequenceEqual(new[] {2.0,-3}),"combined loads preserve signed intensities");
        var shafts=Drawings(VisualTreeHelper.GetDrawing(signed)).OfType<GeometryDrawing>().Where(d=>d.Pen?.Brush is SolidColorBrush brush && brush.Color==Colors.Firebrick && d.Geometry is LineGeometry l && l.StartPoint.X==l.EndPoint.X).Select(d=>(LineGeometry)d.Geometry).ToArray();
        Check(shafts.Any(l=>l.StartPoint.Y<l.EndPoint.Y) && shafts.Any(l=>l.StartPoint.Y>l.EndPoint.Y),"positive loads point down and negative loads point up");
        return checks;
    }
}
