using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using EngineeringDashboard;

internal static class DimensionLaneUiTests
{
    internal static void Run()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,UseBeamNetwork=true,TransferSlabLoads=true,HasSelectedSupportSystem=true};
        var view=new BeamFormworkWorkspace(vm,null,null,null);view.Show();
        vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=1,PositionMm=1000});
        var canvas=view.Drawing;canvas.Measure(new Size(900,650));canvas.Arrange(new Rect(0,0,900,650));canvas.Select(FormworkObject.Crossing,0);
        // Block the normal lane and the first upper lane. A selected dimension
        // must use a higher lane instead of moving its text below the baseline.
        canvas.AnnotationBounds.Clear();canvas.AnnotationLabels.Clear();canvas.HorizontalDimensionLabels.Clear();
        canvas.AnnotationBounds.Add(new Rect(0,365,900,35));
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())
        {
            var placed=typeof(BeamFormworkCanvas).GetMethod("Dimension",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(canvas,new object[] {dc,new Point(100,400),new Point(300,400),"400",FormworkObject.Crossing,0,null,null,-4.0,false});
            if(!Equals(placed,true) || canvas.HorizontalDimensionLabels.Count!=1 || canvas.HorizontalDimensionLabels[0].Item1.Bottom>=400)
                throw new Exception("Crowded crossing dimension text must stay above its horizontal line.");
        }
        if(canvas.AnnotationBounds.Skip(1).Any(r=>r.IntersectsWith(canvas.AnnotationBounds[0])))throw new Exception("Dimension lane must still avoid the blocked labels.");
        view.Close();Console.WriteLine("Passed 2 dimension lane UI checks.");
    }
}
