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
        public bool UseBeamNetwork {get=>useBeamNetwork;set {if(useBeamNetwork==value)return;useBeamNetwork=value;if(value && NetworkBeams.Count==0)InitializeNetwork();Changed();NotifyNetwork();}}
        public bool IsNetworkMode=>IsTransferMode && UseBeamNetwork;
        public bool ShowSingleTransfer=>IsTransferMode && !UseBeamNetwork;
        public bool ShowSingleGeometry=>!IsNetworkMode;
        public bool CanAddNetworkBay=>NetworkBeams.Count<TransferNetworkInput.MaximumBeamCount;
        public bool CanRemoveNetworkBay=>NetworkBeams.Count>2;
        public TransferNetworkResult NetworkResult {get;private set;}
        public int SelectedNetworkBeam {get;private set;}
        public string NetworkSummary=>NetworkResult?.Summary??"";
        public string NetworkSchedule=>NetworkResult?.Schedule??"";
        public string NetworkSelectedLabel=>IsNetworkMode ? $"Inspecting B{SelectedNetworkBeam+1}" : "";
        public string NetworkConfigurationXml
        {
            get {using(var text=new StringWriter(CultureInfo.InvariantCulture)) {new XmlSerializer(typeof(TransferNetworkConfiguration)).Serialize(text,new TransferNetworkConfiguration {SupportFormatVersion=2,Beams=NetworkBeams.ToArray(),Bays=NetworkBays.ToArray(),CrossingBeams=CrossingBeams.ToArray()});return text.ToString();}}
            set
            {
                TransferNetworkConfiguration config;
                using(var text=new StringReader(value??""))using(var reader=XmlReader.Create(text,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50000}))
                    config=(TransferNetworkConfiguration)new XmlSerializer(typeof(TransferNetworkConfiguration)).Deserialize(reader);
                if(config?.Beams==null || config.Bays==null || config.Beams.Length<2 || config.Beams.Length>TransferNetworkInput.MaximumBeamCount || config.Bays.Length!=config.Beams.Length-1 || config.Beams.Any(b=>b==null) || config.Bays.Any(b=>b==null))throw new ArgumentException("Invalid saved beam/slab network.");
                if(config.CrossingBeams!=null && (config.CrossingBeams.Length>30 || config.CrossingBeams.Any(c=>c==null)))throw new ArgumentException("Invalid saved crossing beams.");
                if(config.SupportFormatVersion<0 || config.SupportFormatVersion>2)throw new ArgumentException("Unsupported cross-beam support format.");
                if(config.SupportFormatVersion<2)foreach(var c in config.CrossingBeams??new TransferCrossingBeamRow[0])c.SupportMode=CrossBeamSupportMode.LegacySlabRows;
                UnsubscribeNetwork();NetworkBeams.Clear();NetworkBays.Clear();CrossingBeams.Clear();foreach(var c in config.CrossingBeams??new TransferCrossingBeamRow[0])CrossingBeams.Add(c);foreach(var b in config.Beams)NetworkBeams.Add(b);foreach(var b in config.Bays)NetworkBays.Add(b);SubscribeNetwork();Changed();NotifyNetwork();
            }
        }
        internal void StartNewSession()
        {
            if(IsOptimizing)throw new InvalidOperationException("Cancel or finish the current search before starting a new session.");
            // Preserve the chosen support/workflow mode, and reset document inputs in the retained model.
            var fresh=new BeamCalculatorViewModel {UseBeamNetwork=true};
            depth=fresh.depth;width=fresh.width;leftThickness=fresh.leftThickness;rightThickness=fresh.rightThickness;
            floorHeight=fresh.floorHeight;selectedProp=fresh.selectedProp;hasLeft=fresh.hasLeft;hasRight=fresh.hasRight;
            customLoads=fresh.customLoads;customLive=fresh.customLive;customFormwork=fresh.customFormwork;
            numberOfProps=fresh.numberOfProps;deflection=fresh.deflection;selectedPlywood=fresh.selectedPlywood;slabStripWidthMm=fresh.slabStripWidthMm;
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
            if(NetworkBeams.Count==0)InitializeNetwork();if(!CanAddNetworkBay)return;
            var beam=new TransferBeamRow {Label="B"+(NetworkBeams.Count+1),DepthMm=NetworkBeams.Last().DepthMm,WidthMm=NetworkBeams.Last().WidthMm,BottomElevationMm=NetworkBeams.Last().BottomElevationMm};
            var bay=new TransferBayRow {Label="S"+(NetworkBays.Count+1),WidthMm=NetworkBays.Last().WidthMm,ThicknessMm=NetworkBays.Last().ThicknessMm};
            NetworkBeams.Add(beam);NetworkBays.Add(bay);beam.PropertyChanged+=NetworkChanged;bay.PropertyChanged+=NetworkChanged;Changed();NotifyNetwork();
        }
        public void RemoveNetworkBay()
        {
            if(!CanRemoveNetworkBay)return;NetworkBeams.Last().PropertyChanged-=NetworkChanged;NetworkBays.Last().PropertyChanged-=NetworkChanged;
            NetworkBeams.RemoveAt(NetworkBeams.Count-1);NetworkBays.RemoveAt(NetworkBays.Count-1);
            foreach(var c in CrossingBeams.Where(c=>c.BayNumber>NetworkBays.Count).ToArray())RemoveCrossingBeam(c);
            Changed();NotifyNetwork();
        }
        public void InsertNetworkBay(int beamIndex,bool before)
        {
            if(!CanAddNetworkBay || beamIndex<0 || beamIndex>=NetworkBeams.Count)return;
            var source=NetworkBeams[beamIndex];int bayIndex=beamIndex;
            var bay=NetworkBays[Math.Min(bayIndex,NetworkBays.Count-1)];
            UnsubscribeNetwork();
            NetworkBeams.Insert(before ? beamIndex : beamIndex+1,new TransferBeamRow {WidthMm=source.WidthMm,DepthMm=source.DepthMm,BottomElevationMm=source.BottomElevationMm});
            NetworkBays.Insert(bayIndex,new TransferBayRow {WidthMm=bay.WidthMm,ThicknessMm=bay.ThicknessMm});
            foreach(var c in CrossingBeams)if(c.BayNumber>bayIndex)c.BayNumber++;
            SubscribeNetwork();Changed();NotifyNetwork();
        }
        public void RemoveNetworkBeam(int beamIndex)
        {
            if(!CanRemoveNetworkBay || beamIndex<0 || beamIndex>=NetworkBeams.Count)return;
            int bayIndex=Math.Max(0,beamIndex-1);UnsubscribeNetwork();
            foreach(var c in CrossingBeams.Where(c=>c.BayNumber==bayIndex+1).ToArray())CrossingBeams.Remove(c);
            foreach(var c in CrossingBeams)if(c.BayNumber>bayIndex+1)c.BayNumber--;
            NetworkBeams.RemoveAt(beamIndex);NetworkBays.RemoveAt(bayIndex);
            SubscribeNetwork();Changed();NotifyNetwork();
        }
        public void AddCrossingBeam()
        {
            UseBeamNetwork=true;if(CrossingBeams.Count>=30)return;
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

