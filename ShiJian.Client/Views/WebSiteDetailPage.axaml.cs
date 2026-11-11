using Avalonia.Controls;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class WebSiteDetailPage : UserControl
{
    public WebSiteDetailPage()
    {
        InitializeComponent();
        DataContext = ServiceLocator.GetRequiredService<WebSiteDetailPageViewModel>();
    }
}