using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EngineeringDashboard;
using EngineeringDashboard.Top50;

internal static class Program
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,message);}
    static string Plan(Top50ViewModel vm)=>string.Join("|",vm.Pours.Select(p=>p.Id+":"+p.Name+":"+string.Join(",",p.WallIds)));
    static string Checks(Top50ViewModel vm)=>string.Join("|",vm.PourChecks.Select(c=>c.PourId+":"+c.Status+":"+(c.Group==null?"":Top50Order.Group(c.Group))));
    static RevitWallSnapshot Source(string doc,params int[] ids)=>new RevitWallSnapshot{DocumentKey=doc,DocumentTitle="Controlled Revit selection",CapturedUtc="2026-11-03T00:00:00Z",SelectedIds=ids.Select(n=>n.ToString()).ToArray(),Walls=ids.Select(n=>new RevitWallRecord{ElementId=n.ToString(),UniqueId="unique-"+n,CentreLengthMm=1600,ExteriorLengthMm=1600,InteriorLengthMm=1600}).ToArray()};
    static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"Top50-pours-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var vm=new Top50ViewModel();vm.AddManual(1600,"Measured A");vm.AddManual(1600,"Measured B");vm.AddManual(9000,"Measured C");
            vm.EditSettings(s=>{s.LongestCount=1;s.CatalogMm=new[]{750,3000,6000};});
            string a=vm.Input.Walls[0].Id,b=vm.Input.Walls[1].Id,c=vm.Input.Walls[2].Id;
            WallArrangement Short(string id)=>new WallArrangement(id,new PanelPattern(new[]{750,750},new long[]{100000}));
            var offer=new Proposal("fixture",new Dictionary<int,int>{{750,2},{3000,1},{6000,1}},new[]{new CastingGroup(new[]{new WallArrangement(c,new PanelPattern(new[]{6000,3000},new long[]{0})),Short(a)}),new CastingGroup(new[]{Short(b)})});
            vm.AcceptValidatedRun(new SearchRun{Proposals=new[]{offer}});
            string p1=vm.AddPour(),p2=vm.AddPour();vm.AssignWalls(p1,new[]{a,c});vm.AssignWalls(p2,new[]{b});
            Check(vm.UnassignedWallCount==0&&!vm.IsStale&&ReferenceEquals(vm.Selected,offer),"Pour assignment preserves the selected offer and its allocation groups");
            string before=Plan(vm);vm.CheckPoursAsync().GetAwaiter().GetResult();
            Check(vm.PourChecks.Count==2&&vm.PourChecks.All(p=>p.WithinStock),"Separate pours reuse the same stock after stripping");
            Check(vm.PourChecks[0].Group.Walls.Select(w=>w.WallId).SequenceEqual(new[]{a,c})&&Plan(vm)==before,"The solver preserves wall membership and the engineer's wall ordering");
            string fingerprint=Checks(vm);vm.CheckPoursAsync().GetAwaiter().GetResult();Check(Checks(vm)==fingerprint,"Identical pour inputs produce repeatable arrangements");
            vm.MovePour(p2,-1);Check(vm.Pours[0].Id==p2&&vm.Pours[1].WallIds.SequenceEqual(new[]{a,c})&&vm.PourChecks.Count==0,"Moving a pour changes order without reallocating walls and clears stale checks");
            vm.Undo();Check(Plan(vm)==before,"Undo restores complete pour definitions and their order");vm.Redo();Check(vm.Pours[0].Id==p2,"Redo restores the requested pour order");
            vm.RenamePour(p1,"Core walls");Check(vm.Pours.Single(p=>p.Id==p1).Name=="Core walls","Pour naming retains its stable identity");
            vm.AssignWalls(p1,new[]{a,b,b});Check(vm.Pours.Single(p=>p.Id==p1).WallIds.SequenceEqual(new[]{a,c,b})&&vm.Pours.Single(p=>p.Id==p2).WallIds.Length==0,"Reassignment moves each wall once and ignores repeated selections");
            vm.CheckPoursAsync().GetAwaiter().GetResult();var shortage=vm.PourChecks.Single(p=>p.PourId==p1);
            Check(shortage.AdditionalPanelCount==4&&shortage.AdditionalPanels[750]==2&&!shortage.WithinStock,"Concurrent walls flag actual shown-layout shortages on both faces");
            Check(shortage.AdditionalAdjustable==2&&shortage.AdditionalStandard==0,"Filler connector shortages account for both faces and adjustable reuse");
            Check(vm.PourChecks.Single(p=>p.PourId==p2).Group==null&&vm.PourChecks.Single(p=>p.PourId==p2).Status=="No walls assigned","An empty pour is not presented as validated");
            Check(shortage.Detail.Contains("not proven unavoidable")&&vm.BuildDiagnosticReport().Contains("Additional panels per face: 750 mm × 2"),"Diagnostics explain bounded-search shortages and record checked arrangements");
            var check=shortage.Group;var used=check.Walls.SelectMany(w=>w.Pattern.PanelsMm).GroupBy(n=>n).ToDictionary(g=>g.Key,g=>g.Count());
            Check(Top50Validation.Validate(vm.Input.Walls,new Proposal("pour",used,new[]{check}),new SearchLimits(1,350,1,3,100,50)).Count==0,"Every shortage arrangement independently closes all assigned walls and respects fillers");
            string path=Path.Combine(root,"pours.top50");vm.Save(path);var restored=new Top50ViewModel();restored.Open(path,Path.Combine(root,"cache"));
            Check(NativeProjectStore.Load(path).Version==6&&Plan(restored)==Plan(vm)&&restored.Selected.Stock.SequenceEqual(vm.Selected.Stock),"V6 save/open retains pours, memberships, order and the equipment offer");
            Check(restored.PourChecks.Count==0,"Reopening never presents unverified cached pour checks as current");
            before=Plan(vm);Reject(()=>vm.AssignWalls(p1,new[]{"missing"}),"Unknown wall assignment is rejected atomically");Check(before==Plan(vm),"Rejected assignment leaves the project unchanged");
            vm.UnassignWalls(p1,new[]{a});Check(vm.UnassignedWallCount==1,"Unassigned included walls remain visible");vm.Undo();
            vm.DeleteWalls(new[]{a});Check(vm.Pours.All(p=>!p.WallIds.Contains(a)),"Deleting an imported wall removes its pour assignment");vm.Undo();Check(vm.Pours.Single(p=>p.Id==p1).WallIds.Contains(a),"Undo restores a deleted wall and its pour membership together");
            vm.CheckPoursAsync().GetAwaiter().GetResult();fingerprint=Checks(vm);
            System.ComponentModel.PropertyChangedEventHandler cancel=null;cancel=(s,e)=>{if(e.PropertyName==nameof(vm.IsBusy)&&vm.IsBusy){vm.PropertyChanged-=cancel;vm.Cancel();}};vm.PropertyChanged+=cancel;vm.CheckPoursAsync().GetAwaiter().GetResult();
            Check(!vm.IsBusy&&vm.Status.Contains("cancelled")&&Checks(vm)==fingerprint,"Cancellation keeps previous valid checks and all user assignments");
            var duplicate=PourPlan.Copy(vm.Pours);duplicate[0].WallIds=new[]{a};duplicate[1].WallIds=new[]{a};Reject(()=>PourPlan.Validate(duplicate,vm.Input.Walls),"One wall cannot silently belong to two pours");
            var token=new CancellationTokenSource();token.Cancel();bool stopped=false;try{PourSequencePlanner.Check(vm.Input.Walls,vm.Pours,offer,vm.Limits,vm.Settings.CatalogMm,token.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Cancellation is observed before building a pour model");
            var one=new Top50ViewModel();one.AddManual(1000,"No end filler fixture");string oneId=one.Input.Walls[0].Id,onePour=one.AddPour();one.AssignWalls(onePour,new[]{oneId});
            var oneOffer=new Proposal("single",new Dictionary<int,int>{{750,1}},new[]{new CastingGroup(new[]{new WallArrangement(oneId,new PanelPattern(new[]{750},Array.Empty<long>()))})});
            var noLayout=PourSequencePlanner.Check(one.Input.Walls,one.Pours,oneOffer,new SearchLimits(1,350,1,1,100,50),new[]{750},CancellationToken.None);
            Check(noLayout.Single().Group==null,"A single short panel cannot be closed using an invented end filler");
            RevitAdditions(root);
            ConnectorAlternatives();
            Console.WriteLine("PASS "+checks+" pour sequence, import and persistence checks.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{Directory.Delete(root,true);}
    }
    static void ConnectorAlternatives()
    {
        var vm=new Top50ViewModel();vm.AddManual(1500,"A");vm.AddManual(2000,"B");vm.AddManual(1250,"C");vm.EditSettings(s=>{s.LongestCount=1;s.CatalogMm=new[]{500,1000,1250};});
        var a=vm.Input.Walls[0].Id;var b=vm.Input.Walls[1].Id;var c=vm.Input.Walls[2].Id;
        var offer=new Proposal("direct",new Dictionary<int,int>{{1000,2},{500,1},{1250,1}},new[]{new CastingGroup(new[]{new WallArrangement(b,new PanelPattern(new[]{1000,1000},new long[]{0}))}),new CastingGroup(new[]{new WallArrangement(a,new PanelPattern(new[]{1000,500},new long[]{0}))}),new CastingGroup(new[]{new WallArrangement(c,new PanelPattern(new[]{1250},Array.Empty<long>()))})});
        vm.AcceptValidatedRun(new SearchRun{Proposals=new[]{offer}});string pour=vm.AddPour();vm.AssignWalls(pour,new[]{b});vm.CheckPoursAsync().GetAwaiter().GetResult();
        Check(vm.PourChecks.Single().WithinStock&&vm.PourChecks.Single().Group.Walls.Single().Pattern.FillerLocations==0,"The solver checks available connector stock before choosing a layout; it avoids an unnecessary filler connector shortage");
    }
    static void RevitAdditions(string root)
    {
        var vm=new Top50ViewModel();vm.ImportRevit(Source("project",1,2));string first=vm.Input.Walls[0].Id;vm.ReviewWall(first,1610,true,"Reviewed length");string pour=vm.AddPour();vm.AssignWalls(pour,new[]{first});string before=Plan(vm);
        var more=Source("project",1,3);more.Walls[0].ExteriorLengthMm=3000;vm.AddRevitWalls(more);
        Check(vm.Input.Walls.Count==3&&vm.Input.Walls.Single(w=>w.Id==first).LengthMm==1610&&Plan(vm)==before,"Add wall appends new Revit walls while preserving reviewed measurements and pour assignments");
        var input=vm.Input;vm.AddRevitWalls(more);Check(ReferenceEquals(input,vm.Input)&&vm.Input.Walls.Count==3,"Repeated Add wall selections skip duplicates without replacing the source");
        vm.Undo();Check(vm.Input.Walls.Count==2&&Plan(vm)==before,"Added Revit geometry and its source snapshot undo together");vm.Redo();
        Reject(()=>vm.AddRevitWalls(Source("other-project",4)),"Adding walls from another Revit document is rejected");Check(vm.Input.Walls.Count==3&&Plan(vm)==before,"Cross-document rejection preserves imported geometry and pours");
        vm.AssignRevitSelection(pour,Source("project",2));Check(vm.Pours.Single().WallIds.Length==2,"Current Revit selection assigns known imported walls to a pour");
        Reject(()=>vm.AssignRevitSelection(pour,Source("project",99)),"Unknown Revit selection cannot invent a pour membership");
        string path=Path.Combine(root,"revit-additions.top50");vm.Save(path);var reopened=new Top50ViewModel();reopened.Open(path,Path.Combine(root,"revit-cache"));
        Check(reopened.Input.Walls.Count==3&&reopened.Input.Walls.Single(w=>w.Id==first).LengthMm==1610&&Plan(reopened)==Plan(vm),"Merged Revit sources, review overrides and pours round-trip without the model");
        string last=vm.Input.Walls.Single(w=>w.Revit.UniqueId=="unique-3").Id;vm.DeleteWalls(new[]{last});vm.AddRevitWalls(Source("project",3));
        Check(vm.Input.Walls.Count==3&&vm.Input.RemovedWalls.Count==0,"Add wall can explicitly restore a previously removed imported wall");
    }
}
