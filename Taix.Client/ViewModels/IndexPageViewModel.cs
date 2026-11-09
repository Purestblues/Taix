using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Taix.Client.Controls.Charts.Model;
using Taix.Client.Controls.Select;
using Taix.Client.Events;
using Taix.Client.Foundation;
using Taix.Client.Foundation.Rx;
using Taix.Client.Librarys;
using Taix.Client.Models;
using Taix.Client.Models.Navigation;
using Taix.Client.Servicers;
using Taix.Client.Servicers.Interfaces;
using Taix.Client.Shared.Librarys;
using Taix.Client.Shared.Models;
using Taix.Client.Shared.Models.Data;
using Taix.Client.Shared.Models.Web;
using Taix.Client.Shared.Servicers.Interfaces;
using Taix.Client.Views;

namespace Taix.Client.ViewModels;

public partial class IndexPageViewModel : IndexPageModel
{
    private readonly IData _dataService;
    private readonly IWebData _webDataService;
    private readonly ICategorys _categoryService;
    private readonly INavigationService _navigationService;
    private readonly IAppConfig _appConfig;
    private readonly IStateService _stateService;
    private readonly IAppEventService _appEventService;

    // 图表原始数据缓存（切应用/网站时本地重建，不重复请求）
    private IReadOnlyList<ColumnDataModel> _appColumns = [];
    private IReadOnlyList<ColumnDataModel> _webColumns = [];
    private IReadOnlyList<CategoryModel>? _appColumnCategories;
    private IReadOnlyList<WebSiteCategoryModel>? _webColumnCategories;
    private bool _isWeek;

    public IndexPageViewModel(
        IData data,
        IWebData webData,
        ICategorys categories,
        INavigationService navigationService,
        IAppConfig appConfig,
        IStateService stateService,
        IAppEventService appEventService)
    {
        _dataService = data;
        _webDataService = webData;
        _categoryService = categories;
        _navigationService = navigationService;
        _appConfig = appConfig;
        _stateService = stateService;
        _appEventService = appEventService;

        ToDetailCommand = AsyncRelayCommand.Create<object>(OnToDetail).DisposeWith(Disposables);
        RefreshCommand = AsyncRelayCommand.CreateFromTask<object>(OnRefreshAsync).DisposeWith(Disposables);

        InitializeStaticData();

        ChartColorHelper.ThemeChanged += OnThemeChanged;

        RefreshOnChange(
            _appEventService.AppChanged,
            _appEventService.WebSiteChanged,
            LoadDataAsync);
    }

    public ICommand ToDetailCommand { get; }
    public ICommand RefreshCommand { get; }


    private void InitializeStaticData()
    {
        var config = _appConfig.GetConfig();
        FrequentUseNum = config.General.IndexPageFrequentUseNum + 1;
        MoreNum = config.General.IndexPageMoreNum + 1;

        PeriodOptions =
        [
            new SelectItemModel
            {
                Id = 0,
                Name = Application.Current?.FindResource("Today") as string ?? "Today"
            },
            new SelectItemModel
            {
                Id = 1,
                Name = Application.Current?.FindResource("ThisWeek") as string ?? "This Week"
            }
        ];
        SelectedPeriod = PeriodOptions[0];

        WhenPropertyChanged(this, x => x.TabbarSelectedIndex, _ => LoadDataAsync());

        MoreTypeOptions =
        [
            new SelectItemModel
            {
                Id = 0,
                Name = Application.Current?.FindResource("App") as string ?? "App"
            },
            new SelectItemModel
            {
                Id = 1,
                Name = Application.Current?.FindResource("Website") as string ?? "Website"
            }
        ];
        MoreType = MoreTypeOptions[0];

        ChartTypeOptions =
        [
            new SelectItemModel
            {
                Id = 0,
                Name = Application.Current?.FindResource("App") as string ?? "App"
            },
            new SelectItemModel
            {
                Id = 1,
                Name = Application.Current?.FindResource("Website") as string ?? "Website"
            }
        ];
        ChartType = ChartTypeOptions[0];

        // 切应用/网站不需要重新请求，直接用已经缓存的分类×时段数据重建序列
        WhenPropertyChanged(this, x => x.ChartType, _ =>
        {
            ApplyChartSeries();
            return Task.CompletedTask;
        });
    }

    public override Task OnNavigatedToAsync()
    {
        var restored = TryRestoreState(_navigationService, _stateService);
        return RestoreCachedDataOrLoadAsync(
            restored, _stateService, _appEventService, LoadDataAsync);
    }

    public override void OnNavigatedFrom()
    {
        SaveState(_stateService);
        SaveCachedDataVersion(_stateService, _appEventService);
        base.OnNavigatedFrom();
    }

    private Task OnRefreshAsync(object _) => LoadDataAsync();

    public override Task RefreshAsync() => LoadDataAsync();

    private Task LoadDataAsync()
    {
        return ExecuteAsync(async ct =>
        {
            if (TabbarSelectedIndex == 0)
            {
                var today = DateTime.Now.Date;
                await LoadRangeAsync(today, today, today.AddDays(-1), today.AddDays(-1), false, ct);
            }
            else if (TabbarSelectedIndex == 1)
            {
                var week = Time.GetThisWeekDate();
                await LoadRangeAsync(week[0], week[1], week[0].AddDays(-7), week[1].AddDays(-7), true, ct);
            }
        });
    }

    /// <summary>
    /// 一次取齐榜单 + 概览 + 图表所需数据。
    /// 榜单接口已经把每个应用 / 网站的时长带回来了，总数与数量直接在本地算，
    /// 不再为「一个数字」单独发请求（首屏 16 个请求 → 10 个）。
    /// </summary>
    private async Task LoadRangeAsync(DateTime start, DateTime end,
        DateTime prevStart, DateTime prevEnd, bool isWeek, CancellationToken cancellationToken)
    {
        var appListTask = _dataService.GetDateRangelogListAsync(start, end, cancellationToken: cancellationToken);
        var webListTask = _webDataService.GetDateRangeWebSiteListAsync(start, end, cancellationToken: cancellationToken);
        var prevAppListTask = _dataService.GetDateRangelogListAsync(prevStart, prevEnd, cancellationToken: cancellationToken);
        var prevWebListTask = _webDataService.GetDateRangeWebSiteListAsync(prevStart, prevEnd, cancellationToken: cancellationToken);

        var pageCountTask = _webDataService.GetBrowsePagesTotalAsync(start, end, cancellationToken);
        var prevPageCountTask = _webDataService.GetBrowsePagesTotalAsync(prevStart, prevEnd, cancellationToken);

        var columnTask = isWeek
            ? _dataService.GetCategoryRangeDataAsync(start, end, cancellationToken)
            : _dataService.GetCategoryHoursDataAsync(start, cancellationToken);
        var categoriesTask = _categoryService.GetCategoriesAsync(cancellationToken);

        var webColumnTask = _webDataService.GetBrowseDataByCategoryStatisticsAsync(start, end, cancellationToken);
        var webCategoriesTask = _webDataService.GetWebSiteCategoriesAsync(cancellationToken);

        await Task.WhenAll(appListTask, webListTask, prevAppListTask, prevWebListTask,
            pageCountTask, prevPageCountTask, columnTask, categoriesTask, webColumnTask, webCategoriesTask);
        cancellationToken.ThrowIfCancellationRequested();

        var appLogs = (await appListTask)?.ToList() ?? [];
        var webSites = (await webListTask)?.ToList() ?? [];
        var prevAppLogs = (await prevAppListTask)?.ToList() ?? [];
        var prevWebSites = (await prevWebListTask)?.ToList() ?? [];

        // 榜单
        CompareCaption = isWeek ? ResourceStrings.CategoryVsLastWeek : ResourceStrings.CategoryVsYesterday;

        var appChartData = ChartDataMapper.MapFromDailyLogs(appLogs);
        WeekData = appChartData;
        AppFrequentUseData = appChartData.Take(FrequentUseNum).ToList();
        AppMoreData = appChartData.Skip(FrequentUseNum).Take(MoreNum).ToList();

        var webChartData = ChartDataMapper.MapFromWebSites(webSites);
        WebFrequentUseData = webChartData.Take(FrequentUseNum).ToList();
        WebMoreData = webChartData.Skip(FrequentUseNum).Take(MoreNum).ToList();

        // 概览卡片（总数 = 榜单各行之和，与图表口径一致）
        var appTotal = appLogs.Sum(m => (double)m.Time);
        var prevAppTotal = prevAppLogs.Sum(m => (double)m.Time);
        var webTime = webSites.Sum(m => (double)m.Duration);
        var prevWebTime = prevWebSites.Sum(m => (double)m.Duration);

        TotalTime = appTotal + webTime;
        LastTotalTime = prevAppTotal + prevWebTime;
        TotalTimeText = Time.ToString((int)TotalTime);

        AppCount = appLogs.Count;
        LastAppCount = prevAppLogs.Count;

        WebTime = webTime;
        LastWebTime = prevWebTime;
        WebTimeText = Time.ToString((int)webTime);

        WebSiteCount = webSites.Count;
        LastWebSiteCount = prevWebSites.Count;
        WebPageCount = await pageCountTask;
        LastWebPageCount = await prevPageCountTask;

        // 原始矩阵先存起来，切换"应用/网站"时只需本地重建，不再打服务端
        _isWeek = isWeek;
        _appColumns = await columnTask ?? [];
        _webColumns = await webColumnTask ?? [];
        _appColumnCategories = await categoriesTask;
        _webColumnCategories = await webCategoriesTask;

        ApplyChartSeries();
    }

    /// <summary>
    /// 把缓存的服务端"分类×时段矩阵"拆成时段堆叠序列与分类占比序列。
    /// 切应用/网站、或数据刷新后都走这里。
    /// </summary>
    private void ApplyChartSeries()
    {
        var useWeb = ChartType?.Id == 1;

        var columns = useWeb ? _webColumns : _appColumns;
        var visuals = useWeb ? BuildWebCategoryMap(_webColumnCategories) : BuildAppCategoryMap(_appColumnCategories);

        var series = new List<ChartsDataModel>(columns.Count);
        foreach (var column in columns)
        {
            if (!visuals.TryGetValue(column.CategoryID, out var visual))
                visual = UnknownCategory;

            series.Add(new ChartsDataModel
            {
                Name = visual.Name,
                Icon = visual.Icon,
                Color = ChartColorHelper.Resolve(visual.Color, visual.IsSystem),
                Values = column.Values ?? []
            });
        }

        series = series.OrderByDescending(m => m.Values.Sum()).ToList();

        HourSeries = series;
        HourLabels = _isWeek ? WeekDayLabels() : HourLabels24();
        HourTotalText = Time.ToString((int)series.Sum(m => m.Values.Sum()));

        CategorySeries = series
            .Select(m => new ChartsDataModel
            {
                Name = m.Name,
                Icon = m.Icon,
                Color = m.Color,
                Value = m.Values.Sum(),
                Tag = Time.ToString((int)m.Values.Sum())
            })
            .Where(m => m.Value > 0)
            .ToList();

        CategoryTotalText = Time.ToString((int)CategorySeries.Sum(m => m.Value));
        ChartDonutCaption = useWeb ? ResourceStrings.Website : ResourceStrings.App;
    }

    private sealed record CategoryVisual(string Name, string? Icon, string? Color, bool IsSystem);

    /// <summary>没匹配到分类的桶：按系统分类处理，最终落到主题色</summary>
    private static CategoryVisual UnknownCategory =>
        new(ResourceStrings.Uncategorized, null, null, true);

    private static Dictionary<int, CategoryVisual> BuildAppCategoryMap(IReadOnlyList<CategoryModel>? categories)
    {
        var map = new Dictionary<int, CategoryVisual>();
        if (categories == null) return map;

        foreach (var item in categories)
        {
            // 只存原始色，解析放到 ApplyChartSeries：换主题时同一份数据能直接重算
            map[item.ID] = new CategoryVisual(
                item.Name ?? ResourceStrings.Uncategorized,
                item.IconFile,
                item.Color,
                item.IsSystem);
        }

        return map;
    }

    private static Dictionary<int, CategoryVisual> BuildWebCategoryMap(IReadOnlyList<WebSiteCategoryModel>? categories)
    {
        var map = new Dictionary<int, CategoryVisual>();
        if (categories == null) return map;

        foreach (var item in categories)
        {
            map[item.ID] = new CategoryVisual(
                item.Name ?? ResourceStrings.Uncategorized,
                item.IconFile,
                item.Color,
                item.IsSystem);
        }

        return map;
    }

    /// <summary>主题切换后按新背景重算分类色，数据本身不用重新请求</summary>
    private void OnThemeChanged()
    {
        if (_appColumns.Count == 0 && _webColumns.Count == 0) return;
        ApplyChartSeries();
    }

    private static string[] HourLabels24()
    {
        var labels = new string[24];
        for (var i = 0; i < 24; i++) labels[i] = i.ToString();
        return labels;
    }

    private static string[] WeekDayLabels() =>
    [
        ResourceStrings.Monday, ResourceStrings.Tuesday, ResourceStrings.Wednesday,
        ResourceStrings.Thursday, ResourceStrings.Friday, ResourceStrings.Saturday, ResourceStrings.Sunday
    ];

    private void OnToDetail(object obj)
    {
        if (obj is not ChartsDataModel chartData) return;
        var date = DateTime.Now.Date;

        if (chartData.Data is DailyLogModel { AppModel: not null } dailyModel)
            _navigationService.NavigateTo(nameof(DetailPage), DetailNavigationContext.Create(dailyModel.AppModel, TabbarSelectedIndex, date));
        else if (chartData.Data is WebSiteModel webSiteModel)
            _navigationService.NavigateTo(nameof(WebSiteDetailPage), WebSiteDetailNavigationContext.Create(webSiteModel, TabbarSelectedIndex, date));
    }

    public override void Dispose()
    {
        ChartColorHelper.ThemeChanged -= OnThemeChanged;
        WeekData = [];
        AppFrequentUseData = [];
        AppMoreData = [];
        WebFrequentUseData = [];
        WebMoreData = [];
        HourSeries = [];
        HourLabels = [];
        CategorySeries = [];
        base.Dispose();
    }

}
