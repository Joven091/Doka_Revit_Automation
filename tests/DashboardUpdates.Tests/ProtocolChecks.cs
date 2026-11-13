using System;
using System.IO;
using System.Linq;
using System.Text;
using EngineeringDashboard.DesktopHost;

internal static class ProtocolChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        string id="0123456789abcdef0123456789abcdef",session="fedcba9876543210fedcba9876543210",token=new string('a',64);
        string payload="{\"document\":\"Prøject العربية\",\"mm\":650.125}";
        string json="{\"ProtocolVersion\":1,\"RequestId\":"+IndependentFixtures.Q(id)+",\"SessionId\":"+IndependentFixtures.Q(session)+",\"Token\":"+IndependentFixtures.Q(token)+",\"Operation\":\"beam.read\",\"Payload\":"+IndependentFixtures.Q(payload)+"}";
        var request=BridgeProtocol.Decode<BridgeRequest>(IndependentFixtures.B(json));
        check(request.ProtocolVersion==1&&request.RequestId==id&&request.SessionId==session&&request.Token==token&&request.Operation=="beam.read"&&request.Payload==payload,"independent JSON request preserves protocol, identity, Unicode and decimal payload text");
        BridgeProtocol.ValidateRequest(request,session,token);
        check(true,"valid request authenticates against its expected session and secret");
        foreach(string op in new[]{"hello","access.ensure","access.manage","context","beam.read","top50.read","top50.refresh","top50.highlight","helpers.apply","updates.integration"})
        {var r=Request(id,session,token);r.Operation=op;BridgeProtocol.ValidateRequest(r,session,token);check(true,"approved bridge operation admitted: "+op);}
        var bad=Request(id,session,token);bad.ProtocolVersion=2;reject(()=>BridgeProtocol.ValidateRequest(bad,session,token),"future protocol request rejected");
        bad=Request(id,session,token);bad.RequestId="not-a-request-id";reject(()=>BridgeProtocol.ValidateRequest(bad,session,token),"malformed request identity rejected");
        bad=Request(id,session,token);bad.SessionId=id;reject(()=>BridgeProtocol.ValidateRequest(bad,session,token),"another Dashboard session rejected");
        bad=Request(id,session,token);bad.Token=new string('b',64);reject(()=>BridgeProtocol.ValidateRequest(bad,session,token),"wrong equal-length secret rejected");
        bad=Request(id,session,token);bad.Token="";reject(()=>BridgeProtocol.ValidateRequest(bad,session,token),"missing secret rejected");
        bad=Request(id,session,token);bad.Operation="arbitrary.execute";reject(()=>BridgeProtocol.ValidateRequest(bad,session,token),"unapproved operation rejected");
        bad=Request(id,session,token);bad.Payload=null;reject(()=>BridgeProtocol.ValidateRequest(bad,session,token),"missing payload envelope rejected");
        reject(()=>BridgeProtocol.ValidateRequest(null,session,token),"null request rejected");
        reject(()=>BridgeProtocol.Decode<BridgeRequest>(IndependentFixtures.B("{}")),"required request members enforced");
        reject(()=>BridgeProtocol.Decode<BridgeRequest>(new byte[0]),"empty JSON frame rejected");
        reject(()=>BridgeProtocol.Decode<BridgeRequest>(null),"null JSON frame rejected");
        reject(()=>BridgeProtocol.Decode<BridgeRequest>(IndependentFixtures.B(json+"{}")),"concatenated JSON document rejected");
        var reply=new BridgeReply{RequestId=id,SessionId=session,Payload=payload};BridgeProtocol.ValidateReply(reply,id,session);
        check(BridgeProtocol.Decode<BridgeReply>(BridgeProtocol.Encode(reply)).Payload==payload,"reply round trip preserves detached payload");
        reply.RequestId=session;reject(()=>BridgeProtocol.ValidateReply(reply,id,session),"reply for another request rejected");reply.RequestId=id;
        reply.SessionId=id;reject(()=>BridgeProtocol.ValidateReply(reply,id,session),"reply for an expired session rejected");reply.SessionId=session;
        reply.ProtocolVersion=2;reject(()=>BridgeProtocol.ValidateReply(reply,id,session),"future reply protocol rejected");
        reject(()=>BridgeProtocol.ValidateReply(null,id,session),"null reply rejected");

        byte[] bytes=IndependentFixtures.B("detached payload العربية");
        using(var stream=new MemoryStream())
        {
            BridgeProtocol.WriteFrame(stream,bytes);byte[] encoded=stream.ToArray();
            int n=bytes.Length;
            check(encoded.Length==n+4&&encoded[0]==(byte)n&&encoded[1]==(byte)(n>>8)&&encoded[2]==(byte)(n>>16)&&encoded[3]==(byte)(n>>24)&&encoded.Skip(4).SequenceEqual(bytes),"writer matches independently specified little-endian length frame");
            using(var fragmented=new FragmentedStream(encoded))check(BridgeProtocol.ReadFrame(fragmented).SequenceEqual(bytes),"fragmented pipe reads reconstruct exactly one complete frame");
            using(var pair=new MemoryStream()){pair.Write(encoded,0,encoded.Length);pair.Write(encoded,0,encoded.Length);pair.Position=0;check(BridgeProtocol.ReadFrame(pair).SequenceEqual(bytes)&&BridgeProtocol.ReadFrame(pair).SequenceEqual(bytes)&&pair.Position==pair.Length,"consecutive frames retain their boundaries");}
            for(int length=0;length<4;length++){byte[] shortHeader=encoded.Take(length).ToArray();reject(()=>BridgeProtocol.ReadFrame(new MemoryStream(shortHeader)),"truncated frame header rejected: "+length);}
            reject(()=>BridgeProtocol.ReadFrame(new MemoryStream(encoded.Take(encoded.Length-1).ToArray())),"truncated payload rejected");
        }
        foreach(int length in new[]{0,-1,BridgeProtocol.MaxFrameBytes+1,int.MaxValue})
        {
            byte[] header={(byte)length,(byte)(length>>8),(byte)(length>>16),(byte)(length>>24)};
            reject(()=>BridgeProtocol.ReadFrame(new MemoryStream(header)),"invalid announced frame size rejected: "+length);
        }
        reject(()=>BridgeProtocol.WriteFrame(new MemoryStream(),null),"null write frame rejected");
        reject(()=>BridgeProtocol.WriteFrame(new MemoryStream(),new byte[0]),"zero-length write frame rejected");
        reject(()=>BridgeProtocol.WriteFrame(new MemoryStream(),new byte[BridgeProtocol.MaxFrameBytes+1]),"oversize write frame rejected before emission");

        var helper=Helper();helper.Validate();check(true,"valid detached beam helper update admitted");
        helper=Helper();helper.Kind="slab";helper.SlabThicknessMm=300;helper.PrimarySpacingMm=1000;helper.Validate();check(true,"valid detached slab helper update admitted");
        helper=Helper();helper.DocumentKey="";reject(helper.Validate,"helper write requires original document identity");
        helper=Helper();helper.Kind="arbitrary";reject(helper.Validate,"unsupported helper write kind rejected");
        helper=Helper();helper.PropSpacingMm=0;reject(helper.Validate,"zero prop spacing rejected");
        helper=Helper();helper.WidthMm=0;reject(helper.Validate,"beam helper requires positive width");
        helper=Helper();helper.Kind="slab";helper.SlabThicknessMm=300;helper.PrimarySpacingMm=0;reject(helper.Validate,"slab helper requires positive primary spacing");
        foreach(string field in new[]{"WidthMm","DepthMm","SlabThicknessMm","PropSpacingMm","PrimaryCantileverMm","PrimarySpacingMm"})
        foreach(double value in new[]{double.NaN,double.PositiveInfinity,-1,100001})
        {
            var h=Helper();typeof(HelperApplyRequest).GetProperty(field).SetValue(h,value,null);reject(h.Validate,"invalid helper dimension rejected: "+field+"="+value);
        }
    }
    static BridgeRequest Request(string id,string session,string token)=>new BridgeRequest{RequestId=id,SessionId=session,Token=token,Operation="beam.read",Payload="null"};
    static HelperApplyRequest Helper()=>new HelperApplyRequest{DocumentKey="controlled-project",Kind="beam",WidthMm=650,DepthMm=1150,PropSpacingMm=1000,PrimaryCantileverMm=150};
    sealed class FragmentedStream:MemoryStream
    {
        internal FragmentedStream(byte[] bytes):base(bytes){}
        public override int Read(byte[] buffer,int offset,int count)=>base.Read(buffer,offset,Math.Min(count,3));
    }
}
