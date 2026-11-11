using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using ShiJian.Client.Controls.Base;
using ShiJian.Client.Controls.Charts.Model;

namespace ShiJian.Client.Controls.Charts;

public class ChartsItemTypeList : TemplatedControl
{
    public static readonly RoutedEvent<RoutedEventArgs> ContextMenuRequestedEvent =
        RoutedEvent.Register<ChartsItemTypeList, RoutedEventArgs>(nameof(ContextMenuRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ItemClickRequestedEvent =
        RoutedEvent.Register<ChartsItemTypeList, RoutedEventArgs>(nameof(ItemClickRequested), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> ContextMenuRequested
    {
        add => AddHandler(ContextMenuRequestedEvent, value);
        remove => RemoveHandler(ContextMenuRequestedEvent, value);
    }

    public event EventHandler<RoutedEventArgs> ItemClickRequested
    {
        add => AddHandler(ItemClickRequestedEvent, value);
        remove => RemoveHandler(ItemClickRequestedEvent, value);
    }

    public static readonly DirectProperty<ChartsItemTypeList, ChartsDataModel> DataProperty =
        AvaloniaProperty.RegisterDirect<ChartsItemTypeList, ChartsDataModel>(
            nameof(Data),
            o => o.Data,
            (o, v) => o.Data = v);

    public static readonly DirectProperty<ChartsItemTypeList, double> MaxValueProperty =
        AvaloniaProperty.RegisterDirect<ChartsItemTypeList, double>(
            nameof(MaxValue),
            o => o.MaxValue,
            (o, v) => o.MaxValue = v);

    public static readonly DirectProperty<ChartsItemTypeList, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<ChartsItemTypeList, bool>(
            nameof(IsLoading),
            o => o.IsLoading,
            (o, v) => o.IsLoading = v);

    public static readonly DirectProperty<ChartsItemTypeList, bool> IsShowBadgeProperty =
        AvaloniaProperty.RegisterDirect<ChartsItemTypeList, bool>(
            nameof(IsShowBadge),
            o => o.IsShowBadge,
            (o, v) => o.IsShowBadge = v);

    public static readonly DirectProperty<ChartsItemTypeList, double> IconSizeProperty =
        AvaloniaProperty.RegisterDirect<ChartsItemTypeList, double>(
            nameof(IconSize),
            o => o.IconSize,
            (o, v) => o.IconSize = v);

    /// <summary>
    /// 点击分类徽章时的命令，参数为 <see cref="ChartBadgeModel"/>
    /// </summary>
    public static readonly DirectProperty<ChartsItemTypeList, System.Windows.Input.ICommand?> BadgeClickCommandProperty =
        AvaloniaProperty.RegisterDirect<ChartsItemTypeList, System.Windows.Input.ICommand?>(
            nameof(BadgeClickCommand),
            o => o.BadgeClickCommand,
            (o, v) => o.BadgeClickCommand = v);

    /// <summary>
    /// 进度条颜色，不设置时用主题里的 ChartTypeListProgressBarBrush
    /// </summary>
    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<ChartsItemTypeList, IBrush?>(nameof(BarBrush));

    private ChartsDataModel _data;

    private double _iconSize;

    private bool _isLoading;

    private bool _isShowBadge;

    private double _maxValue;
    private ListBox? _badgeListView;
    private bool _isBadgeEventAdded;
    private System.Windows.Input.ICommand? _badgeClickCommand;
    private Img IconObj;
    private bool IsAddEvent;

    private TextBlock NameTextObj, ValueTextObj;
    private RatioBar ValueBlockObj;
    private StackPanel ValueContainer;

    public ChartsDataModel Data
    {
        get => _data;
        set => SetAndRaise(DataProperty, ref _data, value);
    }

    public double MaxValue
    {
        get => _maxValue;
        set => SetAndRaise(MaxValueProperty, ref _maxValue, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetAndRaise(IsLoadingProperty, ref _isLoading, value);
    }

    public bool IsShowBadge
    {
        get => _isShowBadge;
        set => SetAndRaise(IsShowBadgeProperty, ref _isShowBadge, value);
    }

    public double IconSize
    {
        get => _iconSize;
        set => SetAndRaise(IconSizeProperty, ref _iconSize, value);
    }

    public System.Windows.Input.ICommand? BadgeClickCommand
    {
        get => _badgeClickCommand;
        set => SetAndRaise(BadgeClickCommandProperty, ref _badgeClickCommand, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ChartsItemTypeList);


    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        PointerPressed -= OnPointerPressed;
        Loaded -= ChartsItemTypeList_Loaded;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 虚拟化会把同一个行容器复用给别的数据：数据或最大值变了要立刻重算进度条
        if (change.Property == DataProperty || change.Property == MaxValueProperty)
            Render();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        NameTextObj = e.NameScope.Get<TextBlock>("NameTextObj");
        ValueTextObj = e.NameScope.Get<TextBlock>("ValueTextObj");
        ValueBlockObj = e.NameScope.Get<RatioBar>("ValueBlockObj");
        ValueContainer = e.NameScope.Get<StackPanel>("ValueContainer");
        IconObj = e.NameScope.Get<Img>("IconObj");

        // 徽章点击用隧道先行拦截：否则会被外层的“点击整行”粘到详情页去
        _badgeListView = e.NameScope.Find<ListBox>("BadgeListView");
        if (_badgeListView != null && !_isBadgeEventAdded)
        {
            _badgeListView.AddHandler(PointerPressedEvent, OnBadgePointerPressed, RoutingStrategies.Tunnel);
            _isBadgeEventAdded = true;
        }

        if (!IsAddEvent)
        {
            PointerPressed += OnPointerPressed;
            Loaded += ChartsItemTypeList_Loaded;
            IsAddEvent = true;
        }

    }


    private void ChartsItemTypeList_Loaded(object? sender, RoutedEventArgs e)
    {
        // 虚拟化会把行卸载再装载：重新挂一次数据，比例随之更新
        Render();
    }

    private void Render()
    {
        if (Data == null || ValueTextObj == null) return;

        ValueTextObj.Text = Data.Tag;
        UpdateValueRatio();
    }

    private void OnBadgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (BadgeClickCommand == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not ChartBadgeModel badge) return;
        if (badge.CategoryId <= 0) return;

        BadgeClickCommand.Execute(badge);
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (Data == null) return;

        if (point.Properties.IsLeftButtonPressed)
        {
            var args = new RoutedEventArgs(ItemClickRequestedEvent);
            RaiseEvent(args);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            var args = new RoutedEventArgs(ContextMenuRequestedEvent);
            RaiseEvent(args);
        }
    }

    /// <summary>
    /// 只设置比例，像素宽度交给 <see cref="RatioBar"/> 在绘制时按实际宽度现算。
    /// 这样不依赖「容器尺寸回调」，虚拟化复用、切页进场、快速滚动都不会再出现空条。
    /// </summary>
    public void UpdateValueRatio()
    {
        if (ValueBlockObj == null) return;

        if (Data == null || MaxValue <= 0 || Data.Value <= 0)
        {
            ValueBlockObj.Ratio = 0;
            return;
        }

        ValueBlockObj.Ratio = Math.Min(1d, Data.Value / MaxValue);
    }
}