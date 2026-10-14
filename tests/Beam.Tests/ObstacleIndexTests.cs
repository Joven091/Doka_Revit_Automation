using System;
using System.Linq;
using EngineeringDashboard;

internal static class ObstacleIndexTests
{
    internal static void Run()
    {
        int checks=0;var random=new Random(1481);
        Func<FormworkSolid> solid=()=> {
            double x=random.NextDouble()*8000-500,y=random.NextDouble()*8000-500,z=random.NextDouble()*4000-4000;
            return new FormworkSolid {X0=x,X1=x+random.NextDouble()*3000,Y0=y,Y1=y+random.NextDouble()*3000,
                Z0=z,Z1=z+random.NextDouble()*2000,IsLeg=random.Next(3)==0,IsTowerEnvelope=random.Next(3)==0,TowerOwner="B"+random.Next(4)};
        };
        foreach(int count in new[] {0,1,8,9,32,128,512})
        {
            var obstacles=Enumerable.Range(0,count).Select(_=>solid()).ToArray();var index=new FormworkObstacleIndex(obstacles);
            for(int query=0;query<500;query++)
            {
                var added=solid();var expected=obstacles.FirstOrDefault(added.Conflicts);
                if(!ReferenceEquals(expected,index.FirstConflict(added)))throw new Exception("Obstacle index changed the first physical clash");
                checks++;
            }
        }
        foreach(double gap in new[] {-1.0,-1e-6,-1e-7,0,1e-7,1e-6,1.0})
        foreach(bool frame in new[] {false,true})foreach(bool leg in new[] {false,true})foreach(bool sameOwner in new[] {false,true})
        {
            var added=new FormworkSolid {X0=0,X1=80,Y0=0,Y1=80,Z0=0,Z1=200,IsTowerEnvelope=frame,TowerOwner="A"};
            var other=new FormworkSolid {X0=80+gap,X1=160+gap,Y0=0,Y1=80,Z0=0,Z1=200,IsLeg=leg,TowerOwner=sameOwner ? "A" : "B"};
            var expected=added.Conflicts(other) ? other : null;
            if(!ReferenceEquals(expected,new FormworkObstacleIndex(new[] {other}).FirstConflict(added)))throw new Exception("Obstacle index changed contact tolerance or tower ownership");
            checks++;
        }
        var unusual=new[] {new FormworkSolid {X0=double.NegativeInfinity,X1=10,Y0=0,Y1=10,Z0=0,Z1=10},
            new FormworkSolid {X0=double.NaN,X1=10,Y0=0,Y1=10,Z0=0,Z1=10}};
        var probe=new FormworkSolid {X0=1,X1=2,Y0=1,Y1=2,Z0=1,Z1=2};
        if(!ReferenceEquals(unusual.FirstOrDefault(probe.Conflicts),new FormworkObstacleIndex(unusual).FirstConflict(probe)))throw new Exception("Malformed geometry must preserve linear-scan behavior");
        Console.WriteLine($"Passed {checks+1} obstacle-index clash parity checks.");
    }
}
