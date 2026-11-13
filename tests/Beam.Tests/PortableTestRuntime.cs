using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

/// <summary>Framework-compatible equivalents for diagnostic-test APIs; never included in the production solver.</summary>
internal static class PortableTestRuntime
{
    internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static double NextUp(double value)
    {
        if(double.IsNaN(value)||double.IsPositiveInfinity(value))return value;
        if(value==0)return double.Epsilon;
        long bits=BitConverter.DoubleToInt64Bits(value);
        return BitConverter.Int64BitsToDouble(bits+(value>0?1:-1));
    }
    internal static double NextDown(double value)
    {
        if(double.IsNaN(value)||double.IsNegativeInfinity(value))return value;
        if(value==0)return -double.Epsilon;
        long bits=BitConverter.DoubleToInt64Bits(value);
        return BitConverter.Int64BitsToDouble(bits+(value>0?-1:1));
    }
    internal static string ProcessPath { get { using(var process=Process.GetCurrentProcess())return process.MainModule.FileName; } }
    internal static void MoveOverwrite(string source,string destination)
    {
        if(File.Exists(destination))File.Delete(destination);
        File.Move(source,destination);
    }
    internal static T CreateDelegate<T>(this MethodInfo method) where T:Delegate => (T)method.CreateDelegate(typeof(T));
}
