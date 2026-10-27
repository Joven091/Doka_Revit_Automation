using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EngineeringDashboard
{
    internal static partial class RevitStructureImport
    {
        // Column-supported perimeter members have their own shoring. They are
        // not internal crossing strips in the adjacent slab calculation. Work
        // only on detached copies; keep every source and its actual end limits.
        private static RevitImportArea[] ColumnSupportedAreas(RevitSelectionSnapshot source,string directionId)
        {
            var first=TryColumnSupportedAreas(source,directionId);
            if(first.Length>1 && first.All(a=>a.Result.CanImport))return first;
            var chosen=source.Beams.FirstOrDefault(b=>b.SourceId==(directionId??DefaultDirection(source)));
            var other=chosen==null || !Finite(chosen.Start) || !Finite(chosen.End) || Length(chosen)<=ToleranceMm
                ? null : source.Beams.FirstOrDefault(b=>Finite(b.Start) && Finite(b.End) && !Parallel(b,chosen));
            if(other==null)return first;
            var alternate=TryColumnSupportedAreas(source,other.SourceId);
            if(alternate.Length<2 || alternate.Any(a=>!a.Result.CanImport))return first;
            foreach(var area in alternate)area.Result.Notes=area.Result.Notes.Concat(new[] {"Connected calculation areas use the orthogonal beam axis; all members retain their Revit placement and source IDs."}).ToArray();
            return alternate;
        }
        private static RevitImportArea[] TryColumnSupportedAreas(RevitSelectionSnapshot source,string directionId)
        {
            if(source.Problems.Length>0 || source.Slabs.Length==0 || source.Boundaries.Length>100
                || source.Beams.Any(b=>b.Problem!=null || !Finite(b.Start) || !Finite(b.End) || new[] {b.WidthMm,b.DepthMm,b.BottomMm}.Any(v=>!TowerBeamAnalysis.Finite(v)) || b.WidthMm<=0 || b.DepthMm<=0)
                || source.Slabs.Any(s=>s.Problem!=null || !TowerBeamAnalysis.Finite(s.ThicknessMm) || s.ThicknessMm<=0 || !TowerBeamAnalysis.Finite(s.TopMm))
                || source.Boundaries.Any(b=>b.Problem!=null || !ImportPolygon.Convex(b.Outline,PrecisionMm))
                || source.Beams.GroupBy(b=>b.SourceId).Any(g=>g.Count()>1) || source.Slabs.GroupBy(s=>s.SourceId).Any(g=>g.Count()>1))return new RevitImportArea[0];
            var chosen=source.Beams.FirstOrDefault(b=>b.SourceId==(directionId??DefaultDirection(source)));
            if(chosen==null || Length(chosen)<=ToleranceMm)return new RevitImportArea[0];
            double length=Length(chosen),dx=(chosen.End.X-chosen.Start.X)/length,dy=(chosen.End.Y-chosen.Start.Y)/length;
            if(dx< -1e-6 || Math.Abs(dx)<=1e-6 && dy<0){dx=-dx;dy=-dy;}
            ImportPoint Local(ImportPoint p)=>new ImportPoint((p.X-chosen.Start.X)*dx+(p.Y-chosen.Start.Y)*dy,-(p.X-chosen.Start.X)*dy+(p.Y-chosen.Start.Y)*dx);
            ImportPoint World(ImportPoint p)=>new ImportPoint(chosen.Start.X+p.X*dx-p.Y*dy,chosen.Start.Y+p.X*dy+p.Y*dx);
            var parallel=new List<Beam>();var across=new List<Beam>();
            foreach(var s in source.Beams)
            {
                var a=Local(s.Start);var b=Local(s.End);bool along=Math.Abs(a.Y-b.Y)<=ToleranceMm;
                if(!along && Math.Abs(a.X-b.X)>ToleranceMm)return new RevitImportArea[0];
                (along ? parallel : across).Add(new Beam {Source=s,Parts=new[] {s},Start=along ? Math.Min(a.X,b.X) : Math.Min(a.Y,b.Y),
                    End=along ? Math.Max(a.X,b.X) : Math.Max(a.Y,b.Y),Centre=along ? (a.Y+b.Y)/2 : (a.X+b.X)/2,Bottom=s.BottomMm});
            }
            if(parallel.Count==0)return new RevitImportArea[0];
            var floors=source.Slabs.Select(s=>Tuple.Create(s,s.Outline?.Select(Local).ToArray())).ToArray();
            double datum=source.Slabs[0].TopMm;
            if(floors.Any(f=>!SlabFootprint.ValidEndCutbacks(f.Item2,SlabFootprint.EndAllowance(f.Item1.ThicknessMm)))
                || source.Slabs.Any(s=>Math.Abs(s.TopMm-datum)>PrecisionMm) || source.Beams.Any(b=>Math.Abs(b.BottomMm+b.DepthMm-datum)>PrecisionMm))return new RevitImportArea[0];
            var boundaries=source.Boundaries.Select(b=>Tuple.Create(b,b.Outline.Select(Local).ToArray())).ToArray();
            bool ColumnEnd(Beam beam,bool start)
            {
                double end=start ? beam.Start : beam.End;
                return boundaries.Any(boundary=>
                {
                    var b=boundary.Item1;var outline=boundary.Item2;var box=new Box(outline);
                    return Rectangle(outline) && b.TopMm>=beam.Bottom-PrecisionMm && b.BottomMm<=beam.Bottom+beam.Source.DepthMm+PrecisionMm
                        && box.X0<=beam.Centre-beam.Source.WidthMm/2+ToleranceMm && box.X1>=beam.Centre+beam.Source.WidthMm/2-ToleranceMm
                        && Math.Abs(end-(start ? box.Y1 : box.Y0))<=ToleranceMm;
                });
            }
            var perimeter=across.Where(c=>ColumnEnd(c,true) && ColumnEnd(c,false)
                && !parallel.Any(p=>p.Centre+p.Source.WidthMm/2>c.Start+ToleranceMm && p.Centre-p.Source.WidthMm/2<c.End-ToleranceMm
                    && Math.Min(p.End,c.Centre+c.Source.WidthMm/2)-Math.Max(p.Start,c.Centre-c.Source.WidthMm/2)>ToleranceMm)).ToArray();
            if(perimeter.Length==0)return new RevitImportArea[0];
            var internalCrosses=across.Except(perimeter).ToArray();
            var logical=ConnectedBeams(parallel.Select(b=>new Beam {Source=b.Source,Parts=b.Parts,Start=b.Start,End=b.End,Centre=b.Centre,Bottom=b.Bottom}).ToList(),new List<string>(),internalCrosses,boundaries);
            double x0=parallel.Min(p=>p.Start),x1=parallel.Max(p=>p.End),allowance=floors.Max(f=>SlabFootprint.EndAllowance(f.Item1.ThicknessMm));
            // A row ending at a T junction does not continue across the other
            // side of the slab. Split the calculation strip there, rather than
            // inventing that continuation and losing the slab over its width.
            var candidates=new List<double>();
            foreach(var beam in logical)foreach(bool start in new[] {true,false})
            {
                double end=start ? beam.Start : beam.End;
                if(end-x0<=allowance || x1-end<=allowance)continue;
                // Joined Revit members can end at either face of the selected
                // perpendicular member; retain the face actually captured.
                if(!internalCrosses.Any(c=>Math.Min(Math.Abs(end-(c.Centre-c.Source.WidthMm/2)),Math.Abs(end-(c.Centre+c.Source.WidthMm/2)))<=ToleranceMm
                    && c.End>=beam.Centre-beam.Source.WidthMm/2-ToleranceMm && c.Start<=beam.Centre+beam.Source.WidthMm/2+ToleranceMm))return new RevitImportArea[0];
                candidates.Add(end);
            }
            var cuts=new List<double>();
            foreach(double candidate in candidates.OrderBy(x=>x))
            {
                if(cuts.Any(c=>Math.Abs(c-candidate)<=ToleranceMm))continue;
                double cut=internalCrosses.SelectMany(c=>new[] {c.Centre-c.Source.WidthMm/2,c.Centre+c.Source.WidthMm/2})
                    .Where(c=>Math.Abs(c-candidate)<=ToleranceMm).Concat(new[] {candidate}).Max();
                if(internalCrosses.Any(c=>cut>c.Centre-c.Source.WidthMm/2+ToleranceMm && cut<c.Centre+c.Source.WidthMm/2-ToleranceMm))return new RevitImportArea[0];
                cuts.Add(cut);
            }
            var limits=new[] {x0}.Concat(cuts).Concat(new[] {x1}).OrderBy(x=>x).ToArray();
            double Snap(double value)=>cuts.Where(c=>Math.Abs(c-value)<=ToleranceMm).OrderBy(c=>Math.Abs(c-value)).DefaultIfEmpty(value).First();
            var areas=new List<RevitImportArea>();
            for(int index=0;index<limits.Length-1;index++)
            {
                double from=limits[index],to=limits[index+1];var members=new List<RevitSelectedBeam>();
                foreach(var beam in parallel)
                {
                    double start=Math.Max(from,Snap(beam.Start)),end=Math.Min(to,Snap(beam.End));
                    if(end-start<=PrecisionMm)continue;
                    members.Add(CopyBeam(beam.Source,World(new ImportPoint(start,beam.Centre)),World(new ImportPoint(end,beam.Centre))));
                }
                foreach(var beam in internalCrosses)
                {
                    double centre=beam.Centre;
                    foreach(double cut in cuts)
                    {
                        if(Math.Abs(centre+beam.Source.WidthMm/2-cut)<=ToleranceMm)centre=cut-beam.Source.WidthMm/2;
                        else if(Math.Abs(centre-beam.Source.WidthMm/2-cut)<=ToleranceMm)centre=cut+beam.Source.WidthMm/2;
                    }
                    if(centre-beam.Source.WidthMm/2<from-1e-7 || centre+beam.Source.WidthMm/2>to+1e-7)continue;
                    members.Add(CopyBeam(beam.Source,World(new ImportPoint(centre,beam.Start)),World(new ImportPoint(centre,beam.End))));
                }
                var panels=new List<RevitSelectedSlab>();
                foreach(var floor in floors)
                {
                    var outline=floor.Item2.Select(p=>new ImportPoint(Snap(p.X),p.Y)).ToArray();var box=new Box(outline);
                    if(box.X1<=from+PrecisionMm || box.X0>=to-PrecisionMm)continue;
                    var clipped=SlabFootprint.Clip(outline,from,to,box.Y0,box.Y1);
                    if(clipped.Length<4 || ImportPolygon.Area(clipped)<=PrecisionMm)continue;
                    panels.Add(new RevitSelectedSlab {SourceId=floor.Item1.SourceId,Label=floor.Item1.Label,ThicknessMm=floor.Item1.ThicknessMm,TopMm=floor.Item1.TopMm,Outline=clipped.Select(World).ToArray()});
                }
                if(members.Count==0 || panels.Count==0)return new RevitImportArea[0];
                string axis=members.First(b=>Parallel(b,chosen)).SourceId;
                var selection=new RevitSelectionSnapshot {DocumentTitle=source.DocumentTitle,Beams=members.ToArray(),Slabs=panels.ToArray(),Boundaries=source.Boundaries};
                var result=BuildSingle(selection,axis);
                result.Notes=result.Notes.Concat(new[] {"Column-supported perimeter members retain separate clear-span calculations on this canvas.",
                    "Calculation strip "+(index+1)+": "+from.ToString("0.###",CultureInfo.InvariantCulture)+" to "+to.ToString("0.###",CultureInfo.InvariantCulture)+" mm. Source segments meet without overlapping; T-junction alignment uses the fixed 5 mm allowance."}).ToArray();
                Add(selection,axis,"area:"+directionId+":"+index,"Area "+(index+1)+" · "+panels.Count+" slab panels",result);
            }
            foreach(var beam in perimeter)
            {
                var selection=new RevitSelectionSnapshot {DocumentTitle=source.DocumentTitle,Beams=new[] {beam.Source},Boundaries=source.Boundaries};
                Add(selection,beam.Source.SourceId,"perimeter:"+beam.Source.SourceId,"Beam "+beam.Source.SourceId+" · between columns",BuildSingle(selection,beam.Source.SourceId));
            }
            // Every selected source must still be present. Split floor/beam IDs
            // may occur in several strips, with disjoint geometric extents.
            var allIds=areas.SelectMany(a=>a.SourceIds).Distinct().OrderBy(id=>id,StringComparer.Ordinal);
            var selected=source.Beams.Select(b=>b.SourceId).Concat(source.Slabs.Select(s=>s.SourceId)).Distinct().OrderBy(id=>id,StringComparer.Ordinal);
            return allIds.SequenceEqual(selected) ? areas.ToArray() : new RevitImportArea[0];
            void Add(RevitSelectionSnapshot selection,string axis,string key,string label,RevitStructureImportResult result)
            {
                areas.Add(new RevitImportArea {Key=key,Label=label,DirectionId=axis,Geometry=selection,Result=result,
                    SourceIds=selection.Beams.Select(b=>b.SourceId).Concat(selection.Slabs.Select(s=>s.SourceId)).Distinct().OrderBy(id=>id,StringComparer.Ordinal).ToArray()});
            }
        }
        private static RevitSelectedBeam CopyBeam(RevitSelectedBeam source,ImportPoint start,ImportPoint end)
            =>new RevitSelectedBeam {SourceId=source.SourceId,Label=source.Label,Start=start,End=end,WidthMm=source.WidthMm,DepthMm=source.DepthMm,BottomMm=source.BottomMm,Problem=source.Problem};
    }
}
