using System;
using System.Linq;
using EngineeringDashboard;

internal static class CrossingFootprintTests
{
    private static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    private static double[] Coordinates(FormworkSolid solid)=>new[] {solid.X0,solid.X1,solid.Y0,solid.Y1,solid.Z0,solid.Z1};
    internal static void Run()
    {
        var random=new Random(14973);int comparisons=0;
        for(int example=0;example<300;example++)
        {
            double originX=random.Next(-5000,5000)+0.125,originY=random.Next(-5000,5000)+0.375;
            double width=new[] {600.0,1000,1500,1524,2000}[example%5],head=-random.Next(500,2500)-0.25;
            var layout=new TowerLayout {WidthMm=width,PositionsMm=new[] {random.Next(-400,400)+0.125,random.Next(800,2400)+0.875},Joined=new[] {true}};
            string owner="X"+(example+1);
            var actual=CrossBeamSupportOptimizer.ProjectedTowerLegs(layout,owner,originX,originY,head).ToArray();
            // The original FinishDedicated leg construction, retained as the reference.
            var expected=layout.PositionsMm.SelectMany(station=>new[] {-1,1}.Select(side=>new FormworkSolid {
                Name=owner+" tower leg",TowerOwner=owner,X0=originX+side*layout.WidthMm/2-40,X1=originX+side*layout.WidthMm/2+40,
                Y0=originY+station-40,Y1=originY+station+40,Z0=-1e6,Z1=head,IsLeg=true
            })).ToArray();
            Check(actual.Length==4,"Projected tower footprint must have all four actual legs");
            var obstacles=Enumerable.Range(0,24).Select(i=>new FormworkSolid {
                Name="Obstacle "+i,X0=originX+random.Next(-1500,1500),Y0=originY+random.Next(-500,3000),Z0=head+random.Next(-1000,1000),
                IsLeg=i%4==0,IsTowerEnvelope=i%4==1,IsSideShutter=i%4==2,TowerOwner=i%3==0 ? owner : "Other"
            }).ToArray();
            foreach(var obstacle in obstacles) {obstacle.X1=obstacle.X0+random.Next(80,800);obstacle.Y1=obstacle.Y0+random.Next(80,800);obstacle.Z1=obstacle.Z0+random.Next(80,800);}
            var index=new FormworkObstacleIndex(obstacles);
            for(int leg=0;leg<4;leg++)
            {
                Check(Coordinates(actual[leg]).SequenceEqual(Coordinates(expected[leg])) && actual[leg].Name==expected[leg].Name
                    && actual[leg].TowerOwner==expected[leg].TowerOwner && actual[leg].IsLeg && !actual[leg].IsTowerEnvelope,"Early leg solid differs from final result geometry");
                foreach(var obstacle in obstacles) {Check(actual[leg].Conflicts(obstacle)==expected[leg].Conflicts(obstacle),"Early leg changed a physical clash decision");comparisons++;}
                Check(ReferenceEquals(index.FirstConflict(actual[leg]),obstacles.FirstOrDefault(expected[leg].Conflicts)),"Indexed early leg clash differs from final linear scan");
            }
        }
        var single=new TowerLayout {WidthMm=1524,PositionsMm=new[] {200.0,1724.0},Joined=new[] {true}};
        var legs=CrossBeamSupportOptimizer.ProjectedTowerLegs(single,"X1",3450,1000,-1550).ToArray();var first=legs[0];
        var timber=new FormworkSolid {X0=first.X0,X1=first.X1,Y0=first.Y0,Y1=first.Y1,Z0=-1600,Z1=-1400};
        Check(first.Conflicts(timber),"Actual leg must reject timber intersections");
        Check(!CrossBeamSupportOptimizer.TowerEnvelopes(single,"X1",3450,1000,false,-1550).Any(e=>e.Conflicts(timber)),"Frame reservation alone should not stand in for leg/timber checking");
        timber.Z0=first.Z1;Check(!first.Conflicts(timber),"Intended bearing contact was rejected");
        timber.Z0=first.Z1-0.000001;Check(first.Conflicts(timber),"A strict interior leg intersection was omitted");
        timber.IsTowerEnvelope=true;timber.TowerOwner="X1";Check(!first.Conflicts(timber),"Leg must fit inside its own frame reservation");
        timber.TowerOwner="X2";Check(first.Conflicts(timber),"Leg must reject another tower's frame reservation");
        Console.WriteLine($"Crossing footprints: {comparisons} exact final-leg clash comparisons plus contact, ownership and indexed first-conflict checks passed.");
    }
}
