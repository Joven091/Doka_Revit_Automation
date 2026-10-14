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
            if(result.CrossSupports.Length==0)return;
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
                balanced.LoadKn=original.LoadKn;balanced.SharedFailure=original.SharedFailure;
                // Only receiving secondaries may change. Frames, slab feet, concrete,
                // other crossings and plywood-joint rows remain fixed.
                var otherCrossings=result.CrossSupports.Where((s,i)=>i!=index).SelectMany(s=>s.Solids).ToArray();
                if(!TryAdjustSecondaries(result,balanced.Solids,otherCrossings,out TowerLayout[] beams))continue;
                balanced.MovementMm=DedicatedMovement(layout);
                balanced.JunctionNotice=$"Balanced primary overhangs {layout.PositionsMm[0]-stock.StartMm:0.###}/{stock.EndMm-layout.PositionsMm[1]:0.###} mm. Local receiving H20s are repositioned and added where necessary; plywood-joint supports, design spacing and main-primary UDLs remain unchanged. "
                    +$"Tower leg offset {balanced.TowerSpanOffsetMm:0.###} mm along the crossing span. Existing junction end zones and their checked load paths are retained.";
                result.Beams=beams;result.CrossSupports=(CrossBeamSupportResult[])result.CrossSupports.Clone();result.CrossSupports[index]=balanced;
            }
        }

        private sealed class SettingOutRow
        {
            internal int Design;
            internal double X;
        }

        internal static bool TryAdjustSecondaries(TransferNetworkResult result,FormworkSolid[] crossing,FormworkSolid[] otherCrossings,out TowerLayout[] adjusted)
        {
            adjusted=null;
            var existing=NetworkSolids(result).ToArray();
            var concrete=Concrete(result).Concat(otherCrossings).ToArray();
            var hits=existing.Where(s=>crossing.Any(c=>s.Conflicts(c))).ToArray();
            var changes=new Dictionary<int,List<SettingOutRow>>();
            foreach(var hit in hits)
            {
                if(!hit.Name.StartsWith("B") || !hit.Name.Contains("/T"))return false;
                var ids=hit.Name.Substring(1).Split(new[] {"/T"},StringSplitOptions.None);
                if(!int.TryParse(ids[0],out int beamId) || !int.TryParse(ids[1],out int rowId))return false;
                int beam=beamId-1,design=rowId-1;var t=result.Beams[beam].Transfer;
                if(!changes.TryGetValue(beam,out var rows))
                    changes[beam]=rows=Enumerable.Range(0,t.PhysicalRowCount).Select(i=>new SettingOutRow {Design=t.PhysicalRowDesignIndex(i),X=t.PhysicalRowPosition(i)}).ToList();
                var row=rows.FirstOrDefault(v=>v.Design==design && Math.Abs(v.X-(hit.X0+hit.X1)/2)<1e-7);
                if(row==null || ProtectedRow(result,beam,design,row.X))return false;
                var excluded=crossing.Concat(otherCrossings).Concat(concrete).ToArray();
                if(!MoveAndFill(result,beam,row,rows,excluded))return false;
            }
            adjusted=result.Beams.Select(Copy).ToArray();
            foreach(var change in changes)
            {
                int beam=change.Key;var rows=change.Value.OrderBy(v=>v.X).ToArray();
                var t=adjusted[beam].Transfer=result.Beams[beam].Transfer.Copy();
                t.SettingOutRowsMm=rows.Select(v=>v.X).ToArray();t.SettingOutDesignRows=rows.Select(v=>v.Design).ToArray();
            }
            var trial=new TransferNetworkResult {Input=result.Input,Beams=adjusted,Bays=result.Bays,BeamCentresMm=result.BeamCentresMm};
            // Final checks cover complete physical stock, legs, frames and added H20s.
            var physical=NetworkSolids(trial).ToArray();
            var changed=physical.Where(s=>s.Name.Contains("/T") && !existing.Any(old=>old.Name==s.Name && old.X0==s.X0 && old.X1==s.X1)).ToArray();
            return Clash(crossing,physical.Concat(concrete))==null
                && Clash(changed,physical.Except(changed).Concat(concrete).Concat(crossing))==null;
        }

        private static bool ProtectedRow(TransferNetworkResult r,int beam,int row,double x)
        {
            var t=r.Beams[beam].Transfer;
            if(row==0 || row==t.RowsMm.Length-1 || t.SlabRowIndices?.Contains(row)==true)return true;
            if((t.LeftUpperScales?[row]??0)>0 || (t.RightUpperScales?[row]??0)>0)return true;
            if(r.Input.PlywoodSheetLengthMm<=0)return true; // No sheet-joint information: keep the checked geometry.
            double sheet=r.Input.PlywoodSheetLengthMm;
            if(Math.Abs(x/sheet-Math.Round(x/sheet))<1e-7)return true;
            if(r.CrossSupports.SelectMany(s=>s.Bearings).Any(b=>b.Beam==beam && b.Row==row))return true;
            // Added rows also stay outside all crossing-concrete transfer footprints.
            return r.Input.CrossingBeams.Any(c=>(c.BayNumber==beam || c.BayNumber==beam+1) && Math.Abs(x-c.PositionMm)<c.WidthMm/2+80);
        }

        private static bool MoveAndFill(TransferNetworkResult r,int beam,SettingOutRow moving,List<SettingOutRow> current,FormworkSolid[] obstacles)
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
                if(target<stock.StartMm+40 || target>stock.EndMm-40 || ProtectedRow(r,beam,moving.Design,target) || !clear(target))continue;
                var trial=current.Select(v=>new SettingOutRow {Design=v.Design,X=ReferenceEquals(v,moving) ? target : v.X}).OrderBy(v=>v.X).ToList();
                if(trial.Zip(trial.Skip(1),(a,b)=>b.X-a.X).Any(g=>g<80-1e-7))continue;
                bool failed=false;
                for(int i=1;i<trial.Count;i++)
                {
                    double a=trial[i-1].X,b=trial[i].X;
                    if(b-a<=t.EnteredUpperSpacingMm+1e-7)continue;
                    double low=Math.Max(a+80,b-t.EnteredUpperSpacingMm),high=Math.Min(b-80,a+t.EnteredUpperSpacingMm);
                    var extras=new[] {(low+high)/2,low,high}.Concat(candidates).Where(x=>x>=low && x<=high && x>=stock.StartMm+40 && x<=stock.EndMm-40);
                    double? added=extras.Where(x=>!ProtectedRow(r,beam,moving.Design,x) && clear(x)).Select(x=>(double?)x).FirstOrDefault();
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
