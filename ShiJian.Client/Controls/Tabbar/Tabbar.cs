using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Colors = ShiJian.Client.Base.Color.Colors;

namespace ShiJian.Client.Controls.Tabbar;

public class Tabbar : TemplatedControl
{
    public static readonly StyledProperty<Color> SelectedTextColorProperty =
        AvaloniaProperty.Register<Tabbar, Color>(nameof(SelectedTextColor));

    public static readonly StyledProperty<Color> UnselectedTextColorProperty =
        AvaloniaProperty.Register<Tabbar, Color>(nameof(UnselectedTextColor), Color.Parse("#CCCCCC"));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<Tabbar, int>(nameof(SelectedIndex));

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<Tabbar, double>(nameof(ItemSpacing), 4);

    public static readonly DirectProperty<Tabbar, ObservableCollection<string>> DataProperty =
        AvaloniaProperty.RegisterDirect<Tabbar, ObservableCollection<string>>(
            nameof(Data),
            o => o.Data,
            (o, v) => o.Data = v ?? new ObservableCollection<string>());

    private readonly List<TextBlock> ItemsDictionary;
    private readonly List<Border> ItemWrappers;

    private ObservableCollection<string> _data = new();

    //  选中标记块
    private Border ActiveBlock;
    private Grid ItemsContainer;

    public Tabbar()
    {
        ItemsDictionary = new List<TextBlock>();
        ItemWrappers = new List<Border>();
    }

    public Color SelectedTextColor
    {
        get => GetValue(SelectedTextColorProperty);
        set => SetValue(SelectedTextColorProperty, value);
    }

    public Color UnselectedTextColor
    {
        get => GetValue(UnselectedTextColorProperty);
        set => SetValue(UnselectedTextColorProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public ObservableCollection<string> Data
    {
        get => _data;
        set => SetAndRaise(DataProperty, ref _data, value);
    }

    protected override Type StyleKeyOverride => typeof(Tabbar);

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (Data != null) Data.CollectionChanged -= Data_CollectionChanged;
        if (ItemsContainer != null) ItemsContainer.SizeChanged -= OnItemsContainerSizeChanged;
    }

    private void Data_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e == null || Data == null || ItemsDictionary == null)
            return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Replace:
                if (e.OldStartingIndex >= 0 && e.OldStartingIndex < Data.Count &&
                    e.OldStartingIndex < ItemsDictionary.Count)
                    if (Data[e.OldStartingIndex] != null)
                        ItemsDictionary[e.OldStartingIndex].Text = Data[e.OldStartingIndex];

                break;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ActiveBlock = e.NameScope.Get<Border>("ActiveBlock");
        if (ItemsContainer != null) ItemsContainer.SizeChanged -= OnItemsContainerSizeChanged;
        ItemsContainer = e.NameScope.Get<Grid>("ItemsContainer");
        ItemsContainer.SizeChanged += OnItemsContainerSizeChanged;
        if (Data != null) Data.CollectionChanged += Data_CollectionChanged;
        Render();
    }

    private void OnItemsContainerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (IsLoaded) ScrollToActive(SelectedIndex);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty) OnSelectedItemChanged(change);
        if (change.Property == SelectedTextColorProperty ||
            change.Property == UnselectedTextColorProperty)
            RefreshItemColors();
    }

    private void RefreshItemColors()
    {
        if (ItemsContainer == null || ItemsDictionary.Count == 0) return;
        for (var i = 0; i < ItemsDictionary.Count; i++)
        {
            var isSelected = i == SelectedIndex;
            ItemsDictionary[i].Foreground = new SolidColorBrush(
                isSelected ? SelectedTextColor : UnselectedTextColor);
            ItemsDictionary[i].FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    private static void OnSelectedItemChanged(AvaloniaPropertyChangedEventArgs change)
    {
        var control = change.Sender as Tabbar;
        if (change.NewValue != change.OldValue)
        {
            var oldIndex = change.OldValue is int i ? i : 0;
            control?.ScrollToActive(oldIndex);
        }
    }

    private void ScrollToActive(int oldSelectedIndex = 0)
    {
        if (ItemsDictionary.Count == 0 || ItemWrappers.Count == 0 || !IsLoaded ||
            SelectedIndex < 0 || SelectedIndex >= ItemWrappers.Count) return;

        //  获取选中项并对齐对称容器边界
        var item = ItemsDictionary[SelectedIndex];
        var wrapper = ItemWrappers[SelectedIndex];
        item.Foreground = new SolidColorBrush(SelectedTextColor);
        item.FontWeight = FontWeight.SemiBold;

        var origin = wrapper.TranslatePoint(new Point(0, 0), ActiveBlock?.Parent as Visual ?? ItemsContainer);
        var left = origin?.X ?? wrapper.Bounds.X;

        if (ActiveBlock != null)
        {
            ActiveBlock.Width = wrapper.Bounds.Width;
            ActiveBlock.RenderTransform = TransformOperations.Parse($"translateX({left}px)");
        }
        ReOldSelectedStyle();
    }

    private void ReOldSelectedStyle()
    {
        Reset();
    }


    private void Render()
    {
        if (Data != null)
        {
            ItemsContainer.Children.Clear();
            ItemsContainer.ColumnDefinitions.Clear();
            ItemsDictionary.Clear();
            ItemWrappers.Clear();

            for (var i = 0; i < Data.Count; i++)
            {
                var item = Data[i];
                ItemsContainer.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = GridLength.Auto }
                );
                AddItem(item, i);
            }
        }
    }

    private void AddItem(string item, int col)
    {
        if (ItemsContainer != null)
        {
            var isLast = col == Data.Count - 1;
            var wrapper = new Border
            {
                Margin = new Thickness(0, 0, isLast ? 0 : ItemSpacing, 0),
                Padding = new Thickness(12, 6),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = col,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            var control = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Text = item,
                FontSize = 14,
                FontWeight = col == SelectedIndex ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = new SolidColorBrush(
                    col == SelectedIndex ? SelectedTextColor : UnselectedTextColor),
                IsHitTestVisible = false
            };

            wrapper.Child = control;
            wrapper.PointerPressed += (e, c) =>
            {
                if (e is Control { Tag: int index } && SelectedIndex != index) SelectedIndex = index;
            };
            wrapper.SizeChanged += (_, _) =>
            {
                if (col == SelectedIndex && IsLoaded) ScrollToActive(SelectedIndex);
            };
            if (isLast)
                wrapper.Loaded += (e, c) =>
                {
                    if (SelectedIndex >= 0 && SelectedIndex < ItemWrappers.Count)
                    {
                        ScrollToActive(SelectedIndex);
                    }
                    Reset();
                };
            Grid.SetColumn(wrapper, col);
            ItemsContainer.Children.Add(wrapper);
            ItemsDictionary.Add(control);
            ItemWrappers.Add(wrapper);
        }
    }

    private void Reset()
    {
        for (var i = 0; i < ItemsDictionary.Count; i++)
        {
            if (i != SelectedIndex)
            {
                ItemsDictionary[i].Foreground = new SolidColorBrush(UnselectedTextColor);
                ItemsDictionary[i].FontWeight = FontWeight.Normal;
            }
        }
    }
}
