using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MyRevitAddin.Updates;

internal static class Program
{
    static int checks;
    static void Check(bool condition,string text){checks++;if(!condition)throw new Exception(text);}
    static void Reject(Action action,string text){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(text);}
    static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--hold")
        {
            using(var locked=new FileStream(args[1],FileMode.Open,FileAccess.Read,FileShare.Read))
            {File.WriteAllText(args[2],"ready");var timer=Stopwatch.StartNew();while(!File.Exists(args[3]) && timer.Elapsed<TimeSpan.FromMinutes(2))Thread.Sleep(100);}
            return 0;
        }
        if(args.Length>0 && args[0]=="--stage")
        {
            string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DokaRevitAutomation","Updates");
            string installation=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins","2024");
            using(var host=Process.GetProcessById(int.Parse(args[3])))
            {
                var release=UpdateFiles.Check(args[1],new Version(0,0,0,0));
                var plan=new UpdatePlan {Release=release,InstallDirectory=installation,HostProcessId=host.Id,HostStartedUtcTicks=host.StartTime.ToUniversalTime().Ticks};
                Console.WriteLine(UpdateFiles.Stage(args[1],cache,args[2],plan,CancellationToken.None));return 0;
            }
        }
        string root=Path.Combine(Path.GetTempPath(),"doka-update-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{Run(root);Console.WriteLine("PASS: "+checks+" update checks");return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Directory.Delete(root,true);}
    }
    static void Run(string root)
    {
        string source=Path.Combine(root,"feed"),cache=Path.Combine(root,"cache"),install=Path.Combine(root,"installed");Directory.CreateDirectory(source);Directory.CreateDirectory(install);
        string payload=Path.Combine(source,"Doka-1.1.1.exe"),helper=Path.Combine(root,"helper.exe");File.WriteAllText(payload,"test installer");File.WriteAllText(helper,"test helper");
        var release=new UpdateManifest {SchemaVersion=1,ProductId=UpdateManifest.Product,RevitVersion=2024,Version="1.1.1",InstallerFile=Path.GetFileName(payload),SizeBytes=new FileInfo(payload).Length,Sha256=UpdateFiles.Hash(payload),ReleaseNotes="Release notes"};
        string latest=Path.Combine(source,"latest.json");UpdateFiles.WriteJson(latest,release);
        Check(UpdateFiles.Check(source,UpdateFiles.ParseVersion("1.1.0"))!=null,"new version offered");
        Check(UpdateFiles.Check(source,UpdateFiles.ParseVersion("1.1.1.0"))==null,"same normalized version ignored");
        Check(UpdateFiles.Check(source,UpdateFiles.ParseVersion("2.0.0"))==null,"older version ignored");
        foreach(string name in new[] {"../other.exe","folder/other.exe","folder\\other.exe","C:other.exe","x.cmd","x.exe --flag"})
        {release.InstallerFile=name;Reject(release.Validate,"unsafe installer path accepted");}release.InstallerFile=Path.GetFileName(payload);
        release.RevitVersion=2025;Reject(release.Validate,"wrong Revit accepted");release.RevitVersion=2024;
        release.ProductId="other";Reject(release.Validate,"wrong product accepted");release.ProductId=UpdateManifest.Product;
        Reject(()=>UpdateFiles.ParseVersion("1.2"),"ambiguous version accepted");Reject(()=>UpdateFiles.ParseVersion("1.2.3-beta"),"prerelease accepted");
        release.Sha256="bad";Reject(release.Validate,"invalid hash accepted");release.Sha256=UpdateFiles.Hash(payload);
        var plan=new UpdatePlan {Release=release,InstallDirectory=install,HostProcessId=123,HostStartedUtcTicks=123456};
        long copied=0;string stage=UpdateFiles.Stage(source,cache,helper,plan,CancellationToken.None,(done,total)=>copied=done);
        Check(copied==release.SizeBytes && File.Exists(Path.Combine(stage,"Doka.RevitUpdater.exe")),"installer and helper staged locally");
        string path=Path.Combine(stage,"update-plan.json");Check(UpdateFiles.ReadPlan(path,cache,install).Release.Version=="1.1.1","valid plan accepted");
        Reject(()=>UpdateFiles.ReadPlan(path,source,install),"plan outside cache accepted");Reject(()=>UpdateFiles.ReadPlan(path,cache,source),"foreign installation accepted");
        var fake=new Fake();DeferredUpdate.Apply(plan,fake,CancellationToken.None,null,TimeSpan.Zero);
        Check(fake.Polls>=3 && fake.Installs==1,"waits for host and file locks before installing");
        fake=new Fake {ExitCode=5};try{DeferredUpdate.Apply(plan,fake,CancellationToken.None,null,TimeSpan.Zero);throw new Exception("failed installer accepted");}catch(IOException){checks++;}
        fake=new Fake {Result=new Version(1,1,0,0)};try{DeferredUpdate.Apply(plan,fake,CancellationToken.None,null,TimeSpan.Zero);throw new Exception("wrong installed version accepted");}catch(IOException){checks++;}
        var cancel=new CancellationTokenSource();fake=new Fake();
        try{DeferredUpdate.Apply(plan,fake,cancel.Token,s=>cancel.Cancel(),TimeSpan.Zero);throw new Exception("cancel ignored");}catch(OperationCanceledException){Check(fake.Installs==0,"cancelled before install");}
        File.WriteAllText(plan.InstallerPath,"tampered file!");Reject(()=>UpdateFiles.VerifyInstaller(plan),"corrupt staged payload accepted");
        Reject(()=>DeferredUpdate.Apply(plan,new Fake(),CancellationToken.None,null,TimeSpan.Zero),"corrupt payload installed");
        File.WriteAllText(payload,"bad contents!!");Reject(()=>UpdateFiles.Stage(source,cache,helper,plan,CancellationToken.None),"corrupt source payload staged");
        Check(Directory.GetDirectories(cache).Length==1,"failed staging cleaned up");
        File.WriteAllText(latest,new string(' ',32769));Reject(()=>UpdateFiles.ReadJson<UpdateManifest>(latest),"oversized metadata accepted");
    }
    sealed class Fake : IUpdateEnvironment
    {
        internal int Polls,Installs,ExitCode;internal Version Result=new Version(1,1,1,0);
        public bool RevitIsRunning(UpdatePlan p)=>++Polls<2;
        public bool FilesAreAvailable(UpdatePlan p)=>Polls>=3;
        public int Install(UpdatePlan p){Check(Polls>=3,"install attempted while host running or locked");Installs++;return ExitCode;}
        public Version InstalledVersion(UpdatePlan p)=>Result;
    }
}
