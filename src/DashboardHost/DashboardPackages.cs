using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EngineeringDashboard.DesktopHost
{
    [DataContract] public sealed class DashboardFeed
    {
        [DataMember(Name="schemaVersion",IsRequired=true)] public int SchemaVersion=1;
        [DataMember(Name="productId",IsRequired=true)] public string ProductId=DashboardPackages.Product;
        [DataMember(Name="revitVersion",IsRequired=true)] public int RevitVersion=2024;
        [DataMember(Name="version",IsRequired=true)] public string Version;
        [DataMember(Name="bridgeProtocol",IsRequired=true)] public int BridgeProtocol=1;
        [DataMember(Name="archiveFile",IsRequired=true)] public string ArchiveFile;
        [DataMember(Name="sha256",IsRequired=true)] public string Sha256;
        [DataMember(Name="sizeBytes",IsRequired=true)] public long SizeBytes;
        [DataMember(Name="releaseNotes")] public string ReleaseNotes;
    }
    [DataContract] public sealed class DashboardPackageFile
    {
        [DataMember(Name="path",IsRequired=true)] public string Path;
        [DataMember(Name="sha256",IsRequired=true)] public string Sha256;
        [DataMember(Name="sizeBytes",IsRequired=true)] public long SizeBytes;
    }
    [DataContract] public sealed class DashboardPackageManifest
    {
        [DataMember(Name="schemaVersion",IsRequired=true)] public int SchemaVersion=1;
        [DataMember(Name="productId",IsRequired=true)] public string ProductId=DashboardPackages.Product;
        [DataMember(Name="version",IsRequired=true)] public string Version;
        [DataMember(Name="bridgeProtocol",IsRequired=true)] public int BridgeProtocol=1;
        [DataMember(Name="files",IsRequired=true)] public DashboardPackageFile[] Files;
    }
    public class StagedDashboard
    {
        public string Version {get;set;} public string PackageId {get;set;} public string RelativeDirectory {get;set;}
        public string ArchiveSha256 {get;set;} public int BridgeProtocol {get;set;}
    }
    public sealed class ActiveDashboard : StagedDashboard { public string Generation {get;set;} }
    public sealed class ActivationResult { public ActiveDashboard Current {get;set;} public ActiveDashboard Previous {get;set;} public bool Changed {get;set;} }
    public static class DashboardPaths
    {
        public static string Root
        {
            get {string configured=Environment.GetEnvironmentVariable("DOKA_DASHBOARD_ROOT");return Path.GetFullPath(string.IsNullOrEmpty(configured)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DokaRevitAutomation","Dashboard"):configured);}
        }
        public static string CheckpointsDirectory=>Path.Combine(Root,"Sessions");
        public static string SettingsRoot=>Environment.GetEnvironmentVariable("DOKA_DASHBOARD_SETTINGS_ROOT")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins","2024");
    }
    public static class DashboardPackages
    {
        public const string Product="Doka.EngineeringDashboard.Desktop",EntryFile="Doka.EngineeringDashboard.exe",ManifestFile="dashboard-package.json";
        public const long MaxArchiveBytes=512L*1024*1024,MaxExpandedBytes=2L*1024*1024*1024;
        const int MaxFiles=10000;
        static readonly string[] RequiredFiles={EntryFile,"MyRevitAddin.dll","Doka.DashboardUpdater.exe","google-ortools-native.dll","ortools.dll","BeamSolver/Doka.BeamSolver.exe"};
        public static Version ParseVersion(string value)
        {Version v;if(value==null||!Regex.IsMatch(value,@"\A\d+\.\d+\.\d+(?:\.\d+)?\z")||!System.Version.TryParse(value,out v))throw new InvalidDataException("Invalid Dashboard version.");return new Version(v.Major,v.Minor,v.Build,Math.Max(0,v.Revision));}
        public static string Hash(string path)
        {using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
        static bool IsHash(string value)=>value!=null&&Regex.IsMatch(value,@"\A[0-9a-f]{64}\z");
        public static DashboardFeed ReadFeed(string sourceDirectory,Version installed,int bridgeProtocol)
        {
            var feed=ReadJson<DashboardFeed>(Path.Combine(sourceDirectory,"dashboard-latest.json"),32768);
            if(feed==null||feed.SchemaVersion!=1||feed.ProductId!=Product||feed.RevitVersion!=2024||!IsHash(feed.Sha256)||feed.SizeBytes<=0||feed.SizeBytes>MaxArchiveBytes
                ||feed.ArchiveFile==null||!Regex.IsMatch(feed.ArchiveFile,@"\A[A-Za-z0-9][A-Za-z0-9._-]*\.zip\z")||(feed.ReleaseNotes??"").Length>4000)throw new InvalidDataException("Invalid Dashboard update metadata.");
            var version=ParseVersion(feed.Version);if(version<=installed)return null;
            if(feed.BridgeProtocol!=bridgeProtocol)throw new InvalidDataException("This release needs a newer Revit integration module. Update that module and restart Revit before using this Dashboard release.");
            return feed;
        }
        public static StagedDashboard Stage(string sourceDirectory,string root,DashboardFeed feed,CancellationToken token)
        {
            if(feed==null||feed.SchemaVersion!=1||feed.ProductId!=Product||feed.RevitVersion!=2024||feed.BridgeProtocol!=BridgeProtocol.Version||!IsHash(feed.Sha256)||feed.SizeBytes<=0||feed.SizeBytes>MaxArchiveBytes
                ||feed.ArchiveFile==null||!Regex.IsMatch(feed.ArchiveFile,@"\A[A-Za-z0-9][A-Za-z0-9._-]*\.zip\z"))throw new InvalidDataException("Invalid Dashboard package request.");
            ParseVersion(feed.Version);root=Path.GetFullPath(root);Directory.CreateDirectory(root);NoReparse(root);
            string staging=Path.Combine(root,"staging",Guid.NewGuid().ToString("N")),payload=Path.Combine(staging,"payload"),archive=Path.Combine(staging,"package.zip");
            Directory.CreateDirectory(Path.Combine(root,"staging"));NoReparse(Path.Combine(root,"staging"));Directory.CreateDirectory(payload);
            try
            {
                token.ThrowIfCancellationRequested();
                using(var input=new FileStream(Path.Combine(sourceDirectory,feed.ArchiveFile),FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    if(input.Length!=feed.SizeBytes)throw new InvalidDataException("The Dashboard package size differs from its publication metadata.");
                    byte[] buffer=new byte[65536];int n;long total=0;
                    while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();total+=n;if(total>feed.SizeBytes)throw new InvalidDataException("The Dashboard package changed during copying.");output.Write(buffer,0,n);}
                }
                if(Hash(archive)!=feed.Sha256)throw new InvalidDataException("The Dashboard package checksum does not match.");
                using(var zip=ZipFile.OpenRead(archive))
                {
                    if(zip.Entries.Count==0||zip.Entries.Count>MaxFiles+1)throw new InvalidDataException("Invalid Dashboard archive entry count.");
                    var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0;
                    foreach(var entry in zip.Entries)
                    {
                        var name=SafeRelativePath(entry.FullName.TrimEnd('/'));
                        if(!names.Add(name)||((entry.ExternalAttributes>>16)&0xf000)==0xa000||(entry.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Duplicate or linked Dashboard archive entry.");
                        expanded=checked(expanded+entry.Length);if(expanded>MaxExpandedBytes)throw new InvalidDataException("The expanded Dashboard package is too large.");
                    }
                    var meta=zip.Entries.SingleOrDefault(e=>e.FullName==ManifestFile);
                    if(meta==null||meta.Length<=0||meta.Length>1024*1024)throw new InvalidDataException("Dashboard payload manifest is missing or too large.");
                    DashboardPackageManifest manifest;using(var s=meta.Open()){manifest=ReadJson<DashboardPackageManifest>(s,1024*1024);}
                    ValidateManifest(manifest,feed.Version,feed.BridgeProtocol);
                    var files=manifest.Files.ToDictionary(f=>SafeRelativePath(f.Path),StringComparer.OrdinalIgnoreCase);
                    var archiveFiles=zip.Entries.Where(e=>!e.FullName.EndsWith("/",StringComparison.Ordinal)&&e.FullName!=ManifestFile).ToArray();
                    if(archiveFiles.Length!=files.Count||archiveFiles.Any(e=>!files.ContainsKey(SafeRelativePath(e.FullName))))throw new InvalidDataException("Dashboard archive contents differ from its file manifest.");
                    foreach(var entry in archiveFiles)
                    {
                        token.ThrowIfCancellationRequested();var expected=files[SafeRelativePath(entry.FullName)];if(entry.Length!=expected.SizeBytes)throw new InvalidDataException("Dashboard payload file size mismatch.");
                        string path=InsidePath(payload,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using(var input=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        {byte[] buffer=new byte[65536];int n;long written=0;while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();written+=n;if(written>expected.SizeBytes)throw new InvalidDataException("Expanded Dashboard file exceeded its declared size.");output.Write(buffer,0,n);}if(written!=expected.SizeBytes)throw new InvalidDataException("Dashboard file was truncated.");}
                        if(Hash(path)!=expected.Sha256)throw new InvalidDataException("Dashboard payload file checksum mismatch.");
                    }
                    WriteJson(Path.Combine(payload,ManifestFile),manifest);
                }
                token.ThrowIfCancellationRequested();
                string id=PackageId(feed.Version,feed.Sha256),relative="versions/"+id,final=InsidePath(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(final));NoReparse(Path.GetDirectoryName(final));
                using(Lock(root))
                {
                    if(Directory.Exists(final)){ValidateInstalled(root,new StagedDashboard{Version=feed.Version,PackageId=id,RelativeDirectory=relative,ArchiveSha256=feed.Sha256,BridgeProtocol=feed.BridgeProtocol});}
                    else Directory.Move(payload,final);
                }
                return new StagedDashboard{Version=feed.Version,PackageId=id,RelativeDirectory=relative,ArchiveSha256=feed.Sha256,BridgeProtocol=feed.BridgeProtocol};
            }
            finally{try{if(Directory.Exists(staging))Directory.Delete(staging,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        public static string PackageId(string version,string hash)
        {var v=ParseVersion(version);if(!IsHash(hash))throw new InvalidDataException("Invalid package identity.");return (v.Revision==0?v.ToString(3):v.ToString(4))+"-"+hash.Substring(0,12);}
        public static ActiveDashboard ReadActive(string root)
        {
            string path=Path.Combine(root,"active.json");if(!File.Exists(path))return null;
            NoReparse(path);
            var active=ReadJson<ActiveDashboard>(path,32768);Guid generation;
            if(active==null||!Guid.TryParseExact(active.Generation,"N",out generation))throw new InvalidDataException("Invalid active Dashboard pointer.");
            ValidateIdentity(active);return active;
        }
        public static ActivationResult Activate(string root,StagedDashboard staged,string expectedGeneration)
        {
            Directory.CreateDirectory(root);using(Lock(root))
            {
                var prior=ReadActive(root);if((prior?.Generation??"")!=(expectedGeneration??""))throw new InvalidOperationException("Another Dashboard update changed the active version. Check again.");
                ValidateInstalled(root,staged);
                if(prior!=null&&ParseVersion(prior.Version)>ParseVersion(staged.Version))throw new InvalidOperationException("A newer Dashboard is already active.");
                if(prior!=null&&ParseVersion(prior.Version)==ParseVersion(staged.Version))
                {if(prior.PackageId!=staged.PackageId)throw new InvalidDataException("The same Dashboard version has different contents.");return new ActivationResult{Current=prior,Previous=prior,Changed=false};}
                var current=new ActiveDashboard{Version=staged.Version,PackageId=staged.PackageId,RelativeDirectory=staged.RelativeDirectory,ArchiveSha256=staged.ArchiveSha256,BridgeProtocol=staged.BridgeProtocol,Generation=Guid.NewGuid().ToString("N")};
                AtomicPointer(root,current);return new ActivationResult{Previous=prior,Current=current,Changed=true};
            }
        }
        public static bool Rollback(string root,ActivationResult activation)
        {
            if(activation==null||!activation.Changed)return false;
            using(Lock(root))
            {
                var active=ReadActive(root);if(active?.Generation!=activation.Current.Generation)return false;
                if(activation.Previous==null){File.Delete(Path.Combine(root,"active.json"));return true;}
                ValidateInstalled(root,activation.Previous);var previous=activation.Previous;
                AtomicPointer(root,new ActiveDashboard{Version=previous.Version,PackageId=previous.PackageId,RelativeDirectory=previous.RelativeDirectory,ArchiveSha256=previous.ArchiveSha256,BridgeProtocol=previous.BridgeProtocol,Generation=Guid.NewGuid().ToString("N")});return true;
            }
        }
        public static string EntryPath(string root,ActiveDashboard active)
        {ValidateIdentity(active);return InsidePath(root,active.RelativeDirectory+"/"+EntryFile);}
        public static void ValidateInstalled(string root,StagedDashboard staged)
        {
            ValidateIdentity(staged);NoReparse(root);string folder=InsidePath(root,staged.RelativeDirectory);NoReparse(Path.GetDirectoryName(folder));NoReparse(folder);
            var manifest=ReadJson<DashboardPackageManifest>(Path.Combine(folder,ManifestFile),1024*1024);ValidateManifest(manifest,staged.Version,staged.BridgeProtocol);
            var expectedNames=new HashSet<string>(manifest.Files.Select(f=>SafeRelativePath(f.Path)),StringComparer.OrdinalIgnoreCase){ManifestFile};
            var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var pending=new Queue<string>();pending.Enqueue(folder);
            while(pending.Count>0)
            {
                string directory=pending.Dequeue();NoReparse(directory);
                foreach(string child in Directory.GetDirectories(directory))pending.Enqueue(child);
                foreach(string file in Directory.GetFiles(directory)){NoReparse(file);string relative=file.Substring(folder.Length+1).Replace(Path.DirectorySeparatorChar,'/');if(!found.Add(relative)||!expectedNames.Contains(relative)||found.Count>MaxFiles+1)throw new InvalidDataException("Unexpected Dashboard payload file.");}
            }
            if(!found.SetEquals(expectedNames))throw new InvalidDataException("The Dashboard payload is incomplete.");
            foreach(var file in manifest.Files){string path=InsidePath(folder,file.Path);NoReparseChain(folder,path);var info=new FileInfo(path);if(!info.Exists||info.Length!=file.SizeBytes||Hash(path)!=file.Sha256)throw new InvalidDataException("The installed Dashboard payload has changed or is incomplete.");}
        }
        static void ValidateIdentity(StagedDashboard value)
        {if(value==null||value.BridgeProtocol!=BridgeProtocol.Version||value.PackageId!=PackageId(value.Version,value.ArchiveSha256)||value.RelativeDirectory!="versions/"+value.PackageId)throw new InvalidDataException("Invalid Dashboard version-directory identity.");}
        static void ValidateManifest(DashboardPackageManifest m,string version,int protocol)
        {
            if(m==null||m.SchemaVersion!=1||m.ProductId!=Product||ParseVersion(m.Version)!=ParseVersion(version)||m.BridgeProtocol!=protocol||m.Files==null||m.Files.Length==0||m.Files.Length>MaxFiles)throw new InvalidDataException("Incompatible Dashboard payload manifest.");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
            foreach(var f in m.Files){if(f==null||!names.Add(SafeRelativePath(f.Path))||f.Path==ManifestFile||f.SizeBytes<0||!IsHash(f.Sha256))throw new InvalidDataException("Invalid Dashboard payload file manifest.");total=checked(total+f.SizeBytes);}
            if(total>MaxExpandedBytes||RequiredFiles.Any(f=>!names.Contains(f)||m.Files.Single(p=>string.Equals(SafeRelativePath(p.Path),f,StringComparison.OrdinalIgnoreCase)).SizeBytes<=0)||names.Any(f=>f.EndsWith(".addin",StringComparison.OrdinalIgnoreCase)||f.EndsWith("Doka.RevitBridge.dll",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("The Dashboard package is incomplete or contains Revit integration files.");
        }
        public static string SafeRelativePath(string value)
        {
            if(string.IsNullOrEmpty(value)||value.Length>240||value.IndexOf('\\')>=0||value.StartsWith("/",StringComparison.Ordinal)||value.IndexOf(':')>=0||value.Any(c=>c<32))throw new InvalidDataException("Unsafe Dashboard archive path.");
            foreach(var part in value.Split('/'))
            {if(part.Length==0||part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||part.IndexOfAny(new[]{'<','>','"','|','?','*'})>=0||Regex.IsMatch(part,@"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)",RegexOptions.IgnoreCase))throw new InvalidDataException("Unsafe Dashboard archive path.");}
            return value;
        }
        static string InsidePath(string root,string relative)
        {SafeRelativePath(relative);string path=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar))),prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Dashboard path escapes its installation.");return path;}
        static void NoReparse(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Dashboard installation paths cannot be links.");}
        static void NoReparseChain(string root,string path){NoReparse(path);for(string p=Path.GetDirectoryName(path);p!=null&&p.Length>=Path.GetFullPath(root).Length;p=Path.GetDirectoryName(p))NoReparse(p);}
        static FileStream Lock(string root){Directory.CreateDirectory(root);NoReparse(root);return new FileStream(Path.Combine(root,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        static void AtomicPointer(string root,ActiveDashboard value)
        {string temp=Path.Combine(root,".active-"+Guid.NewGuid().ToString("N")),target=Path.Combine(root,"active.json");try{WriteJson(temp,value);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}}
        public static T ReadJson<T>(string path,int limit){using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))return ReadJson<T>(s,limit);}
        static T ReadJson<T>(Stream input,int limit)
        {using(var s=new MemoryStream()){byte[] b=new byte[4096];int n;while((n=input.Read(b,0,b.Length))>0){if(s.Length+n>limit)throw new InvalidDataException("Dashboard metadata exceeds its size limit.");s.Write(b,0,n);}return BridgeProtocol.Decode<T>(s.ToArray());}}
        public static void WriteJson<T>(string path,T value)
        {
            // Existence is the cross-process readiness signal. Publish only after
            // the complete JSON has been flushed and its exclusive writer closed.
            string temporary=path+".writing-"+Guid.NewGuid().ToString("N");
            try
            {
                byte[] bytes=BridgeProtocol.Encode(value);
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                File.Move(temporary,path); // Create-new semantics: never replace an existing receipt.
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
