using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using ShiJian.Client.Controls.Charts.Model;
using ShiJian.Client.Controls.Select;
using ShiJian.Client.Events;
using ShiJian.Client.Foundation;
using ShiJian.Client.Foundation.Rx;
using ShiJian.Client.Librarys;
using ShiJian.Client.Models;
using ShiJian.Client.Models.Navigation;
using ShiJian.Client.Servicers;
using ShiJian.Client.Servicers.Interfaces;
using ShiJian.Client.Shared.Librarys;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Data;
using ShiJian.Client.Shared.Models.Web;
using ShiJian.Client.Shared.Servicers.Interfaces;
using ShiJian.Client.Views;

namespace ShiJian.Client.ViewModels;

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
    private double[] _compareTotalsAll = [];
    private IReadOnlyList<CategoryModel>? _appColumnCategories;
    private IReadOnlyList<WebSiteCategoryModel>? _webColumnCategories;

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
        CategoryClickCommand = AsyncRelayCommand.Create<object>(OnCategoryClick).DisposeWith(Disposables);
        RefreshCommand = AsyncRelayCommand.CreateFromTask<object>(OnRefreshAsync).DisposeWith(Disposables);

        InitializeStaticData();

        ChartColorHelper.ThemeChanged += OnThemeChanged;

        RefreshOnChange(
            _appEventService.AppChanged,
            _appEventService.WebSiteChanged,
            LoadDataAsync);
    }

    public ICommand ToDetailCommand { get; }
    public ICommand CategoryClickCommand { get; }
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
            },
            new SelectItemModel
            {
                Id = 2,
                Name = ResourceStrings.Custom
            }
        ];
        SelectedPeriod = PeriodOptions[0];

        WhenPropertyChanged(this, x => x.TabbarSelectedIndex, _ => LoadDataAsync());
        WhenPropertyChanged(this, x => x.CustomStart, _ => OnCustomRangeChangedAsync());
        WhenPropertyChanged(this, x => x.CustomEnd, _ => OnCustomRangeChangedAsync());

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

    /// <summary>换日期只在意「自定义」模式下重新拉数据，其他周期不受影响</summary>
    private Task OnCustomRangeChangedAsync() =>
        TabbarSelectedIndex == 2 ? LoadDataAsync() : Task.CompletedTask;

    private Task LoadDataAsync()
    {
        return ExecuteAsync(async ct =>
        {
            var today = DateTime.Now.Date;
            switch (TabbarSelectedIndex)
            {
                case 0:
                    await LoadRangeAsync(today, today, today.AddDays(-1), today.AddDays(-1), true,
                        ResourceStrings.CategoryVsYesterday, HourLabels24(), ct);
                    break;
                case 1:
                {
                    var week = Time.GetThisWeekDate();
                    await LoadRangeAsync(week[0], week[1], week[0].AddDays(-7), week[1].AddDays(-7), false,
                        ResourceStrings.CategoryVsLastWeek, WeekDayLabels(), ct);
                    break;
                }
                default:
                {
                    var start = CustomStart;
                    var end = CustomEnd;
                    if (end < start) (start, end) = (end, start);

                    // 再长就不像图了，最多 366 天
                    if ((end - start).TotalDays > 365) end = start.AddDays(365);

                    var days = (end - start).Days + 1;
                    var isHourly = days == 1;
                    var labels = isHourly ? HourLabels24() : DayLabels(start, days);
                    await LoadRangeAsync(start, end, start.AddDays(-days), start.AddDays(-1), isHourly,
                        ResourceStrings.CategoryVsPrevious, labels, ct);
                    break;
                }
            }
        });
    }

    /// <summary>
    /// 一次取齐榜单 + 概览 + 图表所需数据。
    /// 榜单接口已经把每个应用 / 网站的时长带回来了，总数与数量直接在本地算，
    /// 不再为「一个数字」单独发请求（首屏 16 个请求 → 10 个，再加 1 个对比周期数据）。
    /// </summary>
    private async Task LoadRangeAsync(DateTime start, DateTime end,
        DateTime prevStart, DateTime prevEnd, bool isHourly, string compareCaption, string[] labels,
        CancellationToken cancellationToken)
    {
        var appListTask = _dataService.GetDateRangelogListAsync(start, end, cancellationToken: cancellationToken);
        var webListTask = _webDataService.GetDateRangeWebSiteListAsync(start, end, cancellationToken: cancellationToken);
        var prevAppListTask = _dataService.GetDateRangelogListAsync(prevStart, prevEnd, cancellationToken: cancellationToken);
        var prevWebListTask = _webDataService.GetDateRangeWebSiteListAsync(prevStart, prevEnd, cancellationToken: cancellationToken);

        var pageCountTask = _webDataService.GetBrowsePagesTotalAsync(start, end, cancellationToken);
        var prevPageCountTask = _webDataService.GetBrowsePagesTotalAsync(prevStart, prevEnd, cancellationToken);

        var columnTask = isHourly
            ? _dataService.GetCategoryHoursDataAsync(start, cancellationToken)
            : _dataService.GetCategoryRangeDataAsync(start, end, cancellationToken);
        var categoriesTask = _categoryService.GetCategoriesAsync(cancellationToken);

        var webColumnTask = _webDataService.GetBrowseDataByCategoryStatisticsAsync(start, end, cancellationToken);
        var webCategoriesTask = _webDataService.GetWebSiteCategoriesAsync(cancellationToken);

        // 时段对比：只取「上一个周期的分类×时段矩阵」，总时长在本地按列汇总
        var compareColumnTask = isHourly
            ? _dataService.GetCategoryHoursDataAsync(prevStart, cancellationToken)
            : _dataService.GetCategoryRangeDataAsync(prevStart, prevEnd, cancellationToken);

        await Task.WhenAll(appListTask, webListTask, prevAppListTask, prevWebListTask,
            pageCountTask, prevPageCountTask, columnTask, categoriesTask, webColumnTask, webCategoriesTask,
            compareColumnTask);
        cancellationToken.ThrowIfCancellationRequested();

        var appLogs = (await appListTask)?.ToList() ?? [];
        var webSites = (await webListTask)?.ToList() ?? [];
        var prevAppLogs = (await prevAppListTask)?.ToList() ?? [];
        var prevWebSites = (await prevWebListTask)?.ToList() ?? [];

        // 榜单
        CompareCaption = compareCaption;
        HourLabels = labels;

        var appChartData = ChartDataMapper.MapFromDailyLogs(appLogs, includeBadges: true);
        WeekData = appChartData;
        AppFrequentUseData = appChartData.Take(FrequentUseNum).ToList();
        AppMoreData = appChartData.Skip(FrequentUseNum).Take(MoreNum).ToList();

        var webChartData = ChartDataMapper.MapFromWebSites(webSites, includeBadges: true);
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
        _appColumns = await columnTask ?? [];
        _webColumns = await webColumnTask ?? [];
        _appColumnCategories = await categoriesTask;
        _webColumnCategories = await webCategoriesTask;

        // 对比列：上一个周期的每桶总量（网站维度没有小时级数据，那边不展示对比）
        _compareTotalsAll = SumByBucket(await compareColumnTask, labels.Length);

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
        // 网站维度没有上一周期的同粒度数据，不显示对比柱
        IsCompareSupported = !useWeb;
        CompareValues = useWeb ? [] : _compareTotalsAll;
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

    /// <summary>把「分类×时段矩阵」按列加总成每个时段的对比总量</summary>
    private static double[] SumByBucket(IReadOnlyList<ColumnDataModel>? columns, int count)
    {
        var totals = new double[count];
        if (columns == null) return totals;

        foreach (var column in columns)
        {
            var values = column.Values;
            if (values == null) continue;

            var length = Math.Min(values.Length, count);
            for (var i = 0; i < length; i++) totals[i] += values[i];
        }

        return totals;
    }

    private static string[] WeekDayLabels() =>
    [
        ResourceStrings.Monday, ResourceStrings.Tuesday, ResourceStrings.Wednesday,
        ResourceStrings.Thursday, ResourceStrings.Friday, ResourceStrings.Saturday, ResourceStrings.Sunday
    ];

    /// <summary>自定义范围：每根柱子对应一天，刻度用 M/d</summary>
    private static string[] DayLabels(DateTime start, int days)
    {
        var labels = new string[days];
        for (var i = 0; i < days; i++)
            labels[i] = start.AddDays(i).ToString("M/d");

        return labels;
    }

    /// <summary>点分类徽章 → 该分类在这段时间的汇总</summary>
    private void OnCategoryClick(object obj)
    {
        if (obj is not ChartBadgeModel badge || badge.CategoryId <= 0) return;

        var kind = badge.IsWebCategory ? CategorySummaryKind.Web : CategorySummaryKind.App;
        _navigationService.NavigateTo(nameof(CategorySummaryPage),
            CategorySummaryNavigationContext.Create(kind, badge.CategoryId, badge.Name));
    }

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
        _compareTotalsAll = [];
        CompareValues = [];
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
