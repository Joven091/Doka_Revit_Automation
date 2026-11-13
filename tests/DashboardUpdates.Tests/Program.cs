using System;
using System.IO;
using System.Runtime.Serialization;

internal static class Program
{
    static int checks,failures;
    static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"doka-independent-desktop-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            Run("bridge protocol",()=>ProtocolChecks.Run(Check,Reject));
            Run("desktop package/feed/activation",()=>PackageChecks.Run(root,Check,Reject));
            Run("Dashboard restart transaction",()=>UpdateTransactionChecks.Run(root,Check,Reject));
            Console.WriteLine("Independent Dashboard update checks: "+checks+" passed; "+failures+" failed. Actual Revit host acceptance is separate.");
            return failures==0?0:1;
        }
        finally{try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    static void Run(string group,Action action)
    {try{action();}catch(Exception error){failures++;Console.Error.WriteLine("FAIL group "+group+": "+error);}}
    static void Check(bool value,string message)
    {if(value){checks++;Console.WriteLine("PASS "+message);}else{failures++;Console.Error.WriteLine("FAIL "+message);}}
    static void Reject(Action action,string message)
    {
        try{action();}
        catch(Exception error)when(error is InvalidDataException||error is IOException||error is SerializationException||error is ArgumentException||error is InvalidOperationException||error is OverflowException)
        {Check(true,message);return;}
        Check(false,"accepted invalid data: "+message);
    }
}
