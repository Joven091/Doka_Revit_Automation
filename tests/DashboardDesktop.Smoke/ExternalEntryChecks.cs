using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using static TestSupport;

internal static class ExternalEntryChecks
{
    static EventWaitHandle activate,updates;
    internal static void Run(string executable,FakeRevitPeer peer,string root)
    {
        Check(!File.Exists(Path.Combine(Path.GetDirectoryName(executable),"RevitAPI.dll"))&&!File.Exists(Path.Combine(Path.GetDirectoryName(executable),"RevitAPIUI.dll")),"external production payload does not copy Autodesk host DLLs");
        Launch(executable,peer,root,null,(process,window)=>
        {
            var dome=ByName(window,"Dome calculator — Beta — Locked");Check(!dome.Current.IsEnabled,"actual external process displays locked Dome card");
            updates.Set();var updateWindow=WaitWindow(process.Id,w=>w.Current.Name=="Engineering Dashboard updates");
            Wait(()=>ByName(updateWindow,"Revit integration updates").Current.IsEnabled,"update page finishes checking local feed");
            Check(true,"Revit update event opens the desktop update page in an existing process");Close(updateWindow,process.Id);
            Invoke(ByName(window,"Beam calculator"));
            var beam=WaitWindow(process.Id,w=>w.Current.Name=="Beam Calculator");Invoke(ByName(beam,"Choose D3 Shoring System"));
            beam=WaitWindow(process.Id,w=>w.Current.Name.IndexOf("D3",StringComparison.OrdinalIgnoreCase)>=0);
            Check(ByName(beam,"Use Revit selection").Current.IsEnabled,"external D3 workspace has one-click Revit selection action");
            int previous=peer.Count("beam.read");Invoke(ByName(beam,"Use Revit selection"));Wait(()=>peer.Count("beam.read")==previous+1,"external production entry calls authenticated beam.read");
            Check(Windows(process.Id).All(w=>!w.Current.Name.Contains("Create drawing")),"external one-click import keeps a single working canvas");
            Close(beam,process.Id);WaitWindow(process.Id,w=>w.Current.Name=="Engineering Dashboard");
        });
        Launch(executable,peer,root,NativeSessionChecks.BackproppingCheckpoint,(process,window)=>
        {
            Check(window.Current.Name=="Backpropping Calculator","external entry restores the active backpropping calculator");
            Check(Value(ById(window,"FloorName"))=="QA draft – unsaved"&&Value(ById(window,"Thickness"))=="3e"&&Value(ById(window,"Age"))=="not-yet-known","actual new process restores incomplete raw backpropping inputs");
        });
        Launch(executable,peer,root,NativeSessionChecks.Top50Checkpoint,(process,window)=>
        {
            Check(window.Current.Name=="Top50 Planner","external entry restores the active Top50 planner");
            Check(Value(ById(window,"Top50NumberLongestwallsrequiredsimultaneously"))=="2","actual new process restores unsaved Top50 method settings");
            Invoke(ById(window,"Top50Page0"));var table=ById(window,"Top50ImportWalls");
            Check(table.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem)).Count==3,"actual new process retains imported and unsaved manual walls");
            Check(ById(window,"Top50Outcome").Current.Name.Contains("Optimize"),"external restored planner clearly requires recalculation");
        });
        Launch(executable,peer,root,NativeSessionChecks.SteelCheckpoint,(process,window)=>
        {
            Check(window.Current.Name=="Steel Shape","external entry restores Steel Shape before acknowledging startup");
            Check(ById(window,"SteelShapeBrowser").Current.IsEnabled,"actual new process contains embedded Steel Shape browser");
        },90);
        Launch(executable,peer,root,null,(process,window)=>
        {
            Check(window.Current.Name=="Engineering Dashboard updates","update-only entry opens the update page without a calculator launch");
            activate.Set();var promoted=WaitWindow(process.Id,w=>w.Current.Name=="Engineering Dashboard");
            Check(ByName(promoted,"Beam calculator").Current.IsEnabled,"normal Revit activation promotes an update-only process to its authenticated Dashboard");
        },45,"--updates");
        Check(peer.Failure==null,"external entry contract peer received only valid session requests");
    }
    internal static void SignalUpdate()=>updates.Set();
    internal static void Launch(string executable,FakeRevitPeer peer,string root,string checkpoint,Action<Process,AutomationElement> inspect,int seconds=45,string initialArguments=null)
    {
        string handoff=Path.Combine(root,"handoffs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(handoff);
        string ack=Path.Combine(handoff,"startup.json"),nonce=Guid.NewGuid().ToString("N");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable),Arguments=initialArguments??(checkpoint==null?"":"--restore-session "+Quote(checkpoint))};
        peer.Configure((key,value)=>{if(value==null)start.EnvironmentVariables.Remove(key);else start.EnvironmentVariables[key]=value;},root);
        start.EnvironmentVariables["DOKA_DASHBOARD_STARTUP_ACK"]=ack;start.EnvironmentVariables["DOKA_DASHBOARD_STARTUP_NONCE"]=nonce;
        string activationName="Local\\DokaDashboardActivate-qa-"+Guid.NewGuid().ToString("N"),updatesName="Local\\DokaDashboardUpdates-qa-"+Guid.NewGuid().ToString("N");
        using(activate=new EventWaitHandle(false,EventResetMode.AutoReset,activationName))
        using(updates=new EventWaitHandle(false,EventResetMode.AutoReset,updatesName))
        {
            start.EnvironmentVariables["DOKA_DASHBOARD_ACTIVATE_EVENT"]=activationName;start.EnvironmentVariables["DOKA_DASHBOARD_UPDATE_EVENT"]=updatesName;
            using(var process=Process.Start(start))
            {
            bool complete=false;
            try
            {
                Wait(()=>File.Exists(ack)||process.HasExited,"production entry startup receipt",seconds);
                Check(!process.HasExited&&File.Exists(ack),"actual external Dashboard reaches restored startup state");
                var receipt=Parse(ack);
                Check((string)receipt["Nonce"]==nonce&&Convert.ToInt32(receipt["ProcessId"])==process.Id&&Convert.ToInt64(receipt["StartedUtcTicks"])==process.StartTime.ToUniversalTime().Ticks,"startup acknowledgement identifies the exact launched production process");
                Check(new Version((string)receipt["Version"])==new Version(FileVersionInfo.GetVersionInfo(executable).FileVersion),"startup receipt version matches the actual production EXE");
                Check(!Process.GetCurrentProcess().HasExited,"local bridge peer stays alive while Dashboard process is replaced");
                var window=WaitWindow(process.Id,w=>w.Current.Name!="Engineering Dashboard"||checkpoint==null);
                inspect(process,window);
                CloseAll(process);
                Wait(()=>process.HasExited,"test-owned Dashboard closes normally",15);Check(process.ExitCode==0,"production external entry exits cleanly");complete=true;
            }
            catch(Exception error)
            {
                Console.WriteLine("EXTERNAL ENTRY/CALLBACK FAILURE (PID "+process.Id+"): "+error);
                FailureDiagnostics.Capture("external-entry-"+process.Id,root,process);throw;
            }
            finally
            {
                // Test cleanup only. Product updater must never kill Revit or an unrelated Dashboard.
                if(!complete&&!process.HasExited){process.Kill();process.WaitForExit(5000);}
            }
            }
        }
    }
    internal static void CloseAll(Process process)
    {
        var closing=Stopwatch.StartNew();
        while(!process.HasExited&&closing.Elapsed<TimeSpan.FromSeconds(15))
        {
            // Closing a tool reveals the retained Launcher. Discover again rather
            // than assuming the first set of visible windows is the whole app.
            foreach(var visible in Windows(process.Id).ToArray())
            {if(process.HasExited)break;try{Close(visible,process.Id);}catch(ElementNotAvailableException){}}
            Pump();if(!process.HasExited)Thread.Sleep(50);
        }
    }
    static string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
    internal static AutomationElement[] Windows(int pid)
    {
        var result=new List<AutomationElement>();
        // Owned WPF windows remain top-level HWNDs, but UIA may nest them below
        // a visible owner. Discover HWNDs first, then inspect only this child PID.
        if(!EnumWindows((handle,state)=>
        {
            uint owner;GetWindowThreadProcessId(handle,out owner);
            if(owner!=(uint)pid||!IsWindowVisible(handle))return true;
            try
            {
                var element=AutomationElement.FromHandle(handle);
                var current=element.Current;
                if(current.ProcessId==pid&&current.ControlType==ControlType.Window)result.Add(element);
            }
            catch(Exception error)when(error is ElementNotAvailableException||error is InvalidOperationException||error is ArgumentException||error is COMException)
            { /* A window may close between HWND discovery and its UIA lookup. */ }
            return true;
        },IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot enumerate owned Dashboard windows.");
        return result.ToArray();
    }
    [return:MarshalAs(UnmanagedType.Bool)] delegate bool EnumerateWindow(IntPtr handle,IntPtr state);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool EnumWindows(EnumerateWindow callback,IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle,out uint processId);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool IsWindowVisible(IntPtr handle);
    internal static AutomationElement WaitWindow(int pid,Func<AutomationElement,bool> matches)
    {AutomationElement found=null;Wait(()=>{found=Windows(pid).FirstOrDefault(matches);return found!=null;},"production window opens");return found;}
    static AutomationElement Find(AutomationElement root,Condition condition,string description)
    {AutomationElement found=null;Wait(()=>{try{found=root.FindFirst(TreeScope.Descendants,condition);return found!=null;}catch(ElementNotAvailableException){return false;}},description);return found;}
    internal static AutomationElement ByName(AutomationElement root,string name)=>Find(root,new PropertyCondition(AutomationElement.NameProperty,name),"UI element: "+name);
    internal static AutomationElement ById(AutomationElement root,string id)=>Find(root,new PropertyCondition(AutomationElement.AutomationIdProperty,id),"UI element: "+id);
    internal static void Invoke(AutomationElement element){((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();Pump();}
    internal static string Value(AutomationElement element)=>((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    internal static void SetValue(AutomationElement element,string value)=>((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
    internal static void Close(AutomationElement window,int pid)
    {
        Task close=Task.Run(()=>{try{((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();}catch(ElementNotAvailableException){}});
        Wait(()=>
        {
            foreach(var dialog in Windows(pid).Where(w=>w.Current.Name=="Top50 project"))
            {
                var ok=dialog.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.NameProperty,"OK"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)));
                if(ok!=null)Invoke(ok);
            }
            return close.IsCompleted;
        },"close test-owned Dashboard window");close.GetAwaiter().GetResult();Pump();
    }
}
