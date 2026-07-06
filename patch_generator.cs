using System;
using System.IO;

public class PatchGenerator
{
    public static void Main()
    {
        string path = "src/Models/DiagramDataGenerator.cs";
        string content = File.ReadAllText(path);

        // 1. Update signature
        content = content.Replace(
            "public static (PlotModel shearPlot, PlotModel momentPlot, PlotModel deflectionPlot, double maxShear, double minShear, double maxMoment, double minMoment, double maxDeflection, double minDeflection) GeneratePlots(",
            "public static (PlotModel loadPlot, PlotModel shearPlot, PlotModel momentPlot, PlotModel deflectionPlot, double maxShear, double minShear, double maxMoment, double minMoment, double maxDeflection, double minDeflection) GeneratePlots(");

        // 2. Add loadPlot init
        content = content.Replace(
            "var shearPlot = new PlotModel { Title = $\"{titlePrefix} - Shear Diagram\" };",
            "var loadPlot = new PlotModel { Title = $\"{titlePrefix} - Loading Diagram\" };\n            var shearPlot = new PlotModel { Title = $\"{titlePrefix} - Shear Diagram\" };");

        // 3. Update return
        content = content.Replace(
            "return (shearPlot, momentPlot, deflectionPlot, maxShear, minShear, maxMoment, minMoment, maxDeflection, minDeflection);",
            "return (loadPlot, shearPlot, momentPlot, deflectionPlot, maxShear, minShear, maxMoment, minMoment, maxDeflection, minDeflection);");

        File.WriteAllText(path, content);
    }
}
