using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using static TestSupport;

internal static class FailureDiagnostics
{
    internal static void Capture(string stage,string root,params Process[] owned)
    {
        try
        {
            string destination=Path.Combine(Output,"failure-diagnostics",stage+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(destination);
            CopyLogs(root,destination);
            int[] ids=owned.Where(process=>process!=null).Select(process=>process.Id).Distinct().ToArray();
            var details=new StringBuilder("Stage: "+stage+"\nOwned process IDs: "+string.Join(", ",ids)+"\n");
            foreach(var process in owned.Where(process=>process!=null))
            {
                try{details.AppendLine("PID "+process.Id+": exited="+process.HasExited+(!process.HasExited?"; executable="+process.MainModule.FileName:""));}
                catch(Exception error){details.AppendLine("Process metadata unavailable: "+Describe(error));}
            }
            File.WriteAllText(Path.Combine(destination,"processes.txt"),details.ToString());
            // Remote UIA can stall when the product UI is hung. Keep diagnostics
            // bounded; only inspect the exact child processes owned by the test.
            var tree=Task.Run(()=>WindowTrees(ids));
            if(tree.Wait(TimeSpan.FromSeconds(10)))
            {
                File.WriteAllText(Path.Combine(destination,"uia-windows.txt"),tree.GetAwaiter().GetResult());
                Console.Error.WriteLine("OWNED UIA WINDOWS:\n"+tree.Result);
            }
            else File.WriteAllText(Path.Combine(destination,"uia-windows.txt"),"UI Automation tree collection exceeded 10 seconds. Owned PIDs: "+string.Join(", ",ids));
            Console.Error.WriteLine("Failure diagnostics saved: "+destination);
        }
        catch(Exception error){Console.Error.WriteLine("Failure diagnostics could not complete: "+Describe(error));}
    }
    static void CopyLogs(string root,string destination)
    {
        string logs=Path.Combine(root,"Logs"),handoffs=Path.Combine(root,"handoffs");
        var files=(Directory.Exists(logs)?Directory.GetFiles(logs,"*.txt",SearchOption.AllDirectories):Array.Empty<string>())
            .Concat(Directory.Exists(handoffs)?Directory.GetFiles(handoffs,"update.log",SearchOption.AllDirectories):Array.Empty<string>())
            .Concat(Directory.Exists(handoffs)?Directory.GetFiles(handoffs,"helper-startup.log",SearchOption.AllDirectories):Array.Empty<string>()).ToArray();
        foreach(string source in files)
        {
            try
            {
                string relative=source.Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length+1),target=Path.Combine(destination,"product-logs",relative);Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Write|FileShare.Delete))
                using(var output=File.Create(target))input.CopyTo(output);
                string text=File.ReadAllText(target);Console.Error.WriteLine("PRODUCT LOG "+relative+":\n"+(text.Length>65536?text.Substring(0,65536)+"\n[stdout truncated; full log retained in diagnostics]":text));
            }
            catch(Exception error){Console.Error.WriteLine("Cannot collect product log "+source+": "+Describe(error));}
        }
    }
    static string WindowTrees(int[] ids)
    {
        var text=new StringBuilder();int nodes=0;
        foreach(int id in ids)
        {
            text.AppendLine("Owned PID "+id);
            try
            {
                // Include offscreen/hidden owners as well as visible tools. This
                // shows whether a retained Launcher was revealed or still hidden.
                var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty,id),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Window)));
                foreach(AutomationElement window in windows)AppendTree(text,window,0,ref nodes);
            }
            catch(Exception error){text.AppendLine("  Window enumeration failed: "+Describe(error));}
        }
        return text.ToString();
    }
    static void AppendTree(StringBuilder text,AutomationElement element,int depth,ref int nodes)
    {
        if(nodes++>=1500){if(nodes==1501)text.AppendLine("[UIA node limit reached]");return;}
        try
        {
            var current=element.Current;string readOnly="n/a";object pattern;
            if(element.TryGetCurrentPattern(ValuePattern.Pattern,out pattern))readOnly=((ValuePattern)pattern).Current.IsReadOnly.ToString();
            text.Append(' ',depth*2).Append(current.ControlType.ProgrammaticName).Append(" name=\"").Append(Quote(current.Name)).Append("\" id=\"").Append(Quote(current.AutomationId))
                .Append("\" class=\"").Append(Quote(current.ClassName)).Append("\" enabled=").Append(current.IsEnabled).Append(" readonly=").Append(readOnly).Append(" offscreen=").Append(current.IsOffscreen).AppendLine();
            if(depth>=20){text.AppendLine("[UIA depth limit reached]");return;}
            var walker=TreeWalker.ControlViewWalker;var child=walker.GetFirstChild(element);
            while(child!=null&&nodes<=1500){AppendTree(text,child,depth+1,ref nodes);child=walker.GetNextSibling(child);}
        }
        catch(Exception error){text.Append(' ',depth*2).AppendLine("UIA node unavailable: "+Describe(error));}
    }
    static string Quote(string value)
    {
        string text=(value??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");return text.Length>500?text.Substring(0,500)+"…":text;
    }
    static string Describe(Exception error)=>error.GetType().Name+" 0x"+error.HResult.ToString("X8")+": "+error.Message;
}
