using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineeringDashboard
{
    /// <summary>Compact scope notice, with rejection evidence disclosed on request.</summary>
    public sealed class SeparateSlabExplanationView : Border
    {
        internal readonly Expander Explanation=new Expander {Header="Why separate support?",Margin=new Thickness(0,8,0,0)};
        internal readonly TextBlock Notice=new TextBlock {TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold};
        private readonly TextBlock evidence=new TextBlock {TextWrapping=TextWrapping.Wrap};
        private readonly TextBlock scope=new TextBlock {Text="Independent slab supports, timber and design are not calculated or included in these totals.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)};
        private readonly TextBox fullFailure=new TextBox {IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        private readonly Expander fullDetails=new Expander {Header="Full-transfer search details",Margin=new Thickness(0,8,0,0)};
        private TransferNetworkResult result;
        internal string EvidenceText=>evidence.Text;
        public SeparateSlabExplanationView()
        {
            Background=Brushes.Cornsilk;BorderBrush=Brushes.Goldenrod;BorderThickness=new Thickness(3,0,0,0);Padding=new Thickness(12);Margin=new Thickness(0,0,0,10);Visibility=Visibility.Collapsed;
            var content=new StackPanel();content.Children.Add(Notice);content.Children.Add(scope);
            var reasons=new StackPanel();reasons.Children.Add(new ScrollViewer {Content=evidence,MaxHeight=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            fullDetails.Content=fullFailure;reasons.Children.Add(fullDetails);Explanation.Content=reasons;content.Children.Add(Explanation);Child=content;
        }
        public void SetResult(TransferNetworkResult value)
        {
            if(!ReferenceEquals(value,result)){Explanation.IsExpanded=fullDetails.IsExpanded=false;result=value;}
            Visibility=value?.RequiresSeparateSlabFormwork==true ? Visibility.Visible : Visibility.Collapsed;
            Notice.Text=value?.RequiresSeparateSlabFormwork==true ? "Separate slab supports needed: "+string.Join(", ",value.Bays.Select((b,i)=>b==null ? "S"+(i+1) : null).Where(x=>x!=null))+". No passing transfer arrangement found with the current inputs." : "";
            evidence.Text=value?.SeparateSlabExplanation??"";
            fullFailure.Text=value?.FullTransferFailureDetails??"";
            fullDetails.Visibility=string.IsNullOrEmpty(fullFailure.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        internal void ShowBay(int bay)
        {
            if(result==null || bay<0 || bay>=result.Bays.Length || result.Bays[bay]!=null)return;
            evidence.Text=result.SeparateSlabExplanationForBay(bay);Explanation.IsExpanded=true;
        }
    }
}
