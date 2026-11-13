using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ShiJian.Client.Controls;
using ShiJian.Client.Foundation;
using ShiJian.Client.Foundation.Rx;
using ShiJian.Client.Librarys;
using ShiJian.Client.Models;
using ShiJian.Client.Models.Category;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class CategoryPage : TPage
{
    private readonly IDisposable _editDirectoriesSubscription;
    private readonly IDisposable _editIsDirectoryMatchSubscription;
    private readonly IDisposable _editIsUrlMatchSubscription;
    private readonly IDisposable _editUrlPatternsSubscription;
    private readonly IDisposable _appEmptyExpandedSubscription;
    private readonly IDisposable _webEmptyExpandedSubscription;
    private ObservableCollection<string>? _observedEditDirectories;
    private ObservableCollection<string>? _observedEditUrlPatterns;
    private CategoryPageViewModel _model;
    private Flyout? _activeTagFlyout;

    public CategoryPage()
    {
        InitializeComponent();
        var model = ServiceLocator.GetRequiredService<CategoryPageViewModel>();
        _model = model;
        DataContext = model;
        _editIsDirectoryMatchSubscription = ObservablePropertyChangedExtensions.WhenPropertyChanged(model, x => x.EditIsDirectoryMatch)
            .Subscribe(HandleEditIsDirectoryMatchChange);

        _editDirectoriesSubscription = ObservablePropertyChangedExtensions.WhenPropertyChanged(model, x => x.EditDirectories).Subscribe(val =>
        {
            if (_observedEditDirectories != null)
                _observedEditDirectories.CollectionChanged -= OnEditDirectoriesCollectionChanged;
            _observedEditDirectories = val;
            val.CollectionChanged += OnEditDirectoriesCollectionChanged;
        });

        _editIsUrlMatchSubscription = ObservablePropertyChangedExtensions.WhenPropertyChanged(model, x => x.EditIsUrlMatch)
            .Subscribe(HandleEditIsUrlMatchChange);

        _editUrlPatternsSubscription = ObservablePropertyChangedExtensions.WhenPropertyChanged(model, x => x.EditUrlPatterns).Subscribe(val =>
        {
            if (_observedEditUrlPatterns != null)
                _observedEditUrlPatterns.CollectionChanged -= OnEditUrlPatternsCollectionChanged;
            _observedEditUrlPatterns = val;
            val.CollectionChanged += OnEditUrlPatternsCollectionChanged;
        });

        _appEmptyExpandedSubscription = ObservablePropertyChangedExtensions.WhenPropertyChanged(model, x => x.IsAppEmptyExpanded)
            .Skip(1)
            .Subscribe(expanded => _ = ExpandMotion.SetExpandedAsync(EmptyAppCategoriesHost, expanded));

        _webEmptyExpandedSubscription = ObservablePropertyChangedExtensions.WhenPropertyChanged(model, x => x.IsWebEmptyExpanded)
            .Skip(1)
            .Subscribe(expanded => _ = ExpandMotion.SetExpandedAsync(EmptyWebCategoriesHost, expanded));
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_model != null)
        {
            ExpandMotion.ApplyFinal(EmptyAppCategoriesHost, _model.IsAppEmptyExpanded);
            ExpandMotion.ApplyFinal(EmptyWebCategoriesHost, _model.IsWebEmptyExpanded);
        }
    }

    private void OnEditDirectoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) viewer.ScrollToEnd();
    }

    private void OnEditUrlPatternsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) viewer.ScrollToEnd();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_model != null) _model.IsEditError = false;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _activeTagFlyout?.Hide();
        _activeTagFlyout = null;
        _editIsDirectoryMatchSubscription.Dispose();
        _editDirectoriesSubscription.Dispose();
        _editIsUrlMatchSubscription.Dispose();
        _editUrlPatternsSubscription.Dispose();
        _appEmptyExpandedSubscription.Dispose();
        _webEmptyExpandedSubscription.Dispose();
        if (_observedEditDirectories != null)
            _observedEditDirectories.CollectionChanged -= OnEditDirectoriesCollectionChanged;
        if (_observedEditUrlPatterns != null)
            _observedEditUrlPatterns.CollectionChanged -= OnEditUrlPatternsCollectionChanged;
        _observedEditDirectories = null;
        _observedEditUrlPatterns = null;
        _model = null;
    }

    private void HandleEditIsDirectoryMatchChange(bool isDirectoryMatch)
    {
        if (isDirectoryMatch)
        {
            viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            viewer.ScrollToEnd();
        }
        else
        {
            viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    private void HandleEditIsUrlMatchChange(bool isUrlMatch)
    {
        if (isUrlMatch)
        {
            viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            viewer.ScrollToEnd();
        }
        else
        {
            viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    private void OnAppCategoryListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        if (sender is not ListBox listBox) return;

        var point = e.GetPosition(listBox);
        var hit = listBox.InputHitTest(point) as Visual;

        Visual? visual = hit;
        while (visual != null && visual is not ListBoxItem)
        {
            visual = visual.GetVisualParent();
        }

        if (visual is ListBoxItem listBoxItem && listBoxItem.DataContext is CategoryModel category)
        {
            _model.SelectedAppCategoryItem = category;
        }
    }

    private void OnWebCategoryListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        if (sender is not ListBox listBox) return;

        var point = e.GetPosition(listBox);
        var hit = listBox.InputHitTest(point) as Visual;

        Visual? visual = hit;
        while (visual != null && visual is not ListBoxItem)
        {
            visual = visual.GetVisualParent();
        }

        if (visual is ListBoxItem listBoxItem && listBoxItem.DataContext is CategoryPageModel.WebCategoryModel category)
        {
            _model.SelectedWebCategoryItem = category;
        }
    }

    private void OnEntityListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        if (sender is not ListBox listBox) return;

        var point = e.GetPosition(listBox);
        var hit = listBox.InputHitTest(point) as Visual;

        Visual? visual = hit;
        while (visual != null && visual is not ListBoxItem)
        {
            visual = visual.GetVisualParent();
        }

        if (visual is ListBoxItem listBoxItem && listBoxItem.DataContext is CategoryEntityRow row)
        {
            _model.SelectedEntityItem = row;
        }
    }

    private void OnTagPillPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnTagPillClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_model == null || sender is not Button btn || btn.Tag is not CategoryEntityRow row)
            return;

        if (!this.TryFindResource("TagPickerFlyout", out var res) || res is not Flyout flyout)
            return;

        _model.PrepareTagPicker(row);
        if (flyout.Content is Control content)
            content.DataContext = _model;

        _activeTagFlyout = flyout;
        flyout.ShowAt(btn);
    }

    private void OnTagOptionClicked(object? sender, RoutedEventArgs e)
    {
        _activeTagFlyout?.Hide();
    }
}
