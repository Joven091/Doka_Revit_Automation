using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Text;
using System.Collections.Generic;

namespace EngineeringDashboard
{
    internal sealed class RevitSelectionImportWindow : Window
    {
        private readonly RevitSelectionSnapshot snapshot;
        private readonly string originalSelection;
        private readonly ComboBox direction=new ComboBox {MinWidth=220,DisplayMemberPath="Label",SelectedValuePath="SourceId",Margin=new Thickness(8,0,14,0)};
        private readonly DataGrid beamTable,slabTable;
        private readonly TextBlock summary=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};
        private readonly ContentControl preview=new ContentControl();
        private readonly Expander importNotes=new Expander {Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,8)};
        internal readonly Button ImportButton=new Button {Content="Use this drawing",MinWidth=130,Margin=new Thickness(8,0,0,0)};
        internal readonly Button DiagnoseButton=new Button {Content="Diagnose",Name="DiagnoseRevitImport",MinWidth=95,Margin=new Thickness(8,0,0,0)};
        internal BeamDiagnosticWindow Diagnostics {get;private set;}
        internal Action<string> CopyDiagnosticText {get;set;}
        internal Func<RevitSelectionSnapshot,string,RevitStructureImportResult> BuildPreview {get;set;}=RevitStructureImport.Build;
        internal string LastPreviewException {get;private set;}
        internal RevitStructureImportResult Result {get;private set;}
        internal BeamFormworkCanvas PreviewCanvas {get;private set;}
        internal RevitSelectionImportWindow(RevitSelectionSnapshot snapshot)
        {
            this.snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));
            originalSelection=RevitImportDiagnostics.CaptureSelection(snapshot);
            Title="Create drawing from Revit selection";Width=1100;Height=850;MinWidth=850;MinHeight=650;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Segoe UI");FontSize=13;Background=Brushes.WhiteSmoke;
            Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/MyRevitAddin;component/Views/CalculatorStyles.xaml",UriKind.Absolute)});
            var root=new Grid {Margin=new Thickness(20),Background=Brushes.WhiteSmoke};Content=new Border {Background=Brushes.WhiteSmoke,Child=root};
            foreach(var height in new[] {GridLength.Auto,GridLength.Auto,new GridLength(150),new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})root.RowDefinitions.Add(new RowDefinition {Height=height});
            var heading=new StackPanel();heading.Children.Add(new TextBlock {Text="Create drawing from Revit",FontSize=20,FontWeight=FontWeights.SemiBold});
            heading.Children.Add(new TextBlock {Text="Review the geometry before importing. Columns and walls mark the clear beam limits.",Margin=new Thickness(0,6,0,12),TextWrapping=TextWrapping.Wrap});root.Children.Add(heading);
            var options=new WrapPanel {Margin=new Thickness(0,0,0,12)};Grid.SetRow(options,1);root.Children.Add(options);
            options.Children.Add(new TextBlock {Text="Main beam direction",VerticalAlignment=VerticalAlignment.Center});options.Children.Add(direction);
            direction.ItemsSource=snapshot.Beams;direction.SelectedValue=RevitStructureImport.DefaultDirection(snapshot);
            var update=new Button {Content="Update preview",MinWidth=120};update.Click+=(s,e)=>RefreshPreview();options.Children.Add(update);
            DiagnoseButton.ToolTip="Copy selected-element geometry and import errors for review.";DiagnoseButton.Click+=(s,e)=>ShowDiagnostics();options.Children.Add(DiagnoseButton);
            var tabs=new TabControl {Margin=new Thickness(0,0,0,12)};Grid.SetRow(tabs,2);root.Children.Add(tabs);
            beamTable=Table(snapshot.Beams);beamTable.Columns.Add(Column("Revit beam","Label",true,240));beamTable.Columns.Add(Column("Length (mm)","LengthMm",true));beamTable.Columns.Add(Column("Width (mm)","WidthMm"));beamTable.Columns.Add(Column("Depth (mm)","DepthMm"));beamTable.Columns.Add(Column("Soffit (Revit mm)","BottomMm"));
            tabs.Items.Add(new TabItem {Header="Beams",Content=beamTable});
            slabTable=Table(snapshot.Slabs);slabTable.Columns.Add(Column("Revit slab","Label",true,240));slabTable.Columns.Add(Column("Thickness (mm)","ThicknessMm"));slabTable.Columns.Add(Column("Top (Revit mm)","TopMm"));
            tabs.Items.Add(new TabItem {Header="Slabs",Content=slabTable});
            tabs.Items.Add(new TabItem {Header="Columns & walls",Content=new ListBox {ItemsSource=snapshot.Boundaries.Select(b=>b.Kind+" · "+b.Label).ToArray(),BorderThickness=new Thickness(0)}});
            Grid.SetRow(preview,3);root.Children.Add(preview);
            var detailContent=new StackPanel();detailContent.Children.Add(summary);detailContent.Children.Add(importNotes);
            var details=new ScrollViewer {Content=detailContent,MaxHeight=145,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(details,4);root.Children.Add(details);
            var footer=new DockPanel {LastChildFill=true,Margin=new Thickness(0,8,0,0)};Grid.SetRow(footer,5);root.Children.Add(footer);
            var buttons=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
            var cancel=new Button {Content="Cancel",IsCancel=true,MinWidth=90};buttons.Children.Add(cancel);buttons.Children.Add(ImportButton);ImportButton.SetResourceReference(StyleProperty,"PrimaryAction");
            footer.Children.Add(new TextBlock {Text="Replaces the current drawing; Undo restores it. Project settings are kept.",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,15,0)});
            ImportButton.Click+=(s,e)=> {RefreshPreview();if(Result?.CanImport==true && ImportButton.IsEnabled)DialogResult=true;};
            direction.SelectionChanged+=(s,e)=>RefreshPreview();Loaded+=(s,e)=>RefreshPreview();Closed+=(s,e)=>Diagnostics?.Close();RefreshPreview();
        }
        private void ShowDiagnostics()
        {
            if(Diagnostics!=null){Diagnostics.RefreshReport();Diagnostics.Activate();return;}
            Diagnostics=new BeamDiagnosticWindow(BuildDiagnosticReport,CopyDiagnosticText,"Revit drawing import diagnosis","Add what happened, then copy the report and paste it into the chat for review.");
            Diagnostics.Closed+=(s,e)=>Diagnostics=null;if(IsVisible)Diagnostics.Owner=this;Diagnostics.Show();
        }
        internal string BuildDiagnosticReport(string remarks)
        {
            RefreshPreview();
            return RevitImportDiagnostics.Build(snapshot,originalSelection,direction.SelectedValue as string,Result,ImportButton.IsEnabled,ReviewState(),LastPreviewException,remarks);
        }
        private string ReviewState()
        {
            var text=new StringBuilder();text.AppendLine("Visible status: "+summary.Text);
            foreach(var grid in new[] {beamTable,slabTable})
            {
                text.AppendLine((ReferenceEquals(grid,beamTable) ? "Beams" : "Slabs")+" table has validation errors: "+Errors(grid));
                foreach(var box in Descendants(grid).OfType<TextBox>())
                {
                    DependencyObject parent=box;while(parent!=null && !(parent is DataGridCell))parent=VisualTreeHelper.GetParent(parent);
                    var row=ItemsControl.ContainerFromElement(grid,box) as DataGridRow;
                    string label=(row?.Item as RevitSelectedBeam)?.Label??(row?.Item as RevitSelectedSlab)?.Label??"row";
                    text.AppendLine("  "+label+" | "+(parent as DataGridCell)?.Column?.Header+" | raw text: "+box.Text);
                    foreach(var error in Validation.GetErrors(box))text.AppendLine("    Validation: "+error.ErrorContent);
                }
            }
            return text.ToString();
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
        private static DataGrid Table(object source)=>new DataGrid {ItemsSource=(System.Collections.IEnumerable)source,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,CanUserReorderColumns=false,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,Background=Brushes.White,BorderThickness=new Thickness(0)};
        private static DataGridTextColumn Column(string name,string path,bool readOnly=false,double width=140)
            =>new DataGridTextColumn {Header=name,IsReadOnly=readOnly,Width=new DataGridLength(width),Binding=new Binding(path) {Mode=readOnly ? BindingMode.OneWay : BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true,NotifyOnValidationError=true,StringFormat=readOnly ? null : "0.###"}};
        private static bool Errors(DependencyObject parent)
        {if(Validation.GetHasError(parent))return true;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)if(Errors(VisualTreeHelper.GetChild(parent,i)))return true;return false;}
        internal void RefreshPreview()
        {
            if(beamTable==null || slabTable==null)return;
            try {UpdatePreview();}
            catch(Exception ex)
            {
                LastPreviewException=ex.ToString();Result=null;ImportButton.IsEnabled=false;importNotes.Visibility=Visibility.Collapsed;PreviewCanvas=null;
                summary.Foreground=Brushes.Firebrick;summary.Text="The drawing preview could not be created. Click Diagnose to copy the error details.";
                preview.Content=new TextBlock {Text="Preview unavailable",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.SlateGray};
            }
        }
        private void UpdatePreview()
        {
            bool committed=beamTable.CommitEdit(DataGridEditingUnit.Cell,true) && beamTable.CommitEdit(DataGridEditingUnit.Row,true)
                && slabTable.CommitEdit(DataGridEditingUnit.Cell,true) && slabTable.CommitEdit(DataGridEditingUnit.Row,true);
            Result=BuildPreview(snapshot,direction.SelectedValue as string);
            bool valid=committed && !Errors(beamTable) && !Errors(slabTable) && Result.CanImport;
            ImportButton.IsEnabled=valid;
            if(valid)
            {
                var model=new BeamCalculatorViewModel {UseTowers=true};Result.Apply(model);
                PreviewCanvas=new BeamFormworkCanvas(model) {IsHitTestVisible=false,ImportPreviewMode=true};PreviewCanvas.ClearSelection();preview.Content=PreviewCanvas;
                summary.Foreground=Brushes.DarkSlateGray;
                summary.Text=$"{Result.Configuration.Beams.Length} beams · {Result.Configuration.Bays.Length} slab bays · {Result.Configuration.CrossingBeams.Length} crossings · {Result.Configuration.RevitContext.References.Length} column/wall references\nDrawing length: {Result.LengthMm:0.###} mm. Each beam retains its own endpoints. Canvas elevation 0 = Revit {Result.Configuration.RevitContext.TopElevationMm:0.###} mm.\nReview floor height and shared settings before calculating. This is a geometry snapshot.";
                if(model.HasCombinedRevitDrawing)summary.Text="Combined canvas · "+Result.Areas.Count(a=>a.Label.StartsWith("Area ",StringComparison.Ordinal))+" clear areas · "+Result.Areas.Count(a=>!a.Label.StartsWith("Area ",StringComparison.Ordinal))+" column-to-column beams\nAll selected members and panels retain their Revit positions and source IDs. Calculate all checks each part within its column limits.\nReview floor height and shared settings before calculating.";
                importNotes.Visibility=Result.Notes.Length>0 ? Visibility.Visible : Visibility.Collapsed;importNotes.Header="Import notes ("+Result.Notes.Length+")";
                importNotes.Content=new TextBlock {Text=string.Join("\n",Result.Notes),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
            }
            else
            {
                importNotes.Visibility=Visibility.Collapsed;PreviewCanvas=null;preview.Content=new TextBlock {Text="Review the items below to create the drawing preview.",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.SlateGray};
                summary.Foreground=Brushes.Firebrick;summary.Text=string.Join("\n",Result.Problems);
                if(!committed || Errors(beamTable) || Errors(slabTable))summary.Text="Correct the highlighted number in the review table.\n"+summary.Text;
            }
        }
    }
    internal sealed partial class BeamFormworkWorkspace
    {
        internal Func<RevitSelectionImportWindow,bool?> ShowRevitImportPreview {get;set;}
        private void ImportRevitSelection()
        {
            if(Model.IsOptimizing || readRevitSelection==null)return;
            try
            {
                RevitSelectionSnapshot selection;
                try {selection=readRevitSelection()??throw new InvalidOperationException("No Revit selection data was returned.");}
                catch(Exception ex){selection=new RevitSelectionSnapshot {Problems=new[] {"Revit selection could not be read. Click Diagnose to copy the error details."},ReadException=ex.ToString()};}
                var dialog=new RevitSelectionImportWindow(selection) {Owner=DialogOwner};
                bool? accepted=ShowRevitImportPreview!=null ? ShowRevitImportPreview(dialog) : dialog.ShowDialog();
                if(accepted!=true || dialog.Result?.CanImport!=true || !dialog.ImportButton.IsEnabled)return;
                ApplyRevitImport(dialog.Result);
            }
            catch(Exception ex)
            {MessageBox.Show(DialogOwner,"The selection was not imported.\n"+ex.Message,"Revit selection",MessageBoxButton.OK,MessageBoxImage.Information);}
        }
        internal void ApplyRevitImport(RevitStructureImportResult result)
        {
            if(Model.IsOptimizing || result?.CanImport!=true)return;
            Mutate(()=>result.Apply(Model));ClearDiagrams();refreshInputs?.Invoke();Drawing.ClearSelection();Geometry();Drawing.Fit();
            status.Text="Revit geometry imported. Review floor height and shared settings, then Calculate. Undo restores the previous drawing.";
        }
    }
}
