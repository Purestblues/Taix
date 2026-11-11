using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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
    private Rectangle ValueBlockObj;
    private StackPanel ValueContainer;
    private bool _isValueSizeTracked;

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
        var parent = Parent as Control;
        if (parent != null)
            parent.SizeChanged -= Parent_SizeChanged;

        // 虚拟化列表会反复装载 / 卸载行，这里必须把尺寸订阅一并解掉，否则会重复挂
        DetachValueSizeTracking();
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
        ValueBlockObj = e.NameScope.Get<Rectangle>("ValueBlockObj");
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

        var parent = Parent as Control;
        if (parent != null)
        {
            parent.SizeChanged -= Parent_SizeChanged;
            parent.SizeChanged += Parent_SizeChanged;
        }

        AttachValueSizeTracking();
    }

    /// <summary>
    /// 进度条宽度是算出来的，容器宽度变化（窗口缩放、滚动时重新布局）必须重算，
    /// 否则会停在上一次算出来的值——甚至停在 0，看起来就是「进度条消失了」。
    /// </summary>
    private void AttachValueSizeTracking()
    {
        if (_isValueSizeTracked || ValueContainer == null) return;

        ValueContainer.SizeChanged += OnValueSizeChanged;
        if (ValueTextObj != null) ValueTextObj.SizeChanged += OnValueSizeChanged;
        _isValueSizeTracked = true;
    }

    private void DetachValueSizeTracking()
    {
        if (!_isValueSizeTracked) return;

        if (ValueContainer != null) ValueContainer.SizeChanged -= OnValueSizeChanged;
        if (ValueTextObj != null) ValueTextObj.SizeChanged -= OnValueSizeChanged;
        _isValueSizeTracked = false;
    }

    private void OnValueSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateValueBlockWidth();
    }


    private void Parent_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateValueBlockWidth();
    }

    private void ChartsItemTypeList_Loaded(object? sender, RoutedEventArgs e)
    {
        // 虚拟化会把行卸载再装载，OnApplyTemplate 不会重跑，尺寸订阅必须在这里补回来
        AttachValueSizeTracking();
        Render();

        // 装载这一刻布局还没算完（容器宽度是 0），排到布局之后再补一次
        Dispatcher.UIThread.Post(UpdateValueBlockWidth, DispatcherPriority.Background);
    }

    private void Render()
    {
        if (Data == null || ValueTextObj == null) return;

        ValueTextObj.Text = Data.Tag;

        // 虚拟化复用行时这里会被再次调用：不能把宽度重置成 0 再等回调，
        // 否则滚动过程中每次复用都会闪一下空条，尺寸没算出来时还会一直停在 0。
        UpdateValueBlockWidth();
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

    public void UpdateValueBlockWidth()
    {
        if (Data == null || !IsLoaded) return;
        if (ValueBlockObj == null || ValueContainer == null || ValueTextObj == null) return;

        // 布局还没算出来时先别动，等 SizeChanged 再来一次（负数宽度会被夹成 0，条就没了）
        var available = ValueContainer.Bounds.Width * 0.95 - ValueTextObj.Bounds.Width;
        if (available <= 1 || MaxValue <= 0) return;

        // 一分钟级的数据算出来只有几个像素，给个下限，否则看起来像「条没画出来」
        var width = Data.Value / MaxValue * available;
        ValueBlockObj.Width = Data.Value <= 0 ? 0 : Math.Max(3, width);
    }
}