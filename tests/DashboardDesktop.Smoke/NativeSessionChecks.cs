using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using static TestSupport;

internal static class NativeSessionChecks
{
    internal static string BackproppingCheckpoint,Top50Checkpoint,SteelCheckpoint;
    static Type Sessions=>Type("EngineeringDashboard.DesktopHost.DashboardSessionController");
    static Type Launcher=>Type("EngineeringDashboard.LauncherWindow");
    static Window Active(Window launcher)=>(Window)Get(launcher,"CurrentTool");
    static bool Prepare(out string path,out string error)
    {
        object[] args={null,null};bool success=(bool)Call(Sessions,"TryPrepareForUpdate",args);path=(string)args[0];error=(string)args[1];return success;
    }
    static void Cancel()=>Call(Sessions,"CancelUpdateRestart");
    static Window StartLauncher(){var window=(Window)New("EngineeringDashboard.LauncherWindow");window.Show();Pump();return window;}
    static Window Restart(Window previous,string path)
    {
        Call(Sessions,"BeginUpdateRestart",path);Active(previous)?.Close();previous.Close();Pump();
        var next=StartLauncher();Call(Sessions,"Restore",path);Pump();return next;
    }
    static string Capture(Window launcher)
    {Check(Prepare(out string path,out string error),"production session capture succeeds: "+error);Check(!launcher.IsEnabled&&(!Active(launcher)?.IsEnabled??true),"snapshot leaves working windows frozen until restart or cancellation");return path;}
    internal static void Run(FakeRevitPeer peer,string root,object client)
    {
        var launcher=StartLauncher();
        Check(!((Button)launcher.FindName("BtnDomeCalculator")).IsEnabled,"actual desktop resource keeps Dome Beta locked");
        Click(launcher,"BtnBeamCalculator");var beam=Active(launcher);Click(beam,"PickTowersButton");
        var import=Find<Button>(beam).Single(button=>Equals(button.Content,"Use Revit selection"));Click(import);
        var beamModel=Get(Launcher,"_beamViewModel");
        Wait(()=>peer.Count("beam.read")==1&&(bool)Get(beamModel,"UseBeamNetwork"),"one-click beam import into native drawing");
        string geometry=(string)Get(beamModel,"NetworkConfigurationXml");
        Check(geometry.Contains("701")&&geometry.Contains("703")&&((IEnumerable)Get(beamModel,"NetworkBeams")).Cast<object>().Count()==2,"native drawing receives independently authored beams, slab and crossing through production pipe client");
        Set(beamModel,"TowerLegCapacityKn",64d);Set(beamModel,"AllowCustomH20Lengths",true);Set(beamModel,"StockLengthsText","1800; 2450; 3900");
        beam.Close();Pump();

        Click(launcher,"BtnFlexCalculator");var choice=Active(launcher);Check(!((Button)choice.FindName("PickTowersButton")).IsEnabled,"D3 slab remains locked in desktop chooser");Click(choice,"PickPropsButton");
        var slab=Active(launcher);var slabModel=Get(Launcher,"_flexViewModel");Set(slabModel,"SlabThickness",310d);Set(slabModel,"FloorHeight",4200d);slab.Close();Pump();

        Click(launcher,"BtnTop50");var planner=Active(launcher);Click(Find<Button>(planner).Single(button=>AutomationProperties.GetAutomationId(button)=="Top50UseRevitSelection"));
        var top50=Get(Launcher,"_top50ViewModel");Wait(()=>Get(top50,"Input")!=null&&!(bool)Get(planner,"RestartBusy"),"Top50 production client import");
        Check(((IEnumerable)Get(Get(top50,"Input"),"Walls")).Cast<object>().Sum(w=>(decimal)Get(w,"LengthMm"))==7200m,"Top50 pipe import uses the longer forming faces: 6200 + 1000 mm");
        string project=Path.Combine(Output,"working-project.top50");Call(top50,"Save",project);byte[] savedBytes=File.ReadAllBytes(project);
        Call(top50,"AddManual",1400m,"QA unsaved reviewed addition");Set(Get(top50,"Settings"),"LongestCount",2);Call(top50,"Changed",true);Set(top50,"IsStale",true);Call(planner,"ShowPage",1);Keyboard.ClearFocus();Pump();
        Check((bool)Get(top50,"Dirty")&&(string)Get(top50,"ProjectPath")==project,"fixture has unsaved Top50 inputs and an existing project path");
        Set(top50,"IsBusy",true);
        try{Check(!Prepare(out _,out string busy)&&busy.Contains("Finish"),"active optimizer state refuses update checkpoint");}finally{Set(top50,"IsBusy",false);}
        peer.ContextEntered.Reset();peer.ReleaseContext.Reset();
        var call=client.GetType().GetMethod("CallAsync").MakeGenericMethod(typeof(object),Type("EngineeringDashboard.DesktopHost.ActiveDocumentInfo"));
        var pending=(Task)call.Invoke(client,new object[]{"context",null,CancellationToken.None});
        Wait(()=>peer.ContextEntered.IsSet,"controlled pending Revit request");
        Check(!Prepare(out _,out string pendingError)&&pendingError.Contains("Revit"),"pending production pipe request refuses Dashboard update");
        peer.ReleaseContext.Set();Wait(()=>pending.IsCompleted,"controlled Revit request completion");pending.GetAwaiter().GetResult();
        var helperOwner=new Window{Owner=launcher,Width=350,Height=200,Title="QA helper request owner"};helperOwner.Show();Pump();
        var helper=New("EngineeringDashboard.DesktopHost.HelperApplyRequest");Set(helper,"Kind","beam");Set(helper,"WidthMm",600d);Set(helper,"DepthMm",1200d);Set(helper,"PropSpacingMm",900d);
        peer.ContextEntered.Reset();peer.ReleaseContext.Reset();int helperWrites=peer.Count("helpers.apply");
        var helperTask=(Task)Call(Type("EngineeringDashboard.DesktopHost.DesktopRuntime"),"ApplyHelpersAsync",helperOwner,helper);
        Wait(()=>peer.ContextEntered.IsSet,"controlled helper context lookup");helperOwner.Close();peer.ReleaseContext.Set();
        Wait(()=>helperTask.IsCompleted,"closed helper owner cancels request");
        bool stopped=false;try{helperTask.GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}catch(ObjectDisposedException){stopped=true;}
        Check(stopped&&peer.Count("helpers.apply")==helperWrites&&!helperOwner.IsVisible,"closing a calculator during context lookup prevents the later Revit helper-write request");

        Top50Checkpoint=Capture(launcher);var snapshot=Parse(Top50Checkpoint);var topSnapshot=Map(snapshot["Top50"]);
        Check((bool)topSnapshot["Dirty"]&&(string)topSnapshot["ProjectPath"]==project&&Convert.ToInt32(topSnapshot["Page"])==1,"Top50 snapshot preserves dirty flag, path and selected page");
        Check(Convert.ToInt32(Map(topSnapshot["Settings"])["LongestCount"])==2&&Items(Map(topSnapshot["Input"])["Walls"]).Count()==3,"Top50 snapshot includes unsaved settings and reviewed manual wall");
        var beamFields=Fields(snapshot["Beam"]);Check(beamFields["NetworkConfigurationXml"]==geometry&&beamFields["TowerLegCapacityKn"]=="64"&&beamFields["AllowCustomH20Lengths"]=="True"&&beamFields["StockLengthsText"]=="1800; 2450; 3900","retained D3 drawing geometry and project-specific stock settings are checkpointed exactly");
        var slabFields=Fields(snapshot["Slab"]);Check(slabFields["SlabThickness"]=="310"&&slabFields["FloorHeight"]=="4200","inactive slab inputs are retained alongside active planner");
        Check((bool)Get(top50,"Dirty")&&(string)Get(top50,"ProjectPath")==project&&savedBytes.SequenceEqual(File.ReadAllBytes(project)),"checkpoint does not save, overwrite or mark Top50 project clean");
        Cancel();Check(planner.IsEnabled,"cancelled update re-enables working tool");
        Top50Checkpoint=Capture(launcher);launcher=Restart(launcher,Top50Checkpoint);planner=Active(launcher);top50=Get(Launcher,"_top50ViewModel");
        Check(planner.Title=="Top50 Planner"&&(bool)Get(top50,"Dirty")&&(string)Get(top50,"ProjectPath")==project,"native restore reopens planner with original dirty state and project path");
        Check((bool)Get(top50,"IsStale")&&((IEnumerable)Get(top50,"Proposals")).Cast<object>().Count()==0&&((string)Get(top50,"Status")).Contains("Optimize"),"updated planner preserves inputs and requires a new equipment optimization");
        Check((string)Get(Get(Launcher,"_beamViewModel"),"NetworkConfigurationXml")==geometry&&(double)Get(Get(Launcher,"_flexViewModel"),"SlabThickness")==310d,"restore retains inactive D3 and slab inputs");
        Set(top50,"Dirty",false);planner.Close();Pump();

        Click(launcher,"BtnBackpropping");var back=Active(launcher);
        ((TextBox)back.FindName("FloorName")).Text="QA draft – unsaved";((TextBox)back.FindName("Thickness")).Text="3e";((TextBox)back.FindName("Age")).Text="not-yet-known";((TextBox)back.FindName("ProjectMaximum")).Text="12.";
        BackproppingCheckpoint=Capture(launcher);var backState=Map(Parse(BackproppingCheckpoint)["Backpropping"]);
        Check((string)Items(backState["Draft"]).ElementAt(0)=="QA draft – unsaved"&&(string)Items(backState["Draft"]).ElementAt(1)=="3e"&&(string)Items(backState["Draft"]).ElementAt(3)=="not-yet-known"&&(string)backState["ProjectMaximum"]=="12.","raw invalid backpropping drafts are preserved without interpreting or rounding");
        launcher=Restart(launcher,BackproppingCheckpoint);back=Active(launcher);
        Check(((TextBox)back.FindName("FloorName")).Text=="QA draft – unsaved"&&((TextBox)back.FindName("Age")).Text=="not-yet-known"&&((TextBox)back.FindName("Thickness")).Text=="3e","native backpropping restore recovers invalid drafts verbatim");
        Check(((TextBlock)back.FindName("ResultState")).Text.Contains("calculate"),"backpropping results explicitly require recalculation");back.Close();Pump();

        Click(launcher,"BtnBeamCalculator");beam=Active(launcher);beamModel=Get(Launcher,"_beamViewModel");
        var workspace=Get(beam,"FormworkWorkspace");
        Set(workspace,"readingRevit",true);
        try{Check(!Prepare(out _,out _),"active drawing Revit operation refuses snapshot");}finally{Set(workspace,"readingRevit",false);}
        var dialog=new Window{Owner=beam,Width=350,Height=200,Title="QA unfinished review"};dialog.Show();Pump();
        Check(!Prepare(out _,out string dialogError)&&dialogError.Contains("dialog"),"open unfinished review dialog refuses update without losing its draft");dialog.Close();
        string beamCheckpoint=Capture(launcher);launcher=Restart(launcher,beamCheckpoint);beam=Active(launcher);
        Check(Find<TextBlock>(beam).Any(text=>text.Text.Contains("Optimize towers")),"D3 restore explicitly asks for fresh tower optimization");beam.Close();Pump();

        Click(launcher,"BtnSteelShape");var steel=Active(launcher);var init=(Task<bool>)Get(steel,"Initialization");Wait(()=>init.IsCompleted,"WebView2 initialization",60);
        Check(init.GetAwaiter().GetResult(),"actual desktop Steel Shape WebView2 initializes");
        Js(steel,"(()=>{selectCalculator('round_bar');$('field-D').value='72.25';$('field-D').dispatchEvent(new Event('input',{bubbles:true}));selectCalculator('rhs');$('field-A').value='123.4';$('field-A').dispatchEvent(new Event('input',{bubbles:true}));$('weld-enabled').checked=true;$('weld-enabled').dispatchEvent(new Event('change',{bubbles:true}));$('weld-connection').value='L';$('weld-connection').dispatchEvent(new Event('change',{bubbles:true}));$('weld-type').value='butt';$('weld-type').dispatchEvent(new Event('change',{bubbles:true}));$('weld-L1').value='21e';$('weld-L1').dispatchEvent(new Event('input',{bubbles:true}));return true;})()");
        SteelCheckpoint=Capture(launcher);string before=(string)Parse(SteelCheckpoint)["SteelShape"];
        Check(before.Contains("72.25")&&before.Contains("123.4")&&before.Contains("21e")&&before.Contains("butt"),"Steel Shape snapshot includes inactive shape, current section, connection and incomplete weld draft");
        launcher=Restart(launcher,SteelCheckpoint);steel=Active(launcher);
        Check(Js(steel,"Boolean(current==='rhs' && $('field-A').value==='123.4' && $('weld-L1').value==='21e' && $('weld-type').value==='butt' && $('weld-connection').value==='L' && states.get('round_bar').D==='72.25')")=="true","native browser restore preserves all shape states and exact weld inputs");steel.Close();launcher.Close();Pump();
        Check(peer.Failure==null,"local contract peer completed native tests without protocol errors");
    }
    static string Js(Window steel,string expression)
    {
        var task=(Task<string>)Call(Get(steel,"Browser"),"ExecuteScriptAsync",expression);Wait(()=>task.IsCompleted,"Steel Shape script",60);return task.GetAwaiter().GetResult();
    }
}
