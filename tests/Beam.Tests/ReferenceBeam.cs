using System;
using EngineeringDashboard;
internal static class ReferenceBeam
{
 // Independent three-moment solution, with exact integration between supports.
 public static void Verify(BeamCalculatorViewModel vm)
 {
  int first=vm.NumberOfProps=="2"?2:vm.NumberOfProps=="3"?3:4;
  for(int n=first;n<=(first==4?12:first);n++) {
   double L=vm.MaximumPropSpacing/1000,a=vm.PrimaryBeamCantilever/1000,w=Math.Max(vm.LeftLineLoad,vm.RightLineLoad),EI=449.8756;
   var M=new double[n];M[0]=M[n-1]=-w*a*a/2;
   int k=n-2;var diag=new double[k];var rhs=new double[k];
   for(int i=0;i<k;i++){diag[i]=4;rhs[i]=-w*L*L/2;if(i==0)rhs[i]-=M[0];if(i==k-1)rhs[i]-=M[n-1];if(i>0){double f=1/diag[i-1];diag[i]-=f;rhs[i]-=f*rhs[i-1];}}
   for(int i=k-1;i>=0;i--)M[i+1]=(rhs[i]-(i+1<k?M[i+2]:0))/diag[i];
   var reactions=new double[n];double maxM=0,maxV=0,maxD=0,leftSlope=0,rightSlope=0;
   for(int j=0;j<n-1;j++){
    double V=w*L/2+(M[j+1]-M[j])/L,C=-(M[j]*L*L/2+V*L*L*L/6-w*Math.Pow(L,4)/24)/L;
    reactions[j]+=V;reactions[j+1]+=w*L-V;
    if(j==0)leftSlope=C/EI;if(j==n-2)rightSlope=(M[j]*L+V*L*L/2-w*L*L*L/6+C)/EI;
    for(int q=0;q<=200;q++){double x=L*q/200;maxM=Math.Max(maxM,Math.Abs(M[j]+V*x-w*x*x/2));maxV=Math.Max(maxV,Math.Abs(V-w*x));maxD=Math.Max(maxD,Math.Abs((M[j]*x*x/2+V*x*x*x/6-w*Math.Pow(x,4)/24+C*x)/EI));}
   }
   reactions[0]+=w*a;reactions[n-1]+=w*a;
   for(int q=0;q<=200;q++){double x=a*q/200,curve=-w/(2*EI)*(a*a*x*x/2-a*x*x*x/3+Math.Pow(x,4)/12);maxM=Math.Max(maxM,w*(a-x)*(a-x)/2);maxV=Math.Max(maxV,w*(a-x));maxD=Math.Max(maxD,Math.Max(Math.Abs(-leftSlope*x+curve),Math.Abs(rightSlope*x+curve)));}
   double sum=0;foreach(double R in reactions){sum+=R;if(R>vm.MaximumPropLoad+1e-7)throw new Exception("Reference reaction exceeds envelope");}
   if(Math.Abs(sum-w*((n-1)*L+2*a))>1e-7||maxM>5.000001||maxV>11.000001||maxD*1000>vm.DeflectionLimit.Millimetres+1e-5)throw new Exception($"Reference failed n={n} M={maxM} V={maxV} d={maxD*1000}, geometry={vm.Depth}x{vm.Width}");
  }
 }
}
