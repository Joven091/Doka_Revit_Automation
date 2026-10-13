using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace EngineeringDashboard
{
    public sealed class TransferNetworkConfiguration
    {
        public int SupportFormatVersion {get;set;}
        public double PlanOriginYmm {get;set;}
        public TransferBeamRow[] Beams {get;set;}
        public TransferBayRow[] Bays {get;set;}
        public TransferCrossingBeamRow[] CrossingBeams {get;set;}
    }
    public partial class BeamCalculatorViewModel
    {
        public ObservableCollection<TransferBeamRow> NetworkBeams {get;}=new ObservableCollection<TransferBeamRow>();
        public ObservableCollection<TransferBayRow> NetworkBays {get;}=new ObservableCollection<TransferBayRow>();
        public ObservableCollection<TransferCrossingBeamRow> CrossingBeams {get;}=new ObservableCollection<TransferCrossingBeamRow>();
        private bool useBeamNetwork;
        private double planOriginYmm;
        public double PlanOriginYmm {get=>planOriginYmm;set {if(!TowerBeamAnalysis.Finite(value) || Math.Abs(value)>1000000)throw new ArgumentException("Invalid drawing origin.");if(planOriginYmm==value)return;planOriginYmm=value;OnPropertyChanged(nameof(PlanOriginYmm));}}
        internal double[] GeometryCentres()
        {
            if(!IsNetworkMode)return new[] {PlanOriginYmm};
            var ys=new double[NetworkBeams.Count];ys[0]=PlanOriginYmm;
            for(int i=1;i<ys.Length;i++)ys[i]=ys[i-1]+NetworkBeams[i-1].WidthMm/2+NetworkBays[i-1].WidthMm+NetworkBeams[i].WidthMm/2;
            return ys;
        }
        internal bool TryMoveParallelBeam(int index,double offset)
        {
            if(!TowerBeamAnalysis.Finite(offset) || Math.Abs(offset)<.001)return false;
            if(!IsNetworkMode) {if(index!=0 || Math.Abs(PlanOriginYmm+offset)>1000000)return false;PlanOriginYmm+=offset;return true;}
            if(index<0 || index>=NetworkBeams.Count)return false;
            double before=index>0 ? NetworkBays[index-1].WidthMm+offset : 0,after=index<NetworkBays.Count ? NetworkBays[index].WidthMm-offset : 0;
            if(index>0 && (before<500 || before>20000) || index<NetworkBays.Count && (after<500 || after>20000) || index==0 && Math.Abs(PlanOriginYmm+offset)>1000000)return false;
            if(index>0)NetworkBays[index-1].WidthMm=before;else PlanOriginYmm+=offset;
            if(index<NetworkBays.Count)NetworkBays[index].WidthMm=after;
            return true;
        }
        internal bool TryInsertParallelBeam(TransferBeamRow source,double centre,out int index)
        {
            index=-1;if(source==null || !TowerBeamAnalysis.Finite(source.WidthMm) || source.WidthMm<=0 || source.WidthMm>10000 || !TowerBeamAnalysis.Finite(source.DepthMm) || source.DepthMm<=0 || !TowerBeamAnalysis.Finite(source.BottomElevationMm) || !TowerBeamAnalysis.Finite(centre) || Math.Abs(centre)>1000000 || !CanAddNetworkBay)return false;
            var ys=GeometryCentres();int at=Array.FindIndex(ys,y=>y>=centre);if(at<0)at=ys.Length;
            var rows=IsNetworkMode ? NetworkBeams.ToArray() : new[] {StandaloneDefinition()};
            double before=at>0 ? centre-ys[at-1]-(rows[at-1].WidthMm+source.WidthMm)/2 : 0;
            double after=at<ys.Length ? ys[at]-centre-(rows[at].WidthMm+source.WidthMm)/2 : 0;
            if(at>0 && (before<500 || before>20000) || at<ys.Length && (after<500 || after>20000))return false;
            if(!IsNetworkMode)
            {
                UnsubscribeNetwork();NetworkBeams.Clear();NetworkBays.Clear();CrossingBeams.Clear();NetworkBeams.Add(rows[0]);SubscribeNetwork();
            }
            InsertNetworkBay(at==ys.Length ? at-1 : at,at<ys.Length);
            var row=NetworkBeams[at];row.WidthMm=source.WidthMm;row.DepthMm=source.DepthMm;row.BottomElevationMm=source.BottomElevationMm;
            if(at>0)NetworkBays[at-1].WidthMm=before;else PlanOriginYmm=centre;
            if(at<ys.Length)NetworkBays[at].WidthMm=after;
            index=at;return true;
        }
        public bool UseBeamNetwork {get=>useBeamNetwork;set {if(useBeamNetwork==value)return;useBeamNetwork=value;if(value && NetworkBeams.Count==1){InsertNetworkBay(0,false);return;}if(value && NetworkBeams.Count==0)InitializeNetwork();Changed();NotifyNetwork();}}
        public bool IsNetworkMode=>IsTransferMode && UseBeamNetwork;
        public bool ShowSingleTransfer=>IsTransferMode && !UseBeamNetwork;
        public bool ShowSingleGeometry=>!IsNetworkMode;
        public bool CanAddNetworkBay=>NetworkBeams.Count<TransferNetworkInput.MaximumBeamCount;
        public bool CanRemoveNetworkBay=>NetworkBeams.Count>1;
        public TransferNetworkResult NetworkResult {get;private set;}
        public int SelectedNetworkBeam {get;private set;}
        public string NetworkSummary=>NetworkResult?.Summary??"";
        public string NetworkSchedule=>NetworkResult?.Schedule??"";
        public string NetworkSelectedLabel=>IsNetworkMode ? $"Inspecting B{SelectedNetworkBeam+1}" : "";
        public string NetworkConfigurationXml
        {
            get {using(var text=new StringWriter(CultureInfo.InvariantCulture)) {new XmlSerializer(typeof(TransferNetworkConfiguration)).Serialize(text,new TransferNetworkConfiguration {SupportFormatVersion=2,PlanOriginYmm=PlanOriginYmm,Beams=NetworkBeams.Count==1 && !IsNetworkMode ? new[] {StandaloneDefinition()} : NetworkBeams.ToArray(),Bays=NetworkBays.ToArray(),CrossingBeams=CrossingBeams.ToArray()});return text.ToString();}}
            set
            {
                TransferNetworkConfiguration config;
                using(var text=new StringReader(value??""))using(var reader=XmlReader.Create(text,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50000}))
                    config=(TransferNetworkConfiguration)new XmlSerializer(typeof(TransferNetworkConfiguration)).Deserialize(reader);
                if(config?.Beams==null || config.Bays==null || config.Beams.Length<1 || config.Beams.Length>TransferNetworkInput.MaximumBeamCount || config.Bays.Length!=config.Beams.Length-1 || config.Beams.Any(b=>b==null) || config.Bays.Any(b=>b==null))throw new ArgumentException("Invalid saved beam/slab network.");
                if(config.CrossingBeams!=null && (config.CrossingBeams.Length>30 || config.CrossingBeams.Any(c=>c==null)))throw new ArgumentException("Invalid saved crossing beams.");
                if(config.Beams.Length==1 && config.CrossingBeams?.Length>0)throw new ArgumentException("A standalone beam cannot contain slab crossing beams.");
                if(!TowerBeamAnalysis.Finite(config.PlanOriginYmm) || Math.Abs(config.PlanOriginYmm)>1000000)throw new ArgumentException("Invalid saved drawing origin.");
                if(config.SupportFormatVersion<0 || config.SupportFormatVersion>2)throw new ArgumentException("Unsupported cross-beam support format.");
                if(config.SupportFormatVersion<2)foreach(var c in config.CrossingBeams??new TransferCrossingBeamRow[0])c.SupportMode=CrossBeamSupportMode.LegacySlabRows;
                PlanOriginYmm=config.PlanOriginYmm;UnsubscribeNetwork();NetworkBeams.Clear();NetworkBays.Clear();CrossingBeams.Clear();foreach(var c in config.CrossingBeams??new TransferCrossingBeamRow[0])CrossingBeams.Add(c);foreach(var b in config.Beams)NetworkBeams.Add(b);foreach(var b in config.Bays)NetworkBays.Add(b);SubscribeNetwork();if(NetworkBeams.Count==1)SetStandaloneMode();Changed();NotifyNetwork();
            }
        }
        internal TransferBeamRow StandaloneDefinition()
        {
            var old=NetworkBeams.FirstOrDefault();
            return new TransferBeamRow {WidthMm=Width,DepthMm=Depth,BottomElevationMm=(old==null ? 0 : old.BottomElevationMm+old.DepthMm)-Depth};
        }
        private void SetStandaloneMode()
        {
            var beam=NetworkBeams.Single();depth=beam.DepthMm;width=beam.WidthMm;
            useBeamNetwork=false;transferSlabLoads=false;hasLeft=hasRight=false;SelectedNetworkBeam=0;
            OnPropertyChanged(null);NotifyTransfer();NotifyTower();
        }
        internal void StartNewSession()
        {
            if(IsOptimizing)throw new InvalidOperationException("Cancel or finish the current search before starting a new session.");
            // Preserve the chosen support/workflow mode, and reset document inputs in the retained model.
            var fresh=new BeamCalculatorViewModel {UseBeamNetwork=true};
            depth=fresh.depth;width=fresh.width;leftThickness=fresh.leftThickness;rightThickness=fresh.rightThickness;
            floorHeight=fresh.floorHeight;selectedProp=fresh.selectedProp;hasLeft=fresh.hasLeft;hasRight=fresh.hasRight;
            customLoads=fresh.customLoads;customLive=fresh.customLive;customFormwork=fresh.customFormwork;
            numberOfProps=fresh.numberOfProps;deflection=fresh.deflection;selectedPlywood=fresh.selectedPlywood;plywoodDeflectionLimit=fresh.plywoodDeflectionLimit;slabStripWidthMm=fresh.slabStripWidthMm;
            beamLengthMm=fresh.beamLengthMm;towerLegCapacityKn=fresh.towerLegCapacityKn;stockLengthsText=fresh.stockLengthsText;
            stockEndSetbackMm=fresh.stockEndSetbackMm;maximumConcreteEndOverhangMm=fresh.maximumConcreteEndOverhangMm;
            primaryMaterial=fresh.primaryMaterial;exclude600Standalone=fresh.exclude600Standalone;allowCustomH20Lengths=fresh.allowCustomH20Lengths;customH20Scope=fresh.customH20Scope;ignoreUplift=fresh.ignoreUplift;
            leftSlabBayMm=fresh.leftSlabBayMm;rightSlabBayMm=fresh.rightSlabBayMm;
            calculatorTextErrors=false;additionalTextErrors.Clear();textErrors=false;
            beamSearchTrace=null;SelectedNetworkBeam=0;
            NetworkConfigurationXml=fresh.NetworkConfigurationXml;
            OnPropertyChanged(null);NotifyTower();
        }
        private void SubscribeNetwork() {for(int i=0;i<NetworkBeams.Count;i++)NetworkBeams[i].Label="B"+(i+1);for(int i=0;i<NetworkBays.Count;i++)NetworkBays[i].Label=$"S{i+1}";foreach(var b in NetworkBeams)b.PropertyChanged+=NetworkChanged;foreach(var b in NetworkBays)b.PropertyChanged+=NetworkChanged;foreach(var c in CrossingBeams)c.PropertyChanged+=NetworkChanged;}
        private void UnsubscribeNetwork() {foreach(var b in NetworkBeams)b.PropertyChanged-=NetworkChanged;foreach(var b in NetworkBays)b.PropertyChanged-=NetworkChanged;foreach(var c in CrossingBeams)c.PropertyChanged-=NetworkChanged;}
        private void NetworkChanged(object sender,PropertyChangedEventArgs e) {Changed(nameof(NetworkConfigurationXml));NotifyNetwork();}
        private void InitializeNetwork()
        {
            UnsubscribeNetwork();NetworkBeams.Clear();NetworkBays.Clear();CrossingBeams.Clear();NetworkBeams.Add(new TransferBeamRow {DepthMm=Depth,WidthMm=Width});
            if(HasLeftSlab) {NetworkBays.Add(new TransferBayRow {WidthMm=LeftSlabBayMm,ThicknessMm=LeftSlabThickness});NetworkBeams.Add(new TransferBeamRow {DepthMm=Depth,WidthMm=Width});}
            if(HasRightSlab) {NetworkBays.Add(new TransferBayRow {WidthMm=RightSlabBayMm,ThicknessMm=RightSlabThickness});NetworkBeams.Add(new TransferBeamRow {DepthMm=Depth,WidthMm=Width});}
            if(NetworkBeams.Count==1) {NetworkBays.Add(new TransferBayRow());NetworkBeams.Add(new TransferBeamRow {DepthMm=Depth,WidthMm=Width});}
            SubscribeNetwork();NotifyNetwork();
        }
        public void AddNetworkBay()
        {
            if(NetworkBeams.Count==1){InsertNetworkBay(0,false);return;}
            if(NetworkBeams.Count==0)InitializeNetwork();if(!CanAddNetworkBay)return;
            var beam=new TransferBeamRow {Label="B"+(NetworkBeams.Count+1),DepthMm=NetworkBeams.Last().DepthMm,WidthMm=NetworkBeams.Last().WidthMm,BottomElevationMm=NetworkBeams.Last().BottomElevationMm};
            var bay=new TransferBayRow {Label="S"+(NetworkBays.Count+1),WidthMm=NetworkBays.Last().WidthMm,ThicknessMm=NetworkBays.Last().ThicknessMm};
            NetworkBeams.Add(beam);NetworkBays.Add(bay);beam.PropertyChanged+=NetworkChanged;bay.PropertyChanged+=NetworkChanged;Changed();NotifyNetwork();
        }
        public void RemoveNetworkBay()
        {
            if(CanRemoveNetworkBay)RemoveNetworkBeam(NetworkBeams.Count-1);
        }
        public void InsertNetworkBay(int beamIndex,bool before)
        {
            if(!CanAddNetworkBay || beamIndex<0 || beamIndex>=NetworkBeams.Count)return;
            bool standalone=NetworkBeams.Count==1;
            var source=standalone ? StandaloneDefinition() : NetworkBeams[beamIndex];int bayIndex=beamIndex;
            var bay=NetworkBays.Count==0 ? new TransferBayRow {WidthMm=RightSlabBayMm,ThicknessMm=RightSlabThickness} : NetworkBays[Math.Min(bayIndex,NetworkBays.Count-1)];
            UnsubscribeNetwork();if(standalone)NetworkBeams[0]=source;
            NetworkBeams.Insert(before ? beamIndex : beamIndex+1,new TransferBeamRow {WidthMm=source.WidthMm,DepthMm=source.DepthMm,BottomElevationMm=source.BottomElevationMm});
            NetworkBays.Insert(bayIndex,new TransferBayRow {WidthMm=bay.WidthMm,ThicknessMm=bay.ThicknessMm});
            foreach(var c in CrossingBeams)if(c.BayNumber>bayIndex)c.BayNumber++;
            SubscribeNetwork();if(standalone){useBeamNetwork=true;transferSlabLoads=true;OnPropertyChanged(null);NotifyTransfer();}Changed();NotifyNetwork();
        }
        public void RemoveNetworkBeam(int beamIndex)
        {
            if(!CanRemoveNetworkBay || beamIndex<0 || beamIndex>=NetworkBeams.Count)return;
            int bayIndex=Math.Max(0,beamIndex-1);double nextOrigin=beamIndex==0 ? PlanOriginYmm+NetworkBeams[0].WidthMm/2+NetworkBays[0].WidthMm+NetworkBeams[1].WidthMm/2 : PlanOriginYmm;UnsubscribeNetwork();
            foreach(var c in CrossingBeams.Where(c=>c.BayNumber==bayIndex+1).ToArray())CrossingBeams.Remove(c);
            foreach(var c in CrossingBeams)if(c.BayNumber>bayIndex+1)c.BayNumber--;
            NetworkBeams.RemoveAt(beamIndex);NetworkBays.RemoveAt(bayIndex);PlanOriginYmm=nextOrigin;
            SubscribeNetwork();if(NetworkBeams.Count==1)SetStandaloneMode();Changed();NotifyNetwork();
        }
        public void AddCrossingBeam()
        {
            if(NetworkBeams.Count==1 || CrossingBeams.Count>=30)return;
            UseBeamNetwork=true;
            var c=new TransferCrossingBeamRow {PositionMm=BeamLengthMm/2,DepthMm=Math.Max(600,NetworkBays[0].ThicknessMm)};
            CrossingBeams.Add(c);c.PropertyChanged+=NetworkChanged;Changed();NotifyNetwork();
        }
        public void RemoveCrossingBeam(TransferCrossingBeamRow c)
        {
            if(c==null)return;c.PropertyChanged-=NetworkChanged;CrossingBeams.Remove(c);Changed();NotifyNetwork();
        }
        private TransferNetworkInput CreateNetworkInput()
        {
            return new TransferNetworkInput {PlywoodSheetLengthMm=PlywoodSpacing.SheetLengthMm,Beams=NetworkBeams.Select(b=>new TransferBeamRow {WidthMm=b.WidthMm,DepthMm=b.DepthMm,BottomElevationMm=b.BottomElevationMm}).ToArray(),Bays=NetworkBays.Select(b=>new TransferBayRow {WidthMm=b.WidthMm,ThicknessMm=b.ThicknessMm}).ToArray(),CrossingBeams=CrossingBeams.Select(c=>new TransferCrossingBeamRow {BayNumber=c.BayNumber,PositionMm=c.PositionMm,WidthMm=c.WidthMm,DepthMm=c.DepthMm,BottomElevationMm=c.BottomElevationMm,SupportMode=c.SupportMode}).ToArray(),UpperSpacingMm=UpperSupportSpacingMm,CustomLoads=UseCustomLoads,LiveKnM2=CustomLiveLoad,FormworkKnM2=CustomFormworkLoad};
        }
        internal void SelectNetworkBeam(int beam)
        {
            if(NetworkResult==null || beam<0 || beam>=NetworkResult.Beams.Length)return;SelectedNetworkBeam=beam;TowerResult=NetworkResult.Beams[beam];LeftLineLoad=TowerResult.LeftLineLoad;RightLineLoad=TowerResult.RightLineLoad;TotalLineLoad=LeftLineLoad+RightLineLoad;LimitingFactor=TowerResult.Governing;NotifyTower();NotifyNetwork();
        }
        private readonly Dictionary<int,NetworkBeamOption[]> networkBeamOptions=new Dictionary<int,NetworkBeamOption[]>();
        internal async Task<NetworkBeamOption[]> GetNetworkBeamOptionsAsync(int beam)
        {
            var source=NetworkResult;
            if(!IsCalculationValid || source==null || beam<0 || beam>=source.Beams.Length || IsOptimizing)return new NetworkBeamOption[0];
            if(networkBeamOptions.TryGetValue(beam,out var cached) && cached.Any(o=>ReferenceEquals(o.Result,source)))return cached;
            var cancellation=towerCancellation=new CancellationTokenSource();
            var trace=beamSearchTrace=new BeamSearchTrace(CaptureDiagnosticInputs(),cancellation.Token);
            int revision=towerRevision;IsOptimizing=true;WarningMessage=$"Checking alternatives for B{beam+1}…";NotifyTower();
            try
            {
                var input=CreateTowerInput();input.Trace=trace;
                var options=await Task.Run(()=>TransferNetworkOptimizer.BeamOptions(input,source,beam));
                cancellation.Token.ThrowIfCancellationRequested();
                if(revision!=towerRevision || !ReferenceEquals(NetworkResult,source)){trace.Complete("Discarded: inputs or selection changed");return new NetworkBeamOption[0];}
                networkBeamOptions[beam]=options;WarningMessage="";
                trace.Complete("Alternatives checked",details:string.Join("\n\n",options.Where(o=>!o.IsAvailable).Select(o=>o.Label+"\n"+o.RejectionReason)),summary:$"B{beam+1}: {options.Count(o=>o.IsAvailable)} complete-network options.");return options;
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested){WarningMessage="Alternative search cancelled; the selected layout is retained.";trace.Complete("Cancelled");}
            catch(Exception ex){trace.Complete("Alternative search failed",ex.ToString());if(revision==towerRevision)WarningMessage="Could not complete the alternative search. Select Diagnose for details.";}
            finally{IsOptimizing=false;towerCancellation=null;cancellation.Dispose();NotifyTower();}
            return new NetworkBeamOption[0];
        }
        internal bool ApplyNetworkBeamOption(NetworkBeamOption option)
        {
            if(option==null || !option.IsAvailable || IsOptimizing || !IsCalculationValid || !networkBeamOptions.TryGetValue(option.BeamIndex,out var options)
                || !options.Contains(option) || !options.Any(o=>ReferenceEquals(o.Result,NetworkResult)))return false;
            NetworkResult=option.Result;networkBeamOptions.Clear();networkBeamOptions[option.BeamIndex]=options;
            SelectNetworkBeam(option.BeamIndex);WarningMessage="";NotifyTower();NotifyNetwork();return true;
        }
        private void NotifyNetwork()
        {
            foreach(string name in new[] {nameof(IsNetworkMode),nameof(ShowSingleTransfer),nameof(ShowSingleGeometry),nameof(CanAddNetworkBay),nameof(CanRemoveNetworkBay),nameof(NetworkResult),nameof(NetworkSummary),nameof(NetworkSchedule),nameof(NetworkSelectedLabel)})OnPropertyChanged(name);
        }
    }
}

