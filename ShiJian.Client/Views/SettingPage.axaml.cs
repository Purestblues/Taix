using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.VisualTree;
using ShiJian.Client.Librarys;
using ShiJian.Client.Models;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class SettingPage : UserControl
{
    private IDisposable? _heightBinding;
    private ScrollViewer? _outerScrollViewer;
    private SettingPageViewModel? _boundVm;

    public SettingPage()
        : this(ServiceLocator.GetRequiredService<SettingPageViewModel>())
    {
    }

    public SettingPage(SettingPageViewModel vm)
    {
        InitializeComponent();
        _boundVm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = vm;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingPageModel.TabbarSelectedIndex)) return;
        SmoothScroll.CancelAnimation(ContentScrollViewer);
        if (ContentScrollViewer != null)
        {
            ContentScrollViewer.Offset = default;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _outerScrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_outerScrollViewer != null)
        {
            _outerScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            var binding = new Binding
            {
                Source = _outerScrollViewer,
                Path = "Viewport.Height"
            };
            _heightBinding = this.Bind(HeightProperty, binding);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_boundVm != null)
        {
            _boundVm.PropertyChanged -= OnViewModelPropertyChanged;
            _boundVm = null;
        }

        _heightBinding?.Dispose();
        _heightBinding = null;

        if (_outerScrollViewer != null)
        {
            _outerScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _outerScrollViewer = null;
        }
    }
}
