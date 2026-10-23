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
            if(configuration.SupportFormatVersion!=4 || drawings.Length<2 || drawings.Length>45 || drawings.Any(d=>d==null || string.IsNullOrWhiteSpace(d.Key)
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
            var cancellation=towerCancellation=new CancellationTokenSource();
            var trace=beamSearchTrace=new BeamSearchTrace(CaptureDiagnosticInputs(),cancellation.Token,null);
            IsOptimizing=true;IsCalculationValid=false;TowerResult=null;NetworkResult=null;TowerFailureDetails="";WarningMessage="Searching all imported areas…";NotifyTower();
            try
            {
                CaptureActiveRevitDrawing();models=definitions.ToDictionary(d=>d.Key,d=> {var model=DrawingModel(d);CopyProjectInputs(model);return model;});
                foreach(var definition in definitions)
                {
                    cancellation.Token.ThrowIfCancellationRequested();runningRevitDrawing=models[definition.Key];
                    if(revision!=towerRevision){trace.Complete("Discarded: inputs changed during search");return;}
                    combinedSearchProgress=definition.Label+" ("+(Array.IndexOf(definitions,definition)+1)+"/"+definitions.Length+")";NotifyTower();
                    await runningRevitDrawing.OptimizeTowersAsync(useTimeLimit);
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
                    string clash=CombinedRevitClash();
                    if(clash!=null){TowerFailureDetails=clash;WarningMessage="Combined drawing has a formwork clash. Open failure details.";trace.Complete("No coordinated solution",clash);}
                    else {CopyRevitResult(models[ActiveRevitDrawingKey]);trace.Complete("Succeeded",summary:CombinedRevitSummary);}
                }
            }
            catch(OperationCanceledException) {WarningMessage="Combined drawing calculation cancelled.";trace.Complete("Cancelled");}
            catch(Exception ex) {WarningMessage="Combined drawing calculation failed. Select Diagnose.";TowerFailureDetails=ex.ToString();trace.Complete("Unexpected error",ex.ToString());}
            finally {runningRevitDrawing=null;combinedSearchProgress="";IsOptimizing=false;towerCancellation=null;cancellation.Dispose();trace.Finish();lastCompletedFullSearch=trace;NotifyTower();}
        }
        internal string CombinedRevitDiagnostics()=>!HasCombinedRevitDrawing ? "" : "\n--- Combined Revit drawing calculations ---\n"+string.Join("\n",RevitDrawings.Select(d=>
            d.Label+" | "+d.Key+"\n"+revitDrawingModels[d.Key].BuildDiagnosticReport("Combined drawing component",d.Label)));
        internal string CombinedRevitClash()
        {
            if(!HasCombinedRevitDrawing)return null;
            var frame=revitDrawingModels[RevitDrawings[0].Key].RevitContext;
            var previous=new List<FormworkSolid>();var previousConcrete=new List<FormworkSolid>();
            foreach(var definition in RevitDrawings)
            {
                var model=revitDrawingModels[definition.Key];if(!model.IsCalculationValid)return null;
                var concrete=new List<FormworkSolid>();var solids=new List<FormworkSolid>();
                if(model.NetworkResult!=null)
                {
                    concrete.AddRange(CrossBeamSupportOptimizer.Concrete(model.NetworkResult));
                    solids.AddRange(CrossBeamSupportOptimizer.NetworkSolids(model.NetworkResult));solids.AddRange(model.NetworkResult.CrossSupports.SelectMany(c=>c.Solids));
                }
                else
                {
                    var layout=model.TowerResult;double centre=model.PlanOriginYmm,bottom=model.NetworkBeams[0].BottomElevationMm;
                    concrete.Add(new FormworkSolid {Name="beam concrete",X0=model.BeamSpanStart(0),X1=model.BeamSpanEnd(0),Y0=centre-model.Width/2,Y1=centre+model.Width/2,Z0=bottom,Z1=bottom+model.Depth});
                    foreach(var piece in layout.StockPlan.Pieces)foreach(int side in new[] {-1,1})solids.AddRange(CrossBeamSupportOptimizer.PrimarySolids("primary",piece.StartMm,centre+layout.TransverseOffsetMm+side*layout.WidthMm/2,piece.StockLengthMm,true,bottom-200,layout.Primary));
                    foreach(double station in layout.PositionsMm)foreach(int side in new[] {-1,1})solids.Add(new FormworkSolid {Name="tower leg",TowerOwner="B1",IsLeg=true,X0=station-40,X1=station+40,Y0=centre+layout.TransverseOffsetMm+side*layout.WidthMm/2-40,Y1=centre+layout.TransverseOffsetMm+side*layout.WidthMm/2+40,Z0=-1e6,Z1=bottom-200-layout.Primary.DepthMm});
                    solids.AddRange(CrossBeamSupportOptimizer.TowerEnvelopes(layout,"B1",0,centre+layout.TransverseOffsetMm,true,bottom-200-layout.Primary.DepthMm));
                }
                var mapped=solids.Select(s=>Place(s,model,definition.Label)).ToArray();var blocks=concrete.Select(s=>Place(s,model,definition.Label)).ToArray();
                var obstacles=new FormworkObstacleIndex(previous.Concat(previousConcrete).ToArray());
                foreach(var solid in mapped){var hit=obstacles.FirstConflict(solid);if(hit!=null)return solid.Name+" clashes with "+hit.Name+".";}
                var old=new FormworkObstacleIndex(previous.ToArray());foreach(var block in blocks){var hit=old.FirstConflict(block);if(hit!=null)return hit.Name+" clashes with "+block.Name+".";}
                previous.AddRange(mapped);previousConcrete.AddRange(blocks);
            }
            return null;
            FormworkSolid Place(FormworkSolid solid,BeamCalculatorViewModel model,string label)
            {
                var context=model.RevitContext;var points=new List<ImportPoint>();
                foreach(double x in new[] {solid.X0,solid.X1})foreach(double rawY in new[] {solid.Y0,solid.Y1})
                {
                    double y=rawY+(model.NetworkResult!=null ? model.PlanOriginYmm : 0),wx=context.OriginXmm+x*context.DirectionX-y*context.DirectionY,wy=context.OriginYmm+x*context.DirectionY+y*context.DirectionX;
                    points.Add(new ImportPoint((wx-frame.OriginXmm)*frame.DirectionX+(wy-frame.OriginYmm)*frame.DirectionY,-(wx-frame.OriginXmm)*frame.DirectionY+(wy-frame.OriginYmm)*frame.DirectionX));
                }
                return new FormworkSolid {Name=label+" / "+solid.Name,TowerOwner=label+" / "+solid.TowerOwner,IsLeg=solid.IsLeg,IsTowerEnvelope=solid.IsTowerEnvelope,IsSideShutter=solid.IsSideShutter,
                    X0=points.Min(p=>p.X),X1=points.Max(p=>p.X),Y0=points.Min(p=>p.Y),Y1=points.Max(p=>p.Y),Z0=solid.Z0+context.TopElevationMm-frame.TopElevationMm,Z1=solid.Z1+context.TopElevationMm-frame.TopElevationMm};
            }
        }
    }
}
