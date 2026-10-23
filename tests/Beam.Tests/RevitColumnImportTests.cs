using System;
using System.Linq;
using System.Diagnostics;
using EngineeringDashboard;

internal static class RevitColumnImportTests
{
    private static int checks;
    private static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    private static void Near(double expected,double actual,string name)=>Check(Math.Abs(expected-actual)<1e-6*Math.Max(1,Math.Abs(expected)),name+": "+actual);
    internal static RevitSelectionSnapshot ReportedSelection()
    {
        // Exact printed geometry from the 2026-10-22 14:20:50 diagnostic.
        // Its column outlines are the rounded mesh hulls printed by 1.1.8.
        // Face-edge extraction precision is covered separately below.
        var source=new RevitSelectionSnapshot {DocumentTitle="Calculator test",RevitVersion="Autodesk Revit 2024",RevitBuild="24.2.10.64",ActiveView="{3D}",SelectedCount=13,
            Beams=new[] {
                new RevitSelectedBeam {SourceId="523526",Label="Structural Framing [523526]",Start=new ImportPoint(-30185.020330810548,-14295.343707275391),End=new ImportPoint(-25981.628063964843,-14295.343707275391),WidthMm=649.99985961914069,DepthMm=1150,BottomMm=2850.0000305175781},
                new RevitSelectedBeam {SourceId="523528",Label="Structural Framing [523528]",Start=new ImportPoint(-30185.020330810548,-10095.34309387207),End=new ImportPoint(-25981.628063964843,-10095.34309387207),WidthMm=649.99985961914069,DepthMm=1150,BottomMm=2850.0000305175781},
                new RevitSelectedBeam {SourceId="523530",Label="Structural Framing [523530]",Start=new ImportPoint(-30185.020330810548,-6382.5118721008303),End=new ImportPoint(-25981.628063964843,-6382.5118721008303),WidthMm=650.00044097900388,DepthMm=1150,BottomMm=2850.0000305175781},
                new RevitSelectedBeam {SourceId="523922",Label="Structural Framing [523922]",Start=new ImportPoint(-30035.155059814453,-17595.34344177246),End=new ImportPoint(-26133.323455810547,-17595.34344177246),WidthMm=800.0000061035156,DepthMm=1750.0000000000002,BottomMm=2250.0000305175781},
                new RevitSelectedBeam {SourceId="523924",Label="Structural Framing [523924]",Start=new ImportPoint(-30035.155059814453,-2594.680213737488),End=new ImportPoint(-26133.323455810547,-2594.680213737488),WidthMm=800.00015144348151,DepthMm=1750.0000000000002,BottomMm=2250.0000305175781}},
            Slabs=new[] {
                new RevitSelectedSlab {SourceId="583582",Label="Floors [583582]",ThicknessMm=200.00000152587882,TopMm=4000.0000305175781,Outline=new[] {new ImportPoint(-30185.02045943186,-10420.343565267134),new ImportPoint(-30185.02045943186,-13970.343565267134),new ImportPoint(-25981.628579124266,-13970.343565267134),new ImportPoint(-25981.628579124241,-10420.343565267134)}},
                new RevitSelectedSlab {SourceId="583610",Label="Floors [583610]",ThicknessMm=200.00000152587882,TopMm=4000.0000305175781,Outline=new[] {new ImportPoint(-26135.153899584919,-3145.3435652675039),new ImportPoint(-26135.153899584919,-2994.6801916553454),new ImportPoint(-30035.153899585384,-2994.6801916553454),new ImportPoint(-30035.153899585384,-3145.3435652675039),new ImportPoint(-30185.020459431766,-3145.3435652675039),new ImportPoint(-30185.020459431766,-6057.511878461135),new ImportPoint(-25981.628579124164,-6057.511878461135),new ImportPoint(-25981.628579124146,-3145.3435652675039)}},
                new RevitSelectedSlab {SourceId="583621",Label="Floors [583621]",ThicknessMm=200.00000152587882,TopMm=4000.0000305175781,Outline=new[] {new ImportPoint(-30185.020459431813,-6707.511878461135),new ImportPoint(-30185.020459431813,-9770.3435652671415),new ImportPoint(-25981.628579124215,-9770.3435652671415),new ImportPoint(-25981.628579124197,-6707.511878461135)}},
                new RevitSelectedSlab {SourceId="583678",Label="Floors [583678]",ThicknessMm=200.00000152587882,TopMm=4000.0000305175781,Outline=new[] {new ImportPoint(-30185.020459431911,-14620.343565267134),new ImportPoint(-30185.020459431911,-17045.343565267623),new ImportPoint(-30035.153899585384,-17045.343565267623),new ImportPoint(-30035.153899585384,-17195.343565267856),new ImportPoint(-26135.153899585381,-17195.343565267856),new ImportPoint(-26135.153899585381,-17045.343565267623),new ImportPoint(-25981.628579124303,-17045.343565267623),new ImportPoint(-25981.628579124288,-14620.343565267134)}}},
            Boundaries=new[] {
                new RevitSelectedBoundary {SourceId="523345",Label="Structural Columns [523345]",Kind="Column",BottomMm=-2000.0000152587891,TopMm=3800.0000289916993,Outline=new[] {new ImportPoint(-31135.153033447266,-18145.343591308596),new ImportPoint(-30035.155059814453,-18145.343591308596),new ImportPoint(-30035.155059814453,-17045.343292236328),new ImportPoint(-31135.153033447266,-17045.343292236328)}},
                new RevitSelectedBoundary {SourceId="523347",Label="Structural Columns [523347]",Kind="Column",BottomMm=-2000.0000152587891,TopMm=3800.0000289916993,Outline=new[] {new ImportPoint(-26135.153576660156,-18145.343591308596),new ImportPoint(-25035.153277587891,-18145.343591308596),new ImportPoint(-25035.153277587891,-17045.343292236328),new ImportPoint(-26135.153576660156,-17045.343292236328)}},
                new RevitSelectedBoundary {SourceId="523348",Label="Structural Columns [523348]",Kind="Column",BottomMm=-2000.0000152587891,TopMm=3800.0000289916993,Outline=new[] {new ImportPoint(-31135.153033447266,-3145.3434768676757),new ImportPoint(-30035.155059814453,-3145.3434768676757),new ImportPoint(-30035.155059814453,-2045.3436138153077),new ImportPoint(-31135.153033447266,-2045.3436138153077)}},
                new RevitSelectedBoundary {SourceId="523349",Label="Structural Columns [523349]",Kind="Column",BottomMm=-2000.0000152587891,TopMm=3800.0000289916993,Outline=new[] {new ImportPoint(-26135.153576660156,-3145.3434768676757),new ImportPoint(-25035.153277587891,-3145.3434768676757),new ImportPoint(-25035.153277587891,-2045.3436138153077),new ImportPoint(-26135.153576660156,-2045.3436138153077)}}}};
        source.SelectedIds=source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(f=>f.SourceId)).Concat(source.Boundaries.Select(b=>b.SourceId)).OrderBy(id=>id).ToArray();
        return source;
    }
    internal static void Run()
    {
        var source=ReportedSelection();double nominal=1210000;
        foreach(var column in source.Boundaries)
        {
            // Reproduce the old false rejection, then validate the same measured
            // perimeter with the actual 1 mm linear tolerance.
            double delta=Math.Abs(ImportPolygon.Area(column.Outline)-nominal);
            if(column.SourceId=="523345" || column.SourceId=="523348")Check(delta>Math.Max(1,ImportPolygon.Area(column.Outline)*1e-6),"reported mesh fails the former area check");
            Check(ImportPolygon.BoundaryMatches(column.Outline,column.Outline,nominal,1),"reported rectangular column passes edge tolerance: "+column.SourceId);
        }
        var accurate=RevitStructureImportTests.Rect(-31135.153899585384,-30035.153899585384,-18145.343565267623,-17045.343565267623);
        Check(ImportPolygon.BoundaryMatches(accurate,source.Boundaries[0].Outline,nominal,1),"accurate face edges accept the rounded triangulated mesh");
        Check(!ImportPolygon.BoundaryMatches(accurate,source.Boundaries[0].Outline.Concat(new[] {new ImportPoint(-31140,-17500)}).ToArray(),nominal,1),"a genuinely wider base is not discarded");
        Check(!ImportPolygon.BoundaryMatches(accurate,accurate,nominal-50000,1),"a genuinely missing footprint area remains blocked");
        var nonconvex=new[] {new ImportPoint(0,0),new ImportPoint(100,0),new ImportPoint(100,100),new ImportPoint(50,50),new ImportPoint(0,100)};
        Check(!ImportPolygon.Convex(nonconvex,1),"a genuine concave boundary remains unsupported");
        var result=RevitStructureImport.Build(source,"523526");
        Check(result.CanImport,"reported five beams, four slabs and four columns import: "+string.Join(";",result.Problems));
        Check(result.Configuration.Beams.Length==5 && result.Configuration.Bays.Length==4 && result.Configuration.RevitContext.References.Length==4,"all thirteen selected objects retained");
        Near(4203.392266845705,result.LengthMm,"inner beams keep their complete span");
        Near(3900.0014831542969,result.Configuration.Beams[0].SpanLength(result.LengthMm),"B1 ends at its actual column face");
        Near(3900.0014831542969,result.Configuration.Beams[4].SpanLength(result.LengthMm),"B5 ends at its actual column face");
        Check(result.Configuration.Bays[0].Outline.Length>=8 && result.Configuration.Bays[3].Outline.Length>=8,"actual slab corner cutbacks preserved");
        var topSlab=source.Slabs.Single(s=>s.SourceId=="583678");
        foreach(var column in source.Boundaries.Take(2))
        {
            var bounds=column.Outline;
            double x0=bounds.Min(p=>p.X),x1=bounds.Max(p=>p.X),y0=bounds.Min(p=>p.Y),y1=bounds.Max(p=>p.Y);
            Check(ImportPolygon.Area(SlabFootprint.Clip(topSlab.Outline,x0,x1,y0,y1))<.05,"printed-coordinate overlap is only a rounding sliver");
        }
        foreach(double shift in new[] {.5,1.1,2.0,20.0})
        {
            var altered=ReportedSelection();foreach(var p in altered.Boundaries[1].Outline)p.Y+=shift;
            var r=RevitStructureImport.Build(altered,"523526");
            Check(shift<=1 ? r.CanImport : !r.CanImport && r.Problems.Any(p=>p.StartsWith("S1:") && p.Contains("intersects")),"actual intrusion uses 1 mm tolerance: "+shift);
        }
        var rectangularSlab=RevitStructureImportTests.Rect(0,3000,0,3000);
        Check(!ImportPolygon.InteriorOverlap(rectangularSlab,RevitStructureImportTests.Rect(1000,2000,-1000,.5),0,3000,0,3000,1),"submillimetre contact at an uncut slab edge is accepted");
        Check(ImportPolygon.InteriorOverlap(rectangularSlab,RevitStructureImportTests.Rect(1000,2000,-1000,1.1),0,3000,0,3000,1),"edge tolerance is applied once, not twice");
        var interior=ReportedSelection();interior.Boundaries=interior.Boundaries.Concat(new[] {new RevitSelectedBoundary {SourceId="interior",Label="Actual interior column",Kind="Column",BottomMm=-2000,TopMm=3800,
            Outline=RevitStructureImportTests.Rect(-28000,-27500,-16500,-16000)}}).ToArray();
        var rejected=RevitStructureImport.Build(interior,"523526");Check(!rejected.CanImport && rejected.Problems.Any(p=>p.Contains("Actual interior column") && p.Contains("intersects")),"real interior obstruction still blocks import");
        foreach(bool reverse in new[] {false,true})foreach(double angle in new[] {0.0,.6,Math.PI/2})
        {
            var rotated=ReportedSelection();Func<ImportPoint,ImportPoint> transform=p=>new ImportPoint(125000+p.X*Math.Cos(angle)-p.Y*Math.Sin(angle),-650000+p.X*Math.Sin(angle)+p.Y*Math.Cos(angle));
            foreach(var b in rotated.Beams){b.Start=transform(b.Start);b.End=transform(b.End);}
            foreach(var s in rotated.Slabs){s.Outline=s.Outline.Select(transform).ToArray();if(reverse)s.Outline=s.Outline.Reverse().ToArray();}
            foreach(var c in rotated.Boundaries){c.Outline=c.Outline.Select(transform).ToArray();if(reverse)c.Outline=c.Outline.Reverse().ToArray();}
            var r=RevitStructureImport.Build(rotated,"523526");Check(r.CanImport,"rotated/reversed actual cutback selection: "+angle+" / "+reverse+": "+string.Join(";",r.Problems));
        }
        var model=new BeamCalculatorViewModel {UseTowers=true,AllowCustomH20Lengths=false,TowerLegCapacityKn=70,MaximumConcreteEndOverhangMm=500};result.Apply(model);
        var saved=model.NetworkConfigurationXml;var copy=new BeamCalculatorViewModel {UseTowers=true,UseBeamNetwork=true,TransferSlabLoads=true,BeamLengthMm=model.BeamLengthMm};copy.NetworkConfigurationXml=saved;
        Check(copy.RevitContext.References.Length==4 && copy.NetworkBays[0].Outline.Length>=8,"save/load keeps columns and actual corners");
        Near(model.BeamSpanLength(0),copy.BeamSpanLength(0),"save/load keeps the column-limited span");
        string diagnostic=RevitImportDiagnostics.Build(source,null,"523526",result,true,"",null,"");
        Check(diagnostic.Contains("Ready to import") && diagnostic.Contains("523345") && diagnostic.Contains("523349"),"all column IDs remain diagnosable");
        var input=model.CreateTowerInput(false);var clock=Stopwatch.StartNew();var solved=TransferNetworkOptimizer.Optimize(input,input.Network);clock.Stop();
        Check(solved!=null && solved.Beams.Length==5 && !solved.RequiresSeparateSlabFormwork,"complete design from the reported column selection");
        for(int i=0;i<5;i++)
        {
            var b=solved.Beams[i];Check(b.StockPlan.Pieces.All(p=>p.StartMm>=b.LongitudinalStartMm-1e-6 && p.EndMm<=b.LongitudinalEndMm+1e-6),"primary stocks remain inside corrected column limits");
            Check(b.MaxLegKn<=input.LegCapacityKn+1e-6 && b.Utilization<=1+1e-6,"members and legs retain their capacity checks");
        }
        double expected=Enumerable.Range(0,5).Sum(i=>solved.Input.AreaLoad(solved.Input.Beams[i].DepthMm)*solved.Input.Beams[i].WidthMm*solved.Beams[i].LengthMm/1e6)
            +Enumerable.Range(0,4).Sum(i=>solved.Input.AreaLoad(solved.Input.Bays[i].ThicknessMm)*solved.Input.Bays[i].WidthMm*(solved.Input.BayEnd(i,model.BeamLengthMm)-solved.Input.BayStart(i,model.BeamLengthMm))/1e6);
        double actual=solved.Beams.Sum(b=>b.Transfer.PrimaryCases.Last().Sum(m=>m.Analysis.TotalLoadKn)+b.Transfer.ColumnSupportLoadKn(b.Transfer.CaseNames.Length-1));
        Near(expected,actual,"nominal rectangular loads remain conserved after accurate import");
        Console.WriteLine("Reported 13-element import and complete design: "+clock.Elapsed.TotalSeconds.ToString("0.000")+" s; "+solved.LegCount+" legs.");
        Console.WriteLine("Passed "+checks+" column/cutback import checks.");
    }
}
