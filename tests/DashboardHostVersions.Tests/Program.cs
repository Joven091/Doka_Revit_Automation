using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using EngineeringDashboard.DesktopHost;
using MyRevitAddin.Updates;

internal static class Program
{
    static int checks;
    static void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
    static void Reject(Action action,string text){bool refused=false;try{action();}catch(InvalidDataException){refused=true;}Check(refused,text);}
    static int Main(string[] args)
    {
        var previous=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_REVIT_VERSION");
        string temp=Path.Combine(Path.GetTempPath(),"DokaHostYears-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("DOKA_DASHBOARD_REVIT_VERSION",null);
            Check(DashboardPaths.RevitVersion==2024&&UpdateTarget.Year==2024,"Existing 2024 installations retain their default context");
            string originalRoot=DashboardPaths.Root;
            foreach(int year in new[]{2024,2027})
            {
                Environment.SetEnvironmentVariable("DOKA_DASHBOARD_REVIT_VERSION",year.ToString());
                Check(DashboardPaths.RevitVersion==year&&UpdateTarget.Year==year,"Desktop and integration helpers agree on host "+year);
                Check(Path.GetFileName(DashboardPaths.SettingsRoot)==year.ToString(),"Settings select only Revit "+year);
                Check(year==2024?DashboardPaths.Root==originalRoot:DashboardPaths.Root==Path.Combine(originalRoot,"Revit2027"),"Versioned runtime location is isolated for "+year);
                Check(UpdateSettings.DefaultSource.EndsWith(year==2024?"Updates":@"Updates\Revit2027",StringComparison.Ordinal),"Default company feed routes to "+year);
                string source=Path.Combine(temp,year.ToString()),root=Path.Combine(temp,"runtime"+year);Directory.CreateDirectory(source);
                var manifest=new UpdateManifest{SchemaVersion=1,ProductId=UpdateManifest.Product,RevitVersion=year,Version="1.2.3",InstallerFile="setup.exe",Sha256=new string('a',64),SizeBytes=1};
                manifest.Validate();Check(true,"Installer feed accepts matching Revit "+year);
                manifest.RevitVersion=year==2024?2027:2024;
                Reject(manifest.Validate,"Installer feed rejects the other Revit year before staging "+year);
                // A real archive and production manifests exercise copying, staging and activation.
                string payload=Path.Combine(temp,"payload"+year);Directory.CreateDirectory(payload);
                string[] required={DashboardPackages.EntryFile,"MyRevitAddin.dll","Doka.DashboardUpdater.exe","google-ortools-native.dll","ortools.dll","BeamSolver/Doka.BeamSolver.exe"};
                foreach(string name in required){string path=Path.Combine(payload,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"MZ controlled fixture "+name);}
                var files=required.Select(n=>new DashboardPackageFile{Path=n,Sha256=DashboardPackages.Hash(Path.Combine(payload,n)),SizeBytes=new FileInfo(Path.Combine(payload,n)).Length}).ToArray();
                DashboardPackages.WriteJson(Path.Combine(payload,DashboardPackages.ManifestFile),new DashboardPackageManifest{Version="1.2.3",Files=files});
                string archive=Path.Combine(source,"package.zip");ZipFile.CreateFromDirectory(payload,archive);
                var feed=new DashboardFeed{Version="1.2.3",ArchiveFile="package.zip",Sha256=DashboardPackages.Hash(archive),SizeBytes=new FileInfo(archive).Length};
                DashboardPackages.WriteJson(Path.Combine(source,"dashboard-latest.json"),feed);
                Check(DashboardPackages.ReadFeed(source,new Version(1,2,2,0),1).RevitVersion==year,"Dashboard discovers its own update "+year);
                var stage=DashboardPackages.Stage(source,root,feed,CancellationToken.None);
                var active=DashboardPackages.Activate(root,stage,null);DashboardPackages.ValidateInstalled(root,active.Current);
                Check(DashboardPackages.ReadActive(root).Version=="1.2.3","Matching update stages and activates in isolated runtime "+year);
                feed.RevitVersion=year==2024?2027:2024;
                File.Delete(Path.Combine(source,"dashboard-latest.json"));DashboardPackages.WriteJson(Path.Combine(source,"dashboard-latest.json"),feed);
                Reject(()=>DashboardPackages.ReadFeed(source,new Version(1,2,2,0),1),"Dashboard rejects another host's feed "+year);
                string untouched=Path.Combine(temp,"refused"+year);
                Reject(()=>DashboardPackages.Stage(source,untouched,feed,CancellationToken.None),"Cross-host staging is refused "+year);
                Check(!Directory.Exists(untouched),"Cross-host rejection creates no runtime directory "+year);
            }
            Environment.SetEnvironmentVariable("DOKA_DASHBOARD_REVIT_VERSION","2025");
            Reject(()=>{var value=DashboardPaths.RevitVersion;},"Unsupported Dashboard host context fails closed");
            Reject(()=>{var value=UpdateTarget.Year;},"Unsupported integration helper context fails closed");
            if(args.Length>=2)
            {
                int year=int.Parse(args[1]);
                using(var stream=File.OpenRead(args[0]))using(var pe=new PEReader(stream))
                {
                    var reader=pe.GetMetadataReader();
                    var refs=reader.AssemblyReferences.Select(h=>reader.GetAssemblyReference(h)).ToArray();
                    Check(refs.Any(a=>reader.GetString(a.Name)=="RevitAPI"&&a.Version.Major==year-2000),"Packaged bridge references the requested RevitAPI major "+year);
                    Check(refs.Any(a=>reader.GetString(a.Name)==(year==2024?"mscorlib":"System.Runtime")&&a.Version.Major==(year==2024?4:10)),"Packaged bridge has the correct CLR contract "+year);
                    Check(!refs.Any(a=>reader.GetString(a.Name)=="MyRevitAddin"||reader.GetString(a.Name)=="Google.OrTools"),"Revit integration does not load the calculator or native solver");
                }
                if(args.Length==3)
                {
                    Environment.SetEnvironmentVariable("DOKA_DASHBOARD_REVIT_VERSION",year.ToString());
                    var dashboard=DashboardPackages.ReadFeed(args[2],new Version(0,0,0,0),1);
                    var integration=UpdateFiles.Check(args[2],new Version(0,0,0,0));
                    Check(dashboard.RevitVersion==year&&integration.RevitVersion==year,"Both published indexes target the requested Revit year");
                    Check(DashboardPackages.ParseVersion(dashboard.Version)==UpdateFiles.ParseVersion(integration.Version),"Installer and live update advertise the same version");
                    Check(new FileInfo(Path.Combine(args[2],dashboard.ArchiveFile)).Length==dashboard.SizeBytes&&DashboardPackages.Hash(Path.Combine(args[2],dashboard.ArchiveFile))==dashboard.Sha256,"Published Dashboard ZIP matches its index size and SHA-256");
                    UpdateFiles.VerifyInstaller(new UpdatePlan{Release=integration,InstallerPath=Path.Combine(args[2],integration.InstallerFile)});
                    Check(true,"Published integration installer matches its index size and SHA-256");
                }
            }
            Console.WriteLine("PASS "+checks+" Revit host/version isolation checks.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{Environment.SetEnvironmentVariable("DOKA_DASHBOARD_REVIT_VERSION",previous);if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }
}
