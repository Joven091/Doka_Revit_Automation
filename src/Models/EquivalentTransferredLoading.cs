using System;
using System.Linq;
using System.Collections.Generic;

namespace EngineeringDashboard
{
    // A representative slice of verified primary UDL, never a secondary design case.
    public sealed class EquivalentLoadingComponent
    {
        public string Name;
        public double NominalSpacingMm,ReferenceSpacingMm,SourceValue,EquivalentValue,PositionMm,EndMm;
        public bool IsUniform;
        public double[] ReactionsKn;
    }
    public sealed class EquivalentTransferredLoading
    {
        public double ReferenceSpacingMm;
        public TransferMember Member;
        public EquivalentLoadingComponent[] Components;
        public double[] PrimaryTransferKnM;
        public double[] PrimarySelfWeightKnM;
        public string UnavailableReason;
        public bool IsAvailable=>Member!=null;
        public static EquivalentTransferredLoading Create(TowerLayout layout,int row,int stage)
        {
            var t=layout.Transfer;
            var view=new EquivalentTransferredLoading {ReferenceSpacingMm=t.EnteredUpperSpacingMm};
            Func<string,EquivalentTransferredLoading> unavailable=reason=> {view.UnavailableReason=reason;return view;};
            if(row<0 || row>=t.RowsMm.Length || stage<0 || stage>=t.CaseNames.Length || view.ReferenceSpacingMm<=0)
                return unavailable("Select a checked secondary case.");
            int source=Array.FindIndex(t.UnitLowerCases,m=>m.LoadCase==t.CaseNames[stage]);
            if(source<0 || source>=t.UnitPointLabels.Length)return unavailable("Local crossing stage: use Secondary design checks.");
            var unit=t.UnitLowerCases[source];double reference=view.ReferenceSpacingMm/1000;
            bool slabSupported=t.RowsMm[row]>=t.SlabSupportStartMm-1e-7 && t.RowsMm[row]<=t.SlabSupportEndMm+1e-7;
            var components=new List<EquivalentLoadingComponent>();
            foreach(var q in unit.Uniform)
                components.Add(new EquivalentLoadingComponent {Name="Beam concrete",IsUniform=true,PositionMm=q.StartMm,EndMm=q.EndMm,
                    NominalSpacingMm=view.ReferenceSpacingMm,ReferenceSpacingMm=view.ReferenceSpacingMm,SourceValue=q.KnPerM*reference,EquivalentValue=q.KnPerM*reference});
            for(int i=0;i<unit.Points.Length;i++)
            {
                if(!slabSupported)continue;
                string name=t.UnitPointLabels[source][i];bool left=name.StartsWith("Left ",StringComparison.Ordinal);
                double pitch=t.SlabSpacing(left);var p=unit.Points[i];
                components.Add(new EquivalentLoadingComponent {Name=name,PositionMm=p.PositionMm,NominalSpacingMm=pitch,
                    ReferenceSpacingMm=view.ReferenceSpacingMm,SourceValue=p.Kn*pitch/1000,EquivalentValue=p.Kn*reference});
            }
            var uniform=components.Where(c=>c.IsUniform).Select(c=>new BeamDistributedLoad {StartMm=c.PositionMm,EndMm=c.EndMm,KnPerM=c.EquivalentValue}).ToArray();
            var points=components.Where(c=>!c.IsUniform).Select(c=>new BeamPointLoad {PositionMm=c.PositionMm,Kn=c.EquivalentValue}).ToArray();
            var analysis=LoadedBeamAnalysis.AnalyzeSigned(unit.LengthMm,unit.SupportsMm,uniform,points,TowerOptimizer.PairEI/2,layout.DeflectionDenominator);
            var primary=new double[2];var selfWeight=new double[2];
            for(int line=0;line<2;line++)
            {
                var member=t.PrimaryCases[stage].Where(m=>m.Name.EndsWith(line==0 ? " L" : " R",StringComparison.Ordinal)
                    && t.RowsMm[row]>=m.StartMm-1e-7 && t.RowsMm[row]<m.StartMm+m.LengthMm-1e-7).OrderByDescending(m=>m.StartMm).FirstOrDefault();
                if(member==null)return unavailable("Outside primary coverage: external end supports.");
                double local=t.RowsMm[row]-member.StartMm;
                selfWeight[line]=member.PrimarySelfWeightKnM;
                primary[line]=member.Uniform.Where(q=>q.StartMm<=local+1e-7 && q.EndMm>local+1e-7).Sum(q=>q.KnPerM)-member.PrimarySelfWeightKnM;
                // A localized crossing or unmatched stage must never be relabelled
                // as baseline equivalent loading merely to force an identity.
                if(Math.Abs(analysis.Reactions[line]/reference-primary[line])>1e-7*Math.Max(1,Math.Abs(primary[line])))
                    return unavailable("Localized crossing loads: use Secondary design checks.");
            }
            foreach(var c in components)
            {
                var qs=c.IsUniform ? new[] {new BeamDistributedLoad {StartMm=c.PositionMm,EndMm=c.EndMm,KnPerM=c.EquivalentValue}} : new BeamDistributedLoad[0];
                var ps=c.IsUniform ? new BeamPointLoad[0] : new[] {new BeamPointLoad {PositionMm=c.PositionMm,Kn=c.EquivalentValue}};
                c.ReactionsKn=LoadedBeamAnalysis.AnalyzeSigned(unit.LengthMm,unit.SupportsMm,qs,ps,TowerOptimizer.PairEI/2,layout.DeflectionDenominator).Reactions;
            }
            view.Member=new TransferMember {Name=unit.Name,LoadCase=unit.LoadCase,H20Count=1,StartMm=unit.StartMm,LengthMm=unit.LengthMm,
                SupportsMm=unit.SupportsMm,Uniform=uniform,Points=points,Analysis=analysis};
            view.Components=components.ToArray();view.PrimaryTransferKnM=primary;view.PrimarySelfWeightKnM=selfWeight;return view;
        }
    }
}
