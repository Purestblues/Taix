using ShiJian.Client.Controls;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class CategoryWebSiteListPage : TPage
{
    public CategoryWebSiteListPage()
    {
        InitializeComponent();
        DataContext = ServiceLocator.GetRequiredService<CategoryWebSiteListPageViewModel>();
    }
}