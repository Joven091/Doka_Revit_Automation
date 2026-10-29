using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Automation;
using static TestSupport;
using static ExternalEntryChecks;

internal static class HelperUpdateChecks
{
    sealed class Payload
    {
        internal string Folder,Version,Archive,Hash;
        internal IDictionary<string,object> Manifest;
        internal string[] Files;
    }
    internal static void Run(string initialFolder,string candidateFolder,FakeRevitPeer peer,string root)
    {
        string feed=Path.Combine(root,"empty-local-feed");Directory.CreateDirectory(feed);
        var initial=Archive(initialFolder,Path.Combine(Output,"initial-package.zip"));
        var candidate=Archive(candidateFolder,Path.Combine(feed,"dashboard-candidate.zip"));
        Check(VersionNumber(candidate.Version)>VersionNumber(initial.Version),"actual helper test uses a strictly newer production candidate");
        string id=VersionText(initial.Version)+"-"+initial.Hash.Substring(0,12),relative="versions/"+id,installed=Path.Combine(root,"versions",id);Directory.CreateDirectory(installed);
        foreach(string file in initial.Files.Concat(new[]{"dashboard-package.json"}))
        {string target=Path.Combine(installed,file.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Path.Combine(initial.Folder,file.Replace('/',Path.DirectorySeparatorChar)),target,false);}
        string generation=Guid.NewGuid().ToString("N");
        Write(Path.Combine(root,"active.json"),new Dictionary<string,object>{{"Version",initial.Version},{"PackageId",id},{"RelativeDirectory",relative},{"ArchiveSha256",initial.Hash},{"BridgeProtocol",1},{"Generation",generation}});
        Write(Path.Combine(feed,"dashboard-latest.json"),new Dictionary<string,object>{{"schemaVersion",1},{"productId","Doka.EngineeringDashboard.Desktop"},{"revitVersion",2024},{"version",candidate.Version},{"bridgeProtocol",1},{"archiveFile",Path.GetFileName(candidate.Archive)},{"sha256",candidate.Hash},{"sizeBytes",new FileInfo(candidate.Archive).Length},{"releaseNotes","QA local production helper integration test"}});
        string sessions=Path.Combine(root,"Sessions");Directory.CreateDirectory(sessions);string seed=Path.Combine(sessions,Guid.NewGuid().ToString("N")+".json");
        Write(seed,new Dictionary<string,object>{{"SchemaVersion",1},{"Version",initial.Version},{"Tool","backpropping"},{"Width",940},{"Height",860},{"Maximized",false},{"Backpropping",new Dictionary<string,object>{{"Slabs",new object[0]},{"Maximums",new Dictionary<string,object>()},{"Extensions",new Dictionary<string,object>()},{"ProjectMaximum","initial incomplete maximum"},{"Draft",new[]{"QA initial draft","3e","4000","0","",""}},{"Curve",0},{"Editing",-1},{"Dirty",true},{"HadResults",false}}}});
        int originalPeerPid=Process.GetCurrentProcess().Id;long originalPeerStart=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        Process updated=null,helper=null;bool finished=false;
        try
        {
            Launch(Path.Combine(installed,"Doka.EngineeringDashboard.exe"),peer,root,seed,(old,back)=>
            {
                Check(back.Current.Name=="Backpropping Calculator","old production payload opens the seeded active calculator");
                SetValue(ById(back,"FloorName"),"QA helper update – unsaved");SetValue(ById(back,"Thickness"),"4e");SetValue(ById(back,"ProjectMaximum"),"12.");
                SignalUpdate();var updateWindow=WaitWindow(old.Id,w=>w.Current.Name=="Engineering Dashboard updates");
                var apply=ByName(updateWindow,"Update Dashboard");Wait(()=>apply.Current.IsEnabled,"candidate feed available");Invoke(apply);
                string planPath=null;
                Wait(()=>{planPath=Directory.GetFiles(Path.Combine(root,"handoffs"),"update-plan.json",SearchOption.AllDirectories).SingleOrDefault();return planPath!=null||old.HasExited;},"real updater handoff plan",180);
                Check(planPath!=null,"old production update UI creates its actual handoff plan");
                var plan=Parse(planPath);string checkpoint=(string)plan["CheckpointPath"];
                Check(checkpoint!=seed&&File.Exists(checkpoint)&&Hash(checkpoint)==(string)plan["CheckpointSha256"],"production update captures a fresh verified checkpoint from live edited inputs");
                var captured=Map(Parse(checkpoint)["Backpropping"]);
                Check((string)Items(captured["Draft"]).First()=="QA helper update – unsaved"&&(string)Items(captured["Draft"]).ElementAt(1)=="4e"&&(string)Items(captured["Draft"]).ElementAt(3)=="0"&&(string)captured["ProjectMaximum"]=="12.","actual update checkpoint contains the edited unsaved draft verbatim");
                string helperExe=Path.Combine(installed,"Doka.DashboardUpdater.exe");
                Wait(()=>{helper=Process.GetProcessesByName("Doka.DashboardUpdater").FirstOrDefault(p=>SameExecutable(p,helperExe));return helper!=null;},"actual production update helper process");
                string ready=Path.Combine(Path.GetDirectoryName(planPath),"helper-ready.json");
                Wait(()=>File.Exists(ready)||helper.HasExited,"actual helper completes payload validation and publishes readiness",30);
                Check(!helper.HasExited&&File.Exists(ready),"actual helper validates its plan and acknowledges readiness");
                Wait(()=>old.HasExited,"old Dashboard closes without closing the local Revit peer",180);
                IDictionary<string,object> receipt=null;string receiptPath=null;
                Wait(()=>
                {
                    foreach(string file in Directory.GetFiles(Path.Combine(root,"handoffs"),"startup.json",SearchOption.AllDirectories))
                    {
                        var possible=Parse(file);if(VersionNumber((string)possible["Version"])==VersionNumber(candidate.Version)&&Convert.ToInt32(possible["ProcessId"])!=old.Id){receipt=possible;receiptPath=file;return true;}
                    }
                    return false;
                },"actual candidate starts, restores work and writes its receipt",180);
                updated=Process.GetProcessById(Convert.ToInt32(receipt["ProcessId"]));
                var active=Parse(Path.Combine(root,"active.json"));string selected=Path.Combine(root,((string)active["RelativeDirectory"]).Replace('/',Path.DirectorySeparatorChar),"Doka.EngineeringDashboard.exe");
                Check(VersionNumber((string)active["Version"])==VersionNumber(candidate.Version)&&(string)active["Generation"]!=generation,"real helper atomically activates the candidate version");
                Check(SameExecutable(updated,selected)&&updated.StartTime.ToUniversalTime().Ticks==Convert.ToInt64(receipt["StartedUtcTicks"]),"candidate receipt identifies the actual selected new production process");
                Check(FileVersionInfo.GetVersionInfo(updated.MainModule.FileName).FileVersion==FileVersionInfo.GetVersionInfo(Path.Combine(candidateFolder,"Doka.EngineeringDashboard.exe")).FileVersion,"running Dashboard has the actual candidate EXE version");
                var restored=WaitWindow(updated.Id,w=>w.Current.Name=="Backpropping Calculator");
                Check(Value(ById(restored,"FloorName"))=="QA helper update – unsaved"&&Value(ById(restored,"Thickness"))=="4e"&&Value(ById(restored,"Age"))=="0"&&Value(ById(restored,"ProjectMaximum"))=="12.","actual new Dashboard process restores live unsaved inputs after the real helper update");
                Check(Process.GetCurrentProcess().Id==originalPeerPid&&Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks==originalPeerStart&&peer.Count("hello")>=2,"the same original local Revit peer process stays alive and reconnects across the update");
                Check(old.HasExited&&File.Exists(checkpoint)&&Directory.Exists(installed),"helper leaves no old Dashboard process and retains checkpoint and previous payload");
                var helperWindow=WaitWindow(helper.Id,w=>w.Current.Name=="Engineering Dashboard update");
                Wait(()=>helperWindow.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"Dashboard updated. Revit stayed open and your work was restored."))!=null,"actual helper confirms restored startup");
                Invoke(ByName(helperWindow,"Close"));Wait(()=>helper.HasExited,"production helper closes cooperatively");Check(helper.ExitCode==0,"actual helper exits successfully");
                CloseAll(updated);
                Wait(()=>updated.HasExited,"updated test-owned Dashboard closes normally");Check(updated.ExitCode==0,"updated Dashboard closes normally after helper integration check");
                File.WriteAllText(Path.Combine(Output,"helper-update-evidence.txt"),"Initial version: "+initial.Version+"\nCandidate version: "+candidate.Version+"\nOriginal peer PID: "+originalPeerPid+"\nOld Dashboard PID: "+old.Id+"\nNew Dashboard PID: "+updated.Id+"\nHelper PID: "+helper.Id+"\nActive pointer: "+Path.Combine(root,"active.json")+"\nActual handoff: "+planPath+"\nActual restore receipt: "+receiptPath+"\nRecovery checkpoint: "+checkpoint+"\nActual Revit host acceptance remains unrun.\n");
            },90);
            Check(peer.Failure==null,"real helper integration retained a valid authenticated contract connection");finished=true;
        }
        catch(Exception error)
        {
            Console.WriteLine("ACTUAL HELPER UPDATE FAILURE: "+error);
            FailureDiagnostics.Capture("helper-update",root,helper,updated);throw;
        }
        finally
        {
            if(!finished)foreach(var owned in new[]{helper,updated})if(owned!=null)try{if(!owned.HasExited){owned.Kill();owned.WaitForExit(5000);}}catch(InvalidOperationException){}
            helper?.Dispose();updated?.Dispose();
        }
    }
    static Payload Archive(string folder,string path)
    {
        var manifest=Parse(Path.Combine(folder,"dashboard-package.json"));string version=(string)manifest["version"];
        Check((string)manifest["productId"]=="Doka.EngineeringDashboard.Desktop"&&Convert.ToInt32(manifest["schemaVersion"])==1&&Convert.ToInt32(manifest["bridgeProtocol"])==1,"supplied production payload has the agreed Dashboard identity");
        var files=Items(manifest["files"]).Select(Map).ToArray();string[] names=files.Select(row=>(string)row["path"]).ToArray();
        Check(new[]{"Doka.EngineeringDashboard.exe","MyRevitAddin.dll","Doka.DashboardUpdater.exe","google-ortools-native.dll","ortools.dll","BeamSolver/Doka.BeamSolver.exe"}.All(names.Contains),"production payload includes actual entry, updater, solver and worker");
        foreach(var file in files)
        {
            string name=(string)file["path"],source=Path.GetFullPath(Path.Combine(folder,name.Replace('/',Path.DirectorySeparatorChar)));
            if(!source.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid test payload path.");
            if(new FileInfo(source).Length!=Convert.ToInt64(file["sizeBytes"])||Hash(source)!=(string)file["sha256"])throw new InvalidDataException("Test production payload differs from its manifest: "+name);
        }
        Check(VersionNumber(FileVersionInfo.GetVersionInfo(Path.Combine(folder,"Doka.EngineeringDashboard.exe")).FileVersion)==VersionNumber(version)&&VersionNumber(FileVersionInfo.GetVersionInfo(Path.Combine(folder,"MyRevitAddin.dll")).FileVersion)==VersionNumber(version),"entry and resource DLL versions match their production manifest");
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(string name in names.Concat(new[]{"dashboard-package.json"}))zip.CreateEntryFromFile(Path.Combine(folder,name.Replace('/',Path.DirectorySeparatorChar)),name,CompressionLevel.Fastest);
        return new Payload{Folder=folder,Version=version,Archive=path,Hash=Hash(path),Manifest=manifest,Files=names};
    }
    static string Hash(string path){using(var source=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(source)).Replace("-","").ToLowerInvariant();}
    static string VersionText(string raw){var version=new Version(raw);return version.Revision<=0?version.ToString(3):version.ToString(4);}
    static Version VersionNumber(string raw){var version=new Version(raw);return new Version(version.Major,version.Minor,version.Build,Math.Max(0,version.Revision));}
    static void Write(string path,object data)=>File.WriteAllText(path,Json.Serialize(data));
    static bool SameExecutable(Process process,string path)
    {
        try
        {
            if(process==null||process.HasExited)return false;
            string executable=process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(executable)&&string.Equals(Path.GetFullPath(executable),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase);
        }
        catch(InvalidOperationException){return false;}
        catch(System.ComponentModel.Win32Exception){return false;}
    }
}
