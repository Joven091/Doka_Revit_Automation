using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace MyRevitAddin.Updates
{
    [DataContract]
    internal sealed class UpdateSettings
    {
        internal const string DefaultSource = @"\\DOKAAREDUBA\ApplDat\MEA\ENG\Joven\BeamCalculator\Updates";
        [DataMember(Name="schemaVersion", IsRequired=true)] public int SchemaVersion {get;set;}
        [DataMember(Name="sourceDirectory", IsRequired=true)] public string SourceDirectory {get;set;}
        internal static UpdateSettings Load(string installation)
        {
            string path=Path.Combine(installation,"update-settings.json");
            var settings=File.Exists(path) ? UpdateFiles.ReadJson<UpdateSettings>(path) : new UpdateSettings {SchemaVersion=1,SourceDirectory=DefaultSource};
            if(settings==null || settings.SchemaVersion!=1 || string.IsNullOrWhiteSpace(settings.SourceDirectory) || !Path.IsPathRooted(settings.SourceDirectory))
                throw new InvalidDataException("The company update folder setting is invalid.");
            return settings;
        }
    }

    [DataContract]
    internal sealed class UpdateManifest
    {
        internal const string Product="Doka.RevitAutomation";
        [DataMember(Name="schemaVersion", IsRequired=true)] public int SchemaVersion {get;set;}
        [DataMember(Name="productId", IsRequired=true)] public string ProductId {get;set;}
        [DataMember(Name="revitVersion", IsRequired=true)] public int RevitVersion {get;set;}
        [DataMember(Name="version", IsRequired=true)] public string Version {get;set;}
        [DataMember(Name="installerFile", IsRequired=true)] public string InstallerFile {get;set;}
        [DataMember(Name="sha256", IsRequired=true)] public string Sha256 {get;set;}
        [DataMember(Name="sizeBytes", IsRequired=true)] public long SizeBytes {get;set;}
        [DataMember(Name="releaseNotes")] public string ReleaseNotes {get;set;}
        [DataMember(Name="publishedUtc")] public string PublishedUtc {get;set;}
        internal void Validate()
        {
            UpdateFiles.ParseVersion(Version);
            if(SchemaVersion!=1 || ProductId!=Product || RevitVersion!=2024)
                throw new InvalidDataException("This release is not a compatible Revit 2024 add-in update.");
            if(string.IsNullOrWhiteSpace(InstallerFile) || !Regex.IsMatch(InstallerFile,@"\A[A-Za-z0-9][A-Za-z0-9._-]*\.exe\z",RegexOptions.IgnoreCase))
                throw new InvalidDataException("The update installer name is invalid.");
            if(Sha256==null || !Regex.IsMatch(Sha256,@"\A[0-9a-fA-F]{64}\z") || SizeBytes<=0 || SizeBytes>512L*1024*1024)
                throw new InvalidDataException("The update size or checksum is invalid.");
            if((ReleaseNotes??"").Length>4000)throw new InvalidDataException("The release notes are too long.");
        }
    }

    [DataContract]
    internal sealed class UpdatePlan
    {
        [DataMember(IsRequired=true)] public UpdateManifest Release {get;set;}
        [DataMember(IsRequired=true)] public string InstallerPath {get;set;}
        [DataMember(IsRequired=true)] public string InstallDirectory {get;set;}
        [DataMember(IsRequired=true)] public int HostProcessId {get;set;}
        [DataMember(IsRequired=true)] public long HostStartedUtcTicks {get;set;}
        [DataMember(EmitDefaultValue=false)] public string InstalledAssemblyFile {get;set;}
    }

    internal static class UpdateFiles
    {
        internal static Version ParseVersion(string text)
        {
            Version v;
            if(text==null || !Regex.IsMatch(text,@"\A\d+\.\d+\.\d+(?:\.\d+)?\z") || !System.Version.TryParse(text,out v))
                throw new InvalidDataException("The update version is invalid.");
            return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
        }
        internal static string DisplayVersion(Version v)=>v.Revision==0 ? v.ToString(3) : v.ToString(4);
        internal static T ReadJson<T>(string path)
        {
            // A publisher replaces the manifest atomically; readers permit that rename.
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                if(stream.Length>32768)throw new InvalidDataException("The update metadata exceeds its size limit.");
                using(var bytes=new MemoryStream())
                {
                    var buffer=new byte[4096];int count;
                    while((count=stream.Read(buffer,0,buffer.Length))>0)
                    {if(bytes.Length+count>32768)throw new InvalidDataException("The update metadata exceeds its size limit.");bytes.Write(buffer,0,count);}
                    bytes.Position=0;return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(bytes);
                }
            }
        }
        internal static void WriteJson<T>(string path,T value)
        {using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);}
        internal static string Hash(string path)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        internal static bool SamePath(string a,string b)=>string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
        internal static string InstalledAssembly(UpdatePlan plan)
        {
            string name=string.IsNullOrEmpty(plan.InstalledAssemblyFile)?"MyRevitAddin.dll":plan.InstalledAssemblyFile;
            if(name!="MyRevitAddin.dll"&&name!="Doka.RevitBridge.dll")throw new InvalidDataException("Invalid installed add-in assembly.");
            return Path.Combine(plan.InstallDirectory,name);
        }
        internal static bool Inside(string path,string folder)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        internal static UpdateManifest Check(string source,Version installed)
        {
            var manifest=ReadJson<UpdateManifest>(Path.Combine(source,"latest.json"));
            if(manifest==null)throw new InvalidDataException("No release information was found.");
            manifest.Validate();return ParseVersion(manifest.Version)>installed ? manifest : null;
        }
        internal static void VerifyInstaller(UpdatePlan plan)
        {
            var info=new FileInfo(plan.InstallerPath);
            if(!info.Exists || info.Length!=plan.Release.SizeBytes || !string.Equals(Hash(plan.InstallerPath),plan.Release.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer is incomplete or its checksum does not match. Check for updates again.");
        }
        internal static string Stage(string source,string cacheRoot,string helper,UpdatePlan plan,CancellationToken token,Action<long,long> progress=null)
        {
            plan.Release.Validate();token.ThrowIfCancellationRequested();
            string stage=Path.Combine(cacheRoot,DisplayVersion(ParseVersion(plan.Release.Version))+"-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                plan.InstallerPath=Path.Combine(stage,plan.Release.InstallerFile);
                using(var input=new FileStream(Path.Combine(source,plan.Release.InstallerFile),FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var output=new FileStream(plan.InstallerPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    if(input.Length!=plan.Release.SizeBytes)throw new InvalidDataException("The published installer size does not match its release information.");
                    var buffer=new byte[65536];int count;long total=0;
                    while((count=input.Read(buffer,0,buffer.Length))>0)
                    {
                        token.ThrowIfCancellationRequested();total+=count;
                        if(total>plan.Release.SizeBytes)throw new InvalidDataException("The installer changed while it was being copied.");
                        output.Write(buffer,0,count);progress?.Invoke(total,plan.Release.SizeBytes);
                    }
                }
                token.ThrowIfCancellationRequested();VerifyInstaller(plan);
                File.Copy(helper,Path.Combine(stage,"Doka.RevitUpdater.exe"));
                token.ThrowIfCancellationRequested();WriteJson(Path.Combine(stage,"update-plan.json"),plan);return stage;
            }
            catch
            {
                try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}
                throw;
            }
        }
        internal static UpdatePlan ReadPlan(string path,string cacheRoot,string expectedInstallDirectory)
        {
            if(!Inside(path,cacheRoot))throw new InvalidDataException("The update plan must be in the user's local update cache.");
            var plan=ReadJson<UpdatePlan>(path);
            if(plan?.Release==null)throw new InvalidDataException("The staged update is invalid.");
            plan.Release.Validate();
            InstalledAssembly(plan);
            if(!SamePath(plan.InstallDirectory,expectedInstallDirectory) || !SamePath(Path.GetDirectoryName(plan.InstallerPath),Path.GetDirectoryName(path))
                || Path.GetFileName(plan.InstallerPath)!=plan.Release.InstallerFile || plan.HostProcessId<=0 || plan.HostStartedUtcTicks<=0)
                throw new InvalidDataException("The update plan does not match this user's Revit add-in installation.");
            VerifyInstaller(plan);return plan;
        }
    }

    internal interface IUpdateEnvironment
    {
        bool RevitIsRunning(UpdatePlan plan);
        bool FilesAreAvailable(UpdatePlan plan);
        int Install(UpdatePlan plan);
        Version InstalledVersion(UpdatePlan plan);
    }
    internal static class DeferredUpdate
    {
        internal static void Apply(UpdatePlan plan,IUpdateEnvironment environment,CancellationToken token,Action<string> status,TimeSpan? poll=null)
        {
            while(true)
            {
                token.ThrowIfCancellationRequested();
                if(!environment.RevitIsRunning(plan) && environment.FilesAreAvailable(plan))break;
                status?.Invoke("Update ready. Close all your Revit windows when convenient. Your models will not be closed automatically.");
                if(token.WaitHandle.WaitOne(poll??TimeSpan.FromSeconds(2)))token.ThrowIfCancellationRequested();
            }
            token.ThrowIfCancellationRequested();UpdateFiles.VerifyInstaller(plan);
            status?.Invoke("Installing the update…");
            int exit=environment.Install(plan);
            if(exit!=0)throw new IOException("The installer did not complete (exit code "+exit+"). See install.log in the local update cache.");
            if(environment.InstalledVersion(plan)!=UpdateFiles.ParseVersion(plan.Release.Version))
                throw new IOException("The installed version could not be verified. See install.log before trying again.");
            status?.Invoke("Update installed. Reopen Revit to use version "+plan.Release.Version+".");
        }
    }
}
