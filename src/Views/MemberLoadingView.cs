using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineeringDashboard
{
    internal sealed partial class MemberLoadingView
    {
        internal MemberLoadingView(TowerDiagramWindow.MemberLoadingPlot plot,double length,double[] supports,double[] reactions,double ei,string basis,Func<BeamDistributedLoad,LoadCalculation> describe=null,Func<BeamPointLoad,LoadCalculation> describePoint=null)
            : this(plot, supports, reactions, basis, (u,p) => LoadedBeamAnalysis.AnalyzeSigned(length,supports,u,p,ei,270).Reactions, describe, describePoint) { }

        internal static LoadCalculation Tributary(TransferMember member,EngineerPrimaryPlan plan,int line,BeamDistributedLoad q)
        {
            var n=plan.Network;var beam=n.Beams[plan.Beam];int bay=line==0 ? plan.Beam-1 : plan.Beam;
            double position=member.StartMm+(q.StartMm+q.EndMm)/2;
            var crossing=n.CrossingBeams.Select((c,i)=>new {C=c,I=i}).FirstOrDefault(x=>x.C.BayNumber==bay+1 && position>=x.C.PositionMm-x.C.WidthMm/2 && position<=x.C.PositionMm+x.C.WidthMm/2);
            bool slab=bay>=0 && bay<n.Bays.Length && plan.CarriesBay(bay);
            var result=new LoadCalculation {Name=crossing!=null ? $"Beam + X{crossing.I+1}" : slab ? "Beam + Slab" : "Beam"};
            if(plan.HasPositionedLoads)
            {
                var at=n.CrossingBeams.Select((c,i)=>new {C=c,I=i}).Where(c=>(c.C.BayNumber==plan.Beam || c.C.BayNumber==plan.Beam+1) && position>=c.C.PositionMm-c.C.WidthMm/2 && position<c.C.PositionMm+c.C.WidthMm/2).ToArray();
                result.Name=at.Length>0 ? "Beam + "+string.Join(" + ",at.Select(c=>"X"+(c.I+1))) : plan.SlabLoads.Count>0 ? "Beam + Slab" : "Beam";
                double pitch=plan.ReferenceSpacingMm/1000;
                double reaction=EngineerPrimaryPlan.Reactions(plan.SecondaryBasis,plan.LoadsAt(position))[line]*pitch;
                double primaryTransfer=at.Where(c=>plan.PrimaryBearingTransfers.ContainsKey(c.I)).Sum(c=>plan.PrimaryBearingTransfers[c.I][line]);
                result.Lines.Add($"Positioned beam-secondary reaction: {LoadValue.Format(reaction,5)} kN");
                result.Lines.Add($"Beam secondary spacing: {plan.ReferenceSpacingMm:0.###} mm ({pitch:0.###} m)");
                foreach(var c in at.Where(c=>plan.PrimaryBearingTransfers.ContainsKey(c.I) && Math.Abs(plan.PrimaryBearingTransfers[c.I][line])>1e-10))
                {
                    double transfer=plan.PrimaryBearingTransfers[c.I][line],width=c.C.WidthMm/1000;
                    result.Lines.Add($"X{c.I+1} primary-bearing reactions: {LoadValue.Format(transfer*width,5)} / {width:0.###} = {LoadValue.Format(transfer,5)} kN/m");
                }
                result.Lines.Add(Math.Abs(primaryTransfer)<1e-10 ? $"UDL = {LoadValue.Format(reaction,5)} / {pitch:0.###} = {LoadValue.Format(reaction/pitch,5)} kN/m"
                    : $"UDL = {LoadValue.Format(reaction,5)} / {pitch:0.###} + {LoadValue.Format(primaryTransfer,5)} = {LoadValue.Format(reaction/pitch+primaryTransfer,5)} kN/m");
                foreach(var c in at)result.Lines.Add($"X{c.I+1} footprint: {c.C.PositionMm-c.C.WidthMm/2:0.###}–{c.C.PositionMm+c.C.WidthMm/2:0.###} mm · full width {c.C.WidthMm:0.###} mm");
                foreach(var c in at)if(plan.CrossingResultantOffsetsMm.TryGetValue(c.I,out var offsets) && Math.Abs(offsets[line])>.001)
                    result.Lines.Add($"Physical transfer resultant offset: {offsets[line]:0.###} mm; full-width UDL design zone.");
                if(Math.Abs(member.PrimarySelfWeightKnM)>1e-10)result.Lines.Add($"Primary self-weight: {LoadValue.Format(member.PrimarySelfWeightKnM,5)} kN/m");
                return result;
            }
            if(plan.IsResolved && plan.CrossingTransfers.Count>0)
            {
                double own=n.AreaLoad(beam.DepthMm)*beam.WidthMm/2000;
                var at=n.CrossingBeams.Select((c,i)=>new {C=c,I=i}).Where(c=>(c.C.BayNumber==plan.Beam || c.C.BayNumber==plan.Beam+1) && position>=c.C.PositionMm-c.C.WidthMm/2 && position<=c.C.PositionMm+c.C.WidthMm/2).ToArray();
                if(at.Length>0)result.Name="Beam + "+string.Join(" + ",at.Select(c=>"X"+(c.I+1)));
                result.Lines.Add($"Beam: {n.AreaLoad(beam.DepthMm):0.#####} × {beam.WidthMm/1000:0.#####} ÷ 2 = {own:0.#####} kN/m");
                result.Lines.Add($"Assigned {(at.Length>0 ? "crossing" : "slab")} transfer: {LoadValue.Format(q.KnPerM-member.PrimarySelfWeightKnM-own,5)} kN/m");
                foreach(var c in at)result.Lines.Add($"X{c.I+1} footprint: {c.C.PositionMm-c.C.WidthMm/2:0.###}–{c.C.PositionMm+c.C.WidthMm/2:0.###} mm · full width {c.C.WidthMm:0.###} mm");
                if(at.Length>0)result.Lines.Add("Dedicated tower loads are allocated separately and counted once.");
                if(Math.Abs(member.PrimarySelfWeightKnM)>1e-10)result.Lines.Add($"Primary self-weight: {LoadValue.Format(member.PrimarySelfWeightKnM,5)} kN/m");
                return result;
            }
            result.Lines.Add(Pressure(n,$"B{plan.Beam+1}",beam.DepthMm));
            result.Lines.Add($"Beam: {n.AreaLoad(beam.DepthMm):0.#####} × {beam.WidthMm/1000:0.#####} ÷ 2 = {n.AreaLoad(beam.DepthMm)*beam.WidthMm/2000:0.#####} kN/m");
            if(crossing!=null || slab)
            {
                double depth=crossing?.C.DepthMm??n.Bays[bay].ThicknessMm,width=n.Bays[bay].WidthMm/2000;
                string name=crossing!=null ? $"X{crossing.I+1}" : $"S{bay+1}";
                result.Lines.Add(Pressure(n,name,depth));
                result.Lines.Add($"Tributary width: {n.Bays[bay].WidthMm:0.###} ÷ 2 = {width*1000:0.###} mm");
                result.Lines.Add($"{name}: {n.AreaLoad(depth):0.#####} × {width:0.#####} = {n.AreaLoad(depth)*width:0.#####} kN/m");
            }
            if(crossing!=null)result.Lines.Add($"X{crossing.I+1} footprint: {crossing.C.PositionMm-crossing.C.WidthMm/2:0.###}–{crossing.C.PositionMm+crossing.C.WidthMm/2:0.###} mm · full width {crossing.C.WidthMm:0.###} mm");
            if(Math.Abs(member.PrimarySelfWeightKnM)>1e-10)result.Lines.Add($"Primary self-weight: {member.PrimarySelfWeightKnM:0.#####} kN/m");
            result.Lines.Add($"Beam position {member.StartMm+q.StartMm:0.###}–{member.StartMm+q.EndMm:0.###} mm");return result;
        }
        private static string Pressure(TransferNetworkInput n,string name,double depth)
        {
            double dead=25*depth/1000;
            string allowance=n.CustomLoads ? $"{n.LiveKnM2:0.#####} + {n.FormworkKnM2:0.#####}" : $"{BeamCalculatorViewModel.HeapingLoad(dead):0.#####} + 0.75 + 0.4";
            return $"{name} pressure: {depth/1000:0.#####} × 25 + {allowance} = {n.AreaLoad(depth):0.#####} kN/m²";
        }
    }
}
