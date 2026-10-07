namespace EngineeringDashboard
{
    public partial class BeamCalculatorViewModel
    {
        private bool transferSlabLoads;
        private double upperSupportSpacingMm = 750, leftSlabBayMm = 3900, rightSlabBayMm = 3900;
        public bool TransferSlabLoads { get => transferSlabLoads; set { transferSlabLoads=value; Changed(); NotifyTransfer(); } }
        public bool IsTransferMode => TransferSlabLoads && UseTowers && (HasAnySlab || UseBeamNetwork);
        public bool ShowSlabStrip => HasAnySlab && !IsTransferMode;
        public bool CanSelectTransfer => UseTowers && (HasAnySlab || UseBeamNetwork);
        public bool ShowLeftBay => ShowSingleTransfer && HasLeftSlab;
        public bool ShowRightBay => ShowSingleTransfer && HasRightSlab;
        public double UpperSupportSpacingMm { get => upperSupportSpacingMm; set { upperSupportSpacingMm=value; Changed(); } }
        public double LeftSlabBayMm { get => leftSlabBayMm; set { leftSlabBayMm=value; Changed(); } }
        public double RightSlabBayMm { get => rightSlabBayMm; set { rightSlabBayMm=value; Changed(); } }
        public string TransferSummary => TowerResult?.Transfer?.Summary ?? "";
        public string TransferSchedule => TowerResult?.Transfer?.Schedule ?? "";
        public string TowerCalculationBasis => IsTransferMode ? (IsNetworkMode && NetworkResult?.CrossSupports.Length>0 ? CrossBeamSupportResult.Limitations+"\n"+TransferredSlabResult.Assumptions : TransferredSlabResult.Assumptions) : TowerAssumptions;
        private void NotifyTransfer()
        {
            NotifyNetwork();
            foreach (string name in new[] { nameof(IsTransferMode), nameof(ShowSlabStrip), nameof(CanSelectTransfer), nameof(ShowLeftBay), nameof(ShowRightBay), nameof(TransferSummary), nameof(TransferSchedule), nameof(TowerCalculationBasis) }) OnPropertyChanged(name);
        }
    }
}
