using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace EngineeringDashboard
{
    internal sealed class BeamSolverRequest
    {
        internal TowerInput Main=null;
        internal TowerInput[] Independent=Array.Empty<TowerInput>();
    }
    internal sealed class BeamSolverReply
    {
        internal TowerLayout Main;
        internal TransferNetworkResult Network;
        internal TowerLayout[] Independent=Array.Empty<TowerLayout>();
        internal string Diagnostics=null,Error=null,ErrorDetails=null,ErrorKind=null;
        internal long[] Counters=Array.Empty<long>();
    }
    // Private, versioned IPC. No CLR names supplied by a file are resolved or
    // instantiated: types and fields come exclusively from this fixed model set.
    // IEEE doubles and private response coefficients are copied without rounding.
    internal static class BeamWire
    {
        internal const int Protocol=1;
        internal const long MaximumBytes=256L*1024*1024;
        const int MaximumItems=2000000,MaximumDepth=256;
        static readonly HashSet<string> skipped=new HashSet<string>(StringComparer.Ordinal)
        {
            "TowerInput.Diagnostics","TowerInput.DedicatedCrossCache","TowerInput.CrossingGeometryWork","TowerInput.CrossLowerCases",
            "TowerInput.NetworkSeedCompletion","TowerInput.NetworkReceiverWork","TowerInput.NetworkPrefixWork","TowerInput.NetworkWarmStart",
            "TowerInput.ReceiverAlternatives","TowerInput.Trace","TowerInput.NetworkNumerics","TowerInput.NetworkGeometryWork","TowerInput.NetworkNumericalWork",
            "TowerLayout.ReceiverIdentity","TransferredSlabInput.SharedGeometryCache","TransferredSlabInput.SharedSearchWork",
            "TransferNetworkInput.slabRowsCache","TransferNetworkInput.crossSlabLayouts","EngineerPrimaryPlan.resolvedCache"
        };
        static readonly string[] modelNames={
            "TowerInput","TowerLayout","TowerStockPlan","TowerStockPiece","TowerBeamAnalysis","TowerBeamAnalysis+DeflectionRegion",
            "LoadedBeamAnalysis","LoadedBeamAnalysis+Element","BeamDistributedLoad","BeamPointLoad","TransferredSlabInput","TransferredSlabResult",
            "TransferMember","ColumnEndStripLoad","TransferNetworkInput","TransferNetworkResult","TransferBeamRow","TransferBayRow","TransferCrossingBeamRow",
            "UpperTransferPlan","CrossBeamSupportResult","FormworkSolid","CrossBeamBearing","EngineerPrimaryPlan","PositionedSecondaryLoad","SystemLoadingStage",
            "EquivalentLoadingComponent","EquivalentReactionComponent","EquivalentLoadDisplay","ImportPoint","BeamSolverRequest","BeamSolverReply"
        };
        static readonly Dictionary<string,Type> types=BuildTypes();
        static readonly Dictionary<Type,FieldInfo[]> fields=types.Values.Distinct().Where(t=>t.Namespace==typeof(TowerInput).Namespace && !t.IsEnum)
            .ToDictionary(t=>t,t=>t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Where(f=>!f.IsStatic && !typeof(Delegate).IsAssignableFrom(f.FieldType) && !skipped.Contains(t.Name+"."+f.Name)).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray());
        static Dictionary<string,Type> BuildTypes()
        {
            var result=new Dictionary<string,Type>(StringComparer.Ordinal);
            foreach(var t in new[] {typeof(bool),typeof(byte),typeof(int),typeof(long),typeof(double),typeof(string)})result[t.FullName]=t;
            foreach(string name in modelNames)
            {
                var t=typeof(TowerInput).Assembly.GetType("EngineeringDashboard."+name,false);
                if(t!=null)result[t.FullName]=t;
            }
            Action<Type> visit=null;
            visit=t=>
            {
                if(t.IsEnum){result[t.FullName]=t;return;}
                if(t.IsArray){visit(t.GetElementType());return;}
                if(t.IsGenericType)foreach(var argument in t.GetGenericArguments())visit(argument);
            };
            foreach(var model in result.Values.ToArray().Where(t=>t.Namespace==typeof(TowerInput).Namespace))
                foreach(var field in model.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))visit(field.FieldType);
            return result;
        }
        internal static string EngineIdentity
        {
            get
            {
                using(var stream=typeof(BeamWire).Assembly.GetManifestResourceStream("EngineeringDashboard.BeamSolver.SourceHashes"))
                {
                    if(stream==null)throw new InvalidOperationException("Beam solver source identity is missing.");
                    using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
                }
            }
        }
        // Framework Process.StandardInput can emit a Unicode preamble on its
        // first write. Console.ReadLine does not reliably consume that preamble
        // in a redirected modern-runtime child. Decode the stream explicitly.
        internal static StreamReader RequestReader(Stream input)=>new StreamReader(input,new UTF8Encoding(false,true),true);
        static string TypeId(Type t)
        {
            if(t.IsArray)return "array"+t.GetArrayRank()+"("+TypeId(t.GetElementType())+")";
            if(t.IsGenericType)
            {
                var generic=t.GetGenericTypeDefinition();var a=t.GetGenericArguments();
                if(generic==typeof(List<>))return "list("+TypeId(a[0])+")";
                if(generic==typeof(Dictionary<,>))return "map("+TypeId(a[0])+","+TypeId(a[1])+")";
                throw new InvalidDataException("Unsupported beam wire collection: "+t.Name);
            }
            if(!types.TryGetValue(t.FullName,out var known) || known!=t)throw new InvalidDataException("Unsupported beam wire model: "+t.FullName);
            return t.FullName;
        }
        static Type Resolve(string id,int depth=0)
        {
            if(depth>32 || id.Length>4096)throw new InvalidDataException("Beam wire type nesting exceeds the protocol budget.");
            if(types.TryGetValue(id,out var t))return t;
            if(id.StartsWith("array",StringComparison.Ordinal) && id.EndsWith(")",StringComparison.Ordinal))
            {
                int at=id.IndexOf('(');if(at<6 || !int.TryParse(id.Substring(5,at-5),out int rank)||rank<1||rank>2)throw new InvalidDataException("Invalid beam wire array.");
                var element=Resolve(id.Substring(at+1,id.Length-at-2),depth+1);return rank==1 ? element.MakeArrayType() : element.MakeArrayType(rank);
            }
            if(id.StartsWith("list(",StringComparison.Ordinal) && id.EndsWith(")",StringComparison.Ordinal))return typeof(List<>).MakeGenericType(Resolve(id.Substring(5,id.Length-6),depth+1));
            if(id.StartsWith("map(",StringComparison.Ordinal) && id.EndsWith(")",StringComparison.Ordinal))
            {
                string body=id.Substring(4,id.Length-5);int nesting=0;
                for(int i=0;i<body.Length;i++){if(body[i]=='(')nesting++;if(body[i]==')')nesting--;if(body[i]==','&&nesting==0)return typeof(Dictionary<,>).MakeGenericType(Resolve(body.Substring(0,i),depth+1),Resolve(body.Substring(i+1),depth+1));}
            }
            throw new InvalidDataException("Unknown beam wire type.");
        }
        internal static void Write(string path,object value)
        {
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(stream,Encoding.UTF8))
            {
                writer.Write(0x424D5731);writer.Write(Protocol);Text(writer,EngineIdentity);
                new GraphWriter(writer).Value(value,0);
                if(stream.Length>MaximumBytes)throw new InvalidDataException("Beam worker payload exceeds the transport budget.");
            }
        }
        internal static T Read<T>(string path)
        {
            var info=new FileInfo(path);if(info.Length>MaximumBytes)throw new InvalidDataException("Beam worker payload exceeds the transport budget.");
            using(var stream=File.OpenRead(path))using(var reader=new BinaryReader(stream,Encoding.UTF8))
            {
                if(reader.ReadInt32()!=0x424D5731 || reader.ReadInt32()!=Protocol || Text(reader)!=EngineIdentity)throw new InvalidDataException("Beam worker protocol/source identity mismatch.");
                try
                {
                    var value=new GraphReader(reader).Value(0);if(stream.Position!=stream.Length || !(value is T))throw new InvalidDataException("Incomplete or unexpected beam worker payload.");return (T)value;
                }
                catch(Exception error) when(error is ArgumentException || error is MemberAccessException || error is TargetException || error is TargetInvocationException || error is TypeLoadException)
                {throw new InvalidDataException("Malformed beam worker model data.",error);}
            }
        }
        static void Text(BinaryWriter writer,string value){var bytes=Encoding.UTF8.GetBytes(value);if(writer.BaseStream.Position+4+bytes.Length>MaximumBytes)throw new InvalidDataException("Beam worker payload exceeds the transport budget.");if(bytes.Length>8*1024*1024)throw new InvalidDataException("Beam wire text is too large.");writer.Write(bytes.Length);writer.Write(bytes);}
        static string Text(BinaryReader reader){int count=reader.ReadInt32();if(count<0||count>8*1024*1024)throw new InvalidDataException("Invalid beam wire text.");var bytes=reader.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();return Encoding.UTF8.GetString(bytes);}
        sealed class ReferenceComparer : IEqualityComparer<object>{public new bool Equals(object a,object b)=>ReferenceEquals(a,b);public int GetHashCode(object value)=>RuntimeHelpers.GetHashCode(value);}
        sealed class GraphWriter
        {
            readonly BinaryWriter writer;readonly Dictionary<object,int> ids=new Dictionary<object,int>(new ReferenceComparer());int items;
            internal GraphWriter(BinaryWriter writer){this.writer=writer;}
            internal void Value(object value,int depth)
            {
                if(depth>MaximumDepth || ++items>MaximumItems || writer.BaseStream.Position>MaximumBytes-32)throw new InvalidDataException("Beam wire graph budget exceeded.");
                if(value==null){writer.Write((byte)0);return;}
                var type=value.GetType();
                if(type==typeof(string)){writer.Write((byte)1);Text(writer,(string)value);return;}
                if(type==typeof(bool)){writer.Write((byte)2);writer.Write((bool)value);return;}
                if(type==typeof(int)){writer.Write((byte)3);writer.Write((int)value);return;}
                if(type==typeof(long)){writer.Write((byte)4);writer.Write((long)value);return;}
                if(type==typeof(double)){writer.Write((byte)5);writer.Write((double)value);return;}
                if(type==typeof(byte)){writer.Write((byte)6);writer.Write((byte)value);return;}
                if(type.IsEnum){writer.Write((byte)7);Text(writer,TypeId(type));writer.Write(Convert.ToInt64(value));return;}
                if(ids.TryGetValue(value,out int prior)){writer.Write((byte)8);writer.Write(prior);return;}
                int id=ids.Count;ids.Add(value,id);writer.Write((byte)9);writer.Write(id);Text(writer,TypeId(type));
                if(value is Array array)
                {
                    writer.Write(array.Rank);for(int rank=0;rank<array.Rank;rank++){if(array.GetLowerBound(rank)!=0)throw new InvalidDataException("Only zero-based model arrays are supported.");writer.Write(array.GetLength(rank));}
                    foreach(var element in array)Value(element,depth+1);return;
                }
                if(value is IDictionary map){writer.Write(map.Count);foreach(DictionaryEntry pair in map){Value(pair.Key,depth+1);Value(pair.Value,depth+1);}return;}
                if(value is IList list){writer.Write(list.Count);foreach(var element in list)Value(element,depth+1);return;}
                if(!fields.TryGetValue(type,out var members))throw new InvalidDataException("No beam wire fields for "+type.FullName);
                writer.Write(members.Length);foreach(var field in members){Text(writer,field.Name);Value(field.GetValue(value),depth+1);}
            }
        }
        sealed class GraphReader
        {
            readonly BinaryReader reader;readonly List<object> ids=new List<object>();int items;
            internal GraphReader(BinaryReader reader){this.reader=reader;}
            int Count(){int count=reader.ReadInt32();if(count<0 || count>MaximumItems)throw new InvalidDataException("Invalid beam wire collection size.");return count;}
            internal object Value(int depth)
            {
                if(depth>MaximumDepth || ++items>MaximumItems)throw new InvalidDataException("Beam wire graph budget exceeded.");
                switch(reader.ReadByte())
                {
                    case 0:return null;case 1:return Text(reader);case 2:return reader.ReadBoolean();case 3:return reader.ReadInt32();case 4:return reader.ReadInt64();case 5:return reader.ReadDouble();case 6:return reader.ReadByte();
                    case 7:var enumeration=Resolve(Text(reader));if(!enumeration.IsEnum)throw new InvalidDataException("Expected beam wire enum.");return Enum.ToObject(enumeration,reader.ReadInt64());
                    case 8:int reference=reader.ReadInt32();if(reference<0||reference>=ids.Count)throw new InvalidDataException("Invalid beam wire reference.");return ids[reference];
                    case 9:break;default:throw new InvalidDataException("Invalid beam wire tag.");
                }
                int id=reader.ReadInt32();if(id!=ids.Count)throw new InvalidDataException("Invalid beam wire object ordering.");var type=Resolve(Text(reader));
                if(type.IsArray)
                {
                    int rank=reader.ReadInt32();if(rank!=type.GetArrayRank())throw new InvalidDataException("Invalid beam wire rank.");var dimensions=new int[rank];long total=1;for(int i=0;i<rank;i++){dimensions[i]=Count();total*=dimensions[i];if(total>MaximumItems)throw new InvalidDataException("Beam wire array too large.");}
                    var array=Array.CreateInstance(type.GetElementType(),dimensions);ids.Add(array);
                    if(rank==1)for(int i=0;i<dimensions[0];i++)array.SetValue(Value(depth+1),i);
                    else for(int i=0;i<dimensions[0];i++)for(int j=0;j<dimensions[1];j++)array.SetValue(Value(depth+1),i,j);
                    return array;
                }
                if(type.IsPrimitive || type.IsEnum || type==typeof(string) || !typeof(IList).IsAssignableFrom(type) && !typeof(IDictionary).IsAssignableFrom(type) && !fields.ContainsKey(type))throw new InvalidDataException("Expected a beam wire model object.");
                object instance=Activator.CreateInstance(type,true);ids.Add(instance);
                if(instance is IDictionary map){int count=Count();for(int i=0;i<count;i++)map.Add(Value(depth+1),Value(depth+1));return map;}
                if(instance is IList list){int count=Count();for(int i=0;i<count;i++)list.Add(Value(depth+1));return list;}
                if(!fields.TryGetValue(type,out var members) || Count()!=members.Length)throw new InvalidDataException("Beam wire model shape mismatch.");
                foreach(var field in members)
                {
                    if(Text(reader)!=field.Name)throw new InvalidDataException("Beam wire field mismatch.");var value=Value(depth+1);
                    if(value==null ? field.FieldType.IsValueType && Nullable.GetUnderlyingType(field.FieldType)==null : !field.FieldType.IsInstanceOfType(value))throw new InvalidDataException("Beam wire field type mismatch: "+type.Name+"."+field.Name+" expected "+field.FieldType+", received "+(value?.GetType().ToString()??"null"));
                    field.SetValue(instance,value);
                }
                return instance;
            }
        }
    }
}
