using Avalonia.Controls;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class ChartPage : UserControl
{
    public ChartPage()
    {
        InitializeComponent();
        DataContext = ServiceLocator.GetRequiredService<ChartPageViewModel>();
    }
}
