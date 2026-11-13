using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
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
using ShiJian.Client.Shared.Models.Config;
using ShiJian.Client.Shared.Models.Data;
using ShiJian.Client.Shared.Models.Entity;
using ShiJian.Client.Shared.Models.Web;
using ShiJian.Client.Shared.Servicers.Interfaces;
using ShiJian.Client.Views;

namespace ShiJian.Client.ViewModels;

public partial class ChartPageViewModel : ChartPageModel
{
    private readonly ConfigModel _config;
    private readonly IEntityData _entityData;
    private readonly IAppData _appDataService;
    private readonly IWebData _webDataService;
    private readonly ICategorys _categoryService;
    private readonly INavigationService _navigationService;
    private readonly IStateService _stateService;
    private readonly IAppEventService _appEventService;
    private EntityHoursModel? _cachedEntityHours;
    private int _appCount;
    private double _totalTime;

    public ChartPageViewModel(
        IEntityData entityData,
        IAppData appData,
        ICategorys categories,
        INavigationService navigationService,
        IWebData webData,
        IAppConfig appConfig,
        IStateService stateService,
        IAppEventService appEventService)
    {
        _entityData = entityData;
        _appDataService = appData;
        _categoryService = categories;
        _navigationService = navigationService;
        _webDataService = webData;
        _stateService = stateService;
        _config = appConfig.GetConfig();
        _appEventService = appEventService;

        ToDetailCommand = AsyncRelayCommand.CreateFromTask<object>(OnToDetailAsync).DisposeWith(Disposables);
        RefreshCommand = AsyncRelayCommand.CreateFromTask<object>(OnRefreshAsync).DisposeWith(Disposables);

        Initialize();

        ChartColorHelper.ThemeChanged += OnThemeChanged;

        RefreshOnChange(
            _appEventService.AppChanged,
            _appEventService.WebSiteChanged,
            RefreshAsync);
    }

    public ICommand ToDetailCommand { get; }
    public ICommand RefreshCommand { get; }

    private void Initialize()
    {
        TabbarData = [ResourceStrings.Daily, ResourceStrings.Weekly, ResourceStrings.Monthly, ResourceStrings.Yearly];

        PeriodOptions =
        [
            new() { Id = 0, Name = ResourceStrings.Daily },
            new() { Id = 1, Name = ResourceStrings.Weekly },
            new() { Id = 2, Name = ResourceStrings.Monthly },
            new() { Id = 3, Name = ResourceStrings.Yearly }
        ];

        ShowType = ShowTypeOptions[0];
        SelectedPeriod = PeriodOptions[0];
        Date = DateTime.Now;
        WeekDate = DateTime.Now;
        MonthDate = DateTime.Now;
        YearDate = DateTime.Now;
        TabbarSelectedIndex = 0;

        WhenPropertyChanged(this, x => x.Date, _ => OnDateChangedAsync());
        WhenPropertyChanged(this, x => x.TabbarSelectedIndex, _ => OnTabbarChangedAsync());
        WhenPropertyChanged(this, x => x.WeekDate, _ => OnWeekDateChangedAsync());
        WhenPropertyChanged(this, x => x.MonthDate, _ => OnMonthDateChangedAsync());
        WhenPropertyChanged(this, x => x.YearDate, _ => OnYearDateChangedAsync());
        WhenPropertyChanged(this, x => x.ShowType, _ => OnShowTypeChangedAsync());
        ObservablePropertyChangedExtensions.WhenPropertyChanged(this, x => x.ColumnSelectedIndex)
            .ObserveOn(AvaloniaContextScheduler.Instance)
            .Select(_ => RxObservable.FromAsync(async ct => await LoadSelectedColDataAsync(ct)))
            .Switch()
            .Subscribe()
            .DisposeWith(Disposables);


        WhenPropertyChanged(this, x => x.SelectedPeriod, p =>
        {
            if (p != null) TabbarSelectedIndex = p.Id;
            return Task.CompletedTask;
        });
        ObservablePropertyChangedExtensions.WhenPropertyChanged(this, x => x.WebColSelectedIndex)
            .ObserveOn(AvaloniaContextScheduler.Instance)
            .Select(_ => RxObservable.FromAsync(async ct => await LoadWebSitesColSelectedDataAsync(ct)))
            .Switch()
            .Subscribe()
            .DisposeWith(Disposables);
    }

    public override Task OnNavigatedToAsync()
    {
        var restored = TryRestoreState(_navigationService, _stateService);
        return RestoreCachedDataOrLoadAsync(
            restored,
            _stateService,
            _appEventService,
            () => ExecuteAsync(LoadDataAsync));
    }

    public override void OnNavigatedFrom()
    {
        SaveState(_stateService);
        SaveCachedDataVersion(_stateService, _appEventService);
        base.OnNavigatedFrom();
    }

    private Task OnDateChangedAsync() => ExecuteAsync(LoadDayDataAsync);

    private Task OnTabbarChangedAsync() => ExecuteAsync(async ct =>
    {
        IsCanColumnSelect = true;
        await LoadDataAsync(ct);
    });

    private Task OnWeekDateChangedAsync() => ExecuteAsync(async ct =>
    {
        if (TabbarSelectedIndex != 1) return;
        await LoadWeekDataAsync(ct);
    });

    private Task OnMonthDateChangedAsync() => ExecuteAsync(async ct =>
    {
        if (TabbarSelectedIndex != 2) return;
        await LoadMonthlyDataAsync(ct);
    });

    private Task OnYearDateChangedAsync() => ExecuteAsync(async ct =>
    {
        if (TabbarSelectedIndex != 3) return;
        await LoadYearDataAsync(ct);
    });

    private Task OnShowTypeChangedAsync() => ExecuteAsync(LoadDataAsync);

    private Task OnRefreshAsync(object _) => ExecuteAsync(LoadDataAsync);

    public override Task RefreshAsync() => ExecuteAsync(LoadDataAsync);



    /// <summary>主题切换后分类色要按新背景重算：重拉当前页面数据（本地服务，开销可忽略）</summary>
    private void OnThemeChanged()
    {
        if ((Data?.Count ?? 0) == 0 && (WebBrowseStatisticsData?.Count ?? 0) == 0) return;
        _ = ExecuteAsync(LoadDataAsync);
    }

    private async Task OnToDetailAsync(object obj)
    {
        if (obj is not ChartsDataModel chartData) return;

        var date = TabbarSelectedIndex switch { 0 => Date, 1 => WeekDate, 2 => MonthDate, 3 => YearDate, _ => DateTime.Now };

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

            if (chartData.Data is WebSiteModel webSite)
                _navigationService.NavigateTo(nameof(WebSiteDetailPage), WebSiteDetailNavigationContext.Create(webSite, TabbarSelectedIndex, date));
            else if (chartData.Data is DailyLogModel { AppModel: not null } daily)
                _navigationService.NavigateTo(nameof(DetailPage), DetailNavigationContext.Create(daily.AppModel, TabbarSelectedIndex, date));
            else if (chartData.Data is HoursLogModel { AppModel: not null } hours)
                _navigationService.NavigateTo(nameof(DetailPage), DetailNavigationContext.Create(hours.AppModel, 0, Date));
        }
        catch (Exception ex)
        {
            Logger.Error("打开详情页失败", ex);
        }
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        switch (TabbarSelectedIndex)
        {
            case 0:
                NameIndexStart = 0;
                await LoadDayDataAsync(cancellationToken);
                break;
            case 1:
                NameIndexStart = 0;
                await LoadWeekDataAsync(cancellationToken);
                break;
            case 2:
                NameIndexStart = 1;
                await LoadMonthlyDataAsync(cancellationToken);
                break;
            case 3:
                NameIndexStart = 1;
                await LoadYearDataAsync(cancellationToken);
                break;
        }
    }

    private Task LoadDayDataAsync(CancellationToken cancellationToken)
    {
        var start = Date.Date;
        var prev = start.AddDays(-1);
        return LoadPeriodDataAsync(start, start, prev, prev, "hour", null, cancellationToken);
    }

    private Task LoadWeekDataAsync(CancellationToken cancellationToken)
    {
        var culture = SystemLanguage.CurrentCultureInfo;
        var weekDateArr = GetWeekRange(WeekDate);
        var prevWeek = GetWeekRange(WeekDate.AddDays(-7));
        var toText = Application.Current?.Resources["To"] as string ?? "To";
        WeekDateStr = $"{weekDateArr.Start.ToString("d", culture)} {toText} {weekDateArr.End.ToString("d", culture)}";

        string[] weekNames =
        [
            ResourceStrings.Monday, ResourceStrings.Tuesday, ResourceStrings.Wednesday,
            ResourceStrings.Thursday, ResourceStrings.Friday, ResourceStrings.Saturday, ResourceStrings.Sunday
        ];
        return LoadPeriodDataAsync(weekDateArr.Start, weekDateArr.End, prevWeek.Start, prevWeek.End,
            "day", weekNames, cancellationToken);
    }

    private Task LoadMonthlyDataAsync(CancellationToken cancellationToken)
    {
        var dateArr = Time.GetMonthDate(MonthDate);
        var prevArr = Time.GetMonthDate(MonthDate.AddMonths(-1));
        return LoadPeriodDataAsync(dateArr[0], dateArr[1], prevArr[0], prevArr[1],
            "day", null, cancellationToken);
    }

    private Task LoadYearDataAsync(CancellationToken cancellationToken)
    {
        var yearArr = Time.GetYearDate(YearDate);
        var prevArr = Time.GetYearDate(YearDate.AddYears(-1));
        var names = new string[12];
        for (var i = 0; i < 12; i++)
            names[i] = Application.Current?.Resources[$"{i + 1}Month"] as string ?? $"{i + 1}";

        return LoadPeriodDataAsync(yearArr[0], yearArr[1], prevArr[0], prevArr[1],
            "month", names, cancellationToken);
    }

    private async Task LoadPeriodDataAsync(
        DateTime start, DateTime end, DateTime prevStart, DateTime prevEnd,
        string buckets, string[]? columnNames, CancellationToken cancellationToken)
    {
        ColumnSelectedIndex = -1;
        WebColSelectedIndex = -1;
        DataMaximum = 0;
        HasEntityError = false;
        EntityErrorText = string.Empty;

        var isWeb = ShowType.Id == 1;
        // 统计页始终按「站点独立成行」口径取实体：应用侧已扣掉归属浏览器的站点时长，
        // 网站侧与「应用」页签相加恰好守恒，不会把同一分钟算两遍。
        var hoursTask = _entityData.GetEntityHoursAsync(start, end, buckets, false, cancellationToken);
        var listTask = _entityData.GetEntityListAsync(start, end, false, cancellationToken);
        var summaryTask = _entityData.GetEntitySummaryAsync(start, end, false, cancellationToken);
        var prevSummaryTask = _entityData.GetEntitySummaryAsync(prevStart, prevEnd, false, cancellationToken);

        EntityHoursModel? entityHours;
        EntityListModel? entityList;
        EntitySummaryModel? summary;
        EntitySummaryModel? prevSummary;
        try
        {
            await Task.WhenAll(hoursTask, listTask, summaryTask, prevSummaryTask);
            entityHours = await hoursTask;
            entityList = await listTask;
            summary = await summaryTask;
            prevSummary = await prevSummaryTask;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            HasEntityError = true;
            EntityErrorText = ChartDataMapper.FormatEntityLoadError(ex);
            Logger.Warn($"统计页实体数据加载失败：{ex.Message}");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _cachedEntityHours = entityHours;

        if (!isWeb)
        {
            var categories = await _categoryService.GetCategoriesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var categoryMap = categories.ToDictionary(c => c.ID);
            var columns = ChartDataMapper.AggregateColumnsByCategory(entityHours, isWeb: false);
            var categoryData = new List<ChartsDataModel>(columns.Count);
            foreach (var col in columns)
            {
                if (!categoryMap.TryGetValue(col.CategoryID, out var category))
                    category = new CategoryModel { Name = ResourceStrings.Uncategorized, Color = "#ccc", IconFile = "" };

                categoryData.Add(new ChartsDataModel
                {
                    Name = category.Name,
                    Icon = category.IconFile,
                    Values = col.Values,
                    Color = ChartColorHelper.Resolve(category.Color, category.IsSystem),
                    ColumnNames = columnNames ?? []
                });
            }
            categoryData = categoryData.OrderByDescending(c => c.Values.Sum()).ToList();

            var totalUse = categoryData.Sum(m => m.Values.Sum());
            _totalTime = totalUse;
            Data = categoryData;
            RadarData = categoryData;
            TotalHours = Time.ToHoursString(totalUse);

            var appRows = (entityList?.Rows ?? []).Where(r => r.Kind != "site").Take(5).ToList();
            TopData = ChartDataMapper.MapFromEntities(appRows, includeBadges: false);
            TopHours = TopData.Count > 0 ? Time.ToHoursString(TopData[0].Value) : "0";

            _appCount = summary?.AppCount ?? (entityList?.Rows.Count(r => r.Kind != "site") ?? 0);
            AppCount = _appCount.ToString();
            var topEntity = summary?.TopApp ?? appRows.FirstOrDefault();
            Top1App = topEntity != null
                ? new AppModel { ID = topEntity.EntityId, Description = topEntity.Name, IconFile = topEntity.Icon ?? string.Empty }
                : null!;
        }
        else
        {
            var webCategories = await _webDataService.GetWebSiteCategoriesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var webCategoryMap = webCategories.ToDictionary(c => c.ID);
            var columns = ChartDataMapper.AggregateColumnsByCategory(entityHours, isWeb: true);

            var statsData = new List<ChartsDataModel>(columns.Count);
            var pieData = new List<ChartsDataModel>(columns.Count);
            foreach (var col in columns)
            {
                if (!webCategoryMap.TryGetValue(col.CategoryID, out var category))
                    category = new WebSiteCategoryModel { Name = ResourceStrings.Uncategorized, Color = "#999999", IconFile = "" };

                var resolvedColor = ChartColorHelper.Resolve(category.Color, category.IsSystem);
                var catName = category.Name ?? ResourceStrings.Uncategorized;
                var sum = col.Values?.Sum() ?? 0;

                statsData.Add(new ChartsDataModel
                {
                    Name = catName,
                    Icon = category.IconFile ?? string.Empty,
                    Values = col.Values ?? [],
                    Color = resolvedColor,
                    ColumnNames = columnNames ?? []
                });

                if (sum > 0)
                {
                    pieData.Add(new ChartsDataModel
                    {
                        Name = catName,
                        Value = sum,
                        Color = resolvedColor,
                        PopupText = catName + " " + Time.ToString((int)sum),
                        Icon = category.IconFile ?? string.Empty
                    });
                }
            }

            WebBrowseStatisticsData = statsData.OrderByDescending(m => m.Values.Sum()).ToList();
            WebCategoriesPieData = pieData.OrderByDescending(m => m.Value).ToList();

            var siteRows = (entityList?.Rows ?? []).Where(r => r.Kind == "site").Take(10).ToList();
            WebSitesTopData = ChartDataMapper.MapFromEntities(siteRows, includeBadges: false);

            WebTotalTime = summary?.SiteSeconds ?? siteRows.Sum(r => (double)r.Seconds);
            WebSiteCount = summary?.SiteCount ?? (entityList?.Rows.Count(r => r.Kind == "site") ?? 0);
            WebPageCount = summary?.WebPageCount ?? 0;
            WebTotalTimeText = Time.ToHoursString(WebTotalTime);
        }

        ApplyDiffData(prevSummary);
    }

    private void ApplyDiffData(EntitySummaryModel? prevSummary)
    {
        var lastAppCount = prevSummary?.AppCount ?? 0;
        double lastTotalTime = prevSummary?.AppNetSeconds ?? 0;

        var diffTotalTime = lastTotalTime == 0
            ? (_totalTime > 0 ? 100 : 0)
            : (_totalTime - lastTotalTime) / lastTotalTime * 100;

        DiffTotalTimeType = diffTotalTime > 0 ? "1" : diffTotalTime < 0 ? "-1" : "0";
        DiffTotalTimeValue = DiffTotalTimeType == "0" ? string.Empty :
            diffTotalTime >= 100 ? "100%" : Math.Abs(diffTotalTime).ToString("f2") + "%";

        var diffAppCount = _appCount - lastAppCount;
        DiffAppCountType = diffAppCount > 0 ? "1" : diffAppCount < 0 ? "-1" : "0";
        DiffAppCountValue = DiffAppCountType == "0" ? string.Empty : Math.Abs(diffAppCount).ToString();

        LastWebTotalTime = prevSummary?.SiteSeconds ?? 0;
        LastWebSiteCount = prevSummary?.SiteCount ?? 0;
        LastWebPageCount = prevSummary?.WebPageCount ?? 0;
    }

    private Task LoadSelectedColDataAsync(CancellationToken cancellationToken = default)
    {
        if (ColumnSelectedIndex < 0)
        {
            DayHoursSelectedTime = string.Empty;
            DayHoursData = [];
            return Task.CompletedTask;
        }

        DayHoursSelectedTime = FormatSelectedColumnTime(ColumnSelectedIndex);
        DayHoursData = SliceBucketEntities(_cachedEntityHours, ColumnSelectedIndex, isWeb: false);
        return Task.CompletedTask;
    }

    private Task LoadWebSitesColSelectedDataAsync(CancellationToken cancellationToken = default)
    {
        if (WebColSelectedIndex < 0)
        {
            WebSitesColSelectedTimeText = string.Empty;
            WebSitesColSelectedData = [];
            return Task.CompletedTask;
        }

        WebSitesColSelectedTimeText = FormatSelectedColumnTime(WebColSelectedIndex);
        WebSitesColSelectedData = SliceBucketEntities(_cachedEntityHours, WebColSelectedIndex, isWeb: true);
        return Task.CompletedTask;
    }

    private string FormatSelectedColumnTime(int index)
    {
        var culture = SystemLanguage.CurrentCultureInfo;
        if (TabbarSelectedIndex == 0)
        {
            var time = new DateTime(Date.Year, Date.Month, Date.Day, Math.Clamp(index, 0, 23), 0, 0);
            return time.ToString($"{culture.DateTimeFormat.ShortDatePattern} HH", culture);
        }
        if (TabbarSelectedIndex == 1)
        {
            var weekDateArr = GetWeekRange(WeekDate);
            return weekDateArr.Start.AddDays(index).ToString("d", culture);
        }
        if (TabbarSelectedIndex == 2)
        {
            var dateArr = Time.GetMonthDate(MonthDate);
            return dateArr[0].AddDays(index).ToString("d", culture);
        }
        var monthStart = new DateTime(YearDate.Year, Math.Clamp(index + 1, 1, 12), 1);
        return monthStart.ToString(culture.DateTimeFormat.YearMonthPattern, culture);
    }

    private static List<ChartsDataModel> SliceBucketEntities(EntityHoursModel? hours, int bucketIndex, bool isWeb)
    {
        if (hours == null || bucketIndex < 0) return [];

        var entities = hours.Rows
            .Where(r => (isWeb ? r.Kind == "site" : r.Kind != "site")
                        && bucketIndex < r.Values.Length
                        && r.Values[bucketIndex] > 0)
            .Select(r => r.ToEntityModel((int)r.Values[bucketIndex]));

        return ChartDataMapper.MapFromEntities(entities, includeBadges: true);
    }

    private static (DateTime Start, DateTime End) GetWeekRange(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        dayOfWeek = dayOfWeek == 0 ? 7 : dayOfWeek;
        var start = date.Date.AddDays(-(dayOfWeek - 1));
        var end = start.AddDays(6).Date.AddHours(23).AddMinutes(59).AddSeconds(59);
        return (start, end);
    }

    public override void Dispose()
    {
        ChartColorHelper.ThemeChanged -= OnThemeChanged;
        Data = [];
        TopData = [];
        base.Dispose();
    }
}
