using System;
using System.Linq;
using EngineeringDashboard;
internal static class Program
{
 static int count;
 static void Check(bool value,string name) {count++;if(!value)throw new Exception(name);}
 static void Equal(double a,double b,string name) => Check(!double.IsNaN(b)&&Math.Abs(a-b)<1e-8,name+": "+b);
 static void Invalid(BeamCalculatorViewModel v,string name){Check(!v.IsCalculationValid,name);Equal(0,v.MaximumPropSpacing,name+" spacing");Equal(0,v.MaximumPropLoad,name+" load");Check(!string.IsNullOrEmpty(v.WarningMessage),name+" warning");}
 static int Main(){try{Run();TowerTests.Run();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static void Run()
 {
  var v=new BeamCalculatorViewModel();Check(v.DeflectionLimit.Label=="L/350","slab-aligned default");Check(v.DeflectionOptions.Select(x=>x.Label).SequenceEqual(new[]{"L/500","L/350","L/270"}),"slab-aligned criteria");Equal(2000,v.MaximumPropSpacing,"default");Check(v.LimitingFactor=="Max Spacing Limit","two-prop cap");
  Equal(2.533,v.MaximumPropLoad,"symmetric reaction");Equal(v.TotalLineLoad,v.LeftLineLoad+v.RightLineLoad,"equilibrium");
  v.HasLeftSlab=true;Equal(3.171875,v.LeftLineLoad,"left eccentric reaction");Equal(1.878125,v.RightLineLoad,"right reaction");Equal(5.3921875,v.MaximumPropLoad,"heavier prop");
  v.HasLeftSlab=false;v.HasRightSlab=true;Equal(3.171875,v.RightLineLoad,"mirrored load");Equal(5.3921875,v.MaximumPropLoad,"mirrored prop");
  v.HasLeftSlab=true;Equal(v.LeftLineLoad,v.RightLineLoad,"symmetric slabs");
  v.LeftSlabThickness=100;Check(v.LeftLineLoad<v.RightLineLoad,"unequal thicknesses");Equal(v.TotalLineLoad,v.LeftLineLoad+v.RightLineLoad,"unequal equilibrium");
  Equal(.75,BeamCalculatorViewModel.HeapingLoad(5),"heaping min");Equal(1.6,BeamCalculatorViewModel.HeapingLoad(16),"heaping intermediate");Equal(1.75,BeamCalculatorViewModel.HeapingLoad(20),"heaping max");
  v=new BeamCalculatorViewModel{Depth=800,FloorHeight=3218,Width=600,NumberOfProps="3",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(null,"5")};Equal(2400,v.MaximumPropSpacing,"corrected deep beam");
  v=new BeamCalculatorViewModel{Width=700,NumberOfProps="4+",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(null,"5")};Equal(2800,v.MaximumPropSpacing,"5mm");v.DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(null,"10");Equal(3000,v.MaximumPropSpacing,"10mm");v.DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(null,"3");Check(v.MaximumPropSpacing<2800,"3mm");
  foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,0,-1})
  {v=new BeamCalculatorViewModel{Depth=bad};Invalid(v,"bad depth");v=new BeamCalculatorViewModel{Width=bad};Invalid(v,"bad width");v=new BeamCalculatorViewModel{HasLeftSlab=true,LeftSlabThickness=bad};Invalid(v,"bad slab");}
  foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1})
  {v=new BeamCalculatorViewModel{UseCustomLoads=true,CustomLiveLoad=bad};Invalid(v,"bad live");v=new BeamCalculatorViewModel{UseCustomLoads=true,CustomFormworkLoad=bad};Invalid(v,"bad formwork");}
  v=new BeamCalculatorViewModel{HasRightSlab=true,RightSlabThickness=501};Invalid(v,"slab deeper than beam");v.HasRightSlab=false;Check(v.IsCalculationValid,"disabled slab ignored");
  v=new BeamCalculatorViewModel{UseCustomLoads=true,CustomLiveLoad=0,CustomFormworkLoad=0};Check(v.IsCalculationValid,"zero custom allowances");v.CustomLiveLoad=-1;Invalid(v,"negative live");v.UseCustomLoads=false;Check(v.IsCalculationValid,"inactive custom ignored");
  v.SetTextInputErrors(true);Invalid(v,"invalid text");v.SetTextInputErrors(false);Check(v.IsCalculationValid,"recover text");
  v.NumberOfProps="unknown";Invalid(v,"invalid count");v.NumberOfProps="3";v.DeflectionLimit=null;Invalid(v,"missing limit");
  v=new BeamCalculatorViewModel{Depth=double.MaxValue,Width=double.MaxValue};Invalid(v,"overflow");
  foreach(var option in BeamPropOption.All) {
   foreach(var table in new[]{PropDataHelper.PropCapacities,PropDataHelper.DokaFitPropCapacities}) foreach(var row in table) {
    if(row.Value.TryGetValue(option.CapacityKey,out var cap)&&cap.HasValue&&cap.Value>0) Equal(cap.Value,option.Capacity(row.Key*1000,out var lookup),"shared capacity "+option.Label);
   }
   foreach(double bad in new[]{option.MinimumMm-1,option.MaximumMm+1,double.NaN,double.PositiveInfinity}) { bool rejected=false;try{option.Capacity(bad,out var ignored);}catch(ArgumentException){rejected=true;}Check(rejected,"extension range"); }
  }
  v=new BeamCalculatorViewModel();Equal(2082,v.PropExtensionMm,"automatic extension");Equal(2100,v.PropLookupExtensionMm,"round up table row");
  v.SelectedProp=BeamPropOption.All.First(p=>p.CapacityKey=="DokaFit Prop 400 at bottom");v.FloorHeight=4918;v.Width=1000;v.NumberOfProps="3";v.DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(null,"5");
  Check(v.IsCalculationValid,"capacity example valid");Equal(16,v.PropCapacityKn,"DokaFit capacity");Equal(1700,v.MaximumPropSpacing,"prop governs spacing");Check(v.LimitingFactor=="Prop Capacity","prop governing label");Equal(15.83125,v.MaximumPropLoad,"capacity example reaction");Check(v.PropUtilizationPercent<=100,"utilization");Check(1.25*Math.Max(v.LeftLineLoad,v.RightLineLoad)*(v.MaximumPropSpacing+25)/1000>v.PropCapacityKn,"next increment exceeds capacity");
  v.FloorHeight++;Invalid(v,"beyond extension");v.SelectedProp=null;Invalid(v,"missing prop");
  foreach(double strip in new[]{150.0,300.0,450.0,600.0}) {
   v=new BeamCalculatorViewModel{Width=500,Depth=700,HasLeftSlab=true,SlabStripWidthMm=strip};
   double side=(5+.75+.75+.4)*strip/1000, beam=(17.5+1.75+.75+.4)*.5;
   Equal(beam+side,v.TotalLineLoad,"editable strip total");
   Equal((beam+side)/2+side*((500+strip)/2000)/.95,v.LeftLineLoad,"editable strip eccentric reaction");
   Check(v.IsCalculationValid,"editable strip layout");ReferenceBeam.Verify(v);
  }
  v=new BeamCalculatorViewModel{HasLeftSlab=true,SlabStripWidthMm=0};Invalid(v,"zero active strip");
  v.HasLeftSlab=false;Check(v.IsCalculationValid,"inactive strip ignored");
  v.SlabStripWidthMm=double.NaN;Check(v.IsCalculationValid,"inactive nonfinite strip ignored");v.HasRightSlab=true;Invalid(v,"nonfinite active strip");
  int scenarios=0;
  foreach(int depth in new[]{200,400,600,800,1000,1200,1500,1800,2000}) foreach(int width in new[]{200,400,600,800,1000,1200}) foreach(string n in new[]{"2","3","4+"}) foreach(int side in new[]{0,1,2,3})
  {
   v=new BeamCalculatorViewModel{Depth=depth,FloorHeight=depth+2418,Width=width,NumberOfProps=n,HasLeftSlab=(side&1)!=0,HasRightSlab=(side&2)!=0};scenarios++;
   Check(v.IsCalculationValid,"sweep valid");Check(v.MaximumPropLoad<=v.PropCapacityKn&&v.PropUtilizationPercent<=100,"sweep prop capacity");Check(v.MaximumPropSpacing>0&&v.MaximumPropSpacing<=(n=="2"?2000:3000)&&v.MaximumPropSpacing%25==0,"sweep spacing");Equal(v.TotalLineLoad,v.LeftLineLoad+v.RightLineLoad,"sweep equilibrium");
   foreach(var criterion in BeamCalculatorViewModel.AvailableDeflectionOptions) { v.DeflectionLimit=criterion;Check(v.IsCalculationValid,"ratio sweep valid");ReferenceBeam.Verify(v); }
   double lw=v.LeftLineLoad,rw=v.RightLineLoad;bool left=v.HasLeftSlab;v.HasLeftSlab=v.HasRightSlab;v.HasRightSlab=left;Equal(lw,v.RightLineLoad,"sweep mirror right");Equal(rw,v.LeftLineLoad,"sweep mirror left");
  }
  Console.WriteLine($"Passed {count} beam checks across {scenarios} sweep scenarios.");
 }
}
