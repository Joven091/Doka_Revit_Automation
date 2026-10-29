using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace EngineeringDashboard
{
    public sealed class RevitDrawingDefinition
    {
        public string Key {get;set;}
        public string Label {get;set;}
        public double LengthMm {get;set;}
        public string ConfigurationXml {get;set;}
    }
    public partial class BeamCalculatorViewModel
    {
        internal RevitDrawingDefinition[] RevitDrawings {get;private set;}=new RevitDrawingDefinition[0];
        internal string ActiveRevitDrawingKey {get;private set;}="";
        private Dictionary<string,BeamCalculatorViewModel> revitDrawingModels=new Dictionary<string,BeamCalculatorViewModel>();
        private BeamCalculatorViewModel runningRevitDrawing;
        private string combinedSearchProgress="";
        private string combinedCoordinationDiagnostics="";
        internal bool HasCombinedRevitDrawing=>RevitDrawings.Length>1;
        internal string CombinedRevitSummary=>!HasCombinedRevitDrawing ? "" : RevitDrawings.Length+" drawing parts · "+CombinedLegCount+" tower legs · "+CombinedH20Count+" H20s"+(CombinedWS10Count>0 ? " · "+CombinedWS10Count+" WS10s" : "");
        internal int CombinedLegCount=>RevitSceneModels().Sum(p=>(p.Item2.NetworkResult?.LegCount??p.Item2.TowerResult?.LegCount??0)+p.Item2.IndependentBeamResults.Sum(b=>b.LegCount));
        private static int LayoutH20s(TowerLayout layout)=>layout==null ? 0 : layout.StockPlan.TotalH20Count+(layout.Transfer?.AdditionalH20Count??0);
        internal int CombinedH20Count=>RevitSceneModels().Sum(p=>(p.Item2.NetworkResult?.H20Count??LayoutH20s(p.Item2.TowerResult))+p.Item2.IndependentBeamResults.Sum(LayoutH20s));
        internal int CombinedWS10Count=>RevitSceneModels().Sum(p=>(p.Item2.NetworkResult?.WS10Count??p.Item2.TowerResult?.StockPlan.WS10Count??0)+p.Item2.IndependentBeamResults.Sum(b=>b.StockPlan.WS10Count));
        internal static string SerializeNetwork(TransferNetworkConfiguration configuration)
        {using(var text=new StringWriter(CultureInfo.InvariantCulture)){new XmlSerializer(typeof(TransferNetworkConfiguration)).Serialize(text,configuration);return text.ToString();}}
        internal static TransferNetworkConfiguration ReadNetwork(string xml)
        {
            using(var text=new StringReader(xml??""))using(var reader=XmlReader.Create(text,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1500000}))
                return (TransferNetworkConfiguration)new XmlSerializer(typeof(TransferNetworkConfiguration)).Deserialize(reader);
        }
        private TransferNetworkConfiguration CurrentNetwork(bool combined)
        {
            var config=new TransferNetworkConfiguration {SupportFormatVersion=NetworkBeams.Any(b=>b.SpanLengthMm.HasValue || b.SpanStartMm!=0) || NetworkBays.Any(b=>b.Outline!=null) ? 3 : 2,
                PlanOriginYmm=PlanOriginYmm,Beams=NetworkBeams.Count<=1 && !IsNetworkMode ? new[] {StandaloneDefinition()} : NetworkBeams.ToArray(),Bays=NetworkBays.ToArray(),
                CrossingBeams=CrossingBeams.ToArray(),IndependentBeams=IndependentBeams.ToArray(),RevitContext=RevitContext};
            if(combined && HasCombinedRevitDrawing)
            {
                CaptureActiveRevitDrawing(config);config.SupportFormatVersion=4;config.RevitDrawings=RevitDrawings;config.ActiveRevitDrawingKey=ActiveRevitDrawingKey;
            }
            return config;
        }
        private void CaptureActiveRevitDrawing(TransferNetworkConfiguration configuration=null)
        {
            if(!HasCombinedRevitDrawing)return;
            var active=RevitDrawings.Single(d=>d.Key==ActiveRevitDrawingKey);
            string xml=SerializeNetwork(configuration??CurrentNetwork(false));
            if(active.ConfigurationXml==xml && active.LengthMm==BeamLengthMm)return;
            active.ConfigurationXml=xml;active.LengthMm=BeamLengthMm;
            // Capturing a diagnostic/undo snapshot must also work while numeric
            // inputs are invalid. Validate when switching or calculating instead.
        }
        private static Dictionary<string,BeamCalculatorViewModel> ValidateRevitDrawings(TransferNetworkConfiguration configuration)
        {
            var drawings=configuration.RevitDrawings??new RevitDrawingDefinition[0];var models=new Dictionary<string,BeamCalculatorViewModel>();
            if(drawings.Length==0)
            {if(configuration.SupportFormatVersion==4)throw new ArgumentException("The combined drawing is missing its areas.");return models;}
            if(configuration.SupportFormatVersion!=4 || drawings.Length<2 || drawings.Any(d=>d==null || string.IsNullOrWhiteSpace(d.Key)
                || d.Key.Length>300 || (d.Label?.Length??0)>256 || !TowerBeamAnalysis.Finite(d.LengthMm) || d.LengthMm<=0 || d.LengthMm>50000
                || d.ConfigurationXml==null || d.ConfigurationXml.Length>150000) || drawings.Select(d=>d.Key).Distinct().Count()!=drawings.Length
                || !drawings.Any(d=>d.Key==configuration.ActiveRevitDrawingKey))throw new ArgumentException("Invalid combined Revit drawing.");
            foreach(var drawing in drawings)
            {
                var inner=ReadNetwork(drawing.ConfigurationXml);
                if(inner==null || inner.SupportFormatVersion>3 || (inner.RevitDrawings?.Length??0)!=0 || inner.RevitContext==null)
                    throw new ArgumentException("Combined drawing areas cannot contain nested drawings or omit their Revit placement.");
                models.Add(drawing.Key,DrawingModel(drawing));
            }
            return models;
        }
        private static BeamCalculatorViewModel DrawingModel(RevitDrawingDefinition drawing)
        {
            var model=new BeamCalculatorViewModel {UseTowers=true,BeamLengthMm=drawing.LengthMm};
            try {model.NetworkConfigurationXml=drawing.ConfigurationXml;}
            catch(ArgumentException ex){throw new ArgumentException(drawing.Label+": "+ex.Message,ex);}
            model.UseBeamNetwork=model.NetworkBeams.Count>1;model.TransferSlabLoads=model.UseBeamNetwork;
            return model;
        }
        internal Tuple<RevitDrawingDefinition,BeamCalculatorViewModel>[] RevitSceneModels()
        {
            return RevitDrawings.Select(d=>Tuple.Create(d,d.Key==ActiveRevitDrawingKey ? this : revitDrawingModels[d.Key])).ToArray();
        }
        internal void ActivateRevitDrawing(string key)
        {
            if(!HasCombinedRevitDrawing || key==ActiveRevitDrawingKey || IsOptimizing)return;
            CaptureActiveRevitDrawing();var drawings=RevitDrawings;var models=revitDrawingModels;
            var previous=drawings.Single(d=>d.Key==ActiveRevitDrawingKey);
            if(models[previous.Key].NetworkConfigurationXml!=previous.ConfigurationXml || models[previous.Key].BeamLengthMm!=previous.LengthMm)
                models[previous.Key]=DrawingModel(previous);
            else if(IsCalculationValid)models[previous.Key].CopyRevitResult(this);
            var definition=drawings.Single(d=>d.Key==key);var target=models[key];bool valid=IsCalculationValid;
            BeamLengthMm=definition.LengthMm;NetworkConfigurationXml=definition.ConfigurationXml;
            useBeamNetwork=NetworkBeams.Count>1;transferSlabLoads=useBeamNetwork;
            RevitDrawings=drawings;revitDrawingModels=models;ActiveRevitDrawingKey=key;
            if(valid)CopyRevitResult(target);NotifyNetwork();NotifyTower();OnPropertyChanged(null);
        }
        private void CopyRevitResult(BeamCalculatorViewModel model)
        {
            TowerResult=model.TowerResult;NetworkResult=model.NetworkResult;IndependentBeamResults=model.IndependentBeamResults;
            LeftLineLoad=model.LeftLineLoad;RightLineLoad=model.RightLineLoad;TotalLineLoad=model.TotalLineLoad;LimitingFactor=model.LimitingFactor;
            IsCalculationValid=model.IsCalculationValid;WarningMessage=model.WarningMessage;TowerFailureDetails=model.TowerFailureDetails;
        }
        private void CopyProjectInputs(BeamCalculatorViewModel target)
        {
            const string names="FloorHeight,TowerLegCapacityKn,PrimaryMaterial,StockLengthsText,StockEndSetbackMm,MaximumConcreteEndOverhangMm,AllowCustomH20Lengths,CustomH20Scope,IgnoreUplift,Exclude600Standalone,DeflectionLimit,PlywoodDeflectionLimit,PlywoodProfileId,UseCustomLoads,CustomLiveLoad,CustomFormworkLoad";
            foreach(string name in names.Split(',')){var property=typeof(BeamCalculatorViewModel).GetProperty(name);property.SetValue(target,property.GetValue(this));}
            target.OptimizeCrossingJunctions=OptimizeCrossingJunctions;target.PrecheckCrossingFootprints=PrecheckCrossingFootprints;
            target.TowerSearchTimeLimit=TowerSearchTimeLimit;target.ReplayConstructionStages=ReplayConstructionStages;
        }
        private async Task OptimizeCombinedRevitAsync(bool useTimeLimit)
        {
            var definitions=RevitDrawings;int revision=towerRevision;
            Dictionary<string,BeamCalculatorViewModel> models=null;
            BeamSolverSession solverSession=null;bool ownsSolverSession=SharedSolverSession==null;
            var cancellation=towerCancellation=new CancellationTokenSource();
            var trace=beamSearchTrace=new BeamSearchTrace(CaptureDiagnosticInputs(),cancellation.Token,null);
            IsOptimizing=true;IsCalculationValid=false;TowerResult=null;NetworkResult=null;TowerFailureDetails="";WarningMessage="Searching all imported areas…";NotifyTower();
            try
            {
                combinedCoordinationDiagnostics="";CaptureActiveRevitDrawing();models=definitions.ToDictionary(d=>d.Key,d=> {var model=DrawingModel(d);CopyProjectInputs(model);return model;});
                solverSession=SharedSolverSession??new BeamSolverSession();
                foreach(var definition in definitions)
                {
                    cancellation.Token.ThrowIfCancellationRequested();runningRevitDrawing=models[definition.Key];
                    if(revision!=towerRevision){trace.Complete("Discarded: inputs changed during search");return;}
                    combinedSearchProgress=definition.Label+" ("+(Array.IndexOf(definitions,definition)+1)+"/"+definitions.Length+")";NotifyTower();
                    runningRevitDrawing.SharedSolverSession=solverSession;
                    try {await runningRevitDrawing.OptimizeTowersAsync(useTimeLimit);}
                    finally {runningRevitDrawing.SharedSolverSession=null;}
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                if(revision!=towerRevision)return;
                revitDrawingModels=models;var failures=definitions.Where(d=>!models[d.Key].IsCalculationValid).ToArray();
                if(failures.Length>0)
                {
                    TowerFailureDetails=string.Join("\n\n",failures.Select(d=>d.Label+": "+models[d.Key].WarningMessage+"\n"+models[d.Key].TowerFailureDetails));
                    WarningMessage=failures.Length+" imported areas have no complete solution. Open failure details or Diagnose.";trace.Complete("No solution",TowerFailureDetails);
                }
                else
                {
                    await CoordinateRevitPartsAsync(models,solverSession,useTimeLimit,revision,cancellation.Token);
                    if(revision!=towerRevision)return;
                    string clash=CombinedRevitClash();
                    if(clash!=null){TowerFailureDetails=clash;WarningMessage="Combined drawing has a formwork clash. Open failure details.";trace.Complete("No coordinated solution",clash);}
                    else {CopyRevitResult(models[ActiveRevitDrawingKey]);trace.Complete("Succeeded",summary:CombinedRevitSummary);}
                }
            }
            catch(OperationCanceledException) {WarningMessage="Combined drawing calculation cancelled.";trace.Complete("Cancelled");}
            catch(Exception ex) {WarningMessage="Combined drawing calculation failed. Select Diagnose.";TowerFailureDetails=ex.ToString();trace.Complete("Unexpected error",ex.ToString());}
            finally {if(ownsSolverSession && solverSession!=null)await Task.Run(()=>solverSession.Dispose());runningRevitDrawing=null;combinedSearchProgress="";IsOptimizing=false;towerCancellation=null;cancellation.Dispose();trace.Finish();lastCompletedFullSearch=trace;NotifyTower();}
        }
        internal string CombinedRevitDiagnostics()=>!HasCombinedRevitDrawing ? "" : "\n--- Combined Revit drawing calculations ---\n"+string.Join("\n",RevitDrawings.Select(d=>
            d.Label+" | "+d.Key+"\n"+revitDrawingModels[d.Key].BuildDiagnosticReport("Combined drawing component",d.Label)))+"\n--- Combined support coordination ---\n"+combinedCoordinationDiagnostics;
        private sealed class RevitPartGeometry
        {
            internal RevitDrawingDefinition Definition;
            internal BeamCalculatorViewModel Model;
            internal FormworkSolid[] Solids,Concrete;
        }
        private RevitPartGeometry[] CombinedPartGeometry()
        {
            var frame=revitDrawingModels[RevitDrawings[0].Key].RevitContext;
            return RevitDrawings.Select(definition=>
            {
                var model=definition.Key==ActiveRevitDrawingKey && IsCalculationValid ? this : revitDrawingModels[definition.Key];
                var concrete=new List<FormworkSolid>();var solids=new List<FormworkSolid>();
                if(model.NetworkResult!=null)
                {
                    concrete.AddRange(CrossBeamSupportOptimizer.Concrete(model.NetworkResult));
                    solids.AddRange(CrossBeamSupportOptimizer.NetworkSolids(model.NetworkResult));solids.AddRange(model.NetworkResult.CrossSupports.SelectMany(c=>c.Solids));
                }
                else
                {
                    double centre=model.PlanOriginYmm,bottom=model.NetworkBeams[0].BottomElevationMm;
                    concrete.Add(new FormworkSolid {Name="beam concrete",X0=model.BeamSpanStart(0),X1=model.BeamSpanEnd(0),Y0=centre-model.Width/2,Y1=centre+model.Width/2,Z0=bottom,Z1=bottom+model.Depth});
                    solids.AddRange(CrossBeamSupportOptimizer.LayoutSolids(model.TowerResult,centre,bottom));
                }
                double origin=model.NetworkResult!=null ? model.PlanOriginYmm : 0;
                Func<FormworkSolid,FormworkSolid> place=solid=>MapRevitSolid(solid,model.RevitContext,origin,frame,0,definition.Label);
                return new RevitPartGeometry {Definition=definition,Model=model,Solids=solids.Select(place).ToArray(),Concrete=concrete.Select(place).ToArray()};
            }).ToArray();
        }
        private static FormworkSolid MapRevitSolid(FormworkSolid solid,RevitImportContext from,double fromY,RevitImportContext to,double toY,string label=null)
        {
            var points=new List<ImportPoint>();
            foreach(double x in new[] {solid.X0,solid.X1})foreach(double rawY in new[] {solid.Y0,solid.Y1})
            {
                double y=rawY+fromY,wx=from.OriginXmm+x*from.DirectionX-y*from.DirectionY,wy=from.OriginYmm+x*from.DirectionY+y*from.DirectionX;
                points.Add(new ImportPoint((wx-to.OriginXmm)*to.DirectionX+(wy-to.OriginYmm)*to.DirectionY,-(wx-to.OriginXmm)*to.DirectionY+(wy-to.OriginYmm)*to.DirectionX-toY));
            }
            return new FormworkSolid {Name=label==null ? solid.Name : label+" / "+solid.Name,TowerOwner=label==null ? solid.TowerOwner : label+" / "+solid.TowerOwner,
                IsLeg=solid.IsLeg,IsTowerEnvelope=solid.IsTowerEnvelope,IsSideShutter=solid.IsSideShutter,X0=points.Min(p=>p.X),X1=points.Max(p=>p.X),Y0=points.Min(p=>p.Y),Y1=points.Max(p=>p.Y),
                Z0=solid.Z0+from.TopElevationMm-to.TopElevationMm,Z1=solid.Z1+from.TopElevationMm-to.TopElevationMm};
        }
        private static Tuple<int,int,string> FirstCombinedClash(RevitPartGeometry[] parts)
        {
            var previous=new List<FormworkSolid>();var previousConcrete=new List<FormworkSolid>();var owners=new Dictionary<FormworkSolid,int>();
            for(int i=0;i<parts.Length;i++)
            {
                var part=parts[i];var obstacles=new FormworkObstacleIndex(previous.Concat(previousConcrete).ToArray());
                foreach(var solid in part.Solids){var hit=obstacles.FirstConflict(solid);if(hit!=null)return Tuple.Create(i,owners[hit],solid.Name+" clashes with "+hit.Name+".");}
                var old=new FormworkObstacleIndex(previous.ToArray());
                foreach(var block in part.Concrete){var hit=old.FirstConflict(block);if(hit!=null)return Tuple.Create(i,owners[hit],hit.Name+" clashes with "+block.Name+".");}
                foreach(var solid in part.Solids.Concat(part.Concrete))owners[solid]=i;
                previous.AddRange(part.Solids);previousConcrete.AddRange(part.Concrete);
            }
            return null;
        }
        internal string CombinedRevitClash()
        {
            if(!HasCombinedRevitDrawing || RevitDrawings.Any(d=>!(d.Key==ActiveRevitDrawingKey && IsCalculationValid ? this : revitDrawingModels[d.Key]).IsCalculationValid))return null;
            return FirstCombinedClash(CombinedPartGeometry())?.Item3;
        }
        private async Task CoordinateRevitPartsAsync(Dictionary<string,BeamCalculatorViewModel> models,BeamSolverSession session,bool useTimeLimit,int revision,CancellationToken cancellation)
        {
            var repaired=new HashSet<string>();
            while(true)
            {
                cancellation.ThrowIfCancellationRequested();var parts=CombinedPartGeometry();var clash=FirstCombinedClash(parts);
                if(clash==null)
                {
                    if(repaired.Count==0)combinedCoordinationDiagnostics="No inter-part clashes; no retries needed.";
                    else combinedCoordinationDiagnostics+="All physical inter-part checks pass. Retained "+(parts.Length-repaired.Count)+" original part solutions.\n";
                    return;
                }
                combinedCoordinationDiagnostics+="Detected: "+clash.Item3+"\n";
                // Try the cheaper isolated beam first. Each accepted replacement clears
                // every fixed neighbor, so repairs cannot introduce a new collision.
                var order=new[] {clash.Item1,clash.Item2}.OrderBy(i=>parts[i].Model.NetworkResult==null ? 0 : 1).ThenBy(i=>i).ToArray();
                bool accepted=false;
                foreach(int index in order)
                {
                    var part=parts[index];if(repaired.Contains(part.Definition.Key))continue;
                    var candidate=DrawingModel(part.Definition);CopyProjectInputs(candidate);
                    var frame=parts[0].Model.RevitContext;
                    candidate.combinedObstacles=parts.Where((p,i)=>i!=index).SelectMany(p=>p.Solids.Concat(p.Concrete))
                        .Select(s=>MapRevitSolid(s,frame,0,candidate.RevitContext,candidate.IsNetworkMode ? candidate.PlanOriginYmm : 0)).ToArray();
                    var clock=System.Diagnostics.Stopwatch.StartNew();
                    runningRevitDrawing=candidate;combinedSearchProgress="Coordinating "+part.Definition.Label+"; retaining other parts";NotifyTower();
                    candidate.SharedSolverSession=session;
                    try {await candidate.OptimizeTowersAsync(useTimeLimit);}
                    finally {candidate.SharedSolverSession=null;candidate.combinedObstacles=Array.Empty<FormworkSolid>();}
                    cancellation.ThrowIfCancellationRequested();if(revision!=towerRevision)return;
                    clock.Stop();combinedCoordinationDiagnostics+=part.Definition.Label+": retry "+clock.Elapsed.TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture)+" s; ";
                    if(!candidate.IsCalculationValid)
                    {combinedCoordinationDiagnostics+="no checked alternative in this search. "+candidate.WarningMessage+"\n";continue;}
                    models[part.Definition.Key]=candidate;
                    // Independent final geometry check includes concrete in both directions.
                    var replacement=CombinedPartGeometry()[index];
                    bool clear=parts.Where((p,i)=>i!=index).All(p=>
                        !replacement.Solids.Any(s=>p.Solids.Concat(p.Concrete).Any(s.Conflicts)) && !replacement.Concrete.Any(s=>p.Solids.Any(s.Conflicts)));
                    if(!clear){models[part.Definition.Key]=part.Model;combinedCoordinationDiagnostics+="rejected by final inter-part checks.\n";continue;}
                    repaired.Add(part.Definition.Key);combinedCoordinationDiagnostics+="accepted; members, reactions and physical clearance rechecked.\n";accepted=true;break;
                }
                if(!accepted){combinedCoordinationDiagnostics+="No coordinated alternative found for this pair in the bounded search; this is not proof that all arrangements are impossible.\n";return;}
            }
        }
        internal void ValidateCombinedRevitResult()
        {
            if(!HasCombinedRevitDrawing || !IsCalculationValid)return;
            string clash=CombinedRevitClash();
            if(clash==null)revitDrawingModels[ActiveRevitDrawingKey].CopyRevitResult(this);
            else {IsCalculationValid=false;TowerFailureDetails=clash;WarningMessage="The selected alternative clashes with another drawing part. Recalculate or choose a different arrangement.";NotifyTower();}
        }
    }
}
