using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace EngineeringDashboard.SteelShape
{
    public static class SteelShapeContent
    {
        public static string Read()
        {
            using (var stream = typeof(SteelShapeContent).Assembly.GetManifestResourceStream("EngineeringDashboard.SteelShape.html"))
            {
                if (stream == null) throw new InvalidOperationException("The Steel Shape page is missing from this installation.");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        public static string WriteBrowserCopy()
        {
            var version = typeof(SteelShapeContent).Assembly.GetName().Version.ToString();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DokaRevitAutomation", "SteelShape", version);
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "SteelShape.html");
            var html = Read();
            if (!File.Exists(target) || File.ReadAllText(target, Encoding.UTF8) != html)
                File.WriteAllText(target, html, new UTF8Encoding(false));
            return target;
        }
    }
}
