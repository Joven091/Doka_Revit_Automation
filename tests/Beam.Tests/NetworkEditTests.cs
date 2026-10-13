using System;
using System.Linq;
using EngineeringDashboard;

internal static class NetworkEditTests
{
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception("Network editing: "+name);}
    internal static void Run()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true};
        Check(vm.NetworkBeams.Count==2 && vm.NetworkBays.Count==1,"initial chain");vm.AddNetworkBay();
        var originalBay=vm.NetworkBays[1];vm.AddCrossingBeam();var crossing=vm.CrossingBeams[0];crossing.BayNumber=2;
        vm.InsertNetworkBay(0,true);Check(vm.NetworkBeams.Count==4 && crossing.BayNumber==3 && ReferenceEquals(vm.NetworkBays[2],originalBay),"insert before first beam preserves existing slab and crossing association");
        vm.RemoveNetworkBeam(0);Check(vm.NetworkBeams.Count==3 && crossing.BayNumber==2 && ReferenceEquals(vm.NetworkBays[1],originalBay),"remove first pair restores associations");
        vm.InsertNetworkBay(1,false);Check(vm.NetworkBeams.Count==4 && crossing.BayNumber==3 && vm.NetworkBeams[2].DepthMm==vm.NetworkBeams[1].DepthMm,"insert after interior beam clones selected beam and shifts later bay numbers");
        vm.RemoveNetworkBeam(2);Check(crossing.BayNumber==2 && ReferenceEquals(vm.NetworkBays[1],originalBay),"remove interior inserted pair restores original bay");
        vm.AddCrossingBeam();vm.CrossingBeams.Last().BayNumber=1;vm.RemoveNetworkBeam(0);
        Check(vm.CrossingBeams.Count==1 && ReferenceEquals(vm.CrossingBeams[0],crossing) && crossing.BayNumber==1,"removing bay removes only its crossing loads and reindexes survivors");
        var survivor=vm.NetworkBeams[1];survivor.WidthMm=725;survivor.DepthMm=1375;survivor.BottomElevationMm=-1200;
        vm.RemoveNetworkBeam(0);
        Check(vm.NetworkBeams.Count==1 && vm.NetworkBays.Count==0 && vm.CrossingBeams.Count==0 && !vm.IsNetworkMode && !vm.TransferSlabLoads && !vm.HasLeftSlab && !vm.HasRightSlab,"delete last bay enters beam-only design and removes its dependent crossing");
        Check(vm.Width==725 && vm.Depth==1375 && vm.StandaloneDefinition().BottomElevationMm==-1200,"standalone mode preserves the surviving beam's dimensions and level");
        vm.ValidateTowerInputs();Check(vm.CanOptimizeTowers,"standalone drawing can calculate using the single-beam solver");
        string single=vm.NetworkConfigurationXml;vm.RemoveNetworkBeam(0);Check(vm.NetworkConfigurationXml==single,"last beam is retained");
        vm.Depth=1500;vm.Width=800;var savedSingle=new BeamCalculatorViewModel {UseTowers=true};savedSingle.NetworkConfigurationXml=vm.NetworkConfigurationXml;
        Check(savedSingle.NetworkBeams.Count==1 && !savedSingle.IsNetworkMode && savedSingle.Depth==1500 && savedSingle.Width==800 && savedSingle.NetworkBeams[0].BottomElevationMm==-1325,"edited standalone dimensions serialize while preserving concrete top");
        vm.InsertNetworkBay(0,true);Check(vm.IsNetworkMode && vm.NetworkBeams.Count==2 && vm.NetworkBays.Count==1 && vm.NetworkBeams.All(b=>b.DepthMm==1500 && b.WidthMm==800 && b.BottomElevationMm==-1325),"inserting a bay after standalone editing re-enters transfer mode with current dimensions");
        vm.AddCrossingBeam();
        for(int i=0;i<20;i++)vm.AddNetworkBay();Check(vm.NetworkBeams.Count==15 && vm.NetworkBays.Count==14,"maximum fifteen-beam bound retained");
        vm.InsertNetworkBay(0,true);Check(vm.NetworkBeams.Count==15,"insertion cannot bypass solver beam limit");
        Check(vm.NetworkBeams.Select((b,i)=>b.Label=="B"+(i+1)).All(x=>x) && vm.NetworkBays.Select((b,i)=>b.Label=="S"+(i+1)).All(x=>x),"labels reflect final topology");
        var restored=new BeamCalculatorViewModel {UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true};restored.NetworkConfigurationXml=vm.NetworkConfigurationXml;
        Check(restored.NetworkBeams.Count==15 && restored.CrossingBeams[0].BayNumber==1,"edited chain serializes with crossing associations");
        restored.ValidateTowerInputs();
        object editor1=new object(),editor2=new object();restored.SetAdditionalTextInputErrors(editor1,true);restored.SetTextInputErrors(false);
        Check(!restored.CanOptimizeTowers,"parent validation cannot clear drawing-editor errors");
        restored.SetAdditionalTextInputErrors(editor2,true);restored.SetAdditionalTextInputErrors(editor1,false);Check(!restored.CanOptimizeTowers,"independent contributors retain validation");
        restored.SetAdditionalTextInputErrors(editor2,false);Check(restored.CanOptimizeTowers,"last contributor clears its error");
        restored.SetTextInputErrors(true);restored.SetAdditionalTextInputErrors(editor1,false);Check(!restored.CanOptimizeTowers,"drawing cleanup does not clear parent errors");
        restored.SetTextInputErrors(false);Check(restored.CanOptimizeTowers,"parent conversion recovery");
        Drafting();
        Console.WriteLine($"Passed {checks} network editing checks.");
    }
    static void Drafting()
    {
        var vm=new BeamCalculatorViewModel {UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true};vm.AddNetworkBay();
        foreach(var b in vm.NetworkBeams){b.WidthMm=650;b.DepthMm=1200;}
        foreach(var bay in vm.NetworkBays)bay.WidthMm=3000;
        var centres=vm.GeometryCentres();
        Check(vm.TryMoveParallelBeam(1,250) && vm.GeometryCentres()[0]==centres[0] && vm.GeometryCentres()[1]==centres[1]+250 && vm.GeometryCentres()[2]==centres[2],"moving an interior beam changes its adjacent bays and preserves both neighbors");
        Check(vm.NetworkBays[0].WidthMm==3250 && vm.NetworkBays[1].WidthMm==2750,"moved beam adjusts slab widths on both sides");
        centres=vm.GeometryCentres();Check(vm.TryMoveParallelBeam(0,-500) && vm.GeometryCentres()[0]==centres[0]-500 && vm.GeometryCentres()[1]==centres[1] && vm.GeometryCentres()[2]==centres[2],"moving the first beam retains other world positions");
        string before=vm.NetworkConfigurationXml;Check(!vm.TryMoveParallelBeam(1,10000) && vm.NetworkConfigurationXml==before,"overlap move is rejected atomically");
        Check(vm.TryMoveParallelBeam(2,200) && vm.GeometryCentres()[1]==centres[1] && vm.GeometryCentres()[2]==centres[2]+200,"last beam move changes only the last bay");
        var source=new TransferBeamRow {WidthMm=800,DepthMm=1500,BottomElevationMm=-1300};centres=vm.GeometryCentres();
        Check(vm.TryInsertParallelBeam(source,(centres[0]+centres[1])/2,out int index) && index==1,"copy can be placed inside a slab bay");
        var current=vm.GeometryCentres();Check(current[0]==centres[0] && current[2]==centres[1] && current[3]==centres[2],"copy splits a bay while retaining all existing beam positions");
        Check(vm.NetworkBeams[1].WidthMm==800 && vm.NetworkBeams[1].DepthMm==1500 && vm.NetworkBeams[1].BottomElevationMm==-1300 && vm.NetworkBays.Count==3,"copy carries width, depth and level");
        before=vm.NetworkConfigurationXml;Check(!vm.TryInsertParallelBeam(source,current[1],out index) && vm.NetworkConfigurationXml==before,"copy on an existing beam is rejected without partial changes");
        Check(vm.TryInsertParallelBeam(source,current[0]-2000,out index) && index==0 && vm.GeometryCentres()[1]==current[0],"copy above the first beam preserves the previous origin beam");
        current=vm.GeometryCentres();Check(vm.TryInsertParallelBeam(source,current.Last()+2000,out index) && index==current.Length && vm.GeometryCentres().Take(current.Length).SequenceEqual(current),"copy after the last beam preserves all earlier positions");
        var restored=new BeamCalculatorViewModel {UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true};restored.NetworkConfigurationXml=vm.NetworkConfigurationXml;
        Check(restored.GeometryCentres().SequenceEqual(vm.GeometryCentres()),"drafted origin and bay positions round-trip");
        var standalone=new BeamCalculatorViewModel {UseTowers=true,Width=650,Depth=1200};
        Check(standalone.TryMoveParallelBeam(0,500) && standalone.GeometryCentres()[0]==500,"a standalone beam can move");
        Check(standalone.TryInsertParallelBeam(source,2500,out index) && index==1 && standalone.IsNetworkMode && standalone.GeometryCentres()[0]==500 && standalone.GeometryCentres()[1]==2500,"copying a standalone beam creates connected geometry at the picked point");
        standalone.RemoveNetworkBeam(0);Check(!standalone.IsNetworkMode && standalone.PlanOriginYmm==2500,"deleting the first beam retains the survivor's drafting position");
        standalone.TransferSlabLoads=true;standalone.UseBeamNetwork=true;Check(standalone.NetworkBeams.Count==2 && standalone.NetworkBays.Count==1 && standalone.IsNetworkMode,"classic network toggle cannot leave a one-row network in the multi-beam solver");
    }

}
