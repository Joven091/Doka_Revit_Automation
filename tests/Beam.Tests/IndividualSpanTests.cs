using System;
using System.Linq;
using System.Diagnostics;
using EngineeringDashboard;

internal static class IndividualSpanTests
{
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    private static void Near(double a,double b,string message)=>Check(Math.Abs(a-b)<1e-6*Math.Max(1,Math.Abs(a)),message+": "+a+" / "+b);
    internal static RevitSelectionSnapshot ReportedGeometry()
    {
        // Beam extents, centres and slab thicknesses from the Oct 22 report.
        // The two rectangular 150 mm corner notches reconstruct the reported
        // lost area; the report only retained their convex hull, not the loop.
        double[] y={-17595.34344177246,-14295.343707275391,-10095.34309387207,-6382.51187210083,-2594.680213737488};
        var beams=Enumerable.Range(0,5).Select(i=>new RevitSelectedBeam {SourceId="beam"+i,Label="Revit beam "+i,Start=new ImportPoint(i==0 || i==4 ? -30035.155059814453 : -30185.020330810548,y[i]),End=new ImportPoint(i==0 || i==4 ? -26133.323455810547 : -25981.628063964843,y[i]),WidthMm=i==0 || i==4 ? 800 : 650,DepthMm=i==0 || i==4 ? 1750 : 1150,BottomMm=i==0 || i==4 ? 2250 : 2850}).ToArray();
        var slabs=Enumerable.Range(0,4).Select(i=>
        {
            double x0=-30185.020330810548,x1=-25981.628063964843,lo=y[i]+beams[i].WidthMm/2,hi=y[i+1]-beams[i+1].WidthMm/2;
            var p=RevitStructureImportTests.Rect(x0,x1,lo,hi);
            if(i==0)p=new[] {new ImportPoint(x0,lo+150),new ImportPoint(x0+150,lo+150),new ImportPoint(x0+150,lo),new ImportPoint(x1-150,lo),new ImportPoint(x1-150,lo+150),new ImportPoint(x1,lo+150),new ImportPoint(x1,hi),new ImportPoint(x0,hi)};
            if(i==3)p=new[] {new ImportPoint(x0,lo),new ImportPoint(x1,lo),new ImportPoint(x1,hi-150),new ImportPoint(x1-150,hi-150),new ImportPoint(x1-150,hi),new ImportPoint(x0+150,hi),new ImportPoint(x0+150,hi-150),new ImportPoint(x0,hi-150)};
            return new RevitSelectedSlab {SourceId="floor"+i,Label="Revit slab "+i,Outline=p,TopMm=4000,ThicknessMm=200};
        }).ToArray();
        return new RevitSelectionSnapshot {DocumentTitle="Oct 22 joined-beam and cutback regression",Beams=beams,Slabs=slabs};
    }
    internal static void Run()
    {
        var imported=RevitStructureImport.Build(ReportedGeometry(),"beam1");Check(imported.CanImport,string.Join(";",imported.Problems));
        Near(4203.392266845705,imported.LengthMm,"overall drawing extent");Near(3901.8316040039062,imported.Configuration.Beams[0].SpanLength(0),"short outer beam is not extended");Near(149.8652709960952,imported.Configuration.Beams[4].SpanStartMm,"actual start preserved");
        Check(imported.Configuration.Bays[0].Outline.Length==8 && imported.Configuration.Bays[3].Outline.Length==8,"concave corner loops retained");
        var model=new BeamCalculatorViewModel {UseTowers=true,UseBeamNetwork=true,TransferSlabLoads=true,MaximumConcreteEndOverhangMm=500,TowerLegCapacityKn=70};imported.Apply(model);
        var saved=model.NetworkConfigurationXml;var restored=new BeamCalculatorViewModel {UseTowers=true,UseBeamNetwork=true,TransferSlabLoads=true,BeamLengthMm=model.BeamLengthMm};restored.NetworkConfigurationXml=saved;
        Near(model.BeamSpanEnd(4),restored.BeamSpanEnd(4),"individual endpoints survive save/load");Check(restored.NetworkBays[0].Outline.Length==8,"cutback survives save/load");
        double old=model.BeamSpanLength(1);model.SetBeamSpan(0,200,3700);Near(old,model.BeamSpanLength(1),"editing B1 never resizes B2");Check(model.NetworkBays[0].Outline==null && model.NetworkBays[3].Outline!=null,"only adjacent edited bay becomes a manual rectangle");
        model.SetAllBeamLengths(4000);Check(model.NetworkBeams.All(b=>b.SpanStartMm==0 && !b.SpanLengthMm.HasValue),"explicit bulk edit available");
        var common=new TowerInput {EngineerSearch=true,FullLoadingOnly=true,LengthMm=3900,MaximumConcreteEndOverhangMm=300,LegCapacityKn=70,DeflectionDenominator=270};
        var network=new TransferNetworkInput {PlywoodProfileId=PlywoodCatalog.Default.Id,PlywoodDeflectionDenominator=350,PlywoodSheetLengthMm=2440,UpperSpacingMm=350,
            BeamSpacingsMm=new[] {350.0,350},SlabSpacingsMm=new[] {488.0},CrossingSpacingsMm=new double[0],
            Beams=new[] {new TransferBeamRow {WidthMm=650,DepthMm=1150,SpanStartMm=150,SpanLengthMm=3600},new TransferBeamRow {WidthMm=650,DepthMm=1150}},Bays=new[] {new TransferBayRow {WidthMm=2125,ThicknessMm=200}}};
        network.Validate(common);var input=common.CopyForSearch();input.LengthMm=3600;input.LongitudinalStartMm=150;
        var plan=EngineerPrimaryPlan.Create(input,network,0);Near(plan.Lines[0].Min(q=>q.StartMm),150,"primary loads begin at actual beam start");Near(plan.Lines[1].Max(q=>q.EndMm),3750,"primary loads end at actual beam end");
        Near(plan.Forces.Sum(),(network.AreaLoad(1150)*.65+network.AreaLoad(200)*2.125/2)*3.6,"line loads use real length");
        var cross=new TransferCrossingBeamRow {BayNumber=1,PositionMm=200,WidthMm=650,DepthMm=1150};network.CrossingBeams=new[] {cross};network.CrossingSpacingsMm=new[] {350.0};bool blocked=false;try{network.Validate(common);}catch(ArgumentException){blocked=true;}Check(blocked,"crossing outside shortened adjacent beam is rejected");network.CrossingBeams=new TransferCrossingBeamRow[0];network.CrossingSpacingsMm=new double[0];
        var clock=Stopwatch.StartNew();var solved=TransferNetworkOptimizer.Optimize(common,network);clock.Stop();Check(solved!=null,"unequal-span system finds a fully checked solution");
        Near(solved.Beams[0].LengthMm,3600,"solver uses real shorter length");Near(solved.Beams[0].LongitudinalStartMm,150,"solver retains real shorter start");Near(solved.Beams[1].LengthMm,3900,"other beam retains its full length");
        double total=0;
        for(int i=0;i<2;i++)
        {
            var b=solved.Beams[i];Check(b.StockPlan.Pieces.All(p=>p.StartMm>=b.LongitudinalStartMm-1e-6 && p.EndMm<=b.LongitudinalEndMm+1e-6),"stocks remain within their own concrete beam");Check(b.MaxLegKn<=common.LegCapacityKn+1e-6 && b.Utilization<=1+1e-6,"actual members and legs pass");
            total+=b.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn)+b.Transfer.ColumnSupportLoadKn(b.Transfer.CaseNames.Length-1);
        }
        Near(total,network.AreaLoad(1150)*.65*7.5+network.AreaLoad(200)*2.125*3.6,"system load is conserved using actual spans");
        imported.Apply(model);var reportedInput=model.CreateTowerInput(false);var reportClock=Stopwatch.StartNew();
        var reported=TransferNetworkOptimizer.Optimize(reportedInput,reportedInput.Network);reportClock.Stop();
        Check(reported.Beams.Length==5 && !reported.RequiresSeparateSlabFormwork,"reported five-beam area has complete transferred slab formwork");
        for(int i=0;i<5;i++)Near(reported.Beams[i].LengthMm,model.BeamSpanLength(i),"reported beam actual length "+i);
        double expected=Enumerable.Range(0,5).Sum(i=>reported.Input.AreaLoad(reported.Input.Beams[i].DepthMm)*reported.Input.Beams[i].WidthMm*reported.Beams[i].LengthMm/1e6)
            +Enumerable.Range(0,4).Sum(i=>reported.Input.AreaLoad(200)*reported.Input.Bays[i].WidthMm*(reported.Input.BayEnd(i,model.BeamLengthMm)-reported.Input.BayStart(i,model.BeamLengthMm))/1e6);
        double actual=reported.Beams.Sum(b=>b.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn)+b.Transfer.ColumnSupportLoadKn(b.Transfer.CaseNames.Length-1));
        Near(expected,actual,"reported nominal rectangle loads counted once, with real beam lengths");
        for(int bay=0;bay<4;bay++)for(int row=0;row<reported.SlabRowsMm.Length;row++)if(reported.SlabRowActive(bay,row))
            Check(reported.Input.BayContains(bay,reported.SlabRowsMm[row],reported.Bays[bay].NearFootMm) && reported.Input.BayContains(bay,reported.SlabRowsMm[row],reported.Input.Bays[bay].WidthMm-reported.Bays[bay].FarFootMm),"feet avoid actual corner cuts");
        Console.WriteLine("Reported-size five-beam solve: "+reportClock.Elapsed.TotalSeconds.ToString("0.000")+" s; "+reported.LegCount+" legs.");
        Console.WriteLine("Passed "+checks+" individual-span checks; complete unequal-span solve "+clock.Elapsed.TotalSeconds.ToString("0.000")+" s.");
    }
}
