using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EngineeringDashboard.Top50;

namespace EngineeringDashboard
{
    public sealed partial class Top50Window
    {
        private readonly Top50RevitConnection revit;
        private Button useRevit;
        private MenuItem refreshRevit,locateRevit;
        private bool revitBusy,highlighting,revitClosed;
        private string revitStatus="";
        private RevitWallRequest pendingHighlight;
        private void AddRevitCommands(Panel commands,MenuItem tools)
        {
            if(revit?.ReadSelection==null)return;
            useRevit=Action("Use Revit selection",async()=>await ReadRevit(false));useRevit.Margin=new Thickness(3);useRevit.Padding=new Thickness(10,5,10,5);useRevit.MinHeight=30;
            System.Windows.Automation.AutomationProperties.SetAutomationId(useRevit,"Top50UseRevitSelection");commands.Children.Add(useRevit);
            refreshRevit=MenuCommand("Refresh imported walls",async()=>await ReadRevit(true),"Top50RefreshRevit");tools.Items.Add(refreshRevit);
            locateRevit=MenuCommand("Locate casting group in Revit",()=>QueueHighlight(true),"Top50LocateRevit");tools.Items.Add(locateRevit);
            Closed+=(s,e)=>{revitClosed=true;pendingHighlight=null;};
        }
        private async Task ReadRevit(bool refresh)
        {
            if(revitBusy||highlighting||model.IsBusy)return;revitBusy=true;revitStatus="Waiting for Revit…";UpdateBusy();
            try
            {
                var snapshot=refresh?await revit.Refresh(SourceRequest()):await revit.ReadSelection();
                if(revitClosed)return;
                model.RecordRevitOperation(refresh?"Refresh imported walls":"Use Revit selection",Top50RevitSource.Describe(snapshot));
                if(refresh)model.RefreshRevit(snapshot);else model.ImportRevit(snapshot);
                revitStatus=refresh?"Revit walls refreshed. Optimize again to update the offer.":"Revit walls imported. Review any unequal faces, then Optimize.";ShowPage(0);
            }
            catch(Exception error)
            {
                if(!revitClosed){model.RecordDiagnosticError(refresh?"Refresh imported walls":"Use Revit selection",error);revitStatus=error.Message+" Open Tools → Diagnose for details.";}
            }
            finally{revitBusy=false;if(!revitClosed)UpdateBusy();}
        }
        private RevitWallRequest SourceRequest()
        {
            var source=model.Input?.RevitSource??throw new InvalidOperationException("Import Revit walls before refreshing them.");
            return new RevitWallRequest{DocumentKey=source.DocumentKey,UniqueIds=model.Input.AllWalls.Where(w=>w.Revit!=null).Select(w=>w.Revit.UniqueId).Distinct(StringComparer.Ordinal).ToArray()};
        }
        private void QueueHighlight(bool locate)
        {
            if(revit?.Highlight==null||revitBusy||model.IsBusy||model.IsStale||model.Selected==null||model.Input?.RevitSource==null)return;
            int index=Math.Max(0,Math.Min(castingIndex,model.Selected.Groups.Count-1));if(model.Selected.Groups.Count==0)return;
            var rows=model.Input.Walls.ToDictionary(w=>w.Id);
            var ids=model.Selected.Groups[index].Walls.Where(w=>rows.ContainsKey(w.WallId)&&rows[w.WallId].Revit!=null).Select(w=>rows[w.WallId].Revit.UniqueId).Distinct(StringComparer.Ordinal).ToArray();
            if(ids.Length==0){revitStatus="This casting group has no Revit wall references. DXF and manual walls remain available for planning.";UpdateBusy();return;}
            pendingHighlight=new RevitWallRequest{DocumentKey=model.Input.RevitSource.DocumentKey,UniqueIds=ids,Locate=locate};
            revitStatus="Waiting for Revit to highlight the selected casting group…";model.RecordRevitOperation("Highlight casting group pending","Document: "+pendingHighlight.DocumentKey+"\nWall unique IDs: "+string.Join(", ",ids));UpdateBusy();
            if(!highlighting)RunHighlights();
        }
        private async void RunHighlights()
        {
            highlighting=true;UpdateBusy();
            try
            {
                while(pendingHighlight!=null&&!revitClosed)
                {
                    var request=pendingHighlight;pendingHighlight=null;
                    try
                    {
                        var result=await revit.Highlight(request);if(revitClosed)return;
                        revitStatus=result.Message;model.RecordRevitOperation(request.Locate?"Locate casting group":"Highlight casting group",result.Message+"\nDocument: "+request.DocumentKey+"\nWall unique IDs: "+string.Join(", ",request.UniqueIds));
                    }
                    catch(Exception error){if(!revitClosed){model.RecordDiagnosticError("Highlight casting group",error);revitStatus=error.Message+" Open Tools → Diagnose for details.";}}
                }
            }
            finally{highlighting=false;if(!revitClosed)UpdateBusy();}
        }
    }
}
