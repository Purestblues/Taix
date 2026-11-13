using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    private readonly IWebData _webDataService;
    private readonly ICategorys _categoryService;
    private readonly INavigationService _navigationService;
    private readonly IAppConfig _appConfig;
    private readonly IStateService _stateService;
    private readonly IAppEventService _appEventService;
    private readonly IEntityData _entityData;
    private readonly IAppData _appDataService;

    // 图表原始数据缓存（换主题时本地重建，不重复请求）
    private EntityHoursModel? _entityHours;
    private double[] _compareTotalsAll = [];
    private IReadOnlyList<CategoryModel>? _columnCategories;

    public IndexPageViewModel(
        IWebData webData,
        ICategorys categories,
        INavigationService navigationService,
        IAppConfig appConfig,
        IStateService stateService,
        IAppEventService appEventService,
        IEntityData entityData,
        IAppData appData)
    {
        _webDataService = webData;
        _categoryService = categories;
        _navigationService = navigationService;
        _appConfig = appConfig;
        _stateService = stateService;
        _appEventService = appEventService;
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
        // 榜单、总额与图表统一走实体表与实体时段矩阵（应用与网站同台对齐、总额一致）
        var buckets = isHourly ? "hour" : "day";
        var entityTask = _entityData.GetEntityListAsync(start, end, IsWebBrowserMerged, cancellationToken);
        var prevEntityTask = _entityData.GetEntityListAsync(prevStart, prevEnd, IsWebBrowserMerged, cancellationToken);
        var hoursTask = _entityData.GetEntityHoursAsync(start, end, buckets, IsWebBrowserMerged, cancellationToken);
        var prevHoursTask = _entityData.GetEntityHoursAsync(prevStart, prevEnd, buckets, IsWebBrowserMerged, cancellationToken);

        var pageCountTask = _webDataService.GetBrowsePagesTotalAsync(start, end, cancellationToken);
        var prevPageCountTask = _webDataService.GetBrowsePagesTotalAsync(prevStart, prevEnd, cancellationToken);

        var categoriesTask = _categoryService.GetCategoriesAsync(cancellationToken);

        HasEntityError = false;
        EntityErrorText = string.Empty;

        await Task.WhenAll(pageCountTask, prevPageCountTask, categoriesTask);
        cancellationToken.ThrowIfCancellationRequested();

        EntityListModel? entity;
        EntityListModel? prevEntity;
        EntityHoursModel? hours;
        EntityHoursModel? prevHours;
        try
        {
            entity = await entityTask;
            prevEntity = await prevEntityTask;
            hours = await hoursTask;
            prevHours = await prevHoursTask;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // 实体接口是新东西：服务端没升级时这里是 404，不能表现成「今天没用电脑」
            HasEntityError = true;
            EntityErrorText = ChartDataMapper.FormatEntityLoadError(ex);
            Logger.Warn($"实体表加载失败：{ex.Message}");
            return;
        }

        var rows = entity?.Rows ?? [];
        var appRows = rows.Where(row => row.Kind != "site").ToList();
        var siteRows = rows.Where(row => row.Kind == "site").ToList();

        // 榜单
        CompareCaption = compareCaption;
        HourLabels = labels;

        var appChartData = appRows.Select(ToChartRow).ToList();
        WeekData = appChartData;
        AppFrequentUseData = appChartData.Take(FrequentUseNum).ToList();

        var webChartData = siteRows.Select(ToChartRow).ToList();
        WebFrequentUseData = webChartData.Take(FrequentUseNum).ToList();

        MoreData = appChartData.Skip(FrequentUseNum)
            .Concat(webChartData.Skip(FrequentUseNum))
            .OrderByDescending(x => x.Value)
            .Take(MoreNum)
            .ToList();

        // 概览卡片：总额取实体表的总额
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

        // 缓存原始矩阵，换主题时本地重建即可
        _entityHours = hours;
        _columnCategories = await categoriesTask;
        _compareTotalsAll = ChartDataMapper.SumAllBuckets(prevHours, labels.Length);

        ApplyChartSeries();
    }

    /// <summary>实体行 → 榜单/卡片模型；分类徽章与合并成员说明都挂在这里</summary>
    private static ChartsDataModel ToChartRow(EntityModel entity) =>
        ChartDataMapper.MapFromEntity(entity, includeBadges: true);

    private void ApplyChartSeries()
    {
        var series = ChartDataMapper.BuildUnifiedCategorySeries(
            _entityHours,
            ChartDataMapper.ToVisualMap(_columnCategories, ResourceStrings.Uncategorized),
            ResourceStrings.Uncategorized);

        HourSeries = series;
        IsCompareSupported = true;
        CompareValues = _compareTotalsAll;
        HourTotalText = Time.ToString((int)series.Sum(m => m.Value));

        CategorySeries = series
            .Select(m => new ChartsDataModel
            {
                Name = m.Name,
                Icon = m.Icon,
                Color = m.Color,
                Value = m.Value,
                Tag = m.Tag
            })
            .ToList();

        CategoryTotalText = Time.ToString((int)CategorySeries.Sum(m => m.Value));
        ChartDonutCaption = ResourceStrings.Total;
    }

    /// <summary>主题切换后按新背景重算分类色，数据本身不用重新请求</summary>
    private void OnThemeChanged()
    {
        if (_entityHours == null) return;
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

        _navigationService.NavigateTo(nameof(CategorySummaryPage),
            CategorySummaryNavigationContext.Create(CategorySummaryKind.Unified, badge.CategoryId, badge.Name));
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
                if (entity.Kind == "site" || entity.Key.StartsWith("site:", StringComparison.Ordinal))
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
        _entityHours = null;
        _compareTotalsAll = [];
        CompareValues = [];
        WeekData = [];
        AppFrequentUseData = [];
        WebFrequentUseData = [];
        MoreData = [];
        HourSeries = [];
        HourLabels = [];
        CategorySeries = [];
        base.Dispose();
    }

}
