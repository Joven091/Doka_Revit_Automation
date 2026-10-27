using System;
using System.Collections.Generic;
using System.Linq;
using EngineeringDashboard.Top50;

internal static class Top50RecordedFixture
{
    // Test-only transcription from the user recording at 9.5s. Never linked into the shipping add-in.
    // Group IDs/membership/installed totals are visible. G03 patterns are visible; other patterns
    // below are a reconstruction consistent with the recorded aggregate values, not inspected source code.
    internal static Proposal Create(ImportSnapshot input)
    {
        string[] groups={
            "4054B37,4054B78,4054B96,4054BAF,4054BC8,4054B87",
            "4054B32,4054B41,4054B46,4054BA0",
            "4054B7D,4054B8C,4054BA5,4054BBE,4054BD7,4054BF0,4054C09,4054B4B,4054B50,4054B82",
            "4054B3C,4054B73,4054B91,4054B55,4054BB9",
            "4054BE1,4054BFA,4054B9B,4054B5A,4054BD2",
            "4054C13,4054C2C,4054C45,4054C5E,4054C68",
            "4054BAA,4054B5F,4054B64,4054BEB",
            "4054BB4,4054BC3,4054BCD,4054BDC,4054BE6,4054C04",
            "4054BF5,4054BFF,4054C0E,4054C22,4054C3B,4054C54,4054B69,4054C1D",
            "4054C18,4054C27,4054C31,4054C40,4054C4A,4054C36",
            "4054C59,4054C63,4054B6E,4054C4F"};
        var walls=input.Walls.ToDictionary(w=>w.Id);
        PanelPattern Pattern(WallRow row)
        {
            int[] panels;
            switch(row.LengthMicrometres)
            {
                case 1600000:panels=new[]{750,500};break;
                case 2810000:panels=new[]{2000,750};break;
                case 7200000:panels=new[]{3500,3500};break;
                case 9200000:panels=new[]{4500,4500};break;
                case 19200000:case 19204608:panels=new[]{4500,4500,4500,3500,2000};break;
                default:throw new InvalidOperationException("Unexpected wall length in recorded fixture.");
            }
            var gaps=new long[panels.Length-1];gaps[0]=row.LengthMicrometres-panels.Sum(p=>(long)p*1000);return new PanelPattern(panels,gaps);
        }
        return new Proposal("S3",new Dictionary<int,int>{{500,1},{750,1},{2000,5},{3500,5},{4500,15}},groups.Select(group=>new CastingGroup(group.Split(',').Select(handle=>new WallArrangement("DXF-"+handle,Pattern(walls["DXF-"+handle])))))){Rank=1,Pareto=true,RetainedModelStatus="Recorded optimal (retained model)",RetainedObjective=11,RetainedBound=11};
    }
}
