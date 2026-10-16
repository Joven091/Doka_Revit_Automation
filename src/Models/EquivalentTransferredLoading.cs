using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace EngineeringDashboard
{
    internal static class LoadValue
    {
        internal static string Format(double value,int decimals=3)
        {
            double step=Math.Pow(10,-decimals);
            if(value!=0 && Math.Abs(value)<step/2)return (value<0 ? "−<" : "<")+step.ToString("0."+new string('0',decimals),CultureInfo.CurrentCulture);
            return value.ToString("0."+new string('#',decimals),CultureInfo.CurrentCulture);
        }
    }
    // A representative slice of verified primary UDL, never a secondary design case.
    public sealed class EquivalentLoadingComponent
    {
        public string Name;
        public double NominalSpacingMm,ReferenceSpacingMm,SourceValue,EquivalentValue,PositionMm,EndMm;
        public bool IsUniform;
        public string SpacingLabel="Slab secondary spacing";
        public string SourceCalculation;
        public double AllocationFactor=1;
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
        public bool IsDesignSlice;
        public bool HasPositionedLoads;
        public bool IsAvailable=>Member!=null;
        public static EquivalentTransferredLoading Create(TowerLayout layout,int row,int stage)
        {
            var t=layout.Transfer;
            var view=new EquivalentTransferredLoading {ReferenceSpacingMm=t.EnteredUpperSpacingMm};
            Func<string,EquivalentTransferredLoading> unavailable=reason=> {view.UnavailableReason=reason;return view;};
            if(row<0 || row>=t.RowsMm.Length || stage<0 || stage>=t.CaseNames.Length || view.ReferenceSpacingMm<=0)
                return unavailable("Select a checked secondary case.");
            if(t.PrimaryPlan!=null || t.FullLoadingOnly && t.UnitLowerCases.Length==0)
                return DesignSlice(layout,row,stage);
            int source=Array.FindIndex(t.UnitLowerCases,m=>m.LoadCase==t.CaseNames[stage]);
            if(source<0 || source>=t.UnitPointLabels.Length)return t.FullLoadingOnly ? DesignSlice(layout,row,stage) : unavailable("This case uses the actual checked member loading.");
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
            components.RemoveAll(c=>Math.Abs(c.EquivalentValue)<=1e-10);
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
                    return t.FullLoadingOnly ? DesignSlice(layout,row,stage) : unavailable("The actual primary model differs from the reference-spacing conversion; show actual checked member loads.");
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
        // This is a representative design slice of the checked primary zones.
        // Beam concrete is shared equally; assigned slab/crossing transfers act
        // at the receiving primary bearings. Physical secondary loads and their
        // response plots remain untouched. No scaling of an unrelated physical
        // row is used to manufacture the primary reaction/spacing identity.
        private static EquivalentTransferredLoading PositionedSlice(TowerLayout layout,int row,int stage)
        {
            var t=layout.Transfer;var plan=t.PrimaryPlan;var actual=t.LowerCases[stage][row];
            double pitch=t.EnteredUpperSpacingMm,reference=pitch/1000,x=t.RowsMm[row];
            var target=new double[2];var weight=new double[2];
            for(int line=0;line<2;line++)
            {
                var primary=t.PrimaryCases[stage].FirstOrDefault(m=>m.Name.EndsWith(line==0 ? " L" : " R",StringComparison.Ordinal) && x>=m.StartMm-1e-7 && x<m.StartMm+m.LengthMm-1e-7);
                if(primary==null)return new EquivalentTransferredLoading {ReferenceSpacingMm=pitch,UnavailableReason="Outside primary coverage: external end supports."};
                double local=x-primary.StartMm;weight[line]=primary.PrimarySelfWeightKnM;
                target[line]=primary.Uniform.Where(q=>q.StartMm<=local+1e-7 && q.EndMm>local+1e-7).Sum(q=>q.KnPerM)-weight[line];
            }
            var components=new List<EquivalentLoadingComponent>();
            foreach(var source in plan.LoadsAt(x))
            {
                if(source.ValuePerMetre==0 || source.AllocationFactor==0)continue;
                double nominal=source.IsSlabFoot || source.IsSlabEdge ? t.SlabSpacing(source.Bay==plan.Beam-1) : pitch;
                double sourceValue=source.ValuePerMetre*nominal/1000;
                var component=new EquivalentLoadingComponent {Name=source.Name,IsUniform=source.IsUniform,PositionMm=source.PositionMm,EndMm=source.EndMm,
                    NominalSpacingMm=nominal,ReferenceSpacingMm=pitch,SourceValue=sourceValue,EquivalentValue=source.ValuePerMetre*reference*source.AllocationFactor,AllocationFactor=source.AllocationFactor,
                    SpacingLabel=source.IsSlabFoot || source.IsSlabEdge ? "Slab secondary spacing" : "Source reference spacing"};
                if(source.IsSlabFoot || source.IsSlabEdge)
                    component.SourceCalculation=$"{LoadValue.Format(source.AreaLoadKnM2,5)} × {source.SourceLengthMm/1000:0.#####} × {nominal/1000:0.#####}"+(source.IsSlabFoot ? " / 2" : "")+$" = {LoadValue.Format(sourceValue,5)} kN";
                else if(source.Bay<0 && source.IsUniform)
                    component.SourceCalculation=$"{LoadValue.Format(source.ValuePerMetre,5)} × {reference:0.#####} = {LoadValue.Format(component.EquivalentValue,5)} kN/m";
                component.ReactionsKn=EngineerPrimaryPlan.Reactions(plan.SecondaryBasis,new[] {new PositionedSecondaryLoad {IsUniform=component.IsUniform,PositionMm=component.PositionMm,EndMm=component.EndMm,ValuePerMetre=component.EquivalentValue}});
                components.Add(component);
            }
            var uniform=components.Where(c=>c.IsUniform).Select(c=>new BeamDistributedLoad {StartMm=c.PositionMm,EndMm=c.EndMm,KnPerM=c.EquivalentValue}).ToArray();
            var points=components.Where(c=>!c.IsUniform).Select(c=>new BeamPointLoad {PositionMm=c.PositionMm,Kn=c.EquivalentValue}).ToArray();
            var basis=plan.SecondaryBasis;
            var analysis=LoadedBeamAnalysis.AnalyzeSigned(basis.LengthMm,basis.SupportsMm,uniform,points,actual.EI,layout.DeflectionDenominator);
            for(int line=0;line<2;line++)
                if(Math.Abs(analysis.Reactions[line]/reference-target[line])>1e-7*Math.Max(1,Math.Abs(target[line])))
                    return new EquivalentTransferredLoading {ReferenceSpacingMm=pitch,UnavailableReason="The positioned reference loads do not match the selected primary transfer."};
            return new EquivalentTransferredLoading {IsDesignSlice=true,HasPositionedLoads=true,ReferenceSpacingMm=pitch,PrimaryTransferKnM=target,PrimarySelfWeightKnM=weight,Components=components.ToArray(),
                Member=new TransferMember {Name=actual.Name,LoadCase=actual.LoadCase,H20Count=1,StartMm=actual.StartMm,LengthMm=basis.LengthMm,SupportsMm=basis.SupportsMm,Uniform=uniform,Points=points,Analysis=analysis}};
        }
        private static EquivalentTransferredLoading DesignSlice(TowerLayout layout,int row,int stage)
        {
            var t=layout.Transfer;var actual=t.LowerCases[stage][row];
            if(t.PrimaryPlan?.HasPositionedLoads==true)return PositionedSlice(layout,row,stage);
            double pitch=t.EnteredUpperSpacingMm,reference=pitch/1000,x=t.RowsMm[row];
            var target=new double[2];var weight=new double[2];
            for(int line=0;line<2;line++)
            {
                var primary=t.PrimaryCases[stage].FirstOrDefault(m=>m.Name.EndsWith(line==0 ? " L" : " R",StringComparison.Ordinal) && x>=m.StartMm-1e-7 && x<m.StartMm+m.LengthMm-1e-7);
                if(primary==null)return new EquivalentTransferredLoading {ReferenceSpacingMm=pitch,UnavailableReason="Outside primary coverage: external end supports."};
                double local=x-primary.StartMm;weight[line]=primary.PrimarySelfWeightKnM;
                target[line]=primary.Uniform.Where(q=>q.StartMm<=local+1e-7 && q.EndMm>local+1e-7).Sum(q=>q.KnPerM)-weight[line];
            }
            var parts=new List<EquivalentLoadingComponent>();var plan=t.PrimaryPlan;
            double middle=actual.SupportsMm.Average(),width,beamForce;
            if(plan!=null)
            {
                var beam=plan.Network.Beams[plan.Beam];width=beam.WidthMm;
                beamForce=plan.Network.AreaLoad(beam.DepthMm)*width/1000*reference;
            }
            else
            {
                var q=actual.Uniform.FirstOrDefault();width=q==null ? 0 : q.EndMm-q.StartMm;
                double design=Math.Max(pitch,t.TributaryWidthsMm.Length>row ? t.TributaryWidthsMm[row] : pitch)/1000;
                beamForce=q==null ? 0 : q.KnPerM*width/1000*reference/design;
            }
            width=Math.Min(width,2*Math.Min(middle,actual.LengthMm-middle));
            if(width>1e-7 && Math.Abs(beamForce)>1e-10)
                parts.Add(new EquivalentLoadingComponent {Name="Beam concrete",IsUniform=true,PositionMm=middle-width/2,EndMm=middle+width/2,NominalSpacingMm=pitch,ReferenceSpacingMm=pitch,SourceValue=beamForce*1000/width,EquivalentValue=beamForce*1000/width,SpacingLabel="Beam secondary spacing"});
            for(int line=0;line<2;line++)
            {
                double transfer=target[line]*reference-beamForce/2;
                if(Math.Abs(transfer)<=1e-10)continue;
                int bay=plan==null ? -1 : line==0 ? plan.Beam-1 : plan.Beam;
                var crossings=plan?.Network.CrossingBeams.Select((c,i)=>new {C=c,I=i}).Where(c=>(c.C.BayNumber==plan.Beam || c.C.BayNumber==plan.Beam+1) && x>=c.C.PositionMm-c.C.WidthMm/2-1e-7 && x<c.C.PositionMm+c.C.WidthMm/2-1e-7).ToArray();
                bool crossing=crossings!=null && crossings.Length>0;
                double sourcePitch=crossing || plan==null ? pitch : t.SlabSpacing(line==0);
                string name=crossing ? string.Join(" + ",crossings.Select(c=>"X"+(c.I+1)))+" transfer" : plan!=null && bay>=0 && bay<plan.Network.Bays.Length ? $"S{bay+1} slab transfer" : "End-strip transfer";
                parts.Add(new EquivalentLoadingComponent {Name=name,PositionMm=actual.SupportsMm[line],NominalSpacingMm=sourcePitch,ReferenceSpacingMm=pitch,SourceValue=transfer*sourcePitch/pitch,EquivalentValue=transfer,SpacingLabel=crossing || plan==null ? "Source reference spacing" : "Slab secondary spacing"});
            }
            var uniform=parts.Where(c=>c.IsUniform).Select(c=>new BeamDistributedLoad {StartMm=c.PositionMm,EndMm=c.EndMm,KnPerM=c.EquivalentValue}).ToArray();
            var points=parts.Where(c=>!c.IsUniform).Select(c=>new BeamPointLoad {PositionMm=c.PositionMm,Kn=c.EquivalentValue}).ToArray();
            var analysis=LoadedBeamAnalysis.AnalyzeSigned(actual.LengthMm,actual.SupportsMm,uniform,points,actual.EI,layout.DeflectionDenominator);
            for(int line=0;line<2;line++)
                if(Math.Abs(analysis.Reactions[line]/reference-target[line])>1e-7*Math.Max(1,Math.Abs(target[line])))throw new InvalidOperationException("Reference-spacing reaction does not reproduce the primary UDL.");
            foreach(var c in parts)
                c.ReactionsKn=LoadedBeamAnalysis.AnalyzeSigned(actual.LengthMm,actual.SupportsMm,c.IsUniform ? new[] {new BeamDistributedLoad {StartMm=c.PositionMm,EndMm=c.EndMm,KnPerM=c.EquivalentValue}} : new BeamDistributedLoad[0],c.IsUniform ? new BeamPointLoad[0] : new[] {new BeamPointLoad {PositionMm=c.PositionMm,Kn=c.EquivalentValue}},actual.EI,layout.DeflectionDenominator).Reactions;
            return new EquivalentTransferredLoading {IsDesignSlice=true,ReferenceSpacingMm=pitch,PrimaryTransferKnM=target,PrimarySelfWeightKnM=weight,Components=parts.ToArray(),Member=new TransferMember {Name=actual.Name,LoadCase=actual.LoadCase,H20Count=1,StartMm=actual.StartMm,LengthMm=actual.LengthMm,SupportsMm=actual.SupportsMm,Uniform=uniform,Points=points,Analysis=analysis}};
        }
    }
}
