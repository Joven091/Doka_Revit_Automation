using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace EngineeringDashboard.Top50
{
    /// <summary>Resolve only Top50's known strong-named compatibility dependencies.
    /// Revit.exe.config and other calculators' requests are never redirected.</summary>
    public static class Top50Runtime
    {
        private static readonly object Gate=new object();
        private static bool initialized;
        [ThreadStatic] private static int active;
        private static readonly HashSet<string> Callers=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {"Google.OrTools","Google.Protobuf","System.Memory","System.Buffers","System.Numerics.Vectors","System.Text.Encoding.CodePages","DocumentFormat.OpenXml","DocumentFormat.OpenXml.Framework","IxMilia.Dxf"};
        private static readonly HashSet<string> Libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {"System.Runtime.CompilerServices.Unsafe","System.Memory","System.Buffers","System.Numerics.Vectors","System.Text.Encoding.CodePages"};
        public static void Initialize()
        {
            lock(Gate){if(initialized)return;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;initialized=true;}
        }
        public static IDisposable Enter(){Initialize();active++;return new Scope();}
        private sealed class Scope:IDisposable
        {private bool disposed;public void Dispose(){if(!disposed){active--;disposed=true;}}}
        private static Assembly Resolve(object sender,ResolveEventArgs args)
        {
            // Framework JIT loads can omit RequestingAssembly. Only serve those during
            // a synchronous Top50 operation on this exact thread, never globally.
            if(active==0 || (args.RequestingAssembly!=null&&!Callers.Contains(args.RequestingAssembly.GetName().Name)))return null;
            var requested=new AssemblyName(args.Name);if(!Libraries.Contains(requested.Name))return null;
            var directory=Path.GetDirectoryName(typeof(Top50Runtime).Assembly.Location);
            var path=Path.Combine(directory,requested.Name+".dll");if(!File.Exists(path))return null;
            var actual=AssemblyName.GetAssemblyName(path);
            if(!string.Equals(actual.Name,requested.Name,StringComparison.OrdinalIgnoreCase)||
                !EqualToken(actual.GetPublicKeyToken(),requested.GetPublicKeyToken()))return null;
            return Assembly.LoadFrom(path);
        }
        private static bool EqualToken(byte[] left,byte[] right)
        {if(left==null||right==null||left.Length==0||left.Length!=right.Length)return false;for(int i=0;i<left.Length;i++)if(left[i]!=right[i])return false;return true;}
    }
}
