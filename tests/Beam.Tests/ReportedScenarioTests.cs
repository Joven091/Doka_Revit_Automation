using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using EngineeringDashboard;

// Report-faithful combined searches. No deadline, cancellation or first-result switch.
internal static class ReportedScenarioTests
{
    sealed class Geometry
    {
        public RevitSelectedBeam[] Beams {get;set;}
        public RevitSelectedSlab[] Slabs {get;set;}
        public RevitSelectedBoundary[] Boundaries {get;set;}
    }
    sealed class ExpectedPart {public string Label {get;set;} public string Key {get;set;} public string Inputs {get;set;}}
    sealed class ExpectedSpan {public int Beam {get;set;} public double StartMm {get;set;} public double EndMm {get;set;} public double LengthMm {get;set;}}
    sealed class Fixture
    {
        public int Scenario {get;set;} public string SourceSha256 {get;set;} public string SourceFile {get;set;}
        public int SelectedCount {get;set;} public string[] SelectedIds {get;set;} public string DirectionId {get;set;}
        public Geometry Selection {get;set;} public ExpectedPart[] Parts {get;set;} public string Inputs {get;set;}
        public ExpectedSpan[] ActiveSpans {get;set;} public double SearchElapsedSeconds {get;set;}
    }
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions {WriteIndented=true};
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static string Option(string[] args,string name,string fallback) {int n=Array.IndexOf(args,name);return n<0 ? fallback : args[n+1];}
    static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    static void Near(double a,double b,string message,double relative=1e-9) {Check(Finite(a)&&Finite(b)&&Math.Abs(a-b)<=relative*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b))),message+": "+a.ToString("R",CultureInfo.InvariantCulture)+" != "+b.ToString("R",CultureInfo.InvariantCulture));}
    static Dictionary<string,string> Scalars(string inputs)=>inputs.Split('\n').Where(s=>s.Contains(" = ")).Select(s=>s.Split(new[] {" = "},2,StringSplitOptions.None)).ToDictionary(s=>s[0],s=>s[1].Trim());
    static void ApplyInputs(BeamCalculatorViewModel model,string inputs)
    {
        foreach(var pair in Scalars(inputs))
        {
            var property=typeof(BeamCalculatorViewModel).GetProperty(pair.Key);
            if(property==null || !property.CanWrite)continue;
            object value=property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType,pair.Value) : Convert.ChangeType(pair.Value,property.PropertyType,CultureInfo.InvariantCulture);
            property.SetValue(model,value);
        }
        var values=Scalars(inputs);
        model.SelectedProp=BeamPropOption.All.Single(p=>p.CapacityKey==values["Prop configuration"]);
        model.DeflectionLimit=BeamCalculatorViewModel.RestoreDeflectionOption(values["Deflection criterion"],null);
        model.HasSelectedSupportSystem=true;
    }
    static Fixture Read(int scenario)
    {
        using var stream=typeof(ReportedScenarioTests).Assembly.GetManifestResourceStream("Reported20261103.scenario"+scenario+".json");
        Check(stream!=null,"Missing scenario fixture");return JsonSerializer.Deserialize<Fixture>(stream,Json);
    }
    internal static BeamCalculatorViewModel FixtureModel(int scenario)=>Create(Read(scenario));
    internal static string FixtureHash(int scenario)=>Read(scenario).SourceSha256;
    internal static Tuple<RevitDrawingDefinition,BeamCalculatorViewModel>[] ActualParts(BeamCalculatorViewModel model)
    {
        var saved=(Dictionary<string,BeamCalculatorViewModel>)typeof(BeamCalculatorViewModel).GetField("revitDrawingModels",Private).GetValue(model);
        // A combined failure leaves the solved active child in the dictionary,
        // while the wrapper has no applied result. Retain that child's evidence.
        return model.RevitDrawings.Select(d=>Tuple.Create(d,d.Key==model.ActiveRevitDrawingKey && model.IsCalculationValid ? model : saved[d.Key])).ToArray();
    }
    internal static Tuple<RevitDrawingDefinition,BeamCalculatorViewModel> DiagnosticPart(int scenario,int part)
    {
        var root=FixtureModel(scenario);
        Check(part>=1 && part<=root.RevitDrawings.Length,"Part index out of range");
        var definition=root.RevitDrawings[part-1];
        var child=(BeamCalculatorViewModel)typeof(BeamCalculatorViewModel).GetMethod("DrawingModel",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {definition});
        typeof(BeamCalculatorViewModel).GetMethod("CopyProjectInputs",Private).Invoke(root,new object[] {child});
        return Tuple.Create(definition,child);
    }
    static BeamCalculatorViewModel Create(Fixture fixture)
    {
        var source=new RevitSelectionSnapshot {DocumentTitle="Calculator test",SelectedCount=fixture.SelectedCount,SelectedIds=fixture.SelectedIds,
            Beams=fixture.Selection.Beams,Slabs=fixture.Selection.Slabs,Boundaries=fixture.Selection.Boundaries};
        var imported=RevitStructureImport.Build(source,fixture.DirectionId);
        Check(imported.CanImport,"Reported source geometry rejected: "+string.Join("; ",imported.Problems));
        var model=new BeamCalculatorViewModel();ApplyInputs(model,fixture.Inputs);imported.Apply(model);
        Check(model.RevitDrawings.Length==fixture.Parts.Length,"Incomplete combined scenario");
        foreach(var part in fixture.Parts)
        {
            var definition=model.RevitDrawings.Single(d=>d.Key==part.Key);
            Check(definition.Label==part.Label,"Part label changed");
            var child=model.RevitSceneModels().Single(p=>p.Item1.Key==part.Key).Item2;
            ValidateInputs(child,part.Inputs,part.Label);
            // This is only a fixture/production-input check. Actual searches use
            // production CopyProjectInputs, including its current propagation policy.
            var check=new BeamCalculatorViewModel {BeamLengthMm=definition.LengthMm};
            check.NetworkConfigurationXml=definition.ConfigurationXml;
            ApplyInputs(check,fixture.Inputs);
            check.BeamLengthMm=definition.LengthMm;
            check.UseBeamNetwork=check.NetworkBeams.Count>1;check.TransferSlabLoads=check.UseBeamNetwork;
            check.CreateTowerInput();
        }
        foreach(var span in fixture.ActiveSpans)
        {
            Near(model.BeamSpanStart(span.Beam-1),span.StartMm,"Active beam start");
            Near(model.BeamSpanEnd(span.Beam-1),span.EndMm,"Active beam end");
            Near(model.BeamSpanLength(span.Beam-1),span.LengthMm,"Active beam length");
        }
        return model;
    }
    static void ValidateInputs(BeamCalculatorViewModel model,string inputs,string label)
    {
        Near(model.BeamLengthMm,double.Parse(Scalars(inputs)["BeamLengthMm"],CultureInfo.InvariantCulture),label+" length");
        var beams=Regex.Matches(inputs,@"(?m)^B(\d+): width=([^;]+); depth=([^ ]+) mm; bottom=([^ ]+) mm$");
        var bays=Regex.Matches(inputs,@"(?m)^S(\d+): clear span=([^;]+); thickness=([^ ]+) mm$");
        var crosses=Regex.Matches(inputs,@"(?m)^X(\d+): slab bay=(\d+); centre=([^;]+); width=([^;]+); depth=([^ ]+) mm; bottom=([^ ]+) mm; supports=(\w+)$");
        Check(model.NetworkBeams.Count==beams.Count && model.NetworkBays.Count==bays.Count && model.CrossingBeams.Count==crosses.Count,label+" row counts");
        double Number(Group value)=>double.Parse(value.Value,CultureInfo.InvariantCulture);
        foreach(Match match in beams)
        {
            var beam=model.NetworkBeams[int.Parse(match.Groups[1].Value)-1];
            Near(beam.WidthMm,Number(match.Groups[2]),label+" beam width");Near(beam.DepthMm,Number(match.Groups[3]),label+" beam depth");Near(beam.BottomElevationMm,Number(match.Groups[4]),label+" beam soffit");
        }
        foreach(Match match in bays)
        {
            var bay=model.NetworkBays[int.Parse(match.Groups[1].Value)-1];
            Near(bay.WidthMm,Number(match.Groups[2]),label+" bay width");Near(bay.ThicknessMm,Number(match.Groups[3]),label+" slab thickness");
        }
        foreach(Match match in crosses)
        {
            var cross=model.CrossingBeams[int.Parse(match.Groups[1].Value)-1];
            Check(cross.BayNumber==int.Parse(match.Groups[2].Value),label+" crossing bay");Near(cross.PositionMm,Number(match.Groups[3]),label+" crossing position");
            Near(cross.WidthMm,Number(match.Groups[4]),label+" crossing width");Near(cross.DepthMm,Number(match.Groups[5]),label+" crossing depth");Near(cross.BottomElevationMm,Number(match.Groups[6]),label+" crossing soffit");
            Check(cross.SupportMode.ToString()==match.Groups[7].Value,label+" crossing mode");
        }
    }
    static IEnumerable<TransferMember> Members(TowerLayout layout)=>layout?.Transfer==null ? Enumerable.Empty<TransferMember>() : layout.Transfer.PrimaryCases.SelectMany(c=>c).Concat(layout.Transfer.LowerCases.SelectMany(c=>c)).Concat(layout.Transfer.UpperMembers);
    static void ValidateMember(TransferMember member,TowerInput input)
    {
        Check(member.Analysis.DeflectionEvaluated && Finite(member.Utilization) && member.Utilization<=1+1e-8,"Member capacity/deflection: "+member.Name);
        Check(member.Analysis.MomentKnM<=member.MomentLimitKnM*(1+1e-8) && member.Analysis.ShearKn<=member.ShearLimitKn*(1+1e-8),"Member bending/shear: "+member.Name);
        Check(member.Analysis.DeflectionUtilization<=1+1e-8,"Member deflection limit: "+member.Name);
        if(!input.IgnoreUplift)Check(member.MinimumReactionKn>=-1e-7,"Member uplift");
        double applied=member.Uniform.Sum(q=>(q.EndMm-q.StartMm)*q.KnPerM/1000)+member.Points.Sum(p=>p.Kn);
        Near(applied,member.Analysis.TotalLoadKn,"Member applied load",1e-7);Near(member.Analysis.Reactions.Sum(),applied,"Member reaction equilibrium",1e-6);
        double appliedMoment=member.Uniform.Sum(q=>(q.EndMm-q.StartMm)*q.KnPerM*(q.StartMm+q.EndMm)/2/1000)+member.Points.Sum(p=>p.Kn*p.PositionMm);
        Near(member.Analysis.Reactions.Zip(member.SupportsMm,(r,x)=>r*x).Sum(),appliedMoment,"Member moment equilibrium",1e-6);
        // Recompute final numerical responses from exported loads/supports, with
        // no search caches or worker response coefficients.
        bool signed=member.Uniform.Any(q=>q.KnPerM<0)||member.Points.Any(p=>p.Kn<0);
        var reference=signed
            ? LoadedBeamAnalysis.AnalyzeSigned(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,input.DeflectionDenominator)
            : LoadedBeamAnalysis.AnalyzeDenseReference(member.LengthMm,member.SupportsMm,member.Uniform,member.Points,member.EI,input.DeflectionDenominator);
        Near(reference.MomentKnM,member.Analysis.MomentKnM,"Independent bending response",1e-6);
        Near(reference.ShearKn,member.Analysis.ShearKn,"Independent shear response",1e-6);
        Near(reference.DeflectionMm,member.Analysis.DeflectionMm,"Independent deflection response",1e-6);
        for(int i=0;i<reference.Reactions.Length;i++)Near(reference.Reactions[i],member.Analysis.Reactions[i],"Independent support reaction",1e-6);
    }
    static void ValidateLayout(TowerLayout layout,TowerInput input)
    {
        Check(layout!=null && !layout.SearchTimeLimitReached,"Missing layout or elapsed-time truncation");
        Check(layout.Utilization<=1+1e-8 && layout.MaxLegKn<=input.LegCapacityKn+1e-8,"Layout capacity");
        Check(layout.LeftReactions.Concat(layout.RightReactions).All(r=>Finite(r)&&r<=input.LegCapacityKn+1e-8),"Individual leg capacity");
        Near(layout.MaxLegKn,layout.LeftReactions.Concat(layout.RightReactions).Max(),"Maximum leg envelope",1e-7);
        foreach(var member in Members(layout))ValidateMember(member,input);
        if(input.Exclude600Standalone)Check(layout.Standalone600Count==0,"Standalone 600 tower excluded");
        if(layout.Transfer==null)ValidateStandalone(layout,input);
    }
    internal static void ValidateStandalone(TowerLayout layout,TowerInput input)
    {
        var pieces=layout.StockPlan.Pieces;Check(pieces.Length>0,"Standalone primary stock missing");
        Near(layout.LongitudinalStartMm,input.LongitudinalStartMm,"Standalone concrete origin");Near(layout.LengthMm,input.LengthMm,"Standalone concrete length");
        double near=pieces[0].StartMm-input.LongitudinalStartMm;
        double far=input.LongitudinalStartMm+input.LengthMm-pieces.Last().EndMm;
        Check(near>=-1e-7 && far>=-1e-7 && near<=input.MaximumConcreteEndOverhangMm+1e-7 && far<=input.MaximumConcreteEndOverhangMm+1e-7,"Standalone external end strips exceed configured allowance");
        Near(near,far,"Standalone symmetric external end strips");Near(layout.ConcreteEndOverhangMm,near,"Standalone reported end allowance");
        for(int i=1;i<pieces.Length;i++)Near(pieces[i-1].EndMm,pieces[i].StartMm,"Standalone internal stock continuity");
        double covered=pieces.Sum(p=>p.StockLengthMm);Near(covered+near+far,input.LengthMm,"Standalone full concrete coverage accounting");
        Near(layout.LeftLineLoad+layout.RightLineLoad,input.TotalLineLoad,"Standalone line UDL split");
        Near(layout.LeftLineLoad,input.TotalLineLoad/2-input.TransverseLoadMoment/(layout.WidthMm/1000),"Standalone left line UDL");
        Near(layout.RightLineLoad,input.TotalLineLoad/2+input.TransverseLoadMoment/(layout.WidthMm/1000),"Standalone right line UDL");
        double weight=2*pieces.Sum(p=>layout.Primary.SelfWeightKnM(p.StockLengthMm)*p.StockLengthMm/1000);
        double supported=input.TotalLineLoad*covered/1000;
        double external=input.TotalLineLoad*(near+far)/1000;
        double reactions=layout.LeftReactions.Sum()+layout.RightReactions.Sum();
        Near(reactions,supported+weight,"Standalone supported-stock force conservation",1e-6);
        Near(reactions-weight+external,input.TotalLineLoad*input.LengthMm/1000,"Standalone primary plus external-column force conservation",1e-6);
        var checkedLegs=new bool[layout.PositionsMm.Length];
        foreach(var piece in pieces)
        {
            Check(piece.FirstSupport>=0 && piece.LastSupport<checkedLegs.Length && piece.LastSupport>piece.FirstSupport,"Standalone stock support indices");
            var supports=layout.PositionsMm.Skip(piece.FirstSupport).Take(piece.LastSupport-piece.FirstSupport+1).Select(x=>x-piece.StartMm).ToArray();
            Check(supports[0]>=input.StockEndSetbackMm-1e-7 && piece.StockLengthMm-supports.Last()>=input.StockEndSetbackMm-1e-7,"Standalone stock end setbacks");
            var responses=new[] {layout.LeftLineLoad,layout.RightLineLoad}.Select(q=>LoadedBeamAnalysis.AnalyzeDenseReference(piece.StockLengthMm,supports,
                new[] {new BeamDistributedLoad {StartMm=0,EndMm=piece.StockLengthMm,KnPerM=q+layout.Primary.SelfWeightKnM(piece.StockLengthMm)}},
                Array.Empty<BeamPointLoad>(),layout.Primary.EI,input.DeflectionDenominator)).ToArray();
            Near(piece.MomentKnM,responses.Max(r=>r.MomentKnM),"Standalone stock dense bending",1e-6);
            Near(piece.ShearKn,responses.Max(r=>r.ShearKn),"Standalone stock dense shear",1e-6);
            Near(piece.DeflectionMm,responses.Max(r=>r.DeflectionMm),"Standalone stock dense deflection",1e-6);
            Near(piece.DeflectionUtilization,responses.Max(r=>r.DeflectionUtilization),"Standalone stock dense deflection criterion",1e-6);
            Check(responses.All(r=>r.DeflectionEvaluated && r.DeflectionUtilization<=1+1e-8 && layout.Primary.Strength(r)<=1+1e-8),"Standalone fresh member capacity");
            for(int i=0;i<supports.Length;i++)
            {
                int leg=piece.FirstSupport+i;Check(!checkedLegs[leg],"Standalone support assigned to two independent stocks");checkedLegs[leg]=true;
                Near(layout.LeftReactions[leg],responses[0].Reactions[i],"Standalone dense left leg reaction",1e-6);
                Near(layout.RightReactions[leg],responses[1].Reactions[i],"Standalone dense right leg reaction",1e-6);
            }
        }
        Check(checkedLegs.All(x=>x),"Standalone support without a stock run");
        Near(layout.MomentKnM,pieces.Max(p=>p.MomentKnM),"Standalone layout bending envelope",1e-6);
        Near(layout.ShearKn,pieces.Max(p=>p.ShearKn),"Standalone layout shear envelope",1e-6);
        Near(layout.DeflectionMm,pieces.Max(p=>p.DeflectionMm),"Standalone layout deflection envelope",1e-6);
    }
    internal static void ValidateCrossingLoadPaths(CrossBeamSupportResult cross)
    {
        // Beam>=0 identifies transfers out to existing main receivers, including
        // primary bearings and concrete/shutter/ledger end zones. Dedicated
        // secondary-to-primary and primary-to-leg bearings both have Beam=-1;
        // counting all exported bearings would count the same force twice.
        double external=cross.Bearings.Where(b=>b.Beam>=0).Sum(b=>b.ReactionKn);
        double dedicated=cross.Towers?.Transfer.PrimaryCases.Last().Sum(m=>
            m.Analysis.Reactions.Sum()-m.PrimarySelfWeightKnM*m.LengthMm/1000)??0;
        Near(external+dedicated,cross.LoadKn,"Exported crossing receiver/leg load-path conservation",1e-6);
    }
    internal static void ValidateSolidClashes(IEnumerable<FormworkSolid> exported,IEnumerable<FormworkSolid> concrete)
    {
        var solids=exported.ToArray();var blocks=concrete.ToArray();
        for(int i=0;i<solids.Length;i++)
        {
            foreach(var block in blocks)Check(!solids[i].Conflicts(block),"Final concrete clash: "+solids[i].Name+" / "+block.Name);
            for(int j=0;j<i;j++)Check(!solids[i].Conflicts(solids[j]),"Final formwork clash: "+solids[i].Name+" / "+solids[j].Name);
        }
    }
    internal static void ValidatePart(Tuple<RevitDrawingDefinition,BeamCalculatorViewModel> part,string mode,string savedDiagnostics=null)
    {
        var child=part.Item2;Check(child.IsCalculationValid,part.Item1.Label+" rejected: "+child.WarningMessage+"\n"+child.TowerFailureDetails);var input=child.CreateTowerInput();
        Near(input.LegCapacityKn,70,"Project leg capacity propagated");Check(input.AllowCustomH20Lengths && input.CustomH20Scope==CustomH20LengthScope.Both,"Project custom lengths propagated");
        string diagnostics=savedDiagnostics??child.BuildDiagnosticReport();
        if(mode=="worker")Check(diagnostics.Contains(".NET 10 worker; complete result returned")&&!diagnostics.Contains(".NET Framework fallback"),"Worker required; fallback forbidden: "+part.Item1.Label);
        if(child.NetworkResult!=null)
        {
            var network=child.NetworkResult;Check(!network.SearchTimeLimitReached,"Network truncated");
            Check(network.CrossSupports.Length==input.Network.CrossingBeams.Length,"Incomplete crossing coverage");
            // Repeat the complete exported geometry contract independently of
            // the production obstacle index and crossing search/cache history.
            ValidateSolidClashes(CrossBeamSupportOptimizer.NetworkSolids(network).Concat(network.CrossSupports.SelectMany(c=>c.Solids)),CrossBeamSupportOptimizer.Concrete(network));
            foreach(var layout in network.Beams)ValidateLayout(layout,input);
            foreach(var cross in network.CrossSupports)
            {
                ValidateCrossingLoadPaths(cross);
                if(cross.Towers!=null)ValidateLayout(cross.Towers,input);
                foreach(var member in cross.Members)ValidateMember(member,input);
                double clearWidth=network.Input.Bays[cross.Input.BayNumber-1].WidthMm;
                double physicalCross=network.Input.AreaLoad(cross.Input.DepthMm)*cross.Input.WidthMm*clearWidth/1e6;
                Near(cross.LoadKn,physicalCross+cross.SlabEdgeLoadKn,"Crossing concrete and slab-edge conservation",1e-6);
            }
            double slab=0;
            for(int bay=0;bay<network.Bays.Length;bay++)if(network.Bays[bay]!=null)
            {
                var definition=network.Input.Bays[bay];
                var crosses=network.Input.CrossingBeams.Where(c=>c.BayNumber==bay+1).ToArray();
                double removed=crosses.Where(c=>c.SupportMode!=CrossBeamSupportMode.LegacySlabRows).Sum(c=>c.WidthMm);
                double length=network.Input.BayEnd(bay,input.LengthMm)-network.Input.BayStart(bay,input.LengthMm)-removed;
                slab+=network.Input.AreaLoad(definition.ThicknessMm)*length*definition.WidthMm/1e6;
                slab-=network.CrossSupports.Where(c=>c.Input.BayNumber==bay+1).Sum(c=>c.SlabEdgeLoadKn);
                slab+=crosses.Where(c=>c.SupportMode==CrossBeamSupportMode.LegacySlabRows).Sum(c=>(network.Input.AreaLoad(c.DepthMm)-network.Input.AreaLoad(definition.ThicknessMm))*c.WidthMm*definition.WidthMm/1e6);
            }
            Near(network.SlabLoadKn,slab,"Network slab/crossing footprint conservation",1e-6);
            ControlledPerformanceTests.Validate(input,network);
        }
        else ValidateLayout(child.TowerResult,input);
    }
    static void ValidateResult(BeamCalculatorViewModel model,Fixture fixture,string mode)
    {
        var parts=ActualParts(model);Check(parts.Length==fixture.Parts.Length,"Parts lost after optimization");
        var failures=new List<string>();
        foreach(var part in parts)
            try {ValidatePart(part,mode);}catch(Exception error){failures.Add(part.Item1.Label+": "+error);}
        if(!model.IsCalculationValid)failures.Add("Full scenario rejected: "+model.WarningMessage+"\n"+model.TowerFailureDetails);
        if(parts.All(p=>p.Item2.IsCalculationValid))
        {
            string clash=model.CombinedRevitClash();if(clash!=null)failures.Add("Final combined clash: "+clash);
        }
        Check(failures.Count==0,string.Join("\n\n",failures));
    }
    static string Hash(string text){using var algorithm=SHA256.Create();return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");}
    static object LayoutSnapshot(TowerLayout layout)=>new {layout.LegCount,layout.GroupCount,layout.BayCount,layout.Standalone600Count,layout.StandaloneBracePenalty,layout.BraceLocationPenalty,layout.SymmetryErrorMm,layout.CantileverImbalanceMm,layout.MaxLegKn,layout.Utilization,layout.PositionsMm,layout.Joined,layout.WidthMm,layout.TransverseOffsetMm,layout.LongitudinalStartMm,layout.LengthMm,layout.MinimumLegCountProven,layout.SearchTimeLimitReached,H20=layout.StockPlan.TotalH20Count+(layout.Transfer?.AdditionalH20Count??0),layout.StockPlan.TotalStockLengthMm};
    internal static object PartSnapshot(Tuple<RevitDrawingDefinition,BeamCalculatorViewModel> part)
    {
        var model=part.Item2;var network=model.NetworkResult;
        var members=(network==null ? Members(model.TowerResult) : network.Beams.SelectMany(Members).Concat(network.CrossSupports.SelectMany(c=>c.Members))).ToArray();
        return new {part.Item1.Label,part.Item1.Key,model.IsCalculationValid,model.WarningMessage,
            Network=network==null ? null : new {network.LegCount,network.H20Count,network.TimberMm,network.EconomyScoreMm,network.SeparateSlabCount,network.SymmetryErrorMm,network.PeakLegKn,Crossings=network.CrossSupports.Select(c=>new {c.CrossingIndex,Arrangement=c.Arrangement.ToString(),c.UsesSpreader,c.UsesPrimaryBearings,c.LoadKn,c.SlabEdgeLoadKn,Legs=c.Towers?.LegCount??0}).ToArray(),Fingerprint=Hash(ControlledPerformanceTests.Fingerprint(network))},
            Layouts=(network?.Beams??new[] {model.TowerResult}).Where(b=>b!=null).Select(LayoutSnapshot).ToArray(),
            Members=members.Length,MaxBending=members.Select(m=>m.Analysis.MomentKnM/m.MomentLimitKnM).DefaultIfEmpty().Max(),MaxShear=members.Select(m=>m.Analysis.ShearKn/m.ShearLimitKn).DefaultIfEmpty().Max(),MaxDeflection=members.Select(m=>m.Analysis.DeflectionUtilization).DefaultIfEmpty().Max()};
    }
    internal static void Run(string[] args)
    {
        if(args.Contains("--reported-evidence-checks")){ReportedScenarioEvidenceTests.Run();return;}
        if(args.Contains("--verify-evidence")){ReportedScenarioEvidence.Run(args);return;}
        var scenarios=Option(args,"--scenarios","1,2,3").Split(',').Select(int.Parse).ToArray();
        string output=Option(args,"--output","artifacts/reported-scenarios"),mode=Option(args,"--mode","worker");
        Check(mode=="worker" || mode=="in-process","Mode must be worker or in-process");Directory.CreateDirectory(output);
        bool fixturesOnly=args.Contains("--reported-scenario-fixtures");bool failed=false;
        foreach(int number in scenarios)
        {
            var fixture=Read(number);var setup=Stopwatch.StartNew();var model=Create(fixture);setup.Stop();
            Console.WriteLine("FIXTURE scenario="+number+" parts="+model.RevitDrawings.Length+" beams="+fixture.Selection.Beams.Length+" slabs="+fixture.Selection.Slabs.Length+" boundaries="+fixture.Selection.Boundaries.Length+" inputSha256="+fixture.SourceSha256);
            File.WriteAllText(Path.Combine(output,"scenario"+number+".inputs.xml"),model.NetworkConfigurationXml);
            if(fixturesOnly)continue;
            string worker=Option(args,"--worker",Path.GetFullPath("tools/BeamSolver.Worker/bin/Release/net10.0/Doka.BeamSolver.dll"));
            string host=Option(args,"--dotnet",PortableTestRuntime.ProcessPath);
            if(mode=="worker")Check(File.Exists(worker)&&File.Exists(host),"Build matching worker and provide --dotnet host first");
            using var session=new BeamSolverSession(mode=="worker" ? worker : Path.Combine(output,"explicit-in-process-no-worker"),mode=="worker" ? host : null);
            model.SharedSolverSession=session;
            string validationFailure=null;var wall=Stopwatch.StartNew();
            Console.WriteLine("START scenario="+number+" mode="+mode+" runtime="+Environment.Version+" no deadline; whole scenario includes validation");
            var solve=model.OptimizeTowersAsync();double next=10;
            while(!solve.IsCompleted)
            {
                Thread.Sleep(100);
                if(wall.Elapsed.TotalSeconds>=next)
                {
                    string progress="scenario="+number+" elapsed="+wall.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" "+model.SearchProgressText;
                    Console.WriteLine("PROGRESS "+progress);File.WriteAllText(Path.Combine(output,"scenario"+number+".progress.txt"),progress);
                    // Running child is separate from committed child models in production.
                    var running=(BeamCalculatorViewModel)typeof(BeamCalculatorViewModel).GetField("runningRevitDrawing",Private).GetValue(model);
                    if(running!=null)File.WriteAllText(Path.Combine(output,"scenario"+number+".running.txt"),running.BuildDiagnosticReport("Independent replay live child"));
                    next+=10;
                }
            }
            solve.GetAwaiter().GetResult();double searchSeconds=wall.Elapsed.TotalSeconds;
            try {ValidateResult(model,fixture,mode);}catch(Exception error){validationFailure=error.ToString();failed=true;}
            wall.Stop();
            double evidenceSeconds=0;string evidenceFailure=null;var evidenceClock=Stopwatch.StartNew();
            try {evidenceSeconds=ReportedScenarioEvidence.Write(Path.Combine(output,"scenario"+number),number,mode,model,ActualParts(model),"FullCombinedScenario",wall.Elapsed.TotalSeconds,validationFailure);}
            catch(Exception error){evidenceFailure=error.ToString();failed=true;}
            finally {evidenceSeconds=evidenceClock.Elapsed.TotalSeconds;}
            File.WriteAllText(Path.Combine(output,"scenario"+number+".diagnostics.txt"),model.BuildDiagnosticReport("Independent full combined replay"));
            bool meetsTarget=validationFailure==null&&wall.Elapsed.TotalSeconds<=30;
            var result=new {Scenario=number,fixture.SourceSha256,EngineIdentity=BeamWire.EngineIdentity,Scope="FullCombinedScenario",Mode=mode,Runtime=Environment.Version.ToString(),Processors=Environment.ProcessorCount,SetupSeconds=setup.Elapsed.TotalSeconds,SearchSeconds=searchSeconds,ValidationSeconds=wall.Elapsed.TotalSeconds-searchSeconds,TotalSeconds=wall.Elapsed.TotalSeconds,EvidenceWriteAndReadValidationSeconds=evidenceSeconds,EvidenceFailure=evidenceFailure,TargetSeconds=30,MeetsTarget=meetsTarget,Valid=validationFailure==null,ValidationFailure=validationFailure,model.IsCalculationValid,model.WarningMessage,model.TowerFailureDetails,model.CombinedLegCount,model.CombinedH20Count,Parts=ActualParts(model).Select(PartSnapshot).ToArray()};
            File.WriteAllText(Path.Combine(output,"scenario"+number+".json"),JsonSerializer.Serialize(result,Json));
            Console.WriteLine("RESULT scenario="+number+" totalSeconds="+wall.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" valid="+(validationFailure==null)+" target="+(validationFailure==null&&wall.Elapsed.TotalSeconds<=30));
            if(validationFailure!=null)Console.WriteLine(validationFailure);
            if(args.Contains("--require-target")&&!meetsTarget)failed=true;
        }
        if(failed)throw new Exception("One or more complete reported scenarios failed; see JSON and diagnostics.");
    }
}
