using System;
using System.Linq;
using EngineeringDashboard;
internal static class FivePlusTests
{
 internal static void Run(Action<bool,string> check,Action<double,double,string> equal)
 {
  equal(1.1,ContinuousBeamResponse.Analyze(4,1,0,1,1).Reaction,"four reaction coefficient");
  equal(8.0/7,ContinuousBeamResponse.Analyze(5,1,0,1,1).Reaction,"five reaction coefficient");
  equal(43.0/38,ContinuousBeamResponse.Analyze(6,1,0,1,1).Reaction,"six reaction coefficient");
  double root=-2+Math.Sqrt(3);
  // Independent closed-form solution for the normalized support moment perturbation.
  // Check every adjacent moment pair lies in the 5/6 convex hull through 256 supports.
  for(int n=5;n<=256;n++){
   var f=Enumerable.Range(0,n).Select(i=>(Math.Pow(root,i)+Math.Pow(root,n-1-i))/(1+Math.Pow(root,n-1))).ToArray();
   for(int i=0;i<n-1;i++){
    double x=f[i],y=f[i+1];check(x>=-2.0/7-1e-12&&y>=-2.0/7-1e-12&&x<=1+1e-12&&y<=1+1e-12&&x+y>=-4.0/19-1e-12&&x+y<=14.0/19+1e-12&&4*x+y>=-1-1e-12&&x+4*y>=-1-1e-12,"all-count moment-pair hull");
   }
  }
  foreach(double a in new[]{0,.1,.25,.408248290463863,.5,.75,1,2,5}){
   var envelope=SlabSupportEnvelope.Calculate(1,a,1,1);
   for(int n=5;n<=100;n++){
    var actual=ContinuousBeamResponse.Analyze(n,1,a,1,1);
    check(actual.Reaction<=envelope.Reaction+1e-9&&actual.Shear<=envelope.Shear+1e-9&&actual.Moment<=envelope.Moment+1e-9&&actual.SpanDeflection<=envelope.SpanDeflection+1e-9,"higher-count response enveloped");
    equal(n-1+2*a,actual.Reactions.Sum(),"force equilibrium");
    double[] m=Enumerable.Range(0,n).Select(i=>-1.0/12+(1.0/12-a*a/2)*(Math.Pow(root,i)+Math.Pow(root,n-1-i))/(1+Math.Pow(root,n-1))).ToArray();
    // Direct polynomial integration from the closed-form moments, independent of slope-root search.
    for(int i=0;i<n-1;i++){
     double v=.5+m[i+1]-m[i],c=-(m[i]/2+v/6-1.0/24);
     for(int k=0;k<=20;k++){double x=k/20.0,M=m[i]+v*x-x*x/2,V=v-x,d=c*x+m[i]*x*x/2+v*x*x*x/6-Math.Pow(x,4)/24;
      check(Math.Abs(M)<=envelope.Moment+1e-9&&Math.Abs(V)<=envelope.Shear+1e-9&&Math.Abs(d)<=envelope.SpanDeflection+1e-9,"independent sampled response inside envelope");}
    }
   }
  }
  var lower=SlabSupportEnvelope.Calculate(2,.5,3,449.8756);var scaled=SlabSupportEnvelope.Calculate(2,.5,6,449.8756);equal(2*lower.Reaction,scaled.Reaction,"envelope load scaling");equal(2*lower.SpanDeflection,scaled.SpanDeflection,"envelope displacement scaling");
 }
}
