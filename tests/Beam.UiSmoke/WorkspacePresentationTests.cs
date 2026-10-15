using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EngineeringDashboard;

internal static class WorkspacePresentationTests
{
    static void Check(bool value,string name){if(!value)throw new Exception("Workspace presentation: "+name);}
    static IEnumerable<T> Controls<T>(DependencyObject root) where T:DependencyObject
    {if(root is T match)yield return match;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in Controls<T>(child))yield return item;}
    static void Render(FrameworkElement element,double width=1000)
    {element.Measure(new Size(width,double.PositiveInfinity));element.Arrange(new Rect(0,0,width,element.DesiredSize.Height));element.UpdateLayout();new RenderTargetBitmap((int)width,(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32).Render(element);}
    static TransferMember Member(string name,double q,int count=1)
    {
        var uniform=new[] {new BeamDistributedLoad {StartMm=0,EndMm=2000,KnPerM=q}};var supports=new[] {250.0,1750};
        return new TransferMember {Name=name,LoadCase="Beam",H20Count=count,LengthMm=2000,SupportsMm=supports,Uniform=uniform,Points=new BeamPointLoad[0],Analysis=LoadedBeamAnalysis.AnalyzeSigned(2000,supports,uniform,null,TowerOptimizer.PairEI*count/2,270)};
    }
    static TowerLayout Layout(bool reverse)
    {
        var lower=new[] {Member("T1",reverse ? 1 : 6),Member("T2",2),Member("T3",reverse ? 6 : 1)};
        return new TowerLayout {LengthMm=2000,WidthMm=1524,DeflectionDenominator=270,PositionsMm=new[] {250.0,1750},
            StockPlan=new TowerStockPlan {Pieces=new[] {new TowerStockPiece {StockLengthMm=2000,FirstSupport=0,LastSupport=1}}},
            Transfer=new TransferredSlabResult {RowsMm=new[] {100.0,1000,1900},LowerStockStartMm=-1225,LowerStockLengthMm=2450,
                EnteredUpperSpacingMm=275,LeftUpperScales=new double[3],RightUpperScales=new double[3],CaseNames=new[] {"Beam"},
                LowerCases=new[] {lower},PrimaryCases=new[] {new[] {Member("P1 L",1,2),Member("P1 R",1,2)}}}};
    }
    internal static void Run()
    {
        var points=new[] {new BeamPointLoad {PositionMm=400,Kn=0},new BeamPointLoad {PositionMm=800,Kn=1.483},new BeamPointLoad {PositionMm=1200,Kn=-1.2}};
        var plot=new TowerDiagramWindow.MemberLoadingPlot(2000,new[] {250.0,1750},new[] {0.0,2},new BeamDistributedLoad[0],points);Render(plot);
        Check(plot.DrawnPointCount==2 && plot.DrawnSupportCount==2,"zero applied entries disappear while signed loads and every support remain");
        Check(points.Length==3 && points[0].Kn==0,"filtering does not mutate design loads");
        var result=new TransferNetworkResult {Beams=new[] {Layout(false),Layout(true)},Bays=new UpperTransferPlan[] {null},BeamCentresMm=new[] {0.0,2300},Input=new TransferNetworkInput {
            Beams=new[] {new TransferBeamRow {WidthMm=300,DepthMm=400},new TransferBeamRow {WidthMm=300,DepthMm=400}},Bays=new[] {new TransferBayRow {WidthMm=2000,ThicknessMm=100}}}};
        var section=new TransferNetworkPlan {SectionOnly=true,SectionPositionMm=1000,IsSelectionEnabled=true};section.SetResult(result);Render(section);
        Check(section.SectionRows.Count==2 && section.SectionRows.All(r=>r.Item2==1 && r.Item3==1000),"one cut shows T2 on both beams although their governing rows differ");
        result.Beams[0].Transfer.SettingOutRowsMm=new[] {100.0,1100,1900};result.Beams[0].Transfer.SettingOutDesignRows=new[] {0,1,2};section.InvalidateVisual();Render(section);
        Check(section.SectionRows.Count==1 && section.SectionRows[0].Item1==1,"section follows moved physical H20 positions rather than the retained design rows");
        section.SectionPositionMm=500;section.InvalidateVisual();Render(section);
        Check(section.SectionRows.Count==0 && section.MemberTargets.All(t=>t.Item3==0),"a cut between H20 rows does not invent or substitute a secondary");
        var model=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true};var workspace=new BeamFormworkWorkspace(model,null,null,null);
        Check(workspace.Buttons["NewSession"] is MenuItem && workspace.Buttons["Diagnose"] is MenuItem && !workspace.Buttons.ContainsKey("Close"),"file/tools menus remove redundant commands");
        Check(workspace.Buttons["SecondaryH20s"] is CheckBox,"secondary visibility has an explicit toggle state");
        var groups=Controls<Expander>(workspace.SharedProperties).ToArray();Check(groups.Length==4 && groups.Select(e=>e.Tag).SequenceEqual(new[] {"Materials and design limits","Available lengths","Support layout","Loads"}),"settings are grouped in the agreed order");
        Check(!Controls<TextBlock>(workspace.SharedProperties).Any(t=>t.Text.Contains("spacing limit") || t.Text=="Parallel beam length (mm)"),"geometry and calculated spacing limits are outside shared settings");
        var loads=Controls<ComboBox>(workspace.SharedProperties).Single(c=>c.Name=="LoadAllowanceSelector");loads.SelectedItem="Custom";
        Check(model.UseCustomLoads && workspace.Fields["CustomLiveLoad"].IsEnabled,"custom load mode exposes active fields");
        workspace.Close();Console.WriteLine("Workspace presentation regressions passed.");
    }
}
