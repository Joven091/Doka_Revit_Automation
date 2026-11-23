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

        private static string SectionImage(bool mainShores, bool final)
        {
            // Render each storey at its own origin, independent of its offset in the screen stack.
            var drawing = new Grid();
            drawing.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            drawing.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            BackproppingWindow.AddSectionDrawing(drawing, 0, mainShores, !final, Brushes.DarkGray);
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
.capacity{break-before:page}svg{width:100%;max-height:62mm}.note{color:#455f68;font-size:9pt}</style><h1>Backpropping calculation</h1><h2>Slab and support section</h2>");
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
                html.Append($"<div class='drawing {(final ? "last" : "")}'><img src='data:image/png;base64,{SectionImage(row.Index == 0,final)}'>");
                html.Append(final ? "</div></div>" : "<span class='arrow'>↓</span></div></div>");
                if (row.Index == 0) continue;
                breakdown.Append(Cells(slab.Name,N(row.Incoming),N(row.SelfWeight),N(row.ConstructionLive),N(row.Demand),N(row.Capacity),N(row.Transfer),N(row.DownwardLoad)));
                double basis = row.SelfWeight + slab.Sidl + slab.FinalLiveLoad;
                capacity.Append(Cells(slab.Name,N(slab.AgeDays,"0.##"),$"{N(row.SelfWeight)} + {N(slab.Sidl)} + {N(slab.FinalLiveLoad)} = {N(basis)}",N(row.StrengthPercent,"0.###")+"%",N(row.Capacity)));
                if (row.Transfer <= 0) continue;
                double? limit = slab.MaximumSpacingMm ?? doc.ProjectMaximumMm;
                var grid = BackproppingCalculator.CalculateSpacing(row.DownwardLoad, slab.PropId, slab.PropExtensionMm, limit);
                if (doc.Slabs[row.Index+1].IsRaft)
                    raftReaction = $"{N(grid.PropLoadKn,"0.00")} kN per interior prop foot";
                spacing.Append(Cells(slab.Name+" → "+doc.Slabs[row.Index+1].Name,slab.PropId,
                    N(slab.PropExtensionMm,"0.##"),N(grid.CapacityKn,"0.##"),limit.HasValue ? N(limit.Value,"0.##") : "—",
                    N(grid.SpacingMm,"0")+" × "+N(grid.SpacingMm,"0"),N(grid.PropLoadKn),N(grid.UtilizationPercent,"0.##")+"%"));
            }
            foreach (var lower in doc.Slabs.Skip(loads.Slabs.Count))
            {
                string label = lower.IsRaft ? "Raft foundation · no backprop reaction reaches raft" : "Outside required support stack";
                html.Append($"<div class='level'><div class='info'><b>{H(lower.Name)}</b><small>{label}</small></div><div class='drawing last'><img src='data:image/png;base64,{SectionImage(false,true)}'></div></div>");
            }
            html.Append($"<h2>Load breakdown</h2><p>Active pour (kN/m²): {N(loads.Slabs[0].SelfWeight)} concrete + 0.500 formwork + 0.750 working + {N(loads.Heaping)} heaping = <b>{N(loads.PourLoad)}</b>.</p><p>All table values are in kN/m².</p>");
            html.Append(breakdown).Append("</tbody></table><p>Positive excess passes down with a 0.400 kN/m² allowance per required backprop storey.</p>");
            if (spacing.Length > 0)
                html.Append("<h2>Backprop spacing</h2>").Append(Table("Storey","Prop","Extension (mm)","Capacity (kN)","Max. (mm)","Square grid (mm)","Load/prop (kN)","Use")).Append(spacing).Append("</tbody></table>");
            if (doc.Slabs.Last().IsRaft)
            {
                html.Append(doc.Slabs[loads.Slabs.Last().Index].IsRaft
                    ? "<p>Foundation capacity, local bearing and ground or pile support require a separate check.</p>"
                    : "<p>No backprop reaction reaches the raft.</p>");
                if (loads.Slabs.Count == 2 && doc.Slabs[1].IsRaft)
                    return html.Append("<p>Main shores deliver the active pour load directly to the raft. Main shore reactions are outside this calculation.</p>").ToString();
            }
            html.Append("<section class='capacity'><h1>Slab capacity</h1><p>Concrete self-weight = thickness in metres × 25 kN/m³.</p><p><b>Adopted capacity = (self-weight + SIDL + final live load) × strength fraction at slab age.</b></p><p class='note'>This capacity approximation requires project engineering acceptance.</p><h2>Concrete strength development</h2>");
            html.Append(StrengthGraph(doc, loads)).Append(capacity).Append("</tbody></table><h2>Load passed down</h2>");
            foreach (var row in loads.Slabs.Skip(1).Where(r => !doc.Slabs[r.Index].IsRaft))
            {
                html.Append($"<p><b>{H(doc.Slabs[row.Index].Name)}</b> · Demand {N(row.Demand)} kN/m², adopted capacity {N(row.Capacity)} kN/m². ");
                html.Append(row.Transfer > 0 ? $"Excess {N(row.Transfer)} + 0.400 = <b>{N(row.DownwardLoad)} kN/m²</b> to {H(doc.Slabs[row.Index+1].Name)}.</p>" : "Capacity covers demand. Transfer stops.</p>");
            }
            return html.Append("</section>").ToString();
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
                svg.Append($"<circle cx='{N(48+age*500/28)}' cy='{N(149-1.2*BackproppingCalculator.Strength(age,curve))}' r='4' fill='#126c78'/>");
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
