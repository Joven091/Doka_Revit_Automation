using System;
using EngineeringDashboard;
internal static class Program
{
 static int count;
 static void Check(bool value,string name) {count++;if(!value)throw new Exception(name);}
 static void Equal(double a,double b,string name) => Check(!double.IsNaN(b)&&Math.Abs(a-b)<1e-8,name+": "+b);
 static void Invalid(BeamCalculatorViewModel v,string name){Check(!v.IsCalculationValid,name);Equal(0,v.MaximumPropSpacing,name+" spacing");Equal(0,v.MaximumPropLoad,name+" load");Check(!string.IsNullOrEmpty(v.WarningMessage),name+" warning");}
 static void Main()
 {
  var v=new BeamCalculatorViewModel();Equal(2000,v.MaximumPropSpacing,"default");Check(v.LimitingFactor=="Max Spacing Limit","two-prop cap");
  Equal(2.533,v.MaximumPropLoad,"symmetric reaction");Equal(v.TotalLineLoad,v.LeftLineLoad+v.RightLineLoad,"equilibrium");
  v.HasLeftSlab=true;Equal(3.171875,v.LeftLineLoad,"left eccentric reaction");Equal(1.878125,v.RightLineLoad,"right reaction");Equal(5.3921875,v.MaximumPropLoad,"heavier prop");
  v.HasLeftSlab=false;v.HasRightSlab=true;Equal(3.171875,v.RightLineLoad,"mirrored load");Equal(5.3921875,v.MaximumPropLoad,"mirrored prop");
  v.HasLeftSlab=true;Equal(v.LeftLineLoad,v.RightLineLoad,"symmetric slabs");
  v.LeftSlabThickness=100;Check(v.LeftLineLoad<v.RightLineLoad,"unequal thicknesses");Equal(v.TotalLineLoad,v.LeftLineLoad+v.RightLineLoad,"unequal equilibrium");
  Equal(.75,BeamCalculatorViewModel.HeapingLoad(5),"heaping min");Equal(1.6,BeamCalculatorViewModel.HeapingLoad(16),"heaping intermediate");Equal(1.75,BeamCalculatorViewModel.HeapingLoad(20),"heaping max");
  v=new BeamCalculatorViewModel{Depth=800,Width=600,NumberOfProps="3"};Equal(2400,v.MaximumPropSpacing,"corrected deep beam");
  v=new BeamCalculatorViewModel{Width=700,NumberOfProps="4+"};Equal(2800,v.MaximumPropSpacing,"5mm");v.DeflectionLimit=v.DeflectionOptions[2];Equal(3000,v.MaximumPropSpacing,"10mm");v.DeflectionLimit=v.DeflectionOptions[0];Check(v.MaximumPropSpacing<2800,"3mm");
  foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,0,-1})
  {v=new BeamCalculatorViewModel{Depth=bad};Invalid(v,"bad depth");v=new BeamCalculatorViewModel{Width=bad};Invalid(v,"bad width");v=new BeamCalculatorViewModel{HasLeftSlab=true,LeftSlabThickness=bad};Invalid(v,"bad slab");}
  foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1})
  {v=new BeamCalculatorViewModel{UseCustomLoads=true,CustomLiveLoad=bad};Invalid(v,"bad live");v=new BeamCalculatorViewModel{UseCustomLoads=true,CustomFormworkLoad=bad};Invalid(v,"bad formwork");}
  v=new BeamCalculatorViewModel{HasRightSlab=true,RightSlabThickness=501};Invalid(v,"slab deeper than beam");v.HasRightSlab=false;Check(v.IsCalculationValid,"disabled slab ignored");
  v=new BeamCalculatorViewModel{UseCustomLoads=true,CustomLiveLoad=0,CustomFormworkLoad=0};Check(v.IsCalculationValid,"zero custom allowances");v.CustomLiveLoad=-1;Invalid(v,"negative live");v.UseCustomLoads=false;Check(v.IsCalculationValid,"inactive custom ignored");
  v.SetTextInputErrors(true);Invalid(v,"invalid text");v.SetTextInputErrors(false);Check(v.IsCalculationValid,"recover text");
  v.NumberOfProps="unknown";Invalid(v,"invalid count");v.NumberOfProps="3";v.DeflectionLimit=null;Invalid(v,"missing limit");
  v=new BeamCalculatorViewModel{Depth=double.MaxValue,Width=double.MaxValue};Invalid(v,"overflow");
  int scenarios=0;
  foreach(int depth in new[]{200,400,600,800,1000,1200,1500,1800,2000}) foreach(int width in new[]{200,400,600,800,1000,1200}) foreach(string n in new[]{"2","3","4+"}) foreach(int side in new[]{0,1,2,3})
  {
   v=new BeamCalculatorViewModel{Depth=depth,Width=width,NumberOfProps=n,HasLeftSlab=(side&1)!=0,HasRightSlab=(side&2)!=0};scenarios++;
   Check(v.IsCalculationValid,"sweep valid");Check(v.MaximumPropSpacing>0&&v.MaximumPropSpacing<=(n=="2"?2000:3000)&&v.MaximumPropSpacing%25==0,"sweep spacing");Equal(v.TotalLineLoad,v.LeftLineLoad+v.RightLineLoad,"sweep equilibrium");
   double lw=v.LeftLineLoad,rw=v.RightLineLoad;bool left=v.HasLeftSlab;v.HasLeftSlab=v.HasRightSlab;v.HasRightSlab=left;Equal(lw,v.RightLineLoad,"sweep mirror right");Equal(rw,v.LeftLineLoad,"sweep mirror left");
  }
  Console.WriteLine($"Passed {count} beam checks across {scenarios} sweep scenarios.");
 }
}
