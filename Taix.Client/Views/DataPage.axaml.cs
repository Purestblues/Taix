using Avalonia.Controls;
using Taix.Client.Controls;
using Taix.Client.ViewModels;
using Taix.Client.Views;

namespace Taix.Client.Views;

public partial class DataPage : TPage
{
    /// <summary>右侧洞察栏至少要这么宽，否则表盘和榜单会被挤扁</summary>
    private const double InsightMinWidth = 1080;

    /// <summary>低于这个高度就收起底部柱图，保证表盘还有足够直径</summary>
    private const double DistributionMinHeight = 660;

    private readonly DataPageViewModel? _viewModel;

    public DataPage()
    {
        InitializeComponent();
        _viewModel = ServiceLocator.GetRequiredService<DataPageViewModel>();
        DataContext = _viewModel;

        // TPage 会把自身尺寸对齐到 PageContainer，跟着它做响应式
        SizeChanged += OnPageSizeChanged;
    }

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.IsInsightVisible = Bounds.Width >= InsightMinWidth;
        _viewModel.IsDistributionVisible = Bounds.Height >= DistributionMinHeight;
    }
}
