using Avalonia.Controls;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class DetailPage : UserControl
{
    public DetailPage()
    {
        InitializeComponent();
        DataContext = ServiceLocator.GetService<DetailPageViewModel>();
    }
}