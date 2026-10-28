#if ONE_OFF_TESTER
using System;
using System.IO;
using System.Linq;
internal static class OneOffBeamEntry
{
    [STAThread] internal static int Main(string[] args)
    {
        try{return StandalonePreview.Run(args.Contains("--startup-check"));}
        catch(Exception error){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),error.ToString());return 1;}
    }
}
#endif
