using System;
using System.Linq;
using EngineeringDashboard;
internal static class Program
{
 static int count;
 static void Check(bool value,string name) {count++;if(!value)throw new Exception(name);}
 static void Equal(double a,double b,string name) => Check(!double.IsNaN(b)&&Math.Abs(a-b)<1e-8,name+": "+b);
 static void Invalid(BeamCalculatorViewModel v,string name){Check(!v.IsCalculationValid,name);Equal(0,v.MaximumPropSpacing,name+" spacing");Equal(0,v.MaximumPropLoad,name+" load");Check(!string.IsNullOrEmpty(v.WarningMessage),name+" warning");}
 static int Main(string[] args){try{if(args.Contains("--individual-spans")){IndividualSpanTests.Run();return 0;}if(args.Contains("--revit-selection")){RevitStructureImportTests.Run();RevitImportDiagnosticsTests.Run();RevitColumnImportTests.Run();return 0;}if(args.Contains("--network-search-reuse")){NetworkSearchReuseTests.Run();return 0;}if(args.Contains("--primary-bearings")){PrimaryBearingTests.Run();return 0;}if(args.Contains("--primary-bearing-benchmark")){PrimaryBearingTests.Benchmark(args);return 0;}if(args.Contains("--primary-joint-rows")){PrimaryJointRowTests.Run();return 0;}if(args.Contains("--crossing-search")){CrossingSearchTests.Run();return 0;}if(args.Contains("--local-receiving-rows")){LocalReceivingRowTests.Run();return 0;}if(args.Contains("--local-receiving-report")){LocalReceivingRowTests.Reported();return 0;}if(args.Contains("--required-rows")){RequiredRowTests.Run();return 0;}if(args.Contains("--prepared-receivers")){PreparedReceiverTests.Run();return 0;}if(args.Contains("--crossing-local-reuse")){CrossingLocalReuseTests.Run();return 0;}if(args.Contains("--controlled-performance")){ControlledPerformanceTests.Run(args);return 0;}if(args.Contains("--next-search-speed")){NextSearchSpeedTests.Run();return 0;}if(args.Contains("--search-worker-benchmark")){NextSearchSpeedTests.Workers();return 0;}if(args.Contains("--centred-search-speed")){CentredSearchSpeedTests.Run();return 0;}if(args.Contains("--centred-speed-report")){CentredSearchSpeedTests.Reported();return 0;}if(args.Contains("--slab-plywood-edges")){SlabPlywoodEdgeTests.Run();return 0;}if(args.Contains("--slab-plywood-report")){SlabPlywoodEdgeTests.Reported();return 0;}if(args.Contains("--centred-crossings")){CentredCrossingTests.Run();return 0;}if(args.Contains("--positioned-fallback")){NetworkFallbackTests.ReportedEngineerFallback();return 0;}if(args.Contains("--positioned-transfer")){PositionedTransferTests.Run();return 0;}if(args.Contains("--unified-transfer")){UnifiedTransferTests.Run();return 0;}if(args.Contains("--unified-mixed")){UnifiedTransferTests.Run(true);return 0;}if(args.Contains("--engineer-workflow")){EngineerWorkflowTests.Run();return 0;}if(args.Contains("--cross-shutter-strips")){CrossEndStripTests.ShutterSupported();return 0;}if(args.Contains("--engineer-report-junction")){EngineerSearchTests.Junction(true);return 0;}if(args.Contains("--engineer-junction")){EngineerSearchTests.Junction();return 0;}if(args.Contains("--engineer-search")){EngineerSearchTests.Run();return 0;}if(args.Contains("--slab-edge-ledgers")){CrossingSlabSupportTests.Run();CrossingSlabSupportTests.Reported();return 0;}if(args.Contains("--equivalent-loading")){EquivalentLoadingTests.Run();return 0;}if(args.Contains("--column-slab-ends")){ColumnSlabEndSupportTests.Run();return 0;}if(args.Contains("--independent-spacing-controls")){IndependentSpacingSearchTests.Controls();return 0;}if(args.Contains("--independent-spacing-search")){IndependentSpacingSearchTests.Run(args.Contains("--baseline"));return 0;}if(args.Contains("--spacing-coverage")){SpacingCoverageRegressionTests.Run();return 0;}if(args.Contains("--crossing-footprints")){CrossingFootprintTests.Run();return 0;}if(args.Contains("--crossing-optimization-benchmark")){CrossingOptimizationTests.Benchmark();return 0;}if(args.Contains("--obstacle-index")){ObstacleIndexTests.Run();return 0;}if(args.Contains("--secondary-setting-out")){SecondarySettingOutTests.Run();return 0;}if(args.Contains("--secondary-setting-out-reported")){SecondarySettingOutTests.Reported();return 0;}if(args.Contains("--diagnostic-full-benchmark")){DiagnosticFullBenchmark.Run();return 0;}if(args.Contains("--diagnostic-budget-benchmark")){DiagnosticBudgetBenchmark.Run();return 0;}if(args.Contains("--search-budget")){SearchBudgetTests.Run();return 0;}if(args.Contains("--independent-beams")){IndependentBeamTests.Run();return 0;}if(args.Contains("--individual-prefix")){IndividualSpacingTests.PrefixCacheParity();return 0;}if(args.Contains("--individual-shutter")){IndividualSpacingTests.ShutterException();return 0;}if(args.Contains("--individual-spacing")){IndividualSpacingTests.Run();return 0;}if(args.Contains("--network-editing")){NetworkEditTests.Run();return 0;}if(args.Contains("--slab-end-loads")){SlabEndLoadTests.Run();return 0;}if(args.Contains("--slab-end-loads-reported")){SlabEndLoadTests.Reported();return 0;}if(args.Contains("--plywood")){PlywoodTests.Run();return 0;}if(args.Contains("--beam-options")){CoordinatedDesignerTests.Options();return 0;}if(args.Contains("--coordinated-designer")){CoordinatedDesignerTests.Run();return 0;}if(args.Contains("--designer-preferences")){DesignerPreferenceTests.Run();return 0;}if(args.Contains("--ws10")){WS10Tests.Run();return 0;}if(args.Contains("--local-shutters")){LocalShutterTests.Run();return 0;}if(args.Contains("--side-shutters")){SideShutterClearanceTests.Run();return 0;}if(args.Contains("--custom-h20-scope")){CustomH20Tests.Scope();return 0;}if(args.Contains("--cross-junction")){CrossJunctionTests.Run();return 0;}if(args.Contains("--cross-junction-reported")){CrossJunctionTests.Reported();return 0;}if(args.Contains("--network-fallback")){NetworkFallbackTests.Run();return 0;}if(args.Contains("--cross-end-strips")){CrossEndStripTests.Run();return 0;}if(args.Contains("--cross-reported")){CrossEndStripTests.Reported();return 0;}if(args.Contains("--uplift-reported")){UpliftAssumptionTests.ReportedCase(args.SkipWhile(a=>a!="--uplift-reported").Skip(1).FirstOrDefault());return 0;}if(args.Contains("--uplift")){UpliftAssumptionTests.Run();return 0;}if(args.Contains("--symmetric-slab-supports")){SymmetricSlabSupportTests.Run();return 0;}if(args.Contains("--cross-supports")){CrossSupportTests.Run();return 0;}if(args.Contains("--primary-udl")){PrimaryUdlTests.Run();return 0;}if(args.Contains("--primary-udl-reported")){PrimaryUdlTests.ReportedCase();return 0;}if(args.Contains("--network-speed-benchmark")){NetworkSpeedBenchmark.Run();return 0;}if(args.Contains("--network-economy")){NetworkEconomyTests.Run();return 0;}if(args.Contains("--search-reuse")){SearchReuseTests.Run();return 0;}if(args.Contains("--reported-benchmark")){SearchReuseTests.Benchmark();return 0;}Run();ColumnSlabEndSupportTests.Run();EquivalentLoadingTests.Run();IndependentSpacingSearchTests.Run();IndependentSpacingSearchTests.Controls();SpacingCoverageRegressionTests.Run();PlywoodTests.Run();RequiredRowTests.Run();IndividualSpacingTests.Run();UpliftAssumptionTests.Run();TowerTests.Run();SlabEndLoadTests.Run();PrimaryUdlTests.Run();TransferTests.Run();NetworkTests.Run();SymmetricSlabSupportTests.Run();CrossSupportTests.Run();CrossEndStripTests.Run();CrossEndStripTests.Reported();CrossJunctionTests.Run();LocalShutterTests.Run();NetworkFallbackTests.Run();NetworkEditTests.Run();WS10Tests.Run();PrimaryJointRowTests.Run();CustomH20Tests.Run();DesignerPreferenceTests.Run();CoordinatedDesignerTests.Run();CoordinatedDesignerTests.Options();CustomH20Tests.Scope();DiagnosticTests.Run();ObstacleIndexTests.Run();CrossingFootprintTests.Run();SearchBudgetTests.Run();SearchReuseTests.Run();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);if(ex is TowerOptimizationException optimization)Console.Error.WriteLine(optimization.Details);return 1;}}
 static void Run()
 {
  var v=new BeamCalculatorViewModel();Check(v.DeflectionLimit.Label=="L/270","requested H20 default");Check(v.DeflectionOptions.Select(x=>x.Label).SequenceEqual(new[]{"L/500","L/350","L/270"}),"slab-aligned criteria");Equal(2000,v.MaximumPropSpacing,"default");Check(v.LimitingFactor=="Max Spacing Limit","two-prop cap");
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
  v=new BeamCalculatorViewModel();
  foreach(var option in BeamPropOption.All)
  {
   v.SelectedProp=option;
   Check(v.PropFamily==option.Family && v.PropSize==option.Size && v.OuterTubePosition==option.Orientation,"saved prop maps to separate selectors");
   Check(v.PropSizeOptions.Contains(option.Size) && v.PropOrientationOptions.Contains(option.Orientation),"selector options include restored configuration");
  }
  v=new BeamCalculatorViewModel();v.PropFamily="DokaFit Prop";v.PropSize="400";v.OuterTubePosition="at top";
  Check(v.SelectedProp.CapacityKey=="DokaFit Prop 400 at top","selector changes map to capacity key");
  v.PropFamily="Prop Type Eurex eco 20";
  Check(v.SelectedProp.CapacityKey=="400 at top","family change retains supported size and orientation");
  foreach(string n in new[]{"2","3","4+"})
  {
   v=new BeamCalculatorViewModel {Width=500,Depth=700,HasLeftSlab=true,NumberOfProps=n};
   int first=n=="4+" ? 4 : int.Parse(n),last=n=="4+" ? 12 : first;
   for(int supports=first;supports<=last;supports++)
   {
    var leftResponse=v.AnalyzePropPrimary(0,supports);var rightResponse=v.AnalyzePropPrimary(1,supports);
    double length=((supports-1)*v.MaximumPropSpacing+2*v.PrimaryBeamCantilever)/1000;
    Equal(v.LeftLineLoad*length,leftResponse.Reactions.Sum(),"left primary diagram force equilibrium");
    Equal(v.RightLineLoad*length,rightResponse.Reactions.Sum(),"right primary diagram force equilibrium");
    Equal(v.LeftLineLoad*length*length/2,leftResponse.Reactions.Select((r,i)=>r*(v.PrimaryBeamCantilever+i*v.MaximumPropSpacing)/1000).Sum(),"primary diagram moment equilibrium");
    Equal(leftResponse.Moment*v.RightLineLoad/v.LeftLineLoad,rightResponse.Moment,"primary diagram uses each line's load");
    Check(leftResponse.DeflectionUtilization(v.DeflectionLimit.Millimetres,v.DeflectionLimit.Denominator)<=1+1e-9,"diagram respects selected local deflection limits");
   }
  }
  bool rejectedDiagram=false;try{v.AnalyzePropPrimary(0,13);}catch(ArgumentException){rejectedDiagram=true;}Check(rejectedDiagram,"unsupported diagram layout rejected");
  v.Depth=0;rejectedDiagram=false;try{v.AnalyzePropPrimary(0,4);}catch(ArgumentException){rejectedDiagram=true;}Check(rejectedDiagram,"invalid inputs cannot open stale primary analysis");
  Console.WriteLine($"Passed {count} beam checks across {scenarios} sweep scenarios.");
 }
}
