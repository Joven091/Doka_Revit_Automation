using System;
using System.IO;
using MyRevitAddin.Updates;

internal static class Program
{
    static int checks;
    static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
    static int Main(string[] args)
    {
        try
        {
            string feed=Path.GetFullPath(args[0]);var expected=UpdateFiles.ParseVersion(args[1]);
            var release=UpdateFiles.Check(feed,new Version(1,1,26,0));
            Check(release!=null && UpdateFiles.ParseVersion(release.Version)==expected,"the production updater discovers the published 1.1.27 release from 1.1.26");
            Check(release.InstallerFile=="DokaRevitAutomation-"+args[1]+".exe","the feed names the versioned executable");
            string installer=Path.Combine(feed,release.InstallerFile);
            Check(new FileInfo(installer).Length==release.SizeBytes,"published installer size matches latest.json");
            Check(UpdateFiles.Hash(installer)==release.Sha256,"published installer SHA-256 matches latest.json");
            UpdateFiles.VerifyInstaller(new UpdatePlan{Release=release,InstallerPath=installer});
            Check(true,"the unchanged production installer verification accepts the feed payload");
            Check(UpdateFiles.Check(feed,expected)==null,"the same installed version is correctly treated as up to date");
            Check(UpdateFiles.Check(feed,new Version(1,1,28,0))==null,"a newer installation is not offered a downgrade");
            Console.WriteLine(checks+" real feed-reader checks passed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
