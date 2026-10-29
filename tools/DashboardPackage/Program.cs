using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using EngineeringDashboard.DesktopHost;

internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==4&&args[0]=="pack"){Pack(args[1],args[2],args[3]);return 0;}
            if(args.Length==4&&args[0]=="bootstrap")
            {
                string source=Path.GetFullPath(args[1]),root=Path.GetFullPath(args[2]);
                var feed=DashboardPackages.ReadFeed(source,new Version(0,0,0,0),BridgeProtocol.Version)??throw new InvalidDataException("No Dashboard package was supplied.");
                var staged=DashboardPackages.Stage(source,root,feed,CancellationToken.None);
                var previous=DashboardPackages.ReadActive(root);var activation=DashboardPackages.Activate(root,staged,previous?.Generation);
                try{DashboardPackages.WriteJson(args[3],activation);}catch{DashboardPackages.Rollback(root,activation);throw;}
                Console.WriteLine("Verified and activated Dashboard "+staged.Version);return 0;
            }
            if(args.Length==3&&args[0]=="rollback")
            {var activation=DashboardPackages.ReadJson<ActivationResult>(args[2],65536);if(!DashboardPackages.Rollback(args[1],activation))throw new InvalidOperationException("The active Dashboard changed; rollback was not applied.");return 0;}
            Console.Error.WriteLine("Usage: Doka.DashboardPackage pack <payload> <new-output> <version> | bootstrap <feed> <root> <new-receipt> | rollback <root> <receipt>");return 2;
        }
        catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
    static void Pack(string payload,string output,string version)
    {
        version=DashboardPackages.ParseVersion(version).ToString(3);payload=Path.GetFullPath(payload);output=Path.GetFullPath(output);
        if(!Directory.Exists(payload))throw new DirectoryNotFoundException("Build the Dashboard payload first.");
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Use a new output directory; published packages are immutable.");
        if(File.Exists(Path.Combine(payload,DashboardPackages.ManifestFile)))throw new IOException("The payload already has a package manifest. Build into a clean directory.");
        var files=Directory.GetFiles(payload,"*",SearchOption.AllDirectories).OrderBy(f=>f,StringComparer.Ordinal).Select(f=>new DashboardPackageFile{Path=DashboardPackages.SafeRelativePath(f.Substring(payload.Length+1).Replace(Path.DirectorySeparatorChar,'/')),SizeBytes=new FileInfo(f).Length,Sha256=DashboardPackages.Hash(f)}).ToArray();
        DashboardPackages.WriteJson(Path.Combine(payload,DashboardPackages.ManifestFile),new DashboardPackageManifest{Version=version,Files=files});
        Directory.CreateDirectory(output);string archive=Path.Combine(output,"DokaDashboard-"+version+".zip");
        ZipFile.CreateFromDirectory(payload,archive,CompressionLevel.Optimal,false);
        var feed=new DashboardFeed{Version=version,ArchiveFile=Path.GetFileName(archive),Sha256=DashboardPackages.Hash(archive),SizeBytes=new FileInfo(archive).Length,ReleaseNotes="Dashboard update. Revit stays open; the Dashboard reopens with working inputs restored."};
        DashboardPackages.WriteJson(Path.Combine(output,"dashboard-latest.json"),feed);
        string check=Path.Combine(output,".verification-"+Guid.NewGuid().ToString("N"));
        try{var staged=DashboardPackages.Stage(output,check,feed,CancellationToken.None);DashboardPackages.ValidateInstalled(check,staged);}
        finally{if(Directory.Exists(check))Directory.Delete(check,true);}
        Console.WriteLine("Verified Dashboard ZIP: "+archive+" ("+feed.SizeBytes+" bytes, SHA-256 "+feed.Sha256+")");
    }
}
