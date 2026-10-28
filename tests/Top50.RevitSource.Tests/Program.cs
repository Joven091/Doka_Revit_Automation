using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    private static void Reject(Action action,string text){bool rejected=false;try{action();}catch(Exception error){rejected=true;Check(error.Message.Contains(text),"Expected "+text+", got "+error);}Check(rejected,"Missing rejection: "+text);}
    private static Line Segment(double a,double b)=>new Line{Start=new XYZ(a/304.8,0,0),End=new XYZ(b/304.8,0,0)};
    private static Wall Wall(long id,double exterior,double interior)
    {
        var wall=new Wall{Id=new ElementId(id),UniqueId="unique-"+id,Name="Wall "+id,Location=new LocationCurve{Curve=Segment(0,(exterior+interior)/2)}};
        foreach(var side in new[]{ShellLayerType.Exterior,ShellLayerType.Interior}){var face=new PlanarFace();face.EdgeLoops.Add(new EdgeArray{new Edge{Curve=Segment(0,side==ShellLayerType.Exterior?exterior:interior)}});wall.Sides[side]=new System.Collections.Generic.List<Reference>{new Reference{Geometry=face}};}return wall;
    }
    private static void Main()
    {
        var app=new UIApplication();var first=Wall(1,6000,5800);var second=Wall(2,1000,1000);app.ActiveUIDocument.Document.Elements.AddRange(new Element[]{first,second,new Element{Id=new ElementId(3),UniqueId="other",Category=new Category{Name="RVT link"}}});app.ActiveUIDocument.Selection.Ids.AddRange(new[]{first.Id,second.Id,new ElementId(3)});
        var key=Top50RevitSelection.DocumentKey(app.ActiveUIDocument.Document);app.ActiveUIDocument.Document.PathName="C:\\SavedAs.rvt";Check(Top50RevitSelection.DocumentKey(app.ActiveUIDocument.Document)==key,"saving under a new name retains document identity");
        var snapshot=Top50RevitSelection.ReadSelected(app);var input=Top50RevitSource.Create(snapshot);
        Check(snapshot.SelectedIds.Length==3&&snapshot.Walls.Length==2&&snapshot.Notes.Any(n=>n.Contains("Skipped element 3")),"selection retains skipped source identities without inventing walls");
        Check(input.Walls.Count==2&&input.Walls.All(w=>w.Included)&&input.Walls.Sum(w=>w.LengthMm)==7000,"one physical row uses the longer forming face");
        Check(input.Walls[0].Revit.CentreLengthMm==5900&&input.Walls[0].Diagnostics.Any(n=>n.Contains("unequal faces")),"centre and both actual faces remain inspectable");
        var originalLength=input.Walls[0].LengthMm;snapshot.Walls[0].ExteriorLengthMm=9999;Check(input.Walls[0].LengthMm==originalLength&&input.RevitSource.Walls[0].ExteriorLengthMm==6000,"source snapshot is detached from caller edits");
        var openingFace=(PlanarFace)first.Sides[ShellLayerType.Exterior][0].Geometry;openingFace.EdgeLoops.Add(new EdgeArray{new Edge{Curve=Segment(500,900)}});Check(Top50RevitSelection.ReadSelected(app).Walls[0].PlanningLengthMm==6000,"opening edges do not shorten the overall forming-face allowance");
        var request=new RevitWallRequest{DocumentKey=snapshot.DocumentKey,UniqueIds=new[]{first.UniqueId,"missing"}};
        var highlight=Top50RevitSelection.Highlight(app,request);Check(highlight.Found==1&&highlight.Requested==2&&app.ActiveUIDocument.Located==0,"group highlight selects existing unique IDs without zooming");
        request.Locate=true;Top50RevitSelection.Highlight(app,request);Check(app.ActiveUIDocument.Located==1,"Locate changes the view only when explicitly requested");
        int sets=app.ActiveUIDocument.Selection.Sets;request.UniqueIds=new[]{"missing"};Top50RevitSelection.Highlight(app,request);Check(app.ActiveUIDocument.Selection.Sets==sets,"missing group cannot clear an unrelated Revit selection");
        request.DocumentKey="different";Reject(()=>Top50RevitSelection.Highlight(app,request),"different");Check(app.ActiveUIDocument.Selection.Sets==sets,"wrong document cannot change Revit selection");
        var curved=Wall(4,1000,1000);((LocationCurve)curved.Location).Curve=new Arc();app.ActiveUIDocument.Document.Elements.Add(curved);app.ActiveUIDocument.Selection.Ids=new System.Collections.Generic.List<ElementId>{curved.Id};var unsupported=Top50RevitSource.Create(Top50RevitSelection.ReadSelected(app));Check(!unsupported.Walls[0].Included&&unsupported.Walls[0].Diagnostics.Any(n=>n.Contains("Curved")),"curved walls stay excluded with a clear source diagnostic");
        ((LocationCurve)curved.Location).Curve=Segment(0,1000);((PlanarFace)curved.Sides[ShellLayerType.Interior][0].Geometry).FaceNormal=new XYZ(0,1,.2);Check(Top50RevitSelection.ReadSelected(app).Walls[0].Problem.Contains("vertical"),"slanted side faces are not silently approximated");
        var model=new Top50ViewModel();model.ImportRevit(input.RevitSource);Check(model.Input.Walls.Count==2&&model.CanUndo,"Revit import is a reversible project edit");model.Undo();Check(model.Input==null&&model.CanRedo,"Undo restores the prior empty project");model.Redo();Check(model.Input.RevitSource.DocumentKey==snapshot.DocumentKey,"Redo restores document and wall provenance");
        string firstId=model.Input.Walls.First().Id,secondId=model.Input.Walls.Last().Id;var label=new Top50WallLabels(model.Input.AllWalls)[firstId];model.AddManual(1250,"Manual project wall");Check(new Top50WallLabels(model.Input.AllWalls)[firstId]==label,"manual additions do not renumber imported walls");
        model.ReviewWall(firstId,6100,true,"Reviewed end allowance");model.DeleteWalls(new[]{secondId});
        request=new RevitWallRequest{DocumentKey=snapshot.DocumentKey,UniqueIds=input.RevitSource.Walls.Select(w=>w.UniqueId).ToArray()};var refresh=Top50RevitSelection.Refresh(app,request);model.RefreshRevit(refresh);
        Check(model.Input.Walls.Single(w=>w.Id==firstId).LengthMm==6100&&model.Input.RemovedWalls.Single().Id==secondId,"refresh preserves reviewed unchanged lengths and deleted-row tombstones");
        first.Sides[ShellLayerType.Exterior][0]=new Reference{Geometry=new PlanarFace{EdgeLoops=new EdgeArrayArray{new EdgeArray{new Edge{Curve=Segment(0,6250)}}}}};model.RefreshRevit(Top50RevitSelection.Refresh(app,request));Check(model.Input.Walls.Single(w=>w.Id==firstId).LengthMm==6250,"changed forming geometry replaces an obsolete length override");
        model.Undo();Check(model.Input.Walls.Single(w=>w.Id==firstId).LengthMm==6100,"refresh can restore the prior reviewed geometry");model.Redo();
        var directory=Path.Combine(Path.GetTempPath(),"Top50-Revit-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"source.top50");model.Save(path);Check(NativeProjectStore.Load(path).Version==5,"Revit projects declare V5");
            var opened=new Top50ViewModel();opened.Open(path,Path.Combine(directory,"cache"));Check(opened.Input.Walls.Count==2&&opened.Input.RemovedWalls.Count==1&&opened.Input.Walls.Any(w=>w.Layer=="Manual"),"project reopen preserves Revit walls, manual rows and deletions without the RVT file");
            Check(opened.Input.Walls.Single(w=>w.Id==firstId).Revit.UniqueId==first.UniqueId&&opened.BuildDiagnosticReport().Contains("exterior mm=6250"),"saved native identities and both face measurements survive reopen");
            var stored=NativeProjectStore.Load(path);stored.SourceBase64=Convert.ToBase64String(new byte[]{1,2,3});NativeProjectStore.Save(stored,path);Reject(()=>NativeProjectStore.Load(path),"checksum");
        }
        finally{Directory.Delete(directory,true);}
        app.ActiveUIDocument.Document.Elements.Remove(first);var missing=Top50RevitSelection.Refresh(app,request);model.RefreshRevit(missing);Check(!model.Input.Walls.Single(w=>w.Id==firstId).Included&&model.Input.Walls.Single(w=>w.Id==firstId).Diagnostics.Any(n=>n.Contains("missing")),"deleted native walls retain their identity and are excluded after refresh");
        var previous=model.Input;Reject(()=>model.ImportRevit(unsupported.RevitSource),"No supported");Check(ReferenceEquals(previous,model.Input)&&model.BuildDiagnosticReport().Contains("Curved"),"invalid import preserves the current offer and records source errors");
        var fastModel=new Top50ViewModel();for(int i=0;i<4;i++)fastModel.AddManual(1000,"Fast outcome regression");
        fastModel.EditSettings(s=>{s.CatalogMm=new[]{1000};s.OfferingMode=1;s.Percentage=45;s.BandPoints=1;});
        System.ComponentModel.PropertyChangedEventHandler cancelBusy=null;
        cancelBusy=(sender,change)=>{if(change.PropertyName==nameof(Top50ViewModel.IsBusy)&&fastModel.IsBusy){fastModel.PropertyChanged-=cancelBusy;fastModel.Cancel();}};
        fastModel.PropertyChanged+=cancelBusy;fastModel.OptimizeAsync().GetAwaiter().GetResult();
        Check(!fastModel.IsBusy&&fastModel.Selected==null&&fastModel.Status.Contains("cancelled"),"active first cancellation has a terminal outcome before returning idle");
        string cancelled=fastModel.Status;fastModel.Cancel();Check(fastModel.Status==cancelled,"idle Cancel preserves the completed cancellation outcome");
        fastModel.OptimizeAsync().GetAwaiter().GetResult();string completed=fastModel.Status;fastModel.Cancel();Check(completed.Contains("No complete proposal found")&&fastModel.Status==completed,"late Cancel preserves a fast no-result outcome");
        Console.WriteLine(checks+" Top50 Revit source, reader, refresh, highlight and persistence checks passed (test-only Revit API stand-ins).");
    }
}
