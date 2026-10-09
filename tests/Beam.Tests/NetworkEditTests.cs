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
        int count=vm.NetworkBeams.Count;vm.RemoveNetworkBeam(0);Check(vm.NetworkBeams.Count==count && count==2,"minimum two beams retained");
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
        Console.WriteLine($"Passed {checks} network editing checks.");
    }
}
