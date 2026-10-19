using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineeringDashboard
{
    public static partial class CrossBeamSupportOptimizer
    {
        // A bounded setting-out pass on the selected, checked layout. It retains stock,
        // design spacing and main-primary UDLs rather than repeating the network search.
        internal static void BalanceSettingOut(TowerInput common,TransferNetworkResult result)
        {
            if(common.EngineerWorkflow)CentreMainPrimaries(common,result);
            if(result.CrossSupports.Length==0)return;
            if(common.EngineerWorkflow)CentreCrossingFrames(common,result);
            var check=common.CopyForSearch();check.Trace=null;check.Diagnostics=null;
            for(int index=0;index<result.CrossSupports.Length;index++)
            {
                common.Trace?.CheckUserCancellation();
                var original=result.CrossSupports[index];var tower=original.Towers;
                if(tower==null || tower.StockPlan.Pieces.Length!=1 || tower.PositionsMm.Length!=2)continue;
                var stock=tower.StockPlan.Pieces[0];
                double delta=stock.StartMm+stock.StockLengthMm/2-(tower.PositionsMm[0]+tower.PositionsMm[1])/2;
                if(Math.Abs(delta)<1e-7)continue;
                var layout=Copy(tower);layout.Transfer=tower.Transfer.Copy();
                layout.PositionsMm=tower.PositionsMm.Select(x=>x+delta).ToArray();
                if(layout.PositionsMm[0]-stock.StartMm<common.StockEndSetbackMm-1e-7)continue;
                layout.Transfer.PrimaryCases=tower.Transfer.PrimaryCases.Select(stage=>stage.Select(m=>new TransferMember {
                    PrimaryMaterial=m.PrimaryMaterial,Name=m.Name,H20Count=m.H20Count,StartMm=m.StartMm,LengthMm=m.LengthMm,
                    PrimarySelfWeightKnM=m.PrimarySelfWeightKnM,Uniform=m.Uniform,Points=m.Points,SupportsMm=m.SupportsMm.Select(x=>x+delta).ToArray()
                }).ToArray()).ToArray();
                double origin=original.Bearings.First(b=>b.IsDedicated && b.Label.StartsWith("Dedicated P")).Ymm-tower.PositionsMm[0];
                double centre=original.Bearings.Where(b=>b.IsDedicated && b.Label.StartsWith("Dedicated P")).Average(b=>b.Xmm);
                var allowance=new CrossShutterAllowance(original.NearShutterClearanceMm,original.FarShutterClearanceMm);
                var balanced=FinishDedicated(check,result,original.CrossingIndex,origin,centre-original.Input.PositionMm,
                    original.TowerSpanOffsetMm+delta,layout,check,out string failure,original.PrimaryNearExtensionMm,original.PrimaryFarExtensionMm,allowance:allowance);
                if(balanced==null)continue;
                balanced.Solids=balanced.Solids.Concat(original.Solids.Where(s=>s.Member==null && !s.IsLeg && !s.IsTowerEnvelope)).ToArray();
                balanced.Bearings=balanced.Bearings.Concat(original.Bearings.Where(b=>!b.IsDedicated)).ToArray();
                balanced.LoadKn=original.LoadKn;balanced.SlabEdgeLoadKn=original.SlabEdgeLoadKn;balanced.SharedFailure=original.SharedFailure;
                // Only receiving secondaries may change. Frames, slab feet, concrete,
                // other crossings and plywood-joint rows remain fixed.
                var otherCrossings=result.CrossSupports.Where((s,i)=>i!=index).SelectMany(s=>s.Solids).ToArray();
                if(!TryAdjustSecondaries(result,balanced.Solids,otherCrossings,out TowerLayout[] beams,common.EngineerWorkflow))continue;
                balanced.MovementMm=DedicatedMovement(layout);
                balanced.JunctionNotice=(Math.Abs(centre-original.Input.PositionMm)<1e-7 ? "Frame centred across the cross beam. " : "")
                    +$"Balanced primary overhangs {layout.PositionsMm[0]-stock.StartMm:0.###}/{stock.EndMm-layout.PositionsMm[1]:0.###} mm. Local receiving H20s are repositioned and added where necessary; local plywood cuts/joints are permitted. Design spacing and main-primary UDLs remain unchanged. "
                    +$"Tower leg offset {balanced.TowerSpanOffsetMm:0.###} mm along the crossing span. Existing junction end zones and their checked load paths are retained.";
                result.Beams=beams;result.CrossSupports=(CrossBeamSupportResult[])result.CrossSupports.Clone();result.CrossSupports[index]=balanced;
            }
        }

        // Stock, brace dimensions and load zones remain fixed. Recheck the actual
        // primary support positions before committing any centred setting-out.
        private static void CentreMainPrimaries(TowerInput common,TransferNetworkResult result)
        {
            var check=common.CopyForSearch();check.Trace=null;check.Diagnostics=null;
            for(int beam=0;beam<result.Beams.Length;beam++)
            {
                common.Trace?.CheckUserCancellation();
                var original=result.Beams[beam];
                if(original.CantileverImbalanceMm<1e-7)continue;
                Action<string> retain=notice=>
                {
                    var copy=Copy(original);copy.CentringNotice=notice;
                    var beams=(TowerLayout[])result.Beams.Clone();beams[beam]=copy;result.Beams=beams;
                };
                var layout=Copy(original);layout.Transfer=original.Transfer.Copy();
                var shifts=new double[layout.PositionsMm.Length];
                foreach(var stock in layout.StockPlan.Pieces)
                {
                    double shift=(stock.StartMm+stock.EndMm-original.PositionsMm[stock.FirstSupport]-original.PositionsMm[stock.LastSupport])/2;
                    for(int i=stock.FirstSupport;i<=stock.LastSupport;i++)shifts[i]=shift;
                }
                if(Enumerable.Range(0,original.Joined.Length).Any(i=>original.Joined[i] && Math.Abs(shifts[i]-shifts[i+1])>1e-7))
                {retain("Unequal primary overhangs retained: the linked brace spans a stock joint and prevents independent centring of these pieces.");continue;}
                layout.PositionsMm=original.PositionsMm.Select((x,i)=>x+shifts[i]).ToArray();
                int runs=layout.StockPlan.Pieces.Length;bool passes=true;string checkFailure=null;
                layout.Transfer.PrimaryCases=original.Transfer.PrimaryCases.Select(stage=>stage.Select((m,i)=>
                {
                    var stock=layout.StockPlan.Pieces[i%runs];
                    var member=new TransferMember {PrimaryMaterial=m.PrimaryMaterial,Name=m.Name,LoadCase=m.LoadCase,H20Count=m.H20Count,StartMm=m.StartMm,LengthMm=m.LengthMm,PrimarySelfWeightKnM=m.PrimarySelfWeightKnM,Uniform=m.Uniform,Points=m.Points,LegacyPrimaryUniform=m.LegacyPrimaryUniform,
                        SupportsMm=layout.PositionsMm.Skip(stock.FirstSupport).Take(stock.LastSupport-stock.FirstSupport+1).Select(x=>x-stock.StartMm).ToArray()};
                    member.Analysis=LoadedBeamAnalysis.AnalyzeSigned(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,common.DeflectionDenominator);
                    if(!Pass(member,check,"Centred main primary") || member.Analysis.Reactions.Max()>common.LegCapacityKn+1e-8)
                    {
                        passes=false;
                        if(checkFailure==null)checkFailure=member.Analysis.Reactions.Max()>common.LegCapacityKn+1e-8
                            ? $"{member.Name} centred leg load {member.Analysis.Reactions.Max():0.###}/{common.LegCapacityKn:0.###} kN."
                            : $"{member.Name} centred check: M {member.Analysis.MomentKnM:0.###}/{member.MomentLimitKnM:0.###} kNm, V {member.Analysis.ShearKn:0.###}/{member.ShearLimitKn:0.###} kN, deflection utilization {member.Analysis.DeflectionUtilization*100:0.#}%, minimum bearing reaction {member.MinimumReactionKn:0.###} kN.";
                    }
                    return member;
                }).ToArray()).ToArray();
                if(!passes){retain("Unequal primary overhangs retained: "+checkFailure);continue;}
                string reason=null,owner=$"B{beam+1}";
                // Main towers run along x; use their actual production solids.
                var trialBeams=(TowerLayout[])result.Beams.Clone();trialBeams[beam]=layout;
                var trial=new TransferNetworkResult {Input=result.Input,Beams=trialBeams,Bays=result.Bays,BeamCentresMm=result.BeamCentresMm,SlabCoveredLengthMm=result.SlabCoveredLengthMm,RowPhaseMm=result.RowPhaseMm};
                var targets=NetworkSolids(trial).Where(s=>s.TowerOwner==owner && (s.IsLeg || s.IsTowerEnvelope)).ToArray();
                if(!TryAdjustSecondaries(result,targets,result.CrossSupports.SelectMany(c=>c.Solids).ToArray(),out var adjusted,true,message=>reason=message,owner))
                {retain("Unequal primary overhangs retained: "+(reason??"centred tower clearance check failed."));continue;}
                layout.Transfer.SettingOutRowsMm=adjusted[beam].Transfer.SettingOutRowsMm;layout.Transfer.SettingOutDesignRows=adjusted[beam].Transfer.SettingOutDesignRows;layout.Transfer.HasCustomPlywoodJoints=adjusted[beam].Transfer.HasCustomPlywoodJoints;
                int count=layout.PositionsMm.Length;
                layout.LeftReactions=Enumerable.Repeat(double.NegativeInfinity,count).ToArray();layout.RightReactions=(double[])layout.LeftReactions.Clone();layout.MinimumLeftReactions=Enumerable.Repeat(double.PositiveInfinity,count).ToArray();layout.MinimumRightReactions=(double[])layout.MinimumLeftReactions.Clone();
                foreach(var stage in layout.Transfer.PrimaryCases)for(int i=0;i<stage.Length;i++)
                {
                    var stock=layout.StockPlan.Pieces[i%runs];var high=i<runs ? layout.LeftReactions : layout.RightReactions;var low=i<runs ? layout.MinimumLeftReactions : layout.MinimumRightReactions;
                    for(int j=0;j<stage[i].Analysis.Reactions.Length;j++){int at=stock.FirstSupport+j;high[at]=Math.Max(high[at],stage[i].Analysis.Reactions[j]);low[at]=Math.Min(low[at],stage[i].Analysis.Reactions[j]);}
                }
                layout.MaxLegKn=Math.Max(layout.LeftReactions.Max(),layout.RightReactions.Max());
                var members=layout.Transfer.PrimaryCases.Concat(layout.Transfer.LowerCases).SelectMany(c=>c).Concat(layout.Transfer.UpperMembers).ToArray();
                layout.Utilization=Math.Max(layout.MaxLegKn/layout.LegCapacityKn,members.Max(m=>m.Utilization));layout.MomentKnM=members.Max(m=>m.Analysis.MomentKnM);layout.ShearKn=members.Max(m=>m.Analysis.ShearKn);layout.DeflectionMm=members.Max(m=>m.Analysis.DeflectionMm);
                layout.Governing=layout.MaxLegKn/layout.LegCapacityKn>=members.Max(m=>m.Utilization) ? "Leg capacity" : members.OrderByDescending(m=>m.Utilization).First().Name;
                layout.CentringNotice="Primary supports centred beneath each stock piece; existing UDL zones retained and all revised reactions/member checks pass.";
                RefreshStockDemands(layout);adjusted[beam]=layout;result.Beams=adjusted;
            }
        }

        // The site bearer permits slab feet and receiving secondaries to have
        // independent longitudinal stations. It is outside this member calculator:
        // no new bearer sharing model or slab/primary load allocation is introduced.
        internal static void CentreCrossingFrames(TowerInput common,TransferNetworkResult result,Action<string> rejected=null)
        {
            var check=common.CopyForSearch();check.Trace=null;check.Diagnostics=null;
            var selected=(CrossBeamSupportResult[])result.CrossSupports.Clone();bool changed=false;
            Action<string> reason=message=>
            {
                rejected?.Invoke(message);
                result.CrossSupports=result.CrossSupports.Select(c=>
                {
                    if(c.Towers==null)return c;
                    var copy=c.Copy();copy.SymmetryNotice="Centred setting-out could not be completed: "+message;return copy;
                }).ToArray();
            };
            for(int index=0;index<result.CrossSupports.Length;index++)
            {
                common.Trace?.CheckUserCancellation();
                var original=result.CrossSupports[index];var tower=original.Towers;
                if(tower==null)continue;
                var legs=original.Bearings.Where(b=>b.IsDedicated && b.Label.StartsWith("Dedicated P")).ToArray();
                if(legs.Length==0)continue;
                double offset=legs.Average(b=>b.Xmm)-original.Input.PositionMm;
                if(Math.Abs(offset)<1e-7)continue;
                var layout=Copy(tower);layout.Transfer=tower.Transfer.Copy();layout.TransverseOffsetMm=0;
                var t=layout.Transfer;bool passes=true;
                Func<TransferMember,TransferMember> centred=m=>
                {
                    var source=new TransferMember {PrimaryMaterial=m.PrimaryMaterial,Name=m.Name,LoadCase=m.LoadCase,H20Count=m.H20Count,
                        StartMm=m.StartMm+offset,LengthMm=m.LengthMm,SupportsMm=m.SupportsMm.Select(x=>x-offset).ToArray(),Points=m.Points,Uniform=m.Uniform};
                    return Analyze(source,source.Points,source.Uniform,check,m.LoadCase);
                };
                t.LowerCases=tower.Transfer.LowerCases.Select(stage=>stage.Select(centred).ToArray()).ToArray();
                foreach(var m in t.LowerCases.SelectMany(stage=>stage))if(!Pass(m,check,"Centred crossing secondary"))passes=false;
                if(!passes){reason($"X{index+1}: centred secondary capacity check failed.");continue;}
                t.UnitLowerCases=tower.Transfer.UnitLowerCases.Select(centred).ToArray();
                t.LowerStockStartMm+=offset;
                t.PrimaryCases=new TransferMember[tower.Transfer.PrimaryCases.Length][];
                for(int stage=0;stage<t.PrimaryCases.Length && passes;stage++)
                {
                    var oldReaction=tower.Transfer.LowerCases[stage][0].Analysis.Reactions;
                    var newReaction=t.LowerCases[stage][0].Analysis.Reactions;
                    int runs=tower.StockPlan.Pieces.Length;
                    t.PrimaryCases[stage]=tower.Transfer.PrimaryCases[stage].Select((m,i)=>
                    {
                        int line=i/runs;
                        if(Math.Abs(oldReaction[line])<1e-9){passes=false;return m;}
                        double factor=newReaction[line]/oldReaction[line];
                        int selfWeight=m.PrimarySelfWeightKnM>0 ? m.Uniform.Length-1 : -1;
                        return new TransferMember {PrimaryMaterial=m.PrimaryMaterial,Name=m.Name,LoadCase=m.LoadCase,H20Count=m.H20Count,
                            StartMm=m.StartMm,LengthMm=m.LengthMm,SupportsMm=m.SupportsMm,PrimarySelfWeightKnM=m.PrimarySelfWeightKnM,
                            Uniform=m.Uniform.Select((q,j)=>new BeamDistributedLoad {StartMm=q.StartMm,EndMm=q.EndMm,KnPerM=q.KnPerM*(j==selfWeight ? 1 : factor)}).ToArray(),
                            Points=m.Points.Select(p=>new BeamPointLoad {PositionMm=p.PositionMm,Kn=p.Kn*factor}).ToArray()};
                    }).ToArray();
                }
                if(!passes){reason($"X{index+1}: zero reference reaction cannot be rescaled.");continue;}
                var previous=tower.Transfer.LowerCases[0][0].Analysis.Reactions;
                var revised=t.LowerCases[0][0].Analysis.Reactions;
                layout.LeftLineLoad*=revised[0]/previous[0];layout.RightLineLoad*=revised[1]/previous[1];
                double origin=legs.First().Ymm-tower.PositionsMm[0];
                var allowance=new CrossShutterAllowance(original.NearShutterClearanceMm,original.FarShutterClearanceMm);
                var candidate=FinishDedicated(check,result,original.CrossingIndex,origin,0,original.TowerSpanOffsetMm,layout,check,out string failure,
                    original.PrimaryNearExtensionMm,original.PrimaryFarExtensionMm,allowance:allowance);
                if(candidate==null){reason($"X{index+1}: "+failure);continue;}
                candidate.Solids=candidate.Solids.Concat(original.Solids.Where(s=>s.Member==null && !s.IsLeg && !s.IsTowerEnvelope)).ToArray();
                candidate.Bearings=candidate.Bearings.Concat(original.Bearings.Where(b=>!b.IsDedicated)).ToArray();
                candidate.LoadKn=original.LoadKn;candidate.SlabEdgeLoadKn=original.SlabEdgeLoadKn;candidate.SharedFailure=original.SharedFailure;
                candidate.MovementMm=DedicatedMovement(layout);
                candidate.JunctionNotice="Frame centred across the cross beam. Existing slab-foot loads, end-zone transfers and main-primary UDLs are retained; local lower-secondary setting-out clears the tower legs.";
                selected[index]=candidate;changed=true;
            }
            if(!changed)return;
            // Both ends of a receiving H20 can meet crossing legs. Coordinate all
            // target frames together; an obsolete offset pose must not obstruct
            // the same row needed by another crossing's centred pose.
            if(!TryAdjustSecondaries(result,selected.SelectMany(s=>s.Solids).ToArray(),new FormworkSolid[0],out var beams,true,reason))return;
            result.Beams=beams;result.CrossSupports=selected;
        }

        private sealed class SettingOutRow
        {
            internal int Design;
            internal double X;
        }

        internal static bool TryAdjustSecondaries(TransferNetworkResult result,FormworkSolid[] crossing,FormworkSolid[] otherCrossings,out TowerLayout[] adjusted,bool independentSlabFeet=false,Action<string> rejected=null,string replacedTowerOwner=null)
        {
            adjusted=null;
            Func<FormworkSolid,bool> keep=s=>replacedTowerOwner==null || s.TowerOwner!=replacedTowerOwner || !(s.IsLeg || s.IsTowerEnvelope);
            var existing=NetworkSolids(result).Where(keep).ToArray();
            var concrete=Concrete(result).Concat(otherCrossings).ToArray();
            var hits=existing.Where(s=>crossing.Any(c=>s.Conflicts(c))).ToArray();
            var changes=new Dictionary<int,List<SettingOutRow>>();
            foreach(var hit in hits)
            {
                if(!hit.Name.StartsWith("B") || !hit.Name.Contains("/T")){rejected?.Invoke("Fixed clash: "+hit.Name);return false;}
                var ids=hit.Name.Substring(1).Split(new[] {"/T"},StringSplitOptions.None);
                if(!int.TryParse(ids[0],out int beamId) || !int.TryParse(ids[1],out int rowId))return false;
                int beam=beamId-1,design=rowId-1;var t=result.Beams[beam].Transfer;
                if(!changes.TryGetValue(beam,out var rows))
                    changes[beam]=rows=Enumerable.Range(0,t.PhysicalRowCount).Select(i=>new SettingOutRow {Design=t.PhysicalRowDesignIndex(i),X=t.PhysicalRowPosition(i)}).ToList();
                var row=rows.FirstOrDefault(v=>v.Design==design && Math.Abs(v.X-(hit.X0+hit.X1)/2)<1e-7);
                if(row==null || ProtectedRow(result,beam,design,row.X,independentSlabFeet)){rejected?.Invoke($"Protected row: {hit.Name} at {(hit.X0+hit.X1)/2}");return false;}
                var excluded=crossing.Concat(otherCrossings).Concat(concrete).ToArray();
                if(!MoveAndFill(result,beam,row,rows,excluded,independentSlabFeet)){rejected?.Invoke($"No clear lower setting-out: {hit.Name} at {row.X}");return false;}
            }
            adjusted=result.Beams.Select(Copy).ToArray();
            foreach(var change in changes)
            {
                int beam=change.Key;var rows=change.Value.OrderBy(v=>v.X).ToArray();
                var t=adjusted[beam].Transfer=result.Beams[beam].Transfer.Copy();
                t.HasCustomPlywoodJoints=t.HasCustomPlywoodJoints || rows.Any(v=>Math.Abs(v.X-result.Beams[beam].Transfer.RowsMm[v.Design])>1e-7 && Math.Abs(result.Beams[beam].Transfer.RowsMm[v.Design]/SheetLength(result)-Math.Round(result.Beams[beam].Transfer.RowsMm[v.Design]/SheetLength(result)))<1e-7);
                t.SettingOutRowsMm=rows.Select(v=>v.X).ToArray();t.SettingOutDesignRows=rows.Select(v=>v.Design).ToArray();
            }
            var trial=new TransferNetworkResult {Input=result.Input,Beams=adjusted,Bays=result.Bays,BeamCentresMm=result.BeamCentresMm};
            // Final checks cover complete physical stock, legs, frames and added H20s.
            var physical=NetworkSolids(trial).Where(keep).ToArray();
            var changed=physical.Where(s=>s.Name.Contains("/T") && !existing.Any(old=>old.Name==s.Name && old.X0==s.X0 && old.X1==s.X1)).ToArray();
            var failure=Clash(crossing,physical.Concat(concrete))??Clash(changed,physical.Except(changed).Concat(concrete).Concat(crossing));
            if(failure!=null)rejected?.Invoke(failure);
            return failure==null;
        }

        private static double SheetLength(TransferNetworkResult r)=>r.Input.PlywoodSheetLengthMm>0 ? r.Input.PlywoodSheetLengthMm : 2440;
        private static bool ProtectedRow(TransferNetworkResult r,int beam,int row,double x,bool independentSlabFeet)
        {
            var t=r.Beams[beam].Transfer;
            if(row==0 || row==t.RowsMm.Length-1)return true;
            if(!independentSlabFeet && (t.SlabRowIndices?.Contains(row)==true || (t.LeftUpperScales?[row]??0)>0 || (t.RightUpperScales?[row]??0)>0))return true;
            if(r.Input.PlywoodSheetLengthMm<=0)return true; // No sheet-joint information: keep the checked geometry.
            double sheet=r.Input.PlywoodSheetLengthMm;
            if(!independentSlabFeet && Math.Abs(x/sheet-Math.Round(x/sheet))<1e-7)return true;
            if(r.CrossSupports.SelectMany(s=>s.Bearings).Any(b=>b.Beam==beam && b.Row==row))return true;
            // Added rows also stay outside all crossing-concrete transfer footprints.
            return r.Input.CrossingBeams.Any(c=>(c.BayNumber==beam || c.BayNumber==beam+1) && Math.Abs(x-c.PositionMm)<c.WidthMm/2+80);
        }

        private static bool MoveAndFill(TransferNetworkResult r,int beam,SettingOutRow moving,List<SettingOutRow> current,FormworkSolid[] obstacles,bool independentSlabFeet)
        {
            var l=r.Beams[beam];var t=l.Transfer;double original=moving.X;
            var stock=l.StockPlan.Pieces.FirstOrDefault(p=>original>=p.StartMm && original<p.EndMm);
            if(stock==null)return false;
            double y=r.BeamCentresMm[beam]+t.LowerStockStartMm;
            Func<double,bool> clear=x=>!obstacles.Any(s=>Timber("Local receiving H20",x,y,t.LowerStockLengthMm,false,r.Input.Beams[beam].BottomElevationMm).Conflicts(s));
            // Exact leg/flange clearance positions avoid arbitrary stepping and stay local.
            var candidates=obstacles.SelectMany(s=>new[] {s.X0-40-2,s.X1+40+2}).Where(x=>Math.Abs(x-original)<=t.EnteredUpperSpacingMm)
                .OrderBy(x=>Math.Abs(x-original)).Distinct().ToArray();
            foreach(double target in candidates)
            {
                if(target<stock.StartMm+40 || target>stock.EndMm-40 || ProtectedRow(r,beam,moving.Design,target,independentSlabFeet) || !clear(target))continue;
                var trial=current.Select(v=>new SettingOutRow {Design=v.Design,X=ReferenceEquals(v,moving) ? target : v.X}).OrderBy(v=>v.X).ToList();
                if(trial.Zip(trial.Skip(1),(a,b)=>b.X-a.X).Any(g=>g<80-1e-7))continue;
                bool failed=false;
                for(int i=1;i<trial.Count;i++)
                {
                    double a=trial[i-1].X,b=trial[i].X;
                    if(b-a<=t.EnteredUpperSpacingMm+1e-7)continue;
                    double low=Math.Max(a+80,b-t.EnteredUpperSpacingMm),high=Math.Min(b-80,a+t.EnteredUpperSpacingMm);
                    var extras=new[] {(low+high)/2,low,high}.Concat(candidates).Where(x=>x>=low && x<=high && x>=stock.StartMm+40 && x<=stock.EndMm-40);
                    double? added=extras.Where(x=>!ProtectedRow(r,beam,moving.Design,x,independentSlabFeet) && clear(x)).Select(x=>(double?)x).FirstOrDefault();
                    if(!added.HasValue){failed=true;break;}
                    trial.Insert(i,new SettingOutRow {Design=moving.Design,X=added.Value});i++;
                }
                if(failed)continue;
                // All physical tributaries remain within the checked nominal spacing.
                // End rows stay fixed, preserving the existing cantilever design envelope.
                if(trial.Zip(trial.Skip(1),(a,b)=>b.X-a.X).Any(g=>g>t.EnteredUpperSpacingMm+1e-7 || g<80-1e-7))continue;
                current.Clear();current.AddRange(trial);return true;
            }
            return false;
        }
    }
}
