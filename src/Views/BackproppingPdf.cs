using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EngineeringDashboard.Backpropping;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EngineeringDashboard
{
    internal static class BackproppingPdf
    {
        private static string H(string value) => WebUtility.HtmlEncode(value);
        private static string N(double value, string format = "0.000") => value.ToString(format, CultureInfo.InvariantCulture);
        private static string Cells(params string[] values) => "<tr>" + string.Concat(values.Select(v => "<td>" + H(v) + "</td>")) + "</tr>";
        private static string Table(params string[] titles) => "<table><thead><tr>" + string.Concat(titles.Select(v => "<th>" + H(v) + "</th>")) + "</tr></thead><tbody>";

        private static string SectionImage(bool mainShores, bool final, bool tower=false)
        {
            // Render each storey at its own origin, independent of its offset in the screen stack.
            var drawing = new Grid();
            drawing.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            drawing.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            BackproppingWindow.AddSectionDrawing(drawing, 0, mainShores, !final, Brushes.DarkGray, tower);
            drawing.Measure(new Size(320, double.PositiveInfinity));
            var size = new Size(320, drawing.DesiredSize.Height);
            drawing.Arrange(new Rect(size));
            drawing.UpdateLayout();
            var bitmap = new RenderTargetBitmap(640, (int)Math.Ceiling(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                png.Save(stream);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static string Asset(string name)
        {
            using (var source = typeof(BackproppingPdf).Assembly.GetManifestResourceStream("Backpropping."+name+".png"))
            using (var data = new MemoryStream())
            {
                if (source == null) throw new IOException("Missing report artwork: "+name);
                source.CopyTo(data);
                return "data:image/png;base64,"+Convert.ToBase64String(data.ToArray());
            }
        }

        private static string Cover(BackproppingDocument doc, LoadResult loads)
        {
            var d = doc.Details ?? new BackproppingReportDetails();
            var systems = loads.Slabs.Where(r=>r.Index>0 && r.Transfer>0)
                .Select(r=>doc.Slabs[r.Index].PropId).Where(p=>!string.IsNullOrEmpty(p)).Distinct();
            string Field(string label, string value) => $"<div><dt>{label}</dt><dd>{H(value)}</dd></div>";
            return $"<section class='cover'><header>ENGINEERING CALCULATION<img class='brand' src='{Asset("doka")}'></header>" +
                $"<h1>Repropping calculation</h1><h2>{H(d.Title)}</h2><p>{H(string.Join(" / ",systems))}</p>" +
                $"<dl>{Field("Project",d.Project)}{Field("Contractor",d.Contractor)}</dl>" +
                $"<dl class='signoff'>{Field("Author",d.Author)}{Field("Checker",d.Checker)}{Field("Document no.",d.DocumentNumber)}</dl></section>";
        }

        private static string Check(SpacingResult grid)
        {
            string member=grid.Tower==null?"Prop":"Leg";
            bool safe=grid.PropLoadKn<=grid.CapacityKn;
            string comparison=grid.PropLoadKn==grid.CapacityKn?"=":safe?"&lt;":"&gt;";
            return $"<p class='{(safe?"safe":"exceeded")}'>{member} demand {N(grid.PropLoadKn,"0.00")} kN {comparison} permitted {member.ToLowerInvariant()} load {N(grid.CapacityKn,"0.0")} kN · {(safe?"SAFE":"EXCEEDED")}</p>";
        }

        private static string LayoutPlan(string storey, SlabInput slab, double load, SpacingResult grid)
        {
            bool tower=grid.Tower!=null;
            double pitch=grid.SpacingMm, scale=120/pitch;
            double hx=tower?BackproppingTower.FrameLength*scale/2:0, hy=tower?BackproppingTower.InterFrame*scale/2:0;
            var html=new StringBuilder($"<section class='sheet'><h1>Typical {(tower?"tower":"prop")} layout</h1><p>{H(storey)} · {H(slab.PropId)} · Interior repeating grid</p>");
            html.Append("<svg class='plan' viewBox='0 0 440 350' xmlns='http://www.w3.org/2000/svg'>");
            for(int i=0;i<3;i++)
            {
                int pos=95+i*120;
                html.Append($"<path d='M{pos} 37v285 M59 {pos-25}h321' stroke='#aabcc2' stroke-dasharray='3 3'/>");
            }
            html.Append(tower?"<rect x='215' y='130' width='60' height='60' fill='#ffe176'/>":"<rect x='155' y='130' width='120' height='120' fill='#ffe176'/>");
            for(int j=0;j<3;j++)
            for(int i=0;i<3;i++)
            {
                double x=95+i*120,y=70+j*120;
                if(tower)
                {
                    html.Append($"<rect x='{N(x-hx)}' y='{N(y-hy)}' width='{N(2*hx)}' height='{N(2*hy)}' fill='none' stroke='#123657' stroke-width='1.5'/>");
                    foreach(int dx in new[]{-1,1}) foreach(int dy in new[]{-1,1})
                        html.Append($"<rect x='{N(x+dx*hx-2)}' y='{N(y+dy*hy-2)}' width='4' height='4' fill='#123657'/>");
                }
                else html.Append($"<circle cx='{x}' cy='{y}' r='4' fill='#123657'/>");
            }
            string dim=N(pitch/1000,"0.000")+" m c/c";
            html.Append($"<g stroke='#196373'><path d='M95 29h120 M95 25v8 M215 25v8 M43 70v120 M39 70h8 M39 190h8'/></g><g fill='#196373' font-size='11' text-anchor='middle'><text x='155' y='22'>{dim}</text><text transform='translate(36 130) rotate(-90)'>{dim}</text></g></svg>");
            if(tower) html.Append($"<p>Internal leg centres: 1.524 × 1.500 m. Nearest leg centres between towers: {N((pitch-1524)/1000)} × {N((pitch-1500)/1000)} m.</p>");
            double side=pitch/1000/(tower?2:1), area=side*side;
            html.Append($"<p>Shaded tributary area: {N(side)} × {N(side)} = {N(area)} m².</p><p>Demand = {N(load)} kN/m² × {N(area)} m² = {N(grid.PropLoadKn,"0.00")} kN.</p>");
            return html.Append(Check(grid)).Append("</section>").ToString();
        }

        private static string TowerElevation(string storey, SlabInput slab, SpacingResult grid)
        {
            var t=grid.Tower;
            double height=slab.PropExtensionMm, scale=285/height, y=30+t.Opening*scale;
            var html=new StringBuilder($"<section class='sheet tower'><h1>D3 tower assembly and capacity</h1><p>{H(storey)} · Top-held tower · restrained head units · inter-frame spacing 1.50 m</p><h2>Tower elevation</h2><svg class='elevation' viewBox='0 0 520 340' xmlns='http://www.w3.org/2000/svg'><path d='M60 30v285 M150 30v285 M50 30h20 M140 30h20 M50 315h20 M140 315h20' stroke='#123657' stroke-width='3'/>");
            foreach(var pair in new[]{Tuple.Create(t.Large,1800),Tuple.Create(t.Medium,1200),Tuple.Create(t.Small,900)})
            for(int i=0;i<pair.Item1;i++)
            {
                double next=y+pair.Item2*scale;
                html.Append($"<path d='M60 {N(y)}H150L60 {N(next)}H150L60 {N(y)}' fill='none' stroke='#718799'/>");
                y=next;
            }
            html.Append($"<g fill='#123657' font-size='14'><text x='186' y='43'>Upper jack {N(t.Opening,"0.###")} mm</text><text x='186' y='145'>Vertical frames</text><text x='186' y='168'>{H(t.Stack)}</text><text x='186' y='211'>{(t.Braced?"Brace both jacks":"Unbraced jacks")}</text><text x='186' y='310'>Lower jack {N(t.Opening,"0.###")} mm</text></g></svg>");
            html.Append($"<p>Clear tower height {N(height/1000)} m. Jack openings include the plates.</p><p>Tallest-frame group up to {(t.Large>0?"1.80":"1.20")} m · at least two connected frame planes · maximum height in selected row {N(t.HeightLimit/1000,"0.0")} m.</p>");
            html.Append(Check(grid)).Append("<h2>Frame connections</h2><p>For each tower use the project head-restraint detail. Connect the two frame planes with diagonal crosses at every lift. Engage the cross safety catches and connect successive lifts with the D3 frame connections.</p><p><b>Adjacent towers:</b> No inter-tower bracing for the specified individual top-held towers. Provide the internal bracing and head restraint detailed for each tower.</p>");
            html.Append("<h2>Component schedule</h2>").Append(Table("Vertical frame (hot-dip galvanised steel)", "Doka article", "Diagonal cross for 1.50 m spacing"));
            if (t.Large > 0) html.Append(Cells("1.80 m","582942000","18.150"));
            if (t.Medium > 0) html.Append(Cells("1.20 m","582941000","12.150"));
            if (t.Small > 0) html.Append(Cells("0.90 m","582940000","9.150"));
            return html.Append("</tbody></table></section>").ToString();
        }

        private static string TowerSequence(SlabInput slab, SlabInput upper, TemperatureCurve curve)
        {
            int? stripping = BackproppingCalculator.InitialStripping(slab,curve).ProposedDay;
            double pourAge = slab.AgeDays - upper.AgeDays;
            var html = new StringBuilder($"<section class='sheet sequence'><h1>Stripping and repropping sequence</h1><p>{H(slab.Name)} to {H(upper.Name)} · Programme measured from the {H(slab.Name)} pour</p>");
            string day = stripping.HasValue ? $"Day {stripping.Value} after the {H(slab.Name)} pour" : "Strength target exceeds the selected curve";
            html.Append($"<div class='timing'><b>Proposed initial stripping &nbsp;/&nbsp; {H(slab.Name)}</b><h2>{day}</h2></div>");
            if (!stripping.HasValue || stripping.Value >= pourAge)
                html.Append("<p>Review the programme: the calculated timing does not establish an interval for stripping and repropping before the upper pour.</p>");
            string Step(string timing, string title, string instruction) =>
                $"<div class='step'><b>{H(timing)}</b><div><h3>{H(title)}</h3><p>{H(instruction)}</p></div></div>";
            string pourDay = $"Day {N(pourAge,"0.##")}";
            html.Append(Step(stripping.HasValue ? $"Day {stripping.Value}" : "Await review",
                $"Release original {slab.Name} shores",
                "Before releasing shores in a zone, verify the in-place concrete strength meets the project-approved stripping target and obtain release for the load-transfer sequence."))
                .Append(Step("After stripping", $"Install D3 towers beneath {slab.Name}",
                    "Complete the towers, jack bracing where required and head restraint for that zone. Check the installed arrangement against the approved assembly detail."))
                .Append(Step($"Before {pourDay}", $"Erect {upper.Name} shoring in released zones",
                    $"Confirm {slab.Name} can carry the erection loads at its actual age, including local shore reactions and material stacks. Begin erection once the zone is released for those loads."))
                .Append(Step(pourDay, $"Planned {upper.Name} pour",
                    $"Verify the {slab.Name} concrete strength and the D3 configuration assumed in the pour-stage calculation before the pour is released."))
                .Append($"<div class='removal'><h3>D3 removal after the {H(upper.Name)} pour</h3><p>Retain the towers until the removal-stage check accounts for the actual loads, remaining upper shores and verified slab strengths. Obtain a written release for the zone and dismantling sequence. A D3 removal date has yet to be established.</p></div>");
            return html.Append("</section>").ToString();
        }

        private static string References(BackproppingDocument doc, LoadResult loads)
        {
            var html=new StringBuilder();
            var systems = loads.Slabs.Where(r=>r.Index>0 && r.Transfer>0).Select(r=>doc.Slabs[r.Index].PropId).Distinct().ToArray();
            foreach (var name in systems)
            {
                if(name==BackproppingTower.Name || name.Contains(" eco ")) continue;
                var prop=BackproppingPropCatalog.All.Single(p=>p.Name==name);
                html.Append($"<section class='sheet'><h1>Component capacity reference</h1><h2>{H(name)}</h2>").Append(Table("Installed length (mm)","Restrained prop capacity (kN)"));
                for(int mm=prop.MinimumMm;mm<=prop.MaximumMm;mm+=100) html.Append(Cells(N(mm,"0"),N(prop.CapacityAt(mm),"0.0")));
                html.Append("</tbody></table></section>");
            }
            if (systems.Any(name=>name.Contains(" eco ")))
                html.Append($"<section class='sheet'><h1>Eurex eco props</h1><p>Component capacity reference</p><img class='eco' src='{Asset("eco")}'></section>");
            if (!systems.Contains(BackproppingTower.Name)) return html.ToString();
            html.Append($"<section class='sheet'><h1>D3 shoring towers</h1><p>Component capacity reference · top-held systems</p><div class='reference'><img src='{Asset("d3")}'>");
            foreach(var row in loads.Slabs.Where(r=>r.Index>0 && r.Transfer>0 && doc.Slabs[r.Index].PropId==BackproppingTower.Name))
            {
                var s=doc.Slabs[row.Index]; var tower=BackproppingTower.Fit(s.PropExtensionMm);
                int y=tower.Large>0 ? (tower.Opening<=150?158:245) : (tower.Opening<=150?417:504);
                if(tower.HeightLimit==20000) y+=21;
                html.Append($"<span style='top:{N(100.0*y/685)}%'></span>");
            }
            return html.Append("</div></section>").ToString();
        }

        internal static string Report(BackproppingDocument doc, LoadResult loads)
        {
            var html = new StringBuilder(@"<!doctype html><meta charset='utf-8'><title>Backpropping calculation</title><style>
@page{size:A4;margin:13mm}*{box-sizing:border-box}body{font:10pt 'Segoe UI',sans-serif;color:#263741;margin:0}
h1{font-size:24pt;margin:0 0 3mm}h2{font-size:15pt;margin:4mm 0 2mm;break-after:avoid}
p{margin:1.5mm 0;line-height:1.35}.level{display:grid;grid-template-columns:48% 52%;break-inside:avoid;min-height:16mm}
.level img{display:block;width:100%;height:100%;position:absolute}.drawing{position:relative}.drawing.last img{height:3mm}
.arrow{position:absolute;left:31%;top:44%;font-size:24pt;color:#126c78}
.info{padding:1mm 3mm 1mm 0;overflow-wrap:anywhere}.info small{display:block}
mark{display:inline-block;background:#ffe06a;padding:1mm 2mm;margin-top:1mm;font-weight:bold}
table{border-collapse:collapse;width:100%;font-size:8.5pt;margin:2mm 0}td,th{text-align:right;padding:1.4mm;border-bottom:1px solid #d4dfe1;overflow-wrap:anywhere}
th{background:#e8eff1}td:first-child,th:first-child{text-align:left}tr{break-inside:avoid}thead{display:table-header-group}
.capacity{break-before:page}svg{width:100%;max-height:62mm}.note{color:#455f68;font-size:9pt}
@page{margin:22mm 13mm 17mm;@bottom-right{content:'Page ' counter(page) ' of ' counter(pages);font:9pt sans-serif;color:#455f68}
@top-right{content:'';width:29mm;background: url(LOGO) right center / 29mm auto no-repeat}}
@page:first{@top-right{background:none}}
.cover{break-after:page}.cover header{display:flex;justify-content:space-between;align-items:center;padding-bottom:4mm;margin-bottom:9mm;border-bottom:3px solid #ffda00}.brand{width:42mm}
.cover dl{margin:18mm 0}.cover dl div{padding:3mm 0;border-bottom:1px solid #c7d5d9;overflow-wrap:anywhere}.cover dt{color:#455f68}.cover dd{margin:1.5mm 0;font-weight:600}.signoff{display:grid;grid-template-columns:1fr 1fr;column-gap:7mm}
.sheet{break-before:page}.safe,.exceeded{font-weight:bold;padding:3mm}.safe{color:#165d32;background:#eaf6ec}.exceeded{color:#9c2224}.sheet .plan,.sheet .elevation{max-height:145mm;height:auto}.eco{display:block;max-width:100%;height:auto;max-height:210mm;margin:auto}.reference{position:relative;width:100%;margin:auto}.reference img{display:block;width:100%;height:auto}.reference span{position:absolute;left:77%;width:9%;height:3.1%;border:2px solid #d00000}
.level{grid-template-columns:37% 63%}.drawing{display:grid;grid-template-columns:19% 72% 9%;align-items:start}.level .drawing img{position:static;grid-column:2;grid-row:1;width:100%;height:auto}.drawing .tag{grid-row:1;grid-column:1;text-align:center;align-self:center;font-size:8pt}.drawing .arrow{display:block;position:static;font-size:20pt}.drawing .height{grid-column:3;grid-row:1;align-self:stretch;writing-mode:vertical-rl;text-align:center;border-left:1px solid #196373;color:#196373;font-size:8pt}
.spacing td,.spacing th{overflow-wrap:normal;padding:1mm}.spacing td:last-child,.spacing th:last-child{white-space:nowrap}
.cover .signoff div{border:0}
.loadpage.spacious h1{margin-bottom:9mm}.loadpage.spacious h2{margin-top:11mm}.loadpage.spacious p{margin:3mm 0}.loadpage.spacious td,.loadpage.spacious th{padding-top:3mm;padding-bottom:3mm}
.tower .elevation{display:block;height:78mm}.sequence{overflow-wrap:anywhere}.sequence .timing{background:#e8eff1;padding:4mm;margin:6mm 0 9mm}.sequence .timing h2{margin:2mm 0 0;font-size:19pt}
.sequence .step{display:grid;grid-template-columns:23% 1fr;column-gap:2%;border-top:1px solid #c8d6d9;padding:5mm 0;break-inside:avoid}
.sequence .step>b,.sequence .timing>b{font-size:9pt;text-transform:uppercase;color:#176475}
.sequence h3{margin:0 0 2mm;font-size:12pt}.sequence .step p,.sequence .removal p{margin:0;line-height:1.4}
.sequence .removal{margin-top:5mm;background:#f5f2e5;padding:5mm;break-inside:avoid}
</style>".Replace("LOGO",Asset("doka")));
            html.Append(Cover(doc,loads)).Append(doc.Slabs.Count<=3 ? "<section class='loadpage spacious'>" : "<section class='loadpage'>").Append("<h1>Load-transfer section</h1>");
            var plans=new StringBuilder(); var elevations=new StringBuilder();
            var arrangements=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<string>>();
            var layouts=new System.Collections.Generic.Dictionary<string,Tuple<SlabInput,double,SpacingResult>>();

            var breakdown = new StringBuilder(Table("Slab", "Incoming", "Self-weight", "Working", "Demand", "Capacity", "Excess", "Passed down"));
            var spacing = new StringBuilder();
            var capacity = new StringBuilder(Table("Slab", "Age (d)", "Self-weight + SIDL + final live (kN/m²)", "Strength", "Adopted capacity (kN/m²)"));
            string raftReaction = "";
            foreach (var row in loads.Slabs)
            {
                var slab = doc.Slabs[row.Index];
                bool final = row.Index == loads.Slabs.Count - 1;
                if (slab.IsRaft)
                {
                    html.Append($"<div class='level'><div class='info'><b>{H(slab.Name)} · Raft foundation</b><small>Backpropping terminates at raft</small><mark>{N(row.Incoming)} kN/m² received</mark><small>{raftReaction}</small></div><div class='drawing last'><img src='data:image/png;base64,{SectionImage(false,true)}'></div></div>");
                    breakdown.Append(Cells(slab.Name+" · raft",N(row.Incoming),"—","—","—","—","—","—"));
                    continue;
                }
                html.Append($"<div class='level'><div class='info'><b>{H(slab.Name)}</b><small>{N(slab.ThicknessMm,"0.##")} mm · {N(slab.AgeDays,"0.##")} days</small><small>SIDL {N(slab.Sidl)} · final live {N(slab.FinalLiveLoad)} kN/m²</small><mark>↓ {N(row.DownwardLoad)} kN/m²{(final ? " · transfer stops" : "")}</mark></div>");
                html.Append($"<div class='drawing {(final ? "last" : "")}'><img src='data:image/png;base64,{SectionImage(row.Index == 0,final,slab.PropId==BackproppingTower.Name && row.Index>0)}'>");
                html.Append(final ? "</div></div>" : $"<small class='tag'>{(row.Index==0?"Main shores":"Repropping")}<span class='arrow'>↓</span></small><small class='height'>{N(slab.FloorHeightBelowMm/1000)} m</small></div></div>");
                if (row.Index == 0) continue;
                breakdown.Append(Cells(slab.Name,N(row.Incoming),N(row.SelfWeight),N(row.ConstructionLive),N(row.Demand),N(row.Capacity),N(row.Transfer),N(row.DownwardLoad)));
                double basis = row.SelfWeight + slab.Sidl + slab.FinalLiveLoad;
                capacity.Append(Cells(slab.Name,N(slab.AgeDays,"0.##"),$"{N(row.SelfWeight)} + {N(slab.Sidl)} + {N(slab.FinalLiveLoad)} = {N(basis)}",N(row.StrengthPercent,"0.###")+"%",N(row.Capacity)));
                if (row.Transfer <= 0) continue;
                double? limit = slab.MaximumSpacingMm ?? doc.ProjectMaximumMm;
                var grid = BackproppingCalculator.SupportSpacing(row.DownwardLoad,slab,limit);
                string storey=slab.Name+" → "+doc.Slabs[row.Index+1].Name;
                string key=string.Join("|",slab.PropId,N(slab.PropExtensionMm,"R"),N(row.DownwardLoad,"R"),N(grid.SpacingMm,"R"));
                if(!arrangements.ContainsKey(key)) { arrangements[key]=new System.Collections.Generic.List<string>(); layouts[key]=Tuple.Create(slab,row.DownwardLoad,grid); }
                arrangements[key].Add(storey);
                if (doc.Slabs[row.Index+1].IsRaft)
                    raftReaction = $"{N(grid.PropLoadKn,"0.00")} kN per interior {(grid.Tower==null?"prop foot":"tower leg")}";
                spacing.Append(Cells(slab.Name.Replace(" ","\u00a0")+" → "+doc.Slabs[row.Index+1].Name.Replace(" ","\u00a0"),slab.PropId,
                    N(slab.PropExtensionMm,"0.##"),N(grid.CapacityKn,"0.##"),limit.HasValue ? N(limit.Value,"0.##") : "—",
                    N(grid.SpacingMm,"0")+" × "+N(grid.SpacingMm,"0"),N(grid.PropLoadKn),grid.PropLoadKn<=grid.CapacityKn?"SAFE":"EXCEEDED"));
            }
            foreach (var lower in doc.Slabs.Skip(loads.Slabs.Count))
            {
                string label = lower.IsRaft ? "Raft foundation · no backprop reaction reaches raft" : "Outside required support stack";
                html.Append($"<div class='level'><div class='info'><b>{H(lower.Name)}</b><small>{label}</small></div><div class='drawing last'><img src='data:image/png;base64,{SectionImage(false,true)}'></div></div>");
            }
            html.Append($"<h2>Load breakdown</h2><p>Active pour (kN/m²): {N(loads.Slabs[0].SelfWeight)} concrete + 0.500 formwork + 0.750 working + {N(loads.Heaping)} heaping = <b>{N(loads.PourLoad)}</b>.</p><p>All table values are in kN/m².</p>");
            html.Append(breakdown).Append("</tbody></table><p>Positive excess passes down with a 0.400 kN/m² allowance per required backprop storey.</p>");
            if (spacing.Length > 0)
            {
                bool towers = layouts.Values.All(v=>v.Item3.Tower!=null);
                html.Append(towers?"<h2>Tower spacing</h2>":"<h2>Reprop spacing</h2>").Append("<div class='spacing'>")
                    .Append(Table("Storey","Support","Clear height (mm)",towers?"Per leg (kN)":"Per leg/prop (kN)","Max. (mm)","Square grid (mm)","Demand (kN)","Check"))
                    .Append(spacing).Append("</tbody></table></div>");
            }
            if (doc.Slabs.Last().IsRaft)
            {
                html.Append(doc.Slabs[loads.Slabs.Last().Index].IsRaft
                    ? "<p>Foundation capacity, local bearing and ground or pile support require a separate check.</p>"
                    : "<p>No backprop reaction reaches the raft.</p>");
                if (loads.Slabs.Count == 2 && doc.Slabs[1].IsRaft)
                    return html.Append(InitialStrippingSection(doc)).Append("<p>Main shores deliver the active pour load directly to the raft. Main shore reactions are outside this calculation.</p></section>").Append(References(doc,loads)).ToString();
            }
            foreach(var arrangement in arrangements)
            {
                var data=layouts[arrangement.Key]; string storeys=string.Join(", ",arrangement.Value);
                plans.Append(LayoutPlan(storeys,data.Item1,data.Item2,data.Item3));
                if(data.Item3.Tower!=null) elevations.Append(TowerElevation(storeys,data.Item1,data.Item3));
            }
            html.Append("</section>").Append(plans);
            html.Append("<section class='capacity'><h1>Slab capacity</h1><p>Concrete self-weight = thickness in metres × 25 kN/m³.</p><p><b>Slab capacity = (self-weight + SIDL + final live load) × strength fraction at slab age.</b></p><h2>Concrete strength development</h2>");
            html.Append(StrengthGraph(doc, loads)).Append(capacity).Append("</tbody></table><h2>Load passed down</h2>");
            foreach (var row in loads.Slabs.Skip(1).Where(r => !doc.Slabs[r.Index].IsRaft))
            {
                html.Append($"<p><b>{H(doc.Slabs[row.Index].Name)}</b> · Demand {N(row.Demand)} kN/m², adopted capacity {N(row.Capacity)} kN/m². ");
                html.Append(row.Transfer > 0 ? $"Excess {N(row.Transfer)} + 0.400 = <b>{N(row.DownwardLoad)} kN/m²</b> to {H(doc.Slabs[row.Index+1].Name)}.</p>" : "Capacity covers demand. Transfer stops.</p>");
            }
            html.Append(InitialStrippingSection(doc)).Append("</section>").Append(elevations);
            foreach (var row in loads.Slabs.Where(r => r.Index > 0 && r.Transfer > 0 && doc.Slabs[r.Index].PropId == BackproppingTower.Name))
                html.Append(TowerSequence(doc.Slabs[row.Index],doc.Slabs[row.Index-1],doc.Curve));
            return html.Append(References(doc,loads)).ToString();
        }

        private static string InitialStrippingSection(BackproppingDocument doc)
        {
            var html = new StringBuilder();
            html.Append("<h2>Proposed initial stripping</h2>");
            foreach (var slab in doc.Slabs.Where(s => !s.IsRaft))
            {
                var day = BackproppingCalculator.InitialStripping(slab, doc.Curve).ProposedDay;
                string timing = day.HasValue ? $"Day {day.Value} after this slab’s pour" : "No day proposed — strength target exceeds curve";
                html.Append($"<p><b>{H(slab.Name)}</b> · {H(timing)}</p>");
            }
            return html.ToString();
        }

        private static string StrengthGraph(BackproppingDocument doc, LoadResult loads)
        {
            var curve = doc.Curve;
            int temp = curve == TemperatureCurve.A ? 15 : curve == TemperatureCurve.B ? 10 : 5;
            var svg = new StringBuilder($"<p>Selected curve {curve} · curing temperature ≥{temp}°C · medium strength development</p><svg viewBox='0 0 580 190' xmlns='http://www.w3.org/2000/svg'><text x='10' y='15'>Strength (% of 28-day value)</text>");
            for (int percent = 0; percent <= 100; percent += 25)
            {
                double y = 149 - percent * 1.2;
                svg.Append($"<path d='M48 {N(y)}h500' stroke='#d3dde1'/><text x='40' y='{N(y+4)}' text-anchor='end'>{percent}</text>");
            }
            var points = Enumerable.Range(0,29).Select(day => N(48+day*500.0/28)+","+N(149-1.2*BackproppingCalculator.Strength(day,curve)));
            svg.Append("<polyline fill='none' stroke='#126c78' stroke-width='3' points='").Append(string.Join(" ",points)).Append("'/>");
            foreach (double age in loads.Slabs.Skip(1).Where(r => !doc.Slabs[r.Index].IsRaft).Select(r => Math.Min(28,doc.Slabs[r.Index].AgeDays)).Distinct())
            {
                string x = N(48+age*500/28);
                string y = N(149-1.2*BackproppingCalculator.Strength(age,curve));
                svg.Append($"<path d='M{x} {y}V149' stroke='#126c78' stroke-width='1.2' stroke-dasharray='5 4'/><circle cx='{x}' cy='{y}' r='4' fill='#126c78'/>");
            }
            for (int day = 0; day <= 28; day += 4)
                svg.Append($"<text x='{N(48+day*500.0/28)}' y='167' text-anchor='middle'>{day}</text>");
            return svg.Append("<text x='300' y='188' text-anchor='middle'>Concrete age (days) · strength capped at day 28</text></svg>").ToString();
        }

        internal static void Save(Window owner, string html, string destination)
        {
            var browser = new WebView2();
            var preview = new Window { Owner = owner, Title = "Generating PDF report", Width = 720, Height = 580, Content = browser, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            string source = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".html");
            string staging = Path.Combine(Path.GetDirectoryName(destination), Guid.NewGuid()+".pdf");
            Exception failure = null;
            bool working = true;
            preview.Closing += (s,e) => e.Cancel = working;
            preview.Loaded += async (s,e) =>
            {
                try
                {
                    File.WriteAllText(source, html, new UTF8Encoding(false));
                    string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DokaRevitAutomation","ReportBrowser");
                    var env = await CoreWebView2Environment.CreateAsync(null, profile);
                    await browser.EnsureCoreWebView2Async(env);
                    var loaded = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    browser.NavigationCompleted += (sender,args) => loaded.TrySetResult(args.IsSuccess);
                    browser.Source = new Uri(source);
                    if (!await loaded.Task) throw new IOException("The report could not be rendered.");
                    var settings = env.CreatePrintSettings();
                    settings.PageWidth = 210 / 25.4; settings.PageHeight = 297 / 25.4;
                    settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0;
                    settings.ShouldPrintBackgrounds = true; settings.ShouldPrintHeaderAndFooter = false;
                    if (!await browser.CoreWebView2.PrintToPdfAsync(staging, settings)) throw new IOException("PDF generation failed.");
                    if (File.Exists(destination)) File.Replace(staging, destination, null);
                    else File.Move(staging, destination);
                }
                catch (Exception ex) { failure = ex; }
                finally { working = false; preview.Close(); }
            };
            try { preview.ShowDialog(); }
            finally { browser.Dispose(); File.Delete(source); File.Delete(staging); }
            if (failure != null) throw new IOException("Could not save the PDF report. " + failure.Message, failure);
        }
    }
}
