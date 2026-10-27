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
        var tower=Layout(false);tower.LegCapacityKn=70;
        var crossing=new CrossBeamSupportResult {Input=new TransferCrossingBeamRow {BayNumber=1},Towers=tower,SharedFailure="Full diagnostic should not appear",LoadKn=99};
        string summary=BeamFormworkWorkspace.CrossingLoadSummary(crossing,1,1);
        Check(summary.Contains("Selected cross beam · X2") && summary.Contains("Efficiency:") && summary.Contains("4 tower legs") && summary.Split('\n').Length<=5,"crossing selection uses the same compact load/capacity/efficiency structure");
        Check(summary.Contains(tower.TotalSupportedLoadKn.ToString("0.00")) && !summary.Contains("Full diagnostic") && !summary.Contains("Network totals"),"tower card uses its own supported load and omits schedule details");
        crossing.Towers=null;summary=BeamFormworkWorkspace.CrossingLoadSummary(crossing,1,1);
        Check(summary.Contains("Supported by B1 and B2") && !summary.Contains("Efficiency:"),"shared crossings identify their receiving beams without inventing dedicated capacity");
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
        Check(section.DrawnSectionSupportCount==2 && section.SectionLabels.Any(s=>s.Contains("supports projected")),"support context stays visible between tower stations");
        Check(section.SectionHorizontalScale==section.SectionVerticalScale,"section geometry has one scale in both directions");
        var model=new BeamCalculatorViewModel {UseTowers=true,HasSelectedSupportSystem=true,TransferSlabLoads=true,UseBeamNetwork=true};var workspace=new BeamFormworkWorkspace(model,null,null,null);
        Check(workspace.Buttons["NewSession"] is MenuItem && workspace.Buttons["Diagnose"] is MenuItem && !workspace.Buttons.ContainsKey("Close"),"file/tools menus remove redundant commands");
        Check(workspace.Buttons["SecondaryH20s"] is CheckBox,"secondary visibility has an explicit toggle state");
        var groups=Controls<Expander>(workspace.SharedProperties).ToArray();Check(groups.Length==4 && groups.Select(e=>e.Tag).SequenceEqual(new[] {"Materials and design limits","Available lengths","Support layout","Loads"}),"settings are grouped in the agreed order");
        Check(!Controls<TextBlock>(workspace.SharedProperties).Any(t=>t.Text.Contains("spacing limit") || t.Text=="Parallel beam length (mm)"),"geometry and calculated spacing limits are outside shared settings");
        var loads=Controls<ComboBox>(workspace.SharedProperties).Single(c=>c.Name=="LoadAllowanceSelector");loads.SelectedItem="Custom";
        Check(model.UseCustomLoads && workspace.Fields["CustomLiveLoad"].IsEnabled,"custom load mode exposes active fields");
        workspace.Drawing.Select(FormworkObject.Beam,0);
        double otherLength=model.BeamSpanLength(1);
        Check(workspace.TryEditDimension("BeamLengthMm","7000",out var error) && model.BeamSpanLength(0)==7000 && model.BeamSpanLength(1)==otherLength,"canvas length edits only the selected parallel beam");
        Check(workspace.TryEditDimension("AllBeamLengthMm","7200",out error) && model.BeamSpanLength(0)==7200 && model.BeamSpanLength(1)==7200,"explicit set-all control updates both parallel beams");
        workspace.Drawing.Select(FormworkObject.Slab,0);
        Check(workspace.TryEditDimension("WidthMm","2250",out error) && model.NetworkBays[0].WidthMm==2250,"canvas slab dimension changes the bay width");
        model.AddCrossingBeam();workspace.Drawing.Select(FormworkObject.Crossing,0);
        Check(workspace.TryEditDimension("PositionMm","3350",out error) && model.CrossingBeams[0].PositionMm==3350,"canvas position moves the crossing");
        Check(!workspace.TryEditDimension("PositionMm","-1",out error) && model.CrossingBeams[0].PositionMm==3350,"invalid canvas position cannot mutate geometry");
        Check(!Controls<TextBlock>(workspace).Any(t=>t.Text==CrossBeamSupportResult.Limitations),"crossing inspector omits the design assumptions paragraph");
        var centres=workspace.Drawing.Centres();double y=(centres[0]+centres[1])/2;
        workspace.BeginDrafting("MV");workspace.Drawing.SelectAt(workspace.Drawing.Screen(3350,y));workspace.Drawing.UpdateDraftPointer(workspace.Drawing.Screen(4350,y));
        Check(workspace.AppendDraftDistance("137") && workspace.CommitDraftDistance() && model.CrossingBeams[0].PositionMm==3487,"typed move distance is exact and is not rounded to the snap grid");
        workspace.Buttons["Undo"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(model.CrossingBeams[0].PositionMm==3350,"one undo restores the typed move");
        workspace.Drawing.Select(FormworkObject.Crossing,0);workspace.BeginDrafting("CO");workspace.Drawing.SelectAt(workspace.Drawing.Screen(3350,y));workspace.Drawing.UpdateDraftPointer(workspace.Drawing.Screen(4350,y));
        Check(workspace.AppendDraftDistance("637") && workspace.CommitDraftDistance() && model.CrossingBeams.Count==2 && model.CrossingBeams[1].PositionMm==3987,"typed copy retains the source and places the new beam at the exact offset");
        model.AddNetworkBay();workspace.Drawing.Select(FormworkObject.Crossing,0);centres=workspace.Drawing.Centres();double nextY=(centres[1]+centres[2])/2;
        workspace.BeginDrafting("CO");workspace.Drawing.SelectAt(workspace.Drawing.Screen(3350,y));workspace.Drawing.DraftPointerWorld=new Point(3350,nextY);
        Check(workspace.AppendDraftDistance((nextY-y).ToString()) && workspace.CommitDraftDistance() && model.CrossingBeams.Count==3 && model.CrossingBeams[2].BayNumber==2,"typed crossing copy can follow a direction into another slab bay");
        workspace.Close();Console.WriteLine("Workspace presentation regressions passed.");
    }
}
