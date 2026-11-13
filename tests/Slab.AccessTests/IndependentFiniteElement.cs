using System;
using System.Linq;
using EngineeringDashboard;

/// <summary>Independent displacement-stiffness oracle; no production compatibility or integration code is reused.</summary>
internal static class IndependentFiniteElement
{
    internal static void Compare(SlabBeamAnalysis beam, Action<double,double,string,double> near)
    {
        var nodes=beam.Supports.Concat(beam.Loads.SelectMany(q=>new[]{q.Start,q.End})).Concat(new[]{0.0,beam.Length}).Distinct().OrderBy(x=>x).ToArray();
        int n=nodes.Length*2;
        var stiffness=new double[n,n];var force=new double[n];
        for(int i=0;i<nodes.Length-1;i++)
        {
            double length=nodes[i+1]-nodes[i], factor=beam.Rigidity/Math.Pow(length,3), mid=(nodes[i]+nodes[i+1])/2;
            double q=beam.Loads.Where(p=>p.Start<=mid&&p.End>=mid).Sum(p=>p.Load);
            double[,] k={{12,6*length,-12,6*length},{6*length,4*length*length,-6*length,2*length*length},
                {-12,-6*length,12,-6*length},{6*length,2*length*length,-6*length,4*length*length}};
            double[] f={q*length/2,q*length*length/12,q*length/2,-q*length*length/12};
            for(int a=0;a<4;a++)
            {
                force[2*i+a]+=f[a];
                for(int b=0;b<4;b++)stiffness[2*i+a,2*i+b]+=factor*k[a,b];
            }
        }
        var fixedNodes=beam.Supports.Select(s=>Array.IndexOf(nodes,s)*2).ToArray();
        var free=Enumerable.Range(0,n).Where(i=>!fixedNodes.Contains(i)).ToArray();
        var matrix=new double[free.Length,free.Length+1];
        for(int a=0;a<free.Length;a++)
        {
            for(int b=0;b<free.Length;b++)matrix[a,b]=stiffness[free[a],free[b]];
            matrix[a,free.Length]=force[free[a]];
        }
        for(int col=0;col<free.Length;col++)
        {
            int pivot=Enumerable.Range(col,free.Length-col).OrderByDescending(r=>Math.Abs(matrix[r,col])).First();
            for(int b=col;b<=free.Length;b++){double t=matrix[pivot,b];matrix[pivot,b]=matrix[col,b];matrix[col,b]=t;}
            for(int a=col+1;a<free.Length;a++)
            {
                double multiplier=matrix[a,col]/matrix[col,col];
                for(int b=col;b<=free.Length;b++)matrix[a,b]-=multiplier*matrix[col,b];
            }
        }
        var displacement=new double[n];
        for(int a=free.Length-1;a>=0;a--)
        {
            double value=matrix[a,free.Length];
            for(int b=a+1;b<free.Length;b++)value-=matrix[a,b]*displacement[free[b]];
            displacement[free[a]]=value/matrix[a,a];
        }
        for(int i=0;i<nodes.Length;i++)
        {
            var actual=beam.Response(nodes[i]);
            near(displacement[2*i],actual[2],"Independent FEM displacement",1e-8);
            near(displacement[2*i+1],actual[3],"Independent FEM rotation",1e-8);
        }
        for(int i=0;i<fixedNodes.Length;i++)
        {
            double reaction=force[fixedNodes[i]]-Enumerable.Range(0,n).Sum(j=>stiffness[fixedNodes[i],j]*displacement[j]);
            near(reaction,beam.Reactions[i],"Independent FEM support reaction",1e-8);
        }
    }
}
