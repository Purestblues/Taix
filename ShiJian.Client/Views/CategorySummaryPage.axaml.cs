using Avalonia.Controls;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class CategorySummaryPage : UserControl
{
    public CategorySummaryPage()
    {
        InitializeComponent();
        DataContext = ServiceLocator.GetService<CategorySummaryPageViewModel>();
    }
}
