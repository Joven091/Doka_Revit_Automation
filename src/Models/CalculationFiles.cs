using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace EngineeringDashboard
{
    public sealed class CalculationField { public string Name { get; set; } public string Value { get; set; } }
    public sealed class CalculationRecord
    {
        public int Version { get; set; } = 1;
        public string Kind { get; set; }
        public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public List<CalculationField> Inputs { get; set; } = new List<CalculationField>();
    }
    public static class CalculationFiles
    {
        public static CalculationRecord Capture(string kind, object model, string names)
        {
            var record = new CalculationRecord { Kind = kind };
            foreach (string name in names.Split(','))
                record.Inputs.Add(new CalculationField { Name = name, Value = InputValue(model.GetType().GetProperty(name).GetValue(model)) });
            return record;
        }
        private static string InputValue(object value)
        {
            if(value is double number)
            {
                if(double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("Saved numeric inputs must be finite, including inactive fields. Correct the value before saving or exporting.");
                return number.ToString("R",CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value,CultureInfo.InvariantCulture);
        }
        public static void Apply(CalculationRecord record, string kind, object model, string names)
        {
            if (record == null || record.Version != 1 || record.Kind != kind || record.Inputs == null) throw new ArgumentException("This is not a supported " + kind + " calculation file.");
            if(record.Inputs.Any(f => f == null || f.Name == null) || record.Inputs.Select(f=>f.Name).Distinct().Count()!=record.Inputs.Count) throw new ArgumentException("Calculation fields are missing or duplicated.");
            foreach(string name in names.Split(','))
            {
                var field = record.Inputs.SingleOrDefault(f => f.Name == name);
                if(field == null) throw new ArgumentException("The calculation file is missing " + name + ".");
                var property = model.GetType().GetProperty(name);
                var value = Convert.ChangeType(field.Value, property.PropertyType, CultureInfo.InvariantCulture);
                if(value is double number && (double.IsNaN(number) || double.IsInfinity(number))) throw new ArgumentException("Invalid numeric input: " + name);
                property.SetValue(model, value);
            }
        }
        public static T Read<T>(string path)
        {
            using(var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2000000 }))
                return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
        public static void Write<T>(string path, T value)
        {
            AtomicWrite(path, temp => { using(var writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) })) new XmlSerializer(typeof(T)).Serialize(writer, value); });
        }
        private static void AtomicWrite(string path, Action<string> write)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { write(temp); if(File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        public static void Save<T>(Window owner, T value, string kind)
        {
            var dialog = new SaveFileDialog { Filter = "Calculation files (*.dokacalc)|*.dokacalc", DefaultExt = ".dokacalc", FileName = kind + " calculation", AddExtension = true };
            if(dialog.ShowDialog(owner)==true) Write(dialog.FileName, value);
        }
        public static string ChooseLoad(Window owner)
        {
            var dialog = new OpenFileDialog { Filter = "Calculation files (*.dokacalc)|*.dokacalc" };
            return dialog.ShowDialog(owner)==true ? dialog.FileName : null;
        }
        public static void Export(Window owner, string kind, IEnumerable<CalculationField> inputs, IEnumerable<CalculationField> results, string assumptions)
        {
            string html = Report(kind, inputs, results, assumptions);
            ExportHtml(owner,kind,html);
        }
        public static void ExportHtml(Window owner,string kind,string html)
        {
            var dialog = new SaveFileDialog { Filter = "Printable report (*.html)|*.html", DefaultExt = ".html", FileName = kind + " calculation report", AddExtension = true };
            if(dialog.ShowDialog(owner)==true) { AtomicWrite(dialog.FileName, temp => File.WriteAllText(temp, html, new UTF8Encoding(false))); MessageBox.Show(owner,"Report saved. Open it in a browser to print or save as PDF.\n" + dialog.FileName,"Calculation report"); }
        }
        public static string Report(string kind, IEnumerable<CalculationField> inputs, IEnumerable<CalculationField> results, string assumptions)
        {
            Func<string,string> esc = value => WebUtility.HtmlEncode(value ?? "");
            var html = new StringBuilder("<!doctype html><html><head><meta charset='utf-8'><title>Calculation report</title><style>body{font:14px Arial;color:#202a33;margin:32px;max-width:1000px}h1{border-bottom:5px solid #d4aa00;padding-bottom:12px}table{border-collapse:collapse;width:100%;margin:16px 0}td{border:1px solid #ddd;padding:8px;white-space:pre-wrap}td:first-child{width:48%;background:#f5f5f5}p{white-space:pre-wrap;line-height:1.5}tr{break-inside:avoid}@media print{body{margin:0}h2{break-after:avoid}}</style></head><body>");
            html.Append("<h1>"+esc(kind)+" calculation</h1><p>Generated UTC: "+esc(DateTime.UtcNow.ToString("u"))+"\nCalculator assembly: "+esc(typeof(CalculationFiles).Assembly.GetName().Version.ToString())+" · Record format 1</p>");
            foreach(var section in new[]{Tuple.Create("Inputs",inputs),Tuple.Create("Results and checks",results)})
            { html.Append("<h2>"+section.Item1+"</h2><table>");foreach(var field in section.Item2)html.Append("<tr><td>"+esc(Label(field.Name))+"</td><td>"+esc(field.Value)+"</td></tr>");html.Append("</table>"); }
            return html.Append("<h2>Calculation basis and limitations</h2><p>"+esc(assumptions)+"</p></body></html>").ToString();
        }
        private static string Label(string name) => Regex.Replace(name ?? "", "(?<=[a-z])(?=[A-Z])", " ");
        public static CalculationField Field(string name, object value) => new CalculationField { Name=name, Value=Convert.ToString(value,CultureInfo.InvariantCulture) };
        public static void Run(Window owner, Action action)
        {
            try { action(); }
            catch(Exception ex) when(ex is ArgumentException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is OverflowException || ex is XmlException)
            { MessageBox.Show(owner,ex.Message,"Calculation file",MessageBoxButton.OK,MessageBoxImage.Warning); }
        }
    }
}
