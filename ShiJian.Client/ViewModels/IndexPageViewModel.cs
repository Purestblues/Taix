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
using ShiJian.Client.Logging;
using ShiJian.Client.Models;
using ShiJian.Client.Models.Navigation;
using ShiJian.Client.Servicers;
using ShiJian.Client.Servicers.Interfaces;
using ShiJian.Client.Shared.Librarys;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Data;
using ShiJian.Client.Shared.Models.Entity;
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
    private readonly IAppMergeService _appMergeService;
    private readonly IEntityData _entityData;
    private readonly IAppData _appDataService;

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
        IAppEventService appEventService,
        IAppMergeService appMergeService,
        IEntityData entityData,
        IAppData appData)
    {
        _dataService = data;
        _webDataService = webData;
        _categoryService = categories;
        _navigationService = navigationService;
        _appConfig = appConfig;
        _stateService = stateService;
        _appEventService = appEventService;
        _appMergeService = appMergeService;
        _entityData = entityData;
        _appDataService = appData;

        ToDetailCommand = AsyncRelayCommand.CreateFromTask<object>(OnToDetailAsync).DisposeWith(Disposables);
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

        IsWebBrowserMerged = config.General.WebBrowserMerged;

        WebMergeOptions =
        [
            new SelectItemModel { Id = 0, Name = ResourceStrings.WebListSeparate },
            new SelectItemModel { Id = 1, Name = ResourceStrings.WebListMerged }
        ];
        WebMergeOption = WebMergeOptions[IsWebBrowserMerged ? 1 : 0];
        WhenPropertyChanged(this, x => x.WebMergeOption, option =>
        {
            IsWebBrowserMerged = option.Id == 1;
            return Task.CompletedTask;
        });

        WhenPropertyChanged(this, x => x.TabbarSelectedIndex, _ => LoadDataAsync());
        WhenPropertyChanged(this, x => x.IsWebBrowserMerged, OnWebBrowserMergedChangedAsync);
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

    /// <summary>
    /// 切换「网站并入浏览器」：先落配置（下次启动保持），再重拉实体表。
    /// 实体表由服务端算差额行，客户端不需要本地重算。
    /// </summary>
    private async Task OnWebBrowserMergedChangedAsync(bool merged)
    {
        var config = _appConfig.GetConfig();
        if (config.General.WebBrowserMerged != merged)
        {
            config.General.WebBrowserMerged = merged;
            try
            {
                await _appConfig.SaveAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("保存「网站并入浏览器」开关失败", ex);
            }
        }

        await LoadDataAsync();
    }

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
        // N2：榜单与总额走统一实体表（服务端已算好互斥归属、差额行与合并组），
        // 图表仍然用「分类 × 时段」矩阵（它按分类聚合，不受应用/网站重叠影响）。
        var entityTask = _entityData.GetEntityListAsync(start, end, IsWebBrowserMerged, cancellationToken);
        var prevEntityTask = _entityData.GetEntityListAsync(prevStart, prevEnd, IsWebBrowserMerged, cancellationToken);

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

        await Task.WhenAll(entityTask, prevEntityTask, pageCountTask, prevPageCountTask,
            columnTask, categoriesTask, webColumnTask, webCategoriesTask, compareColumnTask);
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await entityTask;
        var prevEntity = await prevEntityTask;

        var rows = entity?.Rows ?? [];
        var appRows = rows.Where(row => row.Kind != "site").ToList();
        var siteRows = rows.Where(row => row.Kind == "site").ToList();

        // 榜单
        CompareCaption = compareCaption;
        HourLabels = labels;

        var appChartData = appRows.Select(ToChartRow).ToList();
        WeekData = appChartData;
        AppFrequentUseData = appChartData.Take(FrequentUseNum).ToList();
        AppMoreData = appChartData.Skip(FrequentUseNum).Take(MoreNum).ToList();

        var webChartData = siteRows.Select(ToChartRow).ToList();
        WebFrequentUseData = webChartData.Take(FrequentUseNum).ToList();
        WebMoreData = webChartData.Skip(FrequentUseNum).Take(MoreNum).ToList();

        // 概览卡片：总额取实体表的总额（两种视图都是「应用总时长」）
        TotalTime = entity?.TotalSeconds ?? 0;
        LastTotalTime = prevEntity?.TotalSeconds ?? 0;
        TotalTimeText = Time.ToString((int)TotalTime);

        AppCount = appRows.Count;
        LastAppCount = prevEntity?.Rows.Count(row => row.Kind != "site") ?? 0;

        var webTime = siteRows.Sum(row => (double)row.Seconds);
        WebTime = webTime;
        LastWebTime = (prevEntity?.Rows ?? []).Where(row => row.Kind == "site").Sum(row => (double)row.Seconds);
        WebTimeText = Time.ToString((int)webTime);

        WebSiteCount = siteRows.Count;
        LastWebSiteCount = (prevEntity?.Rows ?? []).Count(row => row.Kind == "site");
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

    /// <summary>实体行 → 榜单/卡片模型；分类徽章与合并成员说明都挂在这里</summary>
    private static ChartsDataModel ToChartRow(EntityModel entity)
    {
        var categoryColor = entity.CategoryColor == null
            ? null
            : ChartColorHelper.Resolve(entity.CategoryColor, false);

        var model = new ChartsDataModel
        {
            Data = entity,
            Name = entity.Name,
            Value = entity.Seconds,
            Tag = Time.ToString(entity.Seconds),
            Icon = entity.Icon ?? string.Empty,
            FallbackText = entity.Name,
            CategoryName = entity.CategoryName,
            CategoryColor = categoryColor,
            Color = categoryColor ?? string.Empty
        };

        if (entity.Members.Count > 0)
        {
            var memberText = string.Join(" / ", entity.Members.Select(member => member.Name));
            model.PopupText = entity.Kind == "other"
                ? string.Empty
                : string.Format(ResourceStrings.MergedItems, entity.Members.Count) + "：" + memberText;
        }

        model.BadgeList = [];
        if (!string.IsNullOrWhiteSpace(entity.CategoryName) && entity.CategoryId > 0)
        {
            model.BadgeList.Add(new ChartBadgeModel
            {
                Name = entity.CategoryName!,
                Color = ChartColorHelper.WithAlpha(categoryColor, 0x2E),
                TextColor = categoryColor!,
                CategoryId = entity.CategoryId,
                IsWebCategory = entity.CategoryKind == "site",
                Type = ChartBadgeType.Category
            });
        }

        return model;
    }

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

    private async Task OnToDetailAsync(object obj)
    {
        if (obj is not ChartsDataModel chartData) return;

        var date = DateTime.Now.Date;

        // 实体行只带业务标识，详情页仍然要原始模型：按 Kind 取一次
        try
        {
            if (chartData.Data is EntityModel entity)
            {
                if (entity.Kind == "site")
                {
                    var site = await _webDataService.GetWebSiteAsync(entity.EntityId);
                    if (site != null)
                        _navigationService.NavigateTo(nameof(WebSiteDetailPage),
                            WebSiteDetailNavigationContext.Create(site, TabbarSelectedIndex, date));
                    return;
                }

                var app = await _appDataService.GetAppAsync(entity.EntityId);
                if (app != null)
                    _navigationService.NavigateTo(nameof(DetailPage),
                        DetailNavigationContext.Create(app, TabbarSelectedIndex, date));
                return;
            }

            if (chartData.Data is DailyLogModel { AppModel: not null } dailyModel)
                _navigationService.NavigateTo(nameof(DetailPage), DetailNavigationContext.Create(dailyModel.AppModel, TabbarSelectedIndex, date));
            else if (chartData.Data is WebSiteModel webSiteModel)
                _navigationService.NavigateTo(nameof(WebSiteDetailPage), WebSiteDetailNavigationContext.Create(webSiteModel, TabbarSelectedIndex, date));
        }
        catch (Exception ex)
        {
            Logger.Error("打开详情页失败", ex);
        }
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
