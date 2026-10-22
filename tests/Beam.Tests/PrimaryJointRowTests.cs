using System;
using System.Linq;
using System.Reflection;
using EngineeringDashboard;

internal static class PrimaryJointRowTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Primary joint rows: "+message);}
    static void Spacing(double[] rows,double maximum,double[] joints)
    {
        Check(rows.Zip(rows.Skip(1),(a,b)=>b-a).All(g=>g>=80-1e-7 && g<=maximum+1e-7),"every physical gap meets nominal spacing and flange separation");
        Check(rows.All(x=>joints.All(j=>Math.Abs(x-j)>=40-1e-7)),"full secondary flange bears on one primary stock");
    }
    static TowerInput Reported()
    {
        var vm=new BeamCalculatorViewModel {HasSelectedSupportSystem=true,UseTowers=true,TransferSlabLoads=true,UseBeamNetwork=true,
            BeamLengthMm=7000,Depth=1450,Width=800,FloorHeight=3000,TowerLegCapacityKn=70,PrimaryMaterial=PrimaryBeamMaterial.WS10,
            AllowCustomH20Lengths=true,CustomH20Scope=CustomH20LengthScope.SlabSupports,MaximumConcreteEndOverhangMm=75,StockEndSetbackMm=150,
            Exclude600Standalone=false,PlywoodDeflectionLimit="L/270",DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption("L/270",null)};
        for(int i=0;i<2;i++){var b=vm.NetworkBeams[i];b.WidthMm=i==0 ? 650 : 800;b.DepthMm=i==0 ? 1150 : 1750;b.BottomElevationMm=-b.DepthMm;}
        vm.NetworkBays[0].WidthMm=1875;vm.NetworkBays[0].ThicknessMm=300;
        vm.CrossingBeams.Add(new TransferCrossingBeamRow {BayNumber=1,PositionMm=3450,WidthMm=650,DepthMm=1150,BottomElevationMm=-1150});
        return (TowerInput)typeof(BeamCalculatorViewModel).GetMethod("CreateTowerInput",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(vm,new object[] {true});
    }
    static TowerLayout TwoTowers(TowerInput input,TransferNetworkResult baseline)
    {
        var trial=new TowerLayout {PrimaryMaterial=PrimaryBeamMaterial.WS10,LengthMm=7000,WidthMm=1524,Rotated=false,
            PositionsMm=new[] {750.0,2750,4250,6250},Joined=new[] {true,false,true},Transfer=baseline.Beams[0].Transfer.Copy(),
            StockPlan=new TowerStockPlan {PrimaryMaterial=PrimaryBeamMaterial.WS10,Pieces=new[] {
                new TowerStockPiece {StartMm=0,StockLengthMm=3500,FirstSupport=0,LastSupport=1},new TowerStockPiece {StartMm=3500,StockLengthMm=3500,FirstSupport=2,LastSupport=3}}}};
        trial.Transfer.LowerStockLengthMm=2450;trial.Transfer.LowerStockStartMm=-1225;
        return (TowerLayout)typeof(TransferNetworkOptimizer).GetMethod("Solve",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,
            new object[] {input,baseline.Input,0,null,baseline.Bays[0],7000.0,0.0,new[] {new[] {3500.0,3500}},new TransferredSlabOptimizer.GeometryCache(),8,(double?)1524,0.0,null,trial});
    }
    internal static void Run()
    {
        var source=PlywoodSpacing.FillRequiredRows(0,7000,350,new[] {3165.0,3515,3735},true);
        var before=(double[])source.Clone();
        var requests=source.Select((x,i)=>new ReceivingRowRequest {PositionMm=x,Sides=1,
            Crossing=x>=3125 && x<=3775,MinimumMm=i==0 || i==source.Length-1 ? x : x>=3125 && x<=3775 ? 3165 : x<3125 ? 40 : 3815,
            MaximumMm=i==0 || i==source.Length-1 ? x : x>=3125 && x<=3775 ? 3735 : x<3125 ? 3085 : 6960}).ToArray();
        var adjusted=ReceivingRows.Coordinate(0,7000,350,requests,new[] {3500.0});
        Spacing(adjusted.RowsMm,350,new[] {3500.0});
        Check(source.SequenceEqual(before),"design rows remain unchanged");
        Check(adjusted.ReceiversMm[Array.IndexOf(source,3515)]==3540,"reported row moves 25 mm to clear the 3500 mm joint");
        Check(adjusted.RowsMm.Length>source.Length,"an extra secondary restores the maximum spacing");
        Check(requests.Select((r,i)=>!r.Crossing || adjusted.ReceiversMm[i]>=3165 && adjusted.ReceiversMm[i]<=3735).All(x=>x),"every required X1 receiver remains within its bearing footprint");
        // An original plywood joint may move locally, with a custom sheet cut.
        var sheet=PlywoodSpacing.SheetSupportRows(0,7000,350);
        var custom=ReceivingRows.Coordinate(0,7000,350,sheet.Select((x,i)=>new ReceivingRowRequest {PositionMm=x,Sides=1,
            MinimumMm=i==0 || i==sheet.Length-1 ? x : 40,MaximumMm=i==0 || i==sheet.Length-1 ? x : 6960}).ToArray(),new[] {2440.0});
        Spacing(custom.RowsMm,350,new[] {2440.0});Check(!custom.RowsMm.Contains(2440),"a sheet-joint row no longer rejects an otherwise fitting primary run");
        bool blocked=false;
        try{ReceivingRows.Coordinate(0,7000,350,new[] {new ReceivingRowRequest {PositionMm=3500,MinimumMm=3500,MaximumMm=3500}},new[] {3500.0});}
        catch(ArgumentException){blocked=true;}
        Check(blocked,"a row with no permitted bearing position remains rejected");
        var random=new Random(35002000);
        for(int i=0;i<100;i++)
        {
            double joint=300+random.Next(6400);
            var rows=ReceivingRows.Coordinate(0,7000,350,sheet.Select((x,index)=>new ReceivingRowRequest {PositionMm=x,Sides=1,
                MinimumMm=index==0 || index==sheet.Length-1 ? x : 40,MaximumMm=index==0 || index==sheet.Length-1 ? x : 6960}).ToArray(),new[] {joint});
            Spacing(rows.RowsMm,350,new[] {joint});Check(rows.ReceiversMm.Distinct().Count()==sheet.Length,"distinct design receivers are not merged");
        }
        var input=Reported();input.Trace=new BeamSearchTrace("Reported WS10 joint placement");
        var result=TransferNetworkOptimizer.Optimize(input,input.Network);ControlledPerformanceTests.Validate(input,result);
        foreach(var beam in result.Beams)
        {
            var t=beam.Transfer;var rows=Enumerable.Range(0,t.PhysicalRowCount).Select(t.PhysicalRowPosition).ToArray();
            Spacing(rows,t.EnteredUpperSpacingMm,beam.StockPlan.Pieces.Skip(1).Select(p=>p.StartMm).ToArray());
            Check(t.SettingOutDesignRows==null || t.SettingOutDesignRows.All(i=>i>=0 && i<t.RowsMm.Length),"all moved/added H20s reference checked design rows");
        }
        Console.WriteLine("REPORTED WS10 legs="+string.Join(",",result.Beams.Select(b=>b.LegCount))+" stocks="+string.Join(";",result.Beams.Select(b=>string.Join(",",b.StockPlan.Pieces.Select(p=>p.StockLengthMm))))+" seconds="+input.Trace.ElapsedSeconds.ToString("0.000"));
        input.Diagnostics=new TowerSearchDiagnostics(270,ignoreUplift:true);
        input.CoordinateCrossingLayouts=true;
        var requested=TwoTowers(input,result);
        if(requested==null)
        {
            string failure=input.Diagnostics.Exception("Requested two-tower layout").Details;
            Check(!failure.Contains("primary joint"),"the reported two-tower arrangement is no longer rejected by the adjustable row position");
            Console.WriteLine(failure);
        }
        // Isolate the primary-joint repair from the independent X1 junction design.
        input=Reported();input.Network.CrossingBeams=new TransferCrossingBeamRow[0];input.Network.CrossingSpacingsMm=new double[0];input.Network.SlabSpacingsMm=new[] {350.0};input.CoordinateCrossingLayouts=false;
        var baseline=TransferNetworkOptimizer.Optimize(input,input.Network);
        var passing=TwoTowers(input,baseline);
        Check(passing!=null && passing.LegCount==8 && passing.Utilization<=1+1e-8,"two 3500 mm WS10 runs with 2000 mm spans pass when the independent X1 detail is excluded");
        var transfer=passing.Transfer;
        Check(transfer.SettingOutRowsMm!=null,"joint repair is applied by the production fixed-layout path");
        Spacing(transfer.SettingOutRowsMm,transfer.EnteredUpperSpacingMm,new[] {3500.0});
        Check(transfer.EnteredUpperSpacingMm==350 && transfer.RowsMm.Any(x=>Math.Abs(x-3500)<40),"nominal calculation spacing and the original design grid are preserved");
        Check(transfer.PrimaryCases.SelectMany(c=>c).All(m=>m.Points.Length==0 && m.Utilization<=1+1e-8),"primary calculations retain checked UDLs and no secondary-row point loads");
        Console.WriteLine("Passed "+checks+" primary-joint setting-out checks.");
    }
}
