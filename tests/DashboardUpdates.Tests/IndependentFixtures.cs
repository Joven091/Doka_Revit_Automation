using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal static class IndependentFixtures
{
    internal const string Product="Doka.EngineeringDashboard.Desktop";
    internal const string Entry="Doka.EngineeringDashboard.exe";
    internal static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    internal static string Hash(byte[] data)
    {
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
    }
    internal static string HashFile(string path)=>Hash(File.ReadAllBytes(path));
    internal static string Q(string value)
    {
        var text=new StringBuilder("\"");
        foreach(char c in value??"")
        {
            switch(c)
            {
                case '"':text.Append("\\\"");break;
                case '\\':text.Append("\\\\");break;
                case '\n':text.Append("\\n");break;
                case '\r':text.Append("\\r");break;
                case '\t':text.Append("\\t");break;
                default:if(c<32)text.Append("\\u"+((int)c).ToString("x4"));else text.Append(c);break;
            }
        }
        return text.Append('"').ToString();
    }
    internal static string Manifest(string version,IEnumerable<KeyValuePair<string,byte[]>> files,int protocol=1,string product=Product,int schema=1)
    {
        string rows=string.Join(",",files.Select(p=>"{\"path\":"+Q(p.Key)+",\"sha256\":"+Q(Hash(p.Value))+",\"sizeBytes\":"+p.Value.LongLength+"}"));
        return "{\"schemaVersion\":"+schema+",\"productId\":"+Q(product)+",\"version\":"+Q(version)+",\"bridgeProtocol\":"+protocol+",\"files\":["+rows+"]}";
    }
    internal static string Feed(string version,string archive,string hash,long size,int protocol=1,string product=Product,int schema=1,int revit=2024)
        =>"{\"schemaVersion\":"+schema+",\"productId\":"+Q(product)+",\"revitVersion\":"+revit+",\"version\":"+Q(version)+",\"bridgeProtocol\":"+protocol+",\"archiveFile\":"+Q(archive)+",\"sha256\":"+Q(hash)+",\"sizeBytes\":"+size+",\"releaseNotes\":\"Independent package fixture\"}";
    internal static string Zip(string directory,string filename,IEnumerable<KeyValuePair<string,byte[]>> entries,Action<ZipArchiveEntry> configure=null)
    {
        Directory.CreateDirectory(directory);string path=Path.Combine(directory,filename);
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
            foreach(var pair in entries)
            {
                var entry=zip.CreateEntry(pair.Key,CompressionLevel.Fastest);
                configure?.Invoke(entry);
                using(var output=entry.Open())output.Write(pair.Value,0,pair.Value.Length);
            }
        return path;
    }
    internal static byte[] B(string text)=>Utf8.GetBytes(text);
    internal static KeyValuePair<string,byte[]> Item(string path,string text)=>new KeyValuePair<string,byte[]>(path,B(text));
    internal static Dictionary<string,string> Snapshot(string root)
    {
        if(!Directory.Exists(root))return new Dictionary<string,string>(StringComparer.Ordinal);
        return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p.Substring(root.Length).Replace('\\','/'),HashFile,StringComparer.Ordinal);
    }
    internal static bool SameSnapshot(Dictionary<string,string> a,Dictionary<string,string> b)
        =>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out var hash)&&hash==p.Value);
}
