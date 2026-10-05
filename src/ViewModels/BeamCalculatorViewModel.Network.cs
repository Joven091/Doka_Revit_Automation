using System;
using System.Collections.ObjectModel;
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
        public bool CanAddNetworkBay=>NetworkBeams.Count<8;
        public bool CanRemoveNetworkBay=>NetworkBeams.Count>2;
        public TransferNetworkResult NetworkResult {get;private set;}
        public int SelectedNetworkBeam {get;private set;}
        public string NetworkSummary=>NetworkResult?.Summary??"";
        public string NetworkSchedule=>NetworkResult?.Schedule??"";
        public string NetworkSelectedLabel=>IsNetworkMode ? $"Inspecting B{SelectedNetworkBeam+1}" : "";
        public string NetworkConfigurationXml
        {
            get {using(var text=new StringWriter(CultureInfo.InvariantCulture)) {new XmlSerializer(typeof(TransferNetworkConfiguration)).Serialize(text,new TransferNetworkConfiguration {Beams=NetworkBeams.ToArray(),Bays=NetworkBays.ToArray(),CrossingBeams=CrossingBeams.ToArray()});return text.ToString();}}
            set
            {
                TransferNetworkConfiguration config;
                using(var text=new StringReader(value??""))using(var reader=XmlReader.Create(text,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50000}))
                    config=(TransferNetworkConfiguration)new XmlSerializer(typeof(TransferNetworkConfiguration)).Deserialize(reader);
                if(config?.Beams==null || config.Bays==null || config.Beams.Length<2 || config.Beams.Length>8 || config.Bays.Length!=config.Beams.Length-1 || config.Beams.Any(b=>b==null) || config.Bays.Any(b=>b==null))throw new ArgumentException("Invalid saved beam/slab network.");
                if(config.CrossingBeams!=null && (config.CrossingBeams.Length>30 || config.CrossingBeams.Any(c=>c==null)))throw new ArgumentException("Invalid saved crossing beams.");
                UnsubscribeNetwork();NetworkBeams.Clear();NetworkBays.Clear();CrossingBeams.Clear();foreach(var c in config.CrossingBeams??new TransferCrossingBeamRow[0])CrossingBeams.Add(c);foreach(var b in config.Beams)NetworkBeams.Add(b);foreach(var b in config.Bays)NetworkBays.Add(b);SubscribeNetwork();Changed();NotifyNetwork();
            }
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
            var beam=new TransferBeamRow {Label="B"+(NetworkBeams.Count+1),DepthMm=NetworkBeams.Last().DepthMm,WidthMm=NetworkBeams.Last().WidthMm};
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
            NetworkBeams.Insert(before ? beamIndex : beamIndex+1,new TransferBeamRow {WidthMm=source.WidthMm,DepthMm=source.DepthMm});
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
            return new TransferNetworkInput {Beams=NetworkBeams.Select(b=>new TransferBeamRow {WidthMm=b.WidthMm,DepthMm=b.DepthMm}).ToArray(),Bays=NetworkBays.Select(b=>new TransferBayRow {WidthMm=b.WidthMm,ThicknessMm=b.ThicknessMm}).ToArray(),CrossingBeams=CrossingBeams.Select(c=>new TransferCrossingBeamRow {BayNumber=c.BayNumber,PositionMm=c.PositionMm,WidthMm=c.WidthMm,DepthMm=c.DepthMm}).ToArray(),UpperSpacingMm=UpperSupportSpacingMm,CustomLoads=UseCustomLoads,LiveKnM2=CustomLiveLoad,FormworkKnM2=CustomFormworkLoad};
        }
        internal void SelectNetworkBeam(int beam)
        {
            if(NetworkResult==null || beam<0 || beam>=NetworkResult.Beams.Length)return;SelectedNetworkBeam=beam;TowerResult=NetworkResult.Beams[beam];LeftLineLoad=TowerResult.LeftLineLoad;RightLineLoad=TowerResult.RightLineLoad;TotalLineLoad=LeftLineLoad+RightLineLoad;LimitingFactor=TowerResult.Governing;NotifyTower();NotifyNetwork();
        }
        private void NotifyNetwork()
        {
            foreach(string name in new[] {nameof(IsNetworkMode),nameof(ShowSingleTransfer),nameof(ShowSingleGeometry),nameof(CanAddNetworkBay),nameof(CanRemoveNetworkBay),nameof(NetworkResult),nameof(NetworkSummary),nameof(NetworkSchedule),nameof(NetworkSelectedLabel)})OnPropertyChanged(name);
        }
    }
}
