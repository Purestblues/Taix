using ShiJian.Client.Controls;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class CategoryAppListPage : TPage
{
    public CategoryAppListPage()
    {
        InitializeComponent();
        DataContext = ServiceLocator.GetRequiredService<CategoryAppListPageViewModel>();
    }
}