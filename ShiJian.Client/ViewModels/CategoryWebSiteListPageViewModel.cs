using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ShiJian.Client.Controls.Base;
using ShiJian.Client.Controls.Select;
using ShiJian.Client.Controls.Window;
using ShiJian.Client.Events;
using ShiJian.Client.Foundation;
using ShiJian.Client.Foundation.Rx;
using ShiJian.Client.Models;
using ShiJian.Client.Shared.Librarys;
using ShiJian.Client.Servicers.Interfaces;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Web;
using ShiJian.Client.Shared.Servicers.Interfaces;
using ShiJian.Client.Views;

namespace ShiJian.Client.ViewModels;

public class CategoryWebSiteListPageViewModel : CategoryWebSiteListPageModel
{
    private readonly INavigationDataService _navigationData;
    private readonly INavigationService _navigationService;
    private readonly IToastService _toastService;
    private readonly ICategorys _categoryService;
    private readonly IWebData _webDataService;
    private readonly IAppEventService _appEventService;

    private List<OptionModel> _webSiteOptionsTemp = [];
    private IDisposable? _searchSubscription;

    public CategoryWebSiteListPageViewModel(
        INavigationDataService navigationData,
        INavigationService navigationService,
        IToastService toastService,
        ICategorys categoryService,
        IWebData webData,
        IAppEventService appEventService)
    {
        _navigationData = navigationData;
        _navigationService = navigationService;
        _toastService = toastService;
        _categoryService = categoryService;
        _webDataService = webData;
        _appEventService = appEventService;
        Category = _navigationData.Data switch
        {
            CategoryModel unified => unified,
            WebSiteCategoryModel legacy => new CategoryModel
            {
                ID = legacy.ID,
                Name = legacy.Name,
                IconFile = legacy.IconFile,
                Color = legacy.Color,
                IsSystem = legacy.IsSystem,
                IsUrlMatch = legacy.IsUrlMatch,
                UrlPatterns = legacy.UrlPatterns
            },
            _ => new CategoryModel()
        };

        ShowChooseCommand = AsyncRelayCommand.CreateFromTask<object>(OnShowChooseAsync).DisposeWith(Disposables);
        ChoosedCommand = AsyncRelayCommand.Create<object>(OnChoosed).DisposeWith(Disposables);
        GotoDetailCommand = AsyncRelayCommand.Create<object>(OnGotoDetail).DisposeWith(Disposables);
        ChooseCloseCommand = AsyncRelayCommand.Create<object>(OnChooseClose).DisposeWith(Disposables);
        DelCommand = AsyncRelayCommand.CreateFromTask<object>(OnDelAsync).DisposeWith(Disposables);

        _searchSubscription = ObservablePropertyChangedExtensions.WhenPropertyChanged(this, x => x.SearchInput)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(AvaloniaContextScheduler.Instance)
            .Subscribe(DoSearch);
        _searchSubscription.DisposeWith(Disposables);

        RefreshOnChange(
            _appEventService.WebSiteChanged
                .Where(e => e.ChangeType.HasFlag(AppChangeType.WebSiteCategory)),
            RefreshAsync);
    }

    public override Task OnNavigatedToAsync()
    {
        _ = ExecuteAsync(LoadDataCoreAsync);
        return Task.CompletedTask;
    }

    public override Task RefreshAsync() => ExecuteAsync(LoadDataCoreAsync);

    public ICommand ShowChooseCommand { get; }
    public ICommand ChoosedCommand { get; }
    public ICommand GotoDetailCommand { get; }
    public ICommand ChooseCloseCommand { get; }
    public ICommand DelCommand { get; }

    private async Task LoadDataCoreAsync(CancellationToken cancellationToken)
    {
        if (Category == null)
        {
            _toastService.Toast(ResourceStrings.InvalidParameter, ToastType.Error, IconTypes.Error);
            return;
        }

        var list = await _webDataService.GetWebSitesAsync(Category.ID);
        cancellationToken.ThrowIfCancellationRequested();
        CategoryWebSiteList = new ObservableCollection<WebSiteModel>(list);
    }

    private async Task LoadChooserDataAsync(CancellationToken cancellationToken = default)
    {
        var unsetList = await _webDataService.GetUnSetCategoryWebSitesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var combinedList = (unsetList ?? []).Concat(CategoryWebSiteList ?? [])
            .OrderByDescending(m => m.CategoryID == Category.ID)
            .ThenBy(m => m.Title)
            .ToList();

        var optionList = new List<OptionModel>();
        foreach (var site in combinedList)
        {
            optionList.Add(new OptionModel
            {
                IsChecked = site.CategoryID == Category.ID,
                OptionValue = new SelectItemModel
                {
                    Name = $"{site.Title} - {site.Domain}",
                    Img = site.IconFile
                },
                WebSite = site
            });
        }
        _webSiteOptionsTemp = optionList;

        WebSiteOptionList.Clear();
        foreach (var item in _webSiteOptionsTemp)
            WebSiteOptionList.Add(item);
    }

    private async Task OnDelAsync(object obj)
    {
        if (SelectedItem != null)
        {
            var categories = await _categoryService.GetCategoriesAsync();
            var systemCategory = categories.FirstOrDefault(c => c.IsSystem);
            var categoryId = systemCategory?.ID ?? 0;

            await _webDataService.UpdateWebSitesCategoryAsync(new[] { SelectedItem.ID }, categoryId);
            
            var removedSite = SelectedItem with { CategoryID = categoryId, Category = ToWebSiteCategoryModel(systemCategory) };
            _appEventService.PublishWebSiteChanged(removedSite, AppChangeType.WebSiteCategory);
            
            CategoryWebSiteList.Remove(SelectedItem);
            if (CategoryWebSiteList.Count == 0) CategoryWebSiteList = new ObservableCollection<WebSiteModel>();

            await ExecuteAsync(LoadDataCoreAsync);
        }
    }

    private void OnChooseClose(object obj)
    {
        ChooseVisibility = false;
        SearchInput = "";
    }

    private void DoSearch(string? keyword)
    {
        if (string.IsNullOrEmpty(keyword))
        {
            SyncToList(_webSiteOptionsTemp);
            return;
        }

        var filtered = _webSiteOptionsTemp
            .Where(m =>
                (!string.IsNullOrEmpty(m.WebSite.Title) && WildcardHelper.IsMatch(m.WebSite.Title, keyword)) ||
                (!string.IsNullOrEmpty(m.WebSite.Domain) && WildcardHelper.IsMatch(m.WebSite.Domain, keyword)))
            .ToList();
        SyncToList(filtered);
    }

    private void SyncToList(List<OptionModel> targetList)
    {
        var currentSet = new HashSet<OptionModel>(WebSiteOptionList);
        var targetSet = new HashSet<OptionModel>(targetList);

        var toRemove = currentSet.Except(targetSet).ToList();
        var toAdd = targetSet.Except(currentSet).ToList();

        foreach (var item in toRemove)
            WebSiteOptionList.Remove(item);

        var insertIndex = 0;
        foreach (var item in targetList)
        {
            if (toAdd.Contains(item))
            {
                WebSiteOptionList.Insert(insertIndex, item);
            }
            insertIndex++;
        }
    }

    private void OnGotoDetail(object obj)
    {
        if (SelectedItem != null)
        {
            _navigationService.NavigateTo(nameof(WebSiteDetailPage), SelectedItem);
        }
    }

    private void OnChoosed(object obj)
    {
        ChooseVisibility = false;
        SearchInput = "";
        _ = UpdateCategoryAsync();
    }

    private async Task UpdateCategoryAsync()
    {
        var userCheckedIds = _webSiteOptionsTemp
            .Where(m => m.IsChecked)
            .Select(m => m.WebSite.ID)
            .ToList();

        var toRemove = CategoryWebSiteList
            .Where(m => !userCheckedIds.Contains(m.ID))
            .Select(m => m.ID)
            .ToList();
        var toAdd = userCheckedIds
            .Where(id => !CategoryWebSiteList.Any(s => s.ID == id))
            .ToList();

        foreach (var id in toRemove)
        {
            var item = CategoryWebSiteList.FirstOrDefault(m => m.ID == id);
            if (item != null) CategoryWebSiteList.Remove(item);
        }

        var addSites = _webSiteOptionsTemp
            .Where(m => toAdd.Contains(m.WebSite.ID))
            .Select(m => m.WebSite)
            .ToList();
        foreach (var item in addSites)
            CategoryWebSiteList.Add(item);

        if (toRemove.Count > 0)
        {
            var categories = await _categoryService.GetCategoriesAsync();
            var systemCategory = categories.FirstOrDefault(c => c.IsSystem);
            var removeCategoryId = systemCategory?.ID ?? 0;
            await _webDataService.UpdateWebSitesCategoryAsync(toRemove.ToArray(), removeCategoryId);
            
            var legacySystem = ToWebSiteCategoryModel(systemCategory);
            foreach (var siteId in toRemove)
            {
                var site = _webSiteOptionsTemp.FirstOrDefault(m => m.WebSite.ID == siteId)?.WebSite;
                if (site != null)
                    _appEventService.PublishWebSiteChanged(site with { CategoryID = removeCategoryId, Category = legacySystem }, AppChangeType.WebSiteCategory);
            }
        }

        if (toAdd.Count > 0 && Category != null)
        {
            await _webDataService.UpdateWebSitesCategoryAsync(toAdd.ToArray(), Category.ID);
            
            var legacyCategory = ToWebSiteCategoryModel(Category);
            foreach (var site in addSites)
                _appEventService.PublishWebSiteChanged(site with { CategoryID = Category.ID, Category = legacyCategory }, AppChangeType.WebSiteCategory);
        }
    }

    private static WebSiteCategoryModel? ToWebSiteCategoryModel(CategoryModel? category) =>
        category == null
            ? null
            : new WebSiteCategoryModel
            {
                ID = category.ID,
                Name = category.Name,
                IconFile = category.IconFile,
                Color = category.Color,
                IsSystem = category.IsSystem,
                IsUrlMatch = category.IsUrlMatch,
                UrlPatterns = category.UrlPatterns
            };

    private async Task OnShowChooseAsync(object obj)
    {
        ChooseVisibility = true;
        SearchInput = "";
        await ExecuteAsync(LoadChooserDataAsync);
    }
}
