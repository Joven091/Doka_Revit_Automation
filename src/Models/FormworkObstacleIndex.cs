using System;
using System.Collections;
using System.Collections.Generic;

namespace EngineeringDashboard
{
    // A read-only broad phase for one junction's fixed obstacle snapshot. Every
    // surviving pair still uses FormworkSolid.Conflicts, including tower ownership.
    internal sealed class FormworkObstacleIndex : IEnumerable<FormworkSolid>
    {
        private struct Entry { internal FormworkSolid Solid; internal int Original; }
        private sealed class Node
        {
            internal double X0,X1,Y0,Y1,Z0,Z1;
            internal int First,Count,MinimumOriginal;
            internal Node Left,Right;
            internal bool Overlaps(FormworkSolid s)=>s.X0<X1-1e-7 && s.X1>X0+1e-7
                && s.Y0<Y1-1e-7 && s.Y1>Y0+1e-7 && s.Z0<Z1-1e-7 && s.Z1>Z0+1e-7;
        }
        private readonly FormworkSolid[] original;
        private readonly Entry[] entries;
        private readonly Node root;
        internal FormworkObstacleIndex(FormworkSolid[] obstacles)
        {
            original=obstacles;entries=new Entry[obstacles.Length];
            for(int i=0;i<entries.Length;i++)
            {
                var s=obstacles[i];entries[i]=new Entry {Solid=s,Original=i};
                if(!TowerBeamAnalysis.Finite(s.X0) || !TowerBeamAnalysis.Finite(s.X1) || s.X0>s.X1
                    || !TowerBeamAnalysis.Finite(s.Y0) || !TowerBeamAnalysis.Finite(s.Y1) || s.Y0>s.Y1
                    || !TowerBeamAnalysis.Finite(s.Z0) || !TowerBeamAnalysis.Finite(s.Z1) || s.Z0>s.Z1)return;
            }
            if(entries.Length>0)root=Build(0,entries.Length);
        }
        private static double Centre(FormworkSolid s,int axis)=>axis==0 ? (s.X0+s.X1)/2 : axis==1 ? (s.Y0+s.Y1)/2 : (s.Z0+s.Z1)/2;
        private Node Build(int first,int count)
        {
            var node=new Node {First=first,Count=count,MinimumOriginal=int.MaxValue,
                X0=double.PositiveInfinity,Y0=double.PositiveInfinity,Z0=double.PositiveInfinity,
                X1=double.NegativeInfinity,Y1=double.NegativeInfinity,Z1=double.NegativeInfinity};
            var lo=new[] {double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity};
            var hi=new[] {double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};
            for(int i=first;i<first+count;i++)
            {
                var e=entries[i];var s=e.Solid;node.MinimumOriginal=Math.Min(node.MinimumOriginal,e.Original);
                node.X0=Math.Min(node.X0,s.X0);node.X1=Math.Max(node.X1,s.X1);
                node.Y0=Math.Min(node.Y0,s.Y0);node.Y1=Math.Max(node.Y1,s.Y1);
                node.Z0=Math.Min(node.Z0,s.Z0);node.Z1=Math.Max(node.Z1,s.Z1);
                for(int axis=0;axis<3;axis++) {double c=Centre(s,axis);lo[axis]=Math.Min(lo[axis],c);hi[axis]=Math.Max(hi[axis],c);}
            }
            if(count<=8)return node;
            int split=0;for(int axis=1;axis<3;axis++)if(hi[axis]-lo[axis]>hi[split]-lo[split])split=axis;
            Array.Sort(entries,first,count,Comparer<Entry>.Create((a,b)=> {
                int order=Centre(a.Solid,split).CompareTo(Centre(b.Solid,split));
                return order!=0 ? order : a.Original.CompareTo(b.Original);
            }));
            int half=count/2;node.Left=Build(first,half);node.Right=Build(first+half,count-half);return node;
        }
        internal FormworkSolid FirstConflict(FormworkSolid added)
        {
            if(root==null) {foreach(var obstacle in original)if(added.Conflicts(obstacle))return obstacle;return null;}
            int first=int.MaxValue;Find(root,added,ref first);return first==int.MaxValue ? null : original[first];
        }
        private void Find(Node node,FormworkSolid added,ref int first)
        {
            if(node==null || node.MinimumOriginal>=first || !node.Overlaps(added))return;
            if(node.Left==null)
            {
                for(int i=node.First;i<node.First+node.Count;i++)
                    if(entries[i].Original<first && added.Conflicts(entries[i].Solid))first=entries[i].Original;
                return;
            }
            // Branch-and-bound returns the same earliest obstacle as the original
            // linear scan, preserving failure labels and deterministic traversal.
            if(node.Left.MinimumOriginal<node.Right.MinimumOriginal) {Find(node.Left,added,ref first);Find(node.Right,added,ref first);}
            else {Find(node.Right,added,ref first);Find(node.Left,added,ref first);}
        }
        public IEnumerator<FormworkSolid> GetEnumerator()=>((IEnumerable<FormworkSolid>)original).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
