using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EngineeringDashboard.DesktopHost;

internal static class PackageChecks
{
    static readonly string[] Required={IndependentFixtures.Entry,"MyRevitAddin.dll","Doka.DashboardUpdater.exe","google-ortools-native.dll","ortools.dll","BeamSolver/Doka.BeamSolver.exe"};
    sealed class Fixture
    {
        internal string Source,Root,Archive,Version;
        internal DashboardFeed Feed;
        internal KeyValuePair<string,byte[]>[] Payload;
    }
    internal static void Run(string root,Action<bool,string> check,Action<Action,string> reject)
    {
        AtomicJsonPublication(root,check,reject);
        foreach(string version in new[]{"1.2.0","1.2.0.0","1.2.0.4"})check(DashboardPackages.ParseVersion(version)==new Version(version.Count(c=>c=='.')==2?version+".0":version),"numeric Dashboard version normalized: "+version);
        foreach(string version in new[]{"1.2","1.2.0-beta"," 1.2.0","1.2.0 ","-1.2.0","1.2.0.0.0"})reject(()=>DashboardPackages.ParseVersion(version),"invalid version rejected: "+version);
        var fixture=Make(root,"valid");
        check(DashboardPackages.ReadFeed(fixture.Source,new Version(1,1,27,0),1).Version==fixture.Version,"new desktop release discovered from its independent feed");
        check(DashboardPackages.ReadFeed(fixture.Source,new Version(1,2,0,0),1)==null,"same Dashboard version ignored");
        check(DashboardPackages.ReadFeed(fixture.Source,new Version(9,0,0,0),1)==null,"older Dashboard release ignored");
        check(DashboardPackages.ReadFeed(fixture.Source,new Version(1,2,0,0),999)==null,"same-version feed does not require a pointless bridge update");
        var desktopIndex=File.ReadAllBytes(Path.Combine(fixture.Source,"dashboard-latest.json"));
        File.WriteAllText(Path.Combine(fixture.Source,"latest.json"),"legacy Revit EXE feed must not be read");
        check(DashboardPackages.ReadFeed(fixture.Source,new Version(1,1,27,0),1).ArchiveFile==Path.GetFileName(fixture.Archive),"legacy Revit EXE index cannot shadow the separate desktop index");
        File.Delete(Path.Combine(fixture.Source,"dashboard-latest.json"));
        reject(()=>DashboardPackages.ReadFeed(fixture.Source,new Version(1,0,0,0),1),"missing desktop index does not fall back to the legacy EXE feed");
        File.WriteAllBytes(Path.Combine(fixture.Source,"dashboard-latest.json"),desktopIndex);
        var original=fixture.Feed;
        Action<string> FeedReject=text=>{File.WriteAllText(Path.Combine(fixture.Source,"dashboard-latest.json"),text);reject(()=>DashboardPackages.ReadFeed(fixture.Source,new Version(1,0,0,0),1),"invalid desktop feed: "+text.Substring(0,Math.Min(90,text.Length)));};
        FeedReject(IndependentFixtures.Feed("1.2.0","package.zip",original.Sha256,original.SizeBytes,product:"Doka.RevitAutomation"));
        FeedReject(IndependentFixtures.Feed("1.2.0","package.zip",original.Sha256,original.SizeBytes,schema:2));
        FeedReject(IndependentFixtures.Feed("1.2.0","package.zip",original.Sha256,original.SizeBytes,revit:2025));
        FeedReject(IndependentFixtures.Feed("1.2.0","package.zip",original.Sha256,original.SizeBytes,protocol:2));
        FeedReject(IndependentFixtures.Feed("1.2.0","package.zip",original.Sha256.ToUpperInvariant(),original.SizeBytes));
        foreach(string name in new[]{"../package.zip","sub/package.zip","sub\\package.zip","C:package.zip","package.exe","package.zip --flag"})FeedReject(IndependentFixtures.Feed("1.2.0",name,original.Sha256,original.SizeBytes));
        foreach(long length in new[]{0L,-1,DashboardPackages.MaxArchiveBytes+1})FeedReject(IndependentFixtures.Feed("1.2.0","package.zip",original.Sha256,length));
        FeedReject("{}");FeedReject(new string(' ',32769));
        FeedReject(IndependentFixtures.Utf8.GetString(desktopIndex)+"{}");
        File.WriteAllBytes(Path.Combine(fixture.Source,"dashboard-latest.json"),desktopIndex);

        var staged=DashboardPackages.Stage(fixture.Source,fixture.Root,fixture.Feed,CancellationToken.None);
        check(DashboardPackages.ReadActive(fixture.Root)==null,"staging does not activate the new Dashboard");
        string expectedId="1.2.0-"+IndependentFixtures.HashFile(fixture.Archive).Substring(0,12);
        check(staged.PackageId==expectedId&&staged.RelativeDirectory=="versions/"+expectedId&&staged.ArchiveSha256==IndependentFixtures.HashFile(fixture.Archive),"version directory identity derives independently from version and full archive SHA");
        AssertPayload(fixture,staged,check);
        check(!Directory.GetDirectories(Path.Combine(fixture.Root,"staging")).Any(),"successful staging removes temporary copy/extraction directories");
        var before=IndependentFixtures.Snapshot(fixture.Root);
        var reused=DashboardPackages.Stage(fixture.Source,fixture.Root,fixture.Feed,CancellationToken.None);
        check(reused.PackageId==staged.PackageId&&IndependentFixtures.SameSnapshot(before,IndependentFixtures.Snapshot(fixture.Root)),"an exact package retry reuses verified immutable files without altering them");
        var first=DashboardPackages.Activate(fixture.Root,staged,null);
        check(first.Changed&&first.Previous==null&&first.Current.PackageId==expectedId&&!string.IsNullOrWhiteSpace(first.Current.Generation),"first activation commits a fresh generation atomically");
        var loaded=DashboardPackages.ReadActive(fixture.Root);
        check(loaded.Generation==first.Current.Generation&&loaded.ArchiveSha256==staged.ArchiveSha256,"active pointer round trip retains exact generation and archive identity");
        check(DashboardPackages.EntryPath(fixture.Root,loaded)==Path.Combine(fixture.Root,staged.RelativeDirectory.Replace('/',Path.DirectorySeparatorChar),IndependentFixtures.Entry),"launch path stays within the selected version directory");
        var idempotent=DashboardPackages.Activate(fixture.Root,staged,loaded.Generation);
        check(!idempotent.Changed&&idempotent.Current.Generation==loaded.Generation,"re-activating an identical package does not replace its generation");
        reject(()=>DashboardPackages.Activate(fixture.Root,staged,"stale-generation"),"stale activation cannot overwrite the current generation");
        check(DashboardPackages.ReadActive(fixture.Root).Generation==loaded.Generation,"failed generation comparison preserves active pointer");
        var v2=Make(root,"second","1.3.0",fixture.Root);var stage2=DashboardPackages.Stage(v2.Source,v2.Root,v2.Feed,CancellationToken.None);
        var second=DashboardPackages.Activate(fixture.Root,stage2,loaded.Generation);
        check(second.Changed&&second.Previous.PackageId==staged.PackageId&&second.Current.Generation!=loaded.Generation,"upgrade retains the prior package and produces another generation");
        check(Directory.Exists(Path.Combine(fixture.Root,staged.RelativeDirectory.Replace('/',Path.DirectorySeparatorChar))),"upgrade retains the previous version's payload");
        reject(()=>DashboardPackages.Activate(fixture.Root,staged,second.Current.Generation),"a lower Dashboard version cannot be activated over a newer one");
        check(!DashboardPackages.Rollback(fixture.Root,first)&&DashboardPackages.ReadActive(fixture.Root).Generation==second.Current.Generation,"rollback of an older transaction cannot clobber a later activation");
        check(DashboardPackages.Rollback(fixture.Root,second),"matching failed upgrade can roll back to the prior package");
        var rolled=DashboardPackages.ReadActive(fixture.Root);
        check(rolled.PackageId==first.Current.PackageId&&rolled.Generation!=first.Current.Generation&&rolled.Generation!=second.Current.Generation,"rollback preserves package identity with a fresh generation");
        check(!DashboardPackages.Rollback(fixture.Root,second),"the same rollback cannot replay against a fresh generation");
        var concurrent=Make(root,"concurrent","1.4.0",fixture.Root);var concurrentStage=DashboardPackages.Stage(concurrent.Source,concurrent.Root,concurrent.Feed,CancellationToken.None);
        string generation=rolled.Generation;int wins=0,refused=0;
        var start=new ManualResetEventSlim(false);
        var contenders=new[]{stage2,concurrentStage}.Select(candidate=>System.Threading.Tasks.Task.Run(()=>
        {
            start.Wait();try{DashboardPackages.Activate(fixture.Root,candidate,generation);Interlocked.Increment(ref wins);}
            catch(Exception e)when(e is IOException||e is InvalidOperationException){Interlocked.Increment(ref refused);}
        })).ToArray();start.Set();System.Threading.Tasks.Task.WaitAll(contenders);
        check(wins==1&&refused==1&&DashboardPackages.ReadActive(fixture.Root).Generation!=generation,"simultaneous activation attempts produce exactly one winner without clobbering the active pointer");
        // Return to a separate clean root for the same-version conflict witness.
        fixture=Make(root,"same-version-root");staged=DashboardPackages.Stage(fixture.Source,fixture.Root,fixture.Feed,CancellationToken.None);first=DashboardPackages.Activate(fixture.Root,staged,null);rolled=first.Current;
        var different=Make(root,"same-version-different","1.2.0",fixture.Root);different.Payload[0]=IndependentFixtures.Item(IndependentFixtures.Entry,"MZ changed same-version executable");Rebuild(different,"changed.zip");
        var sameVersionStage=DashboardPackages.Stage(different.Source,different.Root,different.Feed,CancellationToken.None);
        reject(()=>DashboardPackages.Activate(fixture.Root,sameVersionStage,rolled.Generation),"same version with different bytes cannot replace an active release");
        check(DashboardPackages.ReadActive(fixture.Root).Generation==rolled.Generation,"same-version conflict preserves the active generation");
        var initial=Make(root,"rollback-initial");var initialStage=DashboardPackages.Stage(initial.Source,initial.Root,initial.Feed,CancellationToken.None);var initialActivation=DashboardPackages.Activate(initial.Root,initialStage,null);
        check(DashboardPackages.Rollback(initial.Root,initialActivation)&&DashboardPackages.ReadActive(initial.Root)==null,"failed first activation can return to no active version");

        InvalidArchiveCases(root,check,reject);
        var cancelled=Make(root,"cancelled");var cancellation=new CancellationTokenSource();cancellation.Cancel();
        bool observed=false;try{DashboardPackages.Stage(cancelled.Source,cancelled.Root,cancelled.Feed,cancellation.Token);}catch(OperationCanceledException){observed=true;}
        check(observed&&DashboardPackages.ReadActive(cancelled.Root)==null&&!HasVersions(cancelled.Root)&&!Directory.GetDirectories(Path.Combine(cancelled.Root,"staging")).Any(),"cancellation before copy leaves no active/version payload or staged leftovers");
        var tampered=Make(root,"tampered");var tamperStage=DashboardPackages.Stage(tampered.Source,tampered.Root,tampered.Feed,CancellationToken.None);
        string entry=Path.Combine(tampered.Root,tamperStage.RelativeDirectory.Replace('/',Path.DirectorySeparatorChar),IndependentFixtures.Entry);File.AppendAllText(entry,"changed");
        reject(()=>DashboardPackages.ValidateInstalled(tampered.Root,tamperStage),"changed staged file size/hash rejected by installed validation");
        reject(()=>DashboardPackages.Activate(tampered.Root,tamperStage,null),"tampered staged payload cannot become active");
        reject(()=>DashboardPackages.Stage(tampered.Source,tampered.Root,tampered.Feed,CancellationToken.None),"an existing corrupted immutable version cannot be silently reused");
        check(DashboardPackages.ReadActive(tampered.Root)==null,"tamper rejection leaves activation unchanged");
        foreach(string invalid in new[]{"../escape","/absolute","C:/outside","dir\\file","a//b","a/./b","a/../b","a/", "a.","a ","CON","con.txt","COM1.dll","nul","assets/a:b","assets/a?b","assets/a*b","assets/a|b","assets/a<b","assets/\u0001bad"})
            reject(()=>DashboardPackages.SafeRelativePath(invalid),"unsafe relative path rejected: "+invalid);
        foreach(string valid in new[]{IndependentFixtures.Entry,"BeamSolver/Doka.BeamSolver.exe","assets/shape.svg","assets/strings.de-DE.json","assets/العربية.txt"})check(DashboardPackages.SafeRelativePath(valid)==valid,"safe relative path retained: "+valid);
    }
    [DataContract]
    sealed class FailingJsonPayload
    {
        [DataMember] public string Value {get{throw new InvalidOperationException("independent JSON serialization failure");}set{}}
    }
    static void AtomicJsonPublication(string root,Action<bool,string> check,Action<Action,string> reject)
    {
        string directory=Path.Combine(root,"json-publication");Directory.CreateDirectory(directory);
        string payload=new string('a',2*1024*1024);byte[] expected=IndependentFixtures.B("\""+payload+"\"");
        for(int attempt=0;attempt<3;attempt++)
        {
            string trial=Path.Combine(directory,"concurrent-"+attempt);Directory.CreateDirectory(trial);string path=Path.Combine(trial,"startup.json");
            using(var start=new ManualResetEventSlim(false))
            {
                var writer=Task.Run(()=>{start.Wait();DashboardPackages.WriteJson(path,payload);});
                byte[] firstVisible=null;Exception firstReadError=null;var clock=Stopwatch.StartNew();start.Set();
                while(!File.Exists(path))
                {
                    if(writer.IsCompleted)writer.GetAwaiter().GetResult();
                    if(clock.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException("Independent JSON writer did not publish its target.");
                    Thread.Yield();
                }
                // Exactly one attempt: do not retry a sharing violation or hide
                // empty/partial content after the writer exposes its final name.
                try{firstVisible=File.ReadAllBytes(path);}catch(IOException error){firstReadError=error;}
                writer.GetAwaiter().GetResult();
                check(firstReadError==null,"first-visible JSON is immediately readable without retries: "+attempt+(firstReadError==null?"":" ("+firstReadError.GetType().Name+")"));
                check(firstVisible!=null&&firstVisible.SequenceEqual(expected),"first-visible JSON already contains the entire independently specified 2 MiB document: "+attempt);
                check(Directory.GetFiles(trial).SequenceEqual(new[]{path}),"successful JSON publication leaves only its final file: "+attempt);
            }
        }
        string existingDirectory=Path.Combine(directory,"existing-target");Directory.CreateDirectory(existingDirectory);string existing=Path.Combine(existingDirectory,"receipt.json");
        byte[] original=IndependentFixtures.B("independent existing receipt bytes");File.WriteAllBytes(existing,original);
        reject(()=>DashboardPackages.WriteJson(existing,payload),"JSON publication retains create-new semantics for an existing target");
        check(File.ReadAllBytes(existing).SequenceEqual(original),"failed JSON publication never overwrites an existing receipt");
        check(Directory.GetFiles(existingDirectory).SequenceEqual(new[]{existing}),"existing-target publication failure removes its own temporary file");
        string failureDirectory=Path.Combine(directory,"serialization-failure");Directory.CreateDirectory(failureDirectory);string failed=Path.Combine(failureDirectory,"receipt.json");
        bool serializationFailed=false;
        try{DashboardPackages.WriteJson(failed,new FailingJsonPayload());}
        catch(Exception error)when(error.GetBaseException() is InvalidOperationException&&error.GetBaseException().Message=="independent JSON serialization failure"){serializationFailed=true;}
        check(serializationFailed,"independently forced JSON serialization failure is surfaced");
        check(!File.Exists(failed)&&Directory.GetFiles(failureDirectory).Length==0,"serialization failure leaves neither final JSON nor an abandoned temporary file");
        string blockedDirectory=Path.Combine(directory,"blocked-publication");Directory.CreateDirectory(blockedDirectory);string blocked=Path.Combine(blockedDirectory,"receipt.json");Directory.CreateDirectory(blocked);
        bool blockedFailed=false;
        try{DashboardPackages.WriteJson(blocked,payload);}catch(Exception error)when(error is IOException||error is UnauthorizedAccessException){blockedFailed=true;}
        check(blockedFailed&&Directory.Exists(blocked),"blocked final-path publication reports failure without removing the obstruction");
        check(Directory.GetFiles(blockedDirectory).Length==0,"blocked final-path publication cleans its own completed temporary JSON");
    }
    static void InvalidArchiveCases(string root,Action<bool,string> check,Action<Action,string> reject)
    {
        int number=0;
        Action<KeyValuePair<string,byte[]>[],string> Bad=(entries,label)=>
        {
            var f=Make(root,"bad-"+(number++));Rebuild(f,"invalid.zip",entries);
            reject(()=>DashboardPackages.Stage(f.Source,f.Root,f.Feed,CancellationToken.None),label);
            check(!HasVersions(f.Root)&&DashboardPackages.ReadActive(f.Root)==null,"failed package leaves no active/version payload: "+label);
            check(!Directory.Exists(Path.Combine(f.Root,"staging"))||!Directory.GetDirectories(Path.Combine(f.Root,"staging")).Any(),"failed package cleans staging: "+label);
        };
        var payload=Payload();var valid=Entries("1.2.0",payload);
        foreach(string path in new[]{"../escape.txt","/absolute.txt","C:/outside.txt","dir\\escape.txt","a//b","CON.txt","assets/trailing. ","assets/stream:ads"})Bad(valid.Concat(new[]{IndependentFixtures.Item(path,"outside")}).ToArray(),"unsafe ZIP entry rejected: "+path);
        Bad(valid.Concat(new[]{payload[0]}).ToArray(),"duplicate ZIP path rejected");
        Bad(valid.Concat(new[]{IndependentFixtures.Item(IndependentFixtures.Entry.ToUpperInvariant(),"case collision")}).ToArray(),"case-colliding ZIP path rejected");
        Bad(valid.Concat(new[]{IndependentFixtures.Item("unlisted.dll","unlisted")}).ToArray(),"unlisted archive file rejected");
        Bad(payload,"archive missing payload manifest rejected");
        Bad(Entries("1.2.0",payload,manifest:"{}"),"incomplete payload metadata rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("1.2.0",payload)+"{}"),"concatenated payload manifest JSON rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("1.2.0",payload,protocol:2)),"payload protocol mismatch rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("9.0.0",payload)),"payload/feed version mismatch rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("1.2.0",payload,product:"other")),"payload product mismatch rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("1.2.0",payload,schema:2)),"future payload schema rejected");
        Bad(Entries("1.2.0",payload,manifest:new string(' ',1024*1024+1)),"oversize payload manifest rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("1.2.0",payload.Concat(new[]{payload[0]}))),"duplicate payload file record rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("1.2.0",payload).Replace(IndependentFixtures.Hash(payload[0].Value),new string('0',64))),"payload-file checksum contradiction rejected");
        Bad(Entries("1.2.0",payload,manifest:IndependentFixtures.Manifest("1.2.0",payload).Replace("\"sizeBytes\":"+payload[0].Value.Length,"\"sizeBytes\":999999")),"payload-file size contradiction rejected");
        foreach(string required in Required){var missing=payload.Where(p=>p.Key!=required).ToArray();Bad(Entries("1.2.0",missing),"required payload binary missing: "+required);}
        foreach(string forbidden in new[]{"MyRevitAddin.addin","Doka.RevitBridge.dll","sub/other.ADDIN"}){var added=payload.Concat(new[]{IndependentFixtures.Item(forbidden,"must remain in Revit bootstrap")}).ToArray();Bad(Entries("1.2.0",added),"integration file excluded from Dashboard-only package: "+forbidden);}
        foreach(string required in Required){var empty=payload.Select(p=>p.Key==required?new KeyValuePair<string,byte[]>(p.Key,new byte[0]):p).ToArray();Bad(Entries("1.2.0",empty),"zero-length required binary rejected: "+required);}
        var corrupt=Make(root,"corrupt-whole-archive");corrupt.Feed.Sha256=new string('0',64);reject(()=>DashboardPackages.Stage(corrupt.Source,corrupt.Root,corrupt.Feed,CancellationToken.None),"whole archive hash mismatch rejected before extraction");check(!HasVersions(corrupt.Root),"archive hash failure creates no final version");
        var wrongSize=Make(root,"archive-size");wrongSize.Feed.SizeBytes++;reject(()=>DashboardPackages.Stage(wrongSize.Source,wrongSize.Root,wrongSize.Feed,CancellationToken.None),"whole archive size mismatch rejected before extraction");check(!HasVersions(wrongSize.Root),"archive size failure creates no final version");
        foreach(int attributes in new[]{unchecked((int)0xa1ff0000),(int)FileAttributes.ReparsePoint})
        {
            var linked=Make(root,"archive-link-"+attributes);
            linked.Archive=IndependentFixtures.Zip(linked.Source,"linked.zip",Entries(linked.Version,linked.Payload),entry=>{if(entry.FullName=="assets/units.txt")entry.ExternalAttributes=attributes;});SetFeed(linked);
            reject(()=>DashboardPackages.Stage(linked.Source,linked.Root,linked.Feed,CancellationToken.None),"linked ZIP entry rejected for attributes "+attributes);
            check(!HasVersions(linked.Root),"linked archive cannot produce an installed version");
        }
        var huge=Make(root,"expanded-budget");var bytes=File.ReadAllBytes(huge.Archive);int eocd=-1;
        for(int i=bytes.Length-22;i>=0;i--)if(bytes[i]==0x50&&bytes[i+1]==0x4b&&bytes[i+2]==0x05&&bytes[i+3]==0x06){eocd=i;break;}
        if(eocd<0)throw new Exception("Independent ZIP fixture lacks EOCD.");
        int directory=BitConverter.ToInt32(bytes,eocd+16);if(BitConverter.ToInt32(bytes,directory)!=0x02014b50)throw new Exception("Independent ZIP fixture lacks central directory.");
        uint expanded=checked((uint)DashboardPackages.MaxExpandedBytes+1);for(int i=0;i<4;i++)bytes[directory+24+i]=(byte)(expanded>>(8*i));File.WriteAllBytes(huge.Archive,bytes);SetFeed(huge);
        bool bounded=false;try{DashboardPackages.Stage(huge.Source,huge.Root,huge.Feed,CancellationToken.None);}catch(InvalidDataException error){bounded=error.Message.IndexOf("expanded",StringComparison.OrdinalIgnoreCase)>=0;}
        check(bounded&&!HasVersions(huge.Root),"oversize declared expansion is rejected at the expansion budget before payload extraction");
        var tooMany=Make(root,"entry-count");var extra=Enumerable.Range(0,10002).Select(i=>IndependentFixtures.Item("extra/item"+i,""));Rebuild(tooMany,"many.zip",Entries(tooMany.Version,tooMany.Payload).Concat(extra).ToArray());
        bool entryBound=false;try{DashboardPackages.Stage(tooMany.Source,tooMany.Root,tooMany.Feed,CancellationToken.None);}catch(InvalidDataException error){entryBound=error.Message.IndexOf("entry count",StringComparison.OrdinalIgnoreCase)>=0;}
        check(entryBound&&!HasVersions(tooMany.Root),"excessive archive entry count is rejected before extraction");
    }
    static Fixture Make(string root,string label,string version="1.2.0",string sharedRoot=null)
    {
        var f=new Fixture{Source=Path.Combine(root,"feeds",label),Root=sharedRoot??Path.Combine(root,"roots",label),Version=version,Payload=Payload()};Directory.CreateDirectory(f.Source);Rebuild(f,"package.zip");return f;
    }
    static KeyValuePair<string,byte[]>[] Payload()=>Required.Select(p=>IndependentFixtures.Item(p,"MZ independent inert binary fixture: "+p)).Concat(new[]{IndependentFixtures.Item("assets/units.txt","mm; العربية; ø; 650.125"),new KeyValuePair<string,byte[]>("assets/empty.txt",new byte[0])}).ToArray();
    static KeyValuePair<string,byte[]>[] Entries(string version,KeyValuePair<string,byte[]>[] files,string manifest=null)=>files.Concat(new[]{IndependentFixtures.Item("dashboard-package.json",manifest??IndependentFixtures.Manifest(version,files))}).ToArray();
    static void Rebuild(Fixture f,string name,KeyValuePair<string,byte[]>[] entries=null)
    {
        f.Archive=IndependentFixtures.Zip(f.Source,name,entries??Entries(f.Version,f.Payload));
        SetFeed(f);
    }
    static void SetFeed(Fixture f)
    {
        string name=Path.GetFileName(f.Archive);
        var info=new FileInfo(f.Archive);string json=IndependentFixtures.Feed(f.Version,name,IndependentFixtures.HashFile(f.Archive),info.Length);
        File.WriteAllText(Path.Combine(f.Source,"dashboard-latest.json"),json,IndependentFixtures.Utf8);
        f.Feed=DashboardPackages.ReadFeed(f.Source,new Version(0,0,0,0),1);
    }
    static bool HasVersions(string root)=>Directory.Exists(Path.Combine(root,"versions"))&&Directory.GetDirectories(Path.Combine(root,"versions")).Any();
    static void AssertPayload(Fixture f,StagedDashboard staged,Action<bool,string> check)
    {
        string directory=Path.Combine(f.Root,staged.RelativeDirectory.Replace('/',Path.DirectorySeparatorChar));
        foreach(var file in f.Payload)check(File.ReadAllBytes(Path.Combine(directory,file.Key.Replace('/',Path.DirectorySeparatorChar))).SequenceEqual(file.Value),"extracted bytes exactly match independent payload: "+file.Key);
        check(Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length==f.Payload.Length+1,"installed payload has exactly manifest plus declared files");
    }
}
