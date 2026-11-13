using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EngineeringDashboard.DesktopHost
{
    [DataContract] public sealed class BridgeRequest
    {
        [DataMember(IsRequired=true)] public int ProtocolVersion=1;
        [DataMember(IsRequired=true)] public string RequestId;
        [DataMember(IsRequired=true)] public string SessionId;
        [DataMember(IsRequired=true)] public string Token;
        [DataMember(IsRequired=true)] public string Operation;
        [DataMember(IsRequired=true)] public string Payload;
    }
    [DataContract] public sealed class BridgeReply
    {
        [DataMember(IsRequired=true)] public int ProtocolVersion=1;
        [DataMember(IsRequired=true)] public string RequestId;
        [DataMember(IsRequired=true)] public string SessionId;
        [DataMember] public string Payload;
        [DataMember] public string Error;
    }
    public sealed class BridgeHello { public int ProtocolVersion {get;set;} public string BridgeVersion {get;set;} public bool Authorized {get;set;} }
    public sealed class ActiveDocumentInfo { public string DocumentKey {get;set;} public string Title {get;set;} }
    public sealed class HelperApplyRequest
    {
        public string DocumentKey {get;set;}
        public string Kind {get;set;}
        public double WidthMm {get;set;} public double DepthMm {get;set;} public double SlabThicknessMm {get;set;}
        public double PropSpacingMm {get;set;} public double PrimaryCantileverMm {get;set;} public double PrimarySpacingMm {get;set;}
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(DocumentKey)||(Kind!="beam"&&Kind!="slab"))throw new InvalidDataException("Invalid Revit helper request.");
            if(new[]{WidthMm,DepthMm,SlabThicknessMm,PropSpacingMm,PrimaryCantileverMm,PrimarySpacingMm}.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||v<0||v>100000))throw new InvalidDataException("Invalid helper dimensions.");
            if(PropSpacingMm<=0||(Kind=="beam"&&(WidthMm<=0||DepthMm<=0))||(Kind=="slab"&&(SlabThicknessMm<=0||PrimarySpacingMm<=0)))throw new InvalidDataException("Calculate valid helper values before applying them.");
        }
    }
    public sealed class HelperApplyResult { public bool Cancelled {get;set;} public int Updated {get;set;} public int Skipped {get;set;} public int Failed {get;set;} public string Message {get;set;} }
    public static class BridgeProtocol
    {
        public const int Version=1,MaxFrameBytes=32*1024*1024;
        static readonly string[] Operations={"hello","access.ensure","access.manage","context","beam.read","top50.read","top50.refresh","top50.highlight","helpers.apply","updates.integration"};
        public static byte[] Encode<T>(T value)
        {
            using(var s=new MemoryStream()) { Serializer(typeof(T)).WriteObject(s,value);if(s.Length>MaxFrameBytes)throw new InvalidDataException("The Revit transfer exceeds the supported message size.");return s.ToArray(); }
        }
        public static T Decode<T>(byte[] bytes)
        {
            if(bytes==null||bytes.Length==0||bytes.Length>MaxFrameBytes)throw new InvalidDataException("Invalid bridge message length.");
            SingleJsonRoot(bytes);
            var quotas=new XmlDictionaryReaderQuotas{MaxDepth=64,MaxArrayLength=1000000,MaxStringContentLength=MaxFrameBytes,MaxBytesPerRead=65536,MaxNameTableCharCount=65536};
            using(var reader=JsonReaderWriterFactory.CreateJsonReader(bytes,quotas))
            {
                var value=(T)Serializer(typeof(T)).ReadObject(reader,true);
                while(reader.Read())if(reader.NodeType!=XmlNodeType.Whitespace&&reader.NodeType!=XmlNodeType.SignificantWhitespace)throw new InvalidDataException("Unexpected trailing bridge data.");
                return value;
            }
        }
        static DataContractJsonSerializer Serializer(Type t)=>new DataContractJsonSerializer(t,new DataContractJsonSerializerSettings{MaxItemsInObjectGraph=1000000,UseSimpleDictionaryFormat=true});
        static bool White(byte value)=>value==32||value==9||value==10||value==13;
        static void SingleJsonRoot(byte[] bytes)
        {
            int first=0;if(bytes.Length>=3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191)first=3;
            while(first<bytes.Length&&White(bytes[first]))first++;
            if(first==bytes.Length)throw new InvalidDataException("Empty JSON message.");
            int end=-1;byte root=bytes[first];
            if(root==(byte)'{'||root==(byte)'['||root==(byte)'"')
            {
                var stack=new Stack<byte>();bool quoted=false,escaped=false;
                for(int i=first;i<bytes.Length;i++)
                {
                    byte c=bytes[i];
                    if(quoted){if(escaped){escaped=false;continue;}if(c==(byte)'\\'){escaped=true;continue;}if(c==(byte)'"'){quoted=false;if(root==(byte)'"'&&stack.Count==0){end=i+1;break;}}continue;}
                    if(c==(byte)'"'){quoted=true;continue;}
                    if(c==(byte)'{'||c==(byte)'['){stack.Push(c);if(stack.Count>64)throw new InvalidDataException("JSON nesting exceeds the bridge limit.");}
                    else if(c==(byte)'}'||c==(byte)']')
                    {if(stack.Count==0||stack.Pop()!=(c==(byte)'}'?(byte)'{':(byte)'['))throw new InvalidDataException("Unbalanced JSON message.");if(stack.Count==0){end=i+1;break;}}
                }
            }
            else
            {
                end=first;while(end<bytes.Length&&!White(bytes[end]))end++;
                string scalar=Encoding.UTF8.GetString(bytes,first,end-first);
                if(scalar!="true"&&scalar!="false"&&scalar!="null"&&!Regex.IsMatch(scalar,@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z"))throw new InvalidDataException("Invalid JSON scalar.");
            }
            if(end<0)throw new InvalidDataException("Incomplete JSON message.");
            for(int i=end;i<bytes.Length;i++)if(!White(bytes[i]))throw new InvalidDataException("Unexpected trailing bridge data.");
        }
        public static string Json<T>(T value)=>Encoding.UTF8.GetString(Encode(value));
        public static T FromJson<T>(string value)=>Decode<T>(Encoding.UTF8.GetBytes(value??""));
        public static void ValidateRequest(BridgeRequest request,string session,string token)
        {
            Guid id;
            if(request==null||request.ProtocolVersion!=Version||!Guid.TryParseExact(request.RequestId,"N",out id)||request.SessionId!=session||!ConstantEquals(request.Token,token)||!Operations.Contains(request.Operation)||request.Payload==null)
                throw new InvalidDataException("The Dashboard bridge request is incompatible or expired.");
        }
        public static void ValidateReply(BridgeReply reply,string id,string session)
        {if(reply==null||reply.ProtocolVersion!=Version||reply.RequestId!=id||reply.SessionId!=session)throw new InvalidDataException("The Revit reply belongs to a different or expired request.");}
        static bool ConstantEquals(string a,string b)
        {if(a==null||b==null||a.Length!=b.Length)return false;int diff=0;for(int i=0;i<a.Length;i++)diff|=a[i]^b[i];return diff==0;}
        public static void WriteFrame(Stream stream,byte[] bytes)
        {
            if(bytes==null||bytes.Length==0||bytes.Length>MaxFrameBytes)throw new InvalidDataException("Invalid bridge frame size.");
            int n=bytes.Length;byte[] h={(byte)n,(byte)(n>>8),(byte)(n>>16),(byte)(n>>24)};stream.Write(h,0,4);stream.Write(bytes,0,n);stream.Flush();
        }
        public static byte[] ReadFrame(Stream stream)
        {
            var h=ReadExactly(stream,4);int n=h[0]|h[1]<<8|h[2]<<16|h[3]<<24;
            if(n<=0||n>MaxFrameBytes)throw new InvalidDataException("Invalid bridge frame size.");return ReadExactly(stream,n);
        }
        static byte[] ReadExactly(Stream stream,int length)
        {var bytes=new byte[length];int offset=0;while(offset<length){int n=stream.Read(bytes,offset,length-offset);if(n==0)throw new EndOfStreamException("The Revit connection closed.");offset+=n;}return bytes;}
    }
}
