using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using ShiJian.Client.Base.Color;
using ShiJian.Client.Controls.Charts.Model;
using ShiJian.Client.Controls.Timeline;
using ShiJian.Client.Events;
using ShiJian.Client.Foundation;
using ShiJian.Client.Foundation.Rx;
using ShiJian.Client.Librarys;
using ShiJian.Client.Models;
using ShiJian.Client.Models.Navigation;
using ShiJian.Client.Servicers;
using ShiJian.Client.Servicers.Interfaces;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Data;
using ShiJian.Client.Shared.Models.Web;
using ShiJian.Client.Shared.Servicers.Interfaces;
using ShiJian.Client.Views;
using AppModel = ShiJian.Client.Shared.Models.AppModel;

namespace ShiJian.Client.ViewModels;

public partial class DataPageViewModel : DataPageModel
{
    private readonly IWebData _webDataService;
    private readonly IData _dataService;
    private readonly ICategorys _categoryService;
    private readonly INavigationService _navigationService;
    private readonly IStateService _stateService;
    private readonly IAppEventService _appEventService;

    // 会话 / 浏览记录只缓存最近一次请求：一次加载里环形时钟和看板用的是同一段区间，
    // 命中缓存就不会把几 MB 的会话数据拉两遍
    private DateTime _sessionCacheStart;
    private DateTime _sessionCacheEnd;
    private List<AppSessionModel>? _sessionCache;
    private DateTime _browseCacheStart;
    private DateTime _browseCacheEnd;
    private List<WebBrowseLogModel>? _browseCache;

    public DataPageViewModel(
        IData data,
        IWebData webData,
        ICategorys categories,
        INavigationService navigationService,
        IStateService stateService,
        IAppEventService appEventService)
    {
        _dataService = data;
        _webDataService = webData;
        _categoryService = categories;
        _navigationService = navigationService;
        _stateService = stateService;
        _appEventService = appEventService;

        ToDetailCommand = AsyncRelayCommand.Create<object>(OnToDetail).DisposeWith(Disposables);
        ClockLegendClickCommand = AsyncRelayCommand.Create<object>(OnClockLegendClick).DisposeWith(Disposables);
        RefreshCommand = AsyncRelayCommand.CreateFromTask<object>(OnRefreshAsync).DisposeWith(Disposables);
        ToggleHeaderCommand = AsyncRelayCommand.Create(() => { IsHeaderExpanded = !IsHeaderExpanded; })
            .DisposeWith(Disposables);
        Initialize();

        RefreshOnChange(
            _appEventService.AppChanged,
            _appEventService.WebSiteChanged,
            RefreshAsync);

        ChartColorHelper.ThemeChanged += OnThemeChanged;
    }

    /// <summary>主题变了：环形时钟的配色要按新背景重算（分类色靠 ChartColorHelper）</summary>
    private void OnThemeChanged()
    {
        if (!DayClock.HasData && !WeekClock.HasData && !MonthClock.HasData && !YearClock.HasData) return;
        _ = RefreshAsync();
    }

    public ICommand ToDetailCommand { get; }
    public ICommand ClockLegendClickCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleHeaderCommand { get; }

    public override void Dispose()
    {
        ChartColorHelper.ThemeChanged -= OnThemeChanged;
        base.Dispose();
    }

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

        DayDate = DateTime.Now.Date;
        WeekDate = DateTime.Now.Date;
        MonthDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        YearDate = new DateTime(DateTime.Now.Year, 1, 1);

        WhenPropertyChanged(this, x => x.DayDate, date => OnDateChangedAsync(date, 0));
        WhenPropertyChanged(this, x => x.WeekDate, date => OnDateChangedAsync(date, 1));
        WhenPropertyChanged(this, x => x.MonthDate, date => OnDateChangedAsync(date, 2));
        WhenPropertyChanged(this, x => x.YearDate, date => OnDateChangedAsync(date, 3));

        WhenPropertyChanged(this, x => x.SelectedPeriod, p =>
        {
            if (p != null) TabbarSelectedIndex = p.Id;
            return Task.CompletedTask;
        });

        WhenPropertyChanged(this, x => x.TabbarSelectedIndex, async index =>
        {
            DateTime date;
            int dataType;
            if (index == 0)
            {
                if (DayDate == DateTime.MinValue) DayDate = DateTime.Now.Date;
                (date, dataType) = (DayDate, 0);
            }
            else if (index == 1)
            {
                if (WeekDate == DateTime.MinValue) WeekDate = DateTime.Now.Date;
                (date, dataType) = (WeekDate, 1);
            }
            else if (index == 2)
            {
                if (MonthDate == DateTime.MinValue) MonthDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                (date, dataType) = (MonthDate, 2);
            }
            else if (index == 3)
            {
                if (YearDate == DateTime.MinValue) YearDate = new DateTime(DateTime.Now.Year, 1, 1);
                (date, dataType) = (YearDate, 3);
            }
            else
            {
                return;
            }

            // 先切到对应周期的看板，再去拉数据；数据回来前不会闪空白
            CurrentInsight = InsightAt(index);
            await LoadDataAsync(date, dataType);
        }, skipInitial: false);

        WhenPropertyChanged(this, x => x.ShowType, async _ =>
        {
            await LoadDataAsync(DayDate, 0);
            await LoadDataAsync(WeekDate, 1);
            await LoadDataAsync(MonthDate, 2);
            await LoadDataAsync(YearDate, 3);
        });

        // 配色模式只影响扇区取色与图例口径，数据不用重拉：扇区里两种颜色都已备好
        WhenPropertyChanged(this, x => x.UseCategoryColor, _ =>
        {
            DayClock = RebuildLegend(DayClock);
            WeekClock = RebuildLegend(WeekClock);
            MonthClock = RebuildLegend(MonthClock);
            YearClock = RebuildLegend(YearClock);
            return Task.CompletedTask;
        });

        SelectedPeriod = PeriodOptions[0];
        SelectedColorMode = ColorModeOptions[0];
        TabbarSelectedIndex = 0;
    }


    public override Task OnNavigatedToAsync()
    {
        var restored = TryRestoreState(_navigationService, _stateService);
        // 状态恢复只还原了数据，当前看板指针要手动指过去
        CurrentInsight = InsightAt(TabbarSelectedIndex);
        return RestoreCachedDataOrLoadAsync(
            restored,
            _stateService,
            _appEventService,
            () => LoadDataAsync(DayDate, 0));
    }

    public override void OnNavigatedFrom()
    {
        SaveState(_stateService);
        SaveCachedDataVersion(_stateService, _appEventService);
        base.OnNavigatedFrom();
    }

    private Task OnDateChangedAsync(DateTime date, int dataType)
    {
        return LoadDataAsync(date, dataType);
    }

    private Task OnRefreshAsync(object _)
    {
        var (date, dataType) = TabbarSelectedIndex switch
        {
            0 => (DayDate, 0),
            1 => (WeekDate, 1),
            2 => (MonthDate, 2),
            3 => (YearDate, 3),
            _ => (DayDate, 0)
        };
        return LoadDataAsync(date, dataType);
    }

    public override Task RefreshAsync() => OnRefreshAsync(null!);

    private void OnToDetail(object obj)
    {
        var date = TabbarSelectedIndex switch { 0 => DayDate, 1 => WeekDate, 2 => MonthDate, 3 => YearDate, _ => DateTime.Now };

        switch (obj)
        {
            case ClockSegment { Data: AppModel app }:
                _navigationService.NavigateTo(nameof(DetailPage), DetailNavigationContext.Create(app, TabbarSelectedIndex, date));
                break;
            case ChartsDataModel { Data: DailyLogModel { AppModel: not null } model }:
                _navigationService.NavigateTo(nameof(DetailPage), DetailNavigationContext.Create(model.AppModel, TabbarSelectedIndex, date));
                break;
            case ChartsDataModel { Data: WebSiteModel webSite }:
                _navigationService.NavigateTo(nameof(WebSiteDetailPage), WebSiteDetailNavigationContext.Create(webSite, TabbarSelectedIndex, date));
                break;
        }
    }

    /// <summary>点图例：应用 → 应用详情；分类 → 该分类的汇总页</summary>
    private void OnClockLegendClick(object obj)
    {
        switch (obj)
        {
            case ClockLegendItem { Data: AppModel app }:
            {
                var date = TabbarSelectedIndex switch { 0 => DayDate, 1 => WeekDate, 2 => MonthDate, 3 => YearDate, _ => DateTime.Now };
                _navigationService.NavigateTo(nameof(DetailPage), DetailNavigationContext.Create(app, TabbarSelectedIndex, date));
                break;
            }
            case ClockLegendItem { CategoryData: Shared.Models.CategoryModel category }:
                _navigationService.NavigateTo(nameof(CategorySummaryPage),
                    CategorySummaryNavigationContext.Create(CategorySummaryKind.App, category.ID, category.Name));
                break;
        }
    }

    private Task LoadDataAsync(DateTime date, int dataType)
    {
        return ExecuteAsync(async ct =>
        {
            var (start, end) = GetDateRange(date, dataType);
            List<ChartsDataModel> chartData;

            if (ShowType.Id == 0)
            {
                var result = (await _dataService.GetDateRangelogListAsync(start, end, cancellationToken: ct)).ToList();
                ct.ThrowIfCancellationRequested();
                chartData = await Task.Run(() => ChartDataMapper.MapFromDailyLogs(result, includeBadges: true), ct);

                switch (dataType)
                {
                    case 0:
                        Data = chartData;
                        DayClock = await BuildDayClockAsync(date, ct);
                        break;
                    case 1:
                        WeekClock = await BuildHourlyClockAsync(start, end, ClockTickMode.Day,
                            ResourceStrings.ThisWeek, ct);
                        break;
                    case 2:
                        MonthClock = await BuildHourlyClockAsync(start, end, ClockTickMode.Day,
                            ResourceStrings.ThisMonth, ct);
                        break;
                    case 3:
                        YearClock = await BuildMonthlyClockAsync(start, end, ct);
                        break;
                }
            }
            else
            {
                var result = await _webDataService.GetWebSiteLogListAsync(start, end, ct);
                ct.ThrowIfCancellationRequested();
                chartData = await Task.Run(() => ChartDataMapper.MapFromWebSites(result, includeBadges: true), ct);

                if (dataType == 0) Data = chartData;

                // 网站维度没有会话级时间轴，环形时钟一律置空（页面会隐藏表盘）
                switch (dataType)
                {
                    case 0: DayClock = ClockRingData.Empty; break;
                    case 1: WeekClock = ClockRingData.Empty; break;
                    case 2: MonthClock = ClockRingData.Empty; break;
                    case 3: YearClock = ClockRingData.Empty; break;
                }
            }

            ct.ThrowIfCancellationRequested();

            switch (dataType)
            {
                case 1: WeekData = chartData; break;
                case 2: MonthData = chartData; break;
                case 3: YearData = chartData; break;
            }

            // 看板只算当前页签：切应用/网站时会顺次预热四个周期，
            // 每个周期都算一遍看板要多拨好几次会话请求，不划算
            if (dataType != TabbarSelectedIndex) return;

            var insight = await BuildInsightAsync(date, dataType, ct);
            ApplyInsight(dataType, insight);
        });
    }

    #region 环形时钟

    /// <summary>图例只列前几名，剩下的合并成「其他」</summary>
    private const int ClockLegendTopCount = 5;

    private static bool IsDarkTheme() => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>服务端的会话 / 小时桶时间戳是 UTC，画到表盘上必须先换成本地时间</summary>
    private static DateTime ToLocal(DateTime utc) =>
        utc.Kind == DateTimeKind.Local ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>当天：直接用会话，扇区就是真实使用时段</summary>
    private async Task<ClockRingData> BuildDayClockAsync(DateTime date, CancellationToken ct)
    {
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        var periodSeconds = (dayEnd - dayStart).TotalSeconds;

        var sessions = await GetSessionsCachedAsync(dayStart, dayEnd.AddSeconds(-1), ct);
        ct.ThrowIfCancellationRequested();

        var isDark = IsDarkTheme();
        var segments = new List<ClockSegment>();

        foreach (var session in sessions.Where(s => s.AppModel != null).OrderBy(s => s.StartTime))
        {
            var start = ToLocal(session.StartTime);
            var end = ToLocal(session.EndTime);
            if (end <= dayStart || start >= dayEnd) continue;

            var clipStart = start < dayStart ? dayStart : start;
            var clipEnd = end > dayEnd ? dayEnd : end;
            var seconds = (clipEnd - clipStart).TotalSeconds;
            if (seconds <= 0) continue;

            segments.Add(new ClockSegment
            {
                StartFraction = (clipStart - dayStart).TotalSeconds / periodSeconds,
                EndFraction = (clipEnd - dayStart).TotalSeconds / periodSeconds,
                Name = session.AppModel!.GetDisplayName() ?? "Unknown",
                CategoryName = session.AppModel.Category?.Name,
                Color = TimelineColorService.GetColor(session.AppModel.GetDisplayName(), isDark),
                CategoryColor = ResolveCategoryColor(session.AppModel.Category, isDark),
                RangeText = $"{clipStart:HH:mm} ~ {clipEnd:HH:mm}",
                Seconds = seconds,
                Data = session.AppModel,
                CategoryData = session.AppModel.Category
            });
        }

        return BuildRingData(segments, ClockTickMode.Hour, dayStart, dayEnd, ResourceStrings.Today);
    }

    /// <summary>
    /// 周 / 月：服务端只有「应用 × 小时」的桶，所以一个小时的弧长按该小时的
    /// 应用占比拆开——位置仍是真实时段，只是小时内不再区分先后。
    /// </summary>
    private async Task<ClockRingData> BuildHourlyClockAsync(DateTime rangeStart, DateTime rangeEnd,
        ClockTickMode tickMode, string caption, CancellationToken ct)
    {
        var hours = await _dataService.GetHoursRangeLogListAsync(rangeStart, rangeEnd, ct);
        ct.ThrowIfCancellationRequested();

        var periodSeconds = Math.Max(1, (rangeEnd - rangeStart).TotalSeconds);
        var isDark = IsDarkTheme();
        var segments = new List<ClockSegment>();

        var slots = hours
            .Where(h => h.AppModel != null && h.Time > 0)
            .Select(h => (Model: h, LocalTime: TruncateToHour(ToLocal(h.DataTime))))
            .Where(x => x.LocalTime >= rangeStart && x.LocalTime < rangeEnd)
            .GroupBy(x => x.LocalTime);

        foreach (var slot in slots)
        {
            var slotStart = slot.Key;
            var slotTotal = slot.Sum(x => x.Model.Time);
            if (slotTotal <= 0) continue;

            var slotFraction = (slotStart - rangeStart).TotalSeconds / periodSeconds;
            var slotWidth = 3600d / periodSeconds;
            var cursor = 0d;

            foreach (var item in slot.OrderByDescending(x => x.Model.Time))
            {
                var model = item.Model;
                var share = (double)model.Time / slotTotal;
                var startFraction = slotFraction + slotWidth * cursor;
                cursor += share;

                segments.Add(new ClockSegment
                {
                    StartFraction = startFraction,
                    EndFraction = slotFraction + slotWidth * cursor,
                    Name = model.AppModel!.GetDisplayName() ?? "Unknown",
                    CategoryName = model.AppModel.Category?.Name,
                    Color = TimelineColorService.GetColor(model.AppModel.GetDisplayName(), isDark),
                    CategoryColor = ResolveCategoryColor(model.AppModel.Category, isDark),
                    RangeText = $"{slotStart:M/d HH:00} · {FormatDuration(model.Time)}",
                    Seconds = model.Time,
                    Data = model.AppModel,
                    CategoryData = model.AppModel.Category
                });
            }
        }

        return BuildRingData(segments, tickMode, rangeStart, rangeEnd, caption);
    }

    /// <summary>
    /// 年：按月拆分，一个月里再按应用占比铺开。
    /// 服务端的范围查询只按应用汇总、不给到天，所以逐月各查一次（12 个并行请求，都是小包），
    /// 换成整年的小时桶会是几 MB 的响应，不划算。
    /// </summary>
    private async Task<ClockRingData> BuildMonthlyClockAsync(DateTime rangeStart, DateTime rangeEnd,
        CancellationToken ct)
    {
        var periodSeconds = Math.Max(1, (rangeEnd - rangeStart).TotalSeconds);
        var isDark = IsDarkTheme();
        var segments = new List<ClockSegment>();

        var months = Enumerable.Range(0, 12)
            .Select(i => new DateTime(rangeStart.Year, i + 1, 1))
            .ToList();

        var results = await Task.WhenAll(months.Select(month =>
            _dataService.GetDateRangelogListAsync(month, month.AddMonths(1).AddSeconds(-1),
                cancellationToken: ct)));
        ct.ThrowIfCancellationRequested();

        for (var i = 0; i < months.Count; i++)
        {
            var slotStart = months[i];
            var rows = results[i].Where(d => d.AppModel != null && d.Time > 0).ToList();
            var slotTotal = rows.Sum(d => d.Time);
            if (slotTotal <= 0) continue;

            var slotFraction = (slotStart - rangeStart).TotalSeconds / periodSeconds;
            var slotWidth = (slotStart.AddMonths(1) - slotStart).TotalSeconds / periodSeconds;
            var cursor = 0d;

            foreach (var item in rows.OrderByDescending(d => d.Time))
            {
                var appModel = item.AppModel!;
                var share = (double)item.Time / slotTotal;
                var startFraction = slotFraction + slotWidth * cursor;
                cursor += share;

                segments.Add(new ClockSegment
                {
                    StartFraction = startFraction,
                    EndFraction = slotFraction + slotWidth * cursor,
                    Name = appModel.GetDisplayName() ?? "Unknown",
                    CategoryName = appModel.Category?.Name,
                    Color = TimelineColorService.GetColor(appModel.GetDisplayName(), isDark),
                    CategoryColor = ResolveCategoryColor(appModel.Category, isDark),
                    RangeText = $"{slotStart:yyyy-MM} · {FormatDuration(item.Time)}",
                    Seconds = item.Time,
                    Data = appModel,
                    CategoryData = appModel.Category
                });
            }
        }

        return BuildRingData(segments, ClockTickMode.Month, rangeStart, rangeEnd, ResourceStrings.ThisYear);
    }

    private static string ResolveCategoryColor(Shared.Models.CategoryModel? category, bool isDark)
    {
        return category != null
            ? ChartColorHelper.Resolve(category.Color, category.IsSystem)
            : TimelineColorService.GetColor(ResourceStrings.SystemCategory, isDark);
    }

    private static DateTime TruncateToHour(DateTime time) =>
        new(time.Year, time.Month, time.Day, time.Hour, 0, 0);

    private ClockRingData BuildRingData(List<ClockSegment> segments, ClockTickMode tickMode,
        DateTime rangeStart, DateTime rangeEnd, string caption)
    {
        var totalSeconds = segments.Sum(s => s.Seconds);

        return new ClockRingData
        {
            Segments = segments,
            Legend = BuildLegend(segments),
            CenterText = totalSeconds > 0 ? FormatDuration(totalSeconds) : string.Empty,
            CenterCaption = totalSeconds > 0 ? caption : string.Empty,
            TickMode = tickMode,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd
        };
    }

    /// <summary>只重算图例（应用 / 分类口径），扇区列表原样复用</summary>
    private ClockRingData RebuildLegend(ClockRingData source)
    {
        if (!source.HasData) return source;

        return new ClockRingData
        {
            Segments = source.Segments,
            Legend = BuildLegend(source.Segments),
            CenterText = source.CenterText,
            CenterCaption = source.CenterCaption,
            TickMode = source.TickMode,
            RangeStart = source.RangeStart,
            RangeEnd = source.RangeEnd
        };
    }

    /// <summary>图例跟配色模式走：应用色时列应用，分类色时列分类</summary>
    private List<ClockLegendItem> BuildLegend(List<ClockSegment> segments)
    {
        var total = segments.Sum(s => s.Seconds);
        if (total <= 0) return [];

        var useCategory = UseCategoryColor;
        var groups = segments
            .GroupBy(s => useCategory ? s.CategoryName ?? ResourceStrings.SystemCategory : s.Name)
            .Select(g => new
            {
                Name = g.Key,
                Seconds = g.Sum(s => s.Seconds),
                Color = useCategory ? g.First().CategoryColor : g.First().Color,
                Data = useCategory ? null : g.First().Data,
                CategoryData = useCategory ? g.First().CategoryData : null
            })
            .OrderByDescending(g => g.Seconds)
            .ToList();

        var legend = new List<ClockLegendItem>();
        var top = groups.Take(ClockLegendTopCount).ToList();

        foreach (var group in top)
        {
            legend.Add(new ClockLegendItem
            {
                Name = group.Name,
                Color = group.Color,
                DurationText = FormatDuration(group.Seconds),
                ShareText = $"{(int)Math.Round(group.Seconds / total * 100)}%",
                Data = group.Data,
                CategoryData = group.CategoryData
            });
        }

        var restSeconds = groups.Skip(ClockLegendTopCount).Sum(g => g.Seconds);
        if (restSeconds <= 0) return legend;

        legend.Add(new ClockLegendItem
        {
            Name = ResourceStrings.ClockLegendOther,
            Color = "#8b949e",
            DurationText = FormatDuration(restSeconds),
            ShareText = $"{(int)Math.Round(restSeconds / total * 100)}%",
            IsOther = true
        });

        return legend;
    }

    #endregion

    #region 看板

    /// <summary>热力图最多画几行分类（日视图）</summary>
    private const int HeatmapTopRows = 6;

    /// <summary>分类占比环形图最多画几段，剩下合并成「其他」</summary>
    private const int CategoryShareTopCount = 6;

    /// <summary>时段偏好横条的最大宽度（右侧洞察栏的可用宽度）</summary>
    private const double BandMaxWidth = 96;

    private sealed record CategoryVisual(string Name, string Color);

    private DataPageInsight InsightAt(int index) => index switch
    {
        0 => DayInsight,
        1 => WeekInsight,
        2 => MonthInsight,
        3 => YearInsight,
        _ => DataPageInsight.Empty
    };

    private void ApplyInsight(int dataType, DataPageInsight insight)
    {
        switch (dataType)
        {
            case 0: DayInsight = insight; break;
            case 1: WeekInsight = insight; break;
            case 2: MonthInsight = insight; break;
            case 3: YearInsight = insight; break;
            default: return;
        }

        if (dataType == TabbarSelectedIndex) CurrentInsight = insight;
    }

    private async Task<DataPageInsight> BuildInsightAsync(DateTime date, int dataType, CancellationToken ct)
    {
        var isWeb = ShowType.Id == 1;
        var (start, end) = GetDateRange(date, dataType);
        var previousDate = PreviousDate(date, dataType);
        var (prevStart, prevEnd) = GetDateRange(previousDate, dataType);

        // 分类 × 时段矩阵：一份数据同时供柱图与分类占比使用
        var currentColumns = await GetCategoryColumnsAsync(date, dataType, isWeb, ct);
        ct.ThrowIfCancellationRequested();
        var previousColumns = await GetCategoryColumnsAsync(previousDate, dataType, isWeb, ct);
        ct.ThrowIfCancellationRequested();

        var spans = await GetSpansAsync(start, end, isWeb, ct);
        ct.ThrowIfCancellationRequested();
        var periodSpans = spans.Select(s => (s.Start, s.End)).ToList();

        var visuals = await GetCategoryVisualsAsync(isWeb, ct);
        ct.ThrowIfCancellationRequested();

        var buckets = BucketTotals(currentColumns, dataType, start);
        var prevBuckets = BucketTotals(previousColumns, dataType, prevStart);
        var labels = BucketLabels(dataType, buckets.Length, start);

        var totalSeconds = buckets.Sum();
        var prevTotalSeconds = prevBuckets.Sum();
        var summary = InsightBuilder.Summarize(periodSpans, start, end);
        var hourOfDay = InsightBuilder.HourOfDayHistogram(periodSpans, start, end);

        var listCount = CurrentList(dataType)?.Count ?? 0;
        var previousCount = isWeb
            ? await _webDataService.GetBrowseSitesTotalAsync(prevStart, prevEnd, ct)
            : await _dataService.GetDateRangeAppCountAsync(prevStart, prevEnd, ct);
        ct.ThrowIfCancellationRequested();

        var pageCount = isWeb ? await _webDataService.GetBrowsePagesTotalAsync(start, end, ct) : 0;
        ct.ThrowIfCancellationRequested();

        var vsCaption = VersusCaption(dataType);
        var totalDiff = InsightBuilder.BuildDiff(totalSeconds, prevTotalSeconds);
        var countDiff = InsightBuilder.BuildDiff(listCount, previousCount);

        var cards = new List<InsightStatCard>(4)
        {
            BuildCard(ResourceStrings.CategoryTotalUsage, InsightBuilder.FormatDuration(totalSeconds), totalDiff, vsCaption),
            BuildCard(isWeb ? ResourceStrings.InsightSites : ResourceStrings.InsightApps, listCount.ToString(),
                countDiff, vsCaption),
            isWeb
                ? BuildCard(ResourceStrings.InsightPages, pageCount.ToString(), null, null)
                : BuildCard(ResourceStrings.InsightSessions, summary.Count.ToString(), null, null,
                    $"{ResourceStrings.InsightAverageSession} {InsightBuilder.FormatDuration(summary.AverageSeconds)}"),
            isWeb
                ? BuildCard(ResourceStrings.InsightRecords, spans.Count.ToString(), null, null,
                    summary.HasData
                        ? $"{ResourceStrings.InsightLongestRecord} {InsightBuilder.FormatDuration(summary.LongestSeconds)}"
                        : null)
                : BuildCard(ResourceStrings.InsightLongest, InsightBuilder.FormatDuration(summary.LongestSeconds),
                    null, null,
                    summary.HasData ? InsightBuilder.FormatMoment(summary.LongestStart, start, end) : null)
        };

        var peakHour = InsightBuilder.PeakHour(hourOfDay);
        var activeCount = buckets.Count(v => v > 0);
        var activeCaption = dataType switch
        {
            0 => ResourceStrings.InsightActiveHours,
            3 => ResourceStrings.InsightActiveMonths,
            _ => ResourceStrings.CategoryActiveDays
        };

        var habits = new List<InsightHabitRow>
        {
            new()
            {
                Label = isWeb ? ResourceStrings.InsightFirstBrowse : ResourceStrings.InsightFirstUse,
                Value = summary.HasData ? InsightBuilder.FormatMoment(summary.FirstStart, start, end) : "—"
            },
            new()
            {
                Label = isWeb ? ResourceStrings.InsightLastBrowse : ResourceStrings.InsightLastUse,
                Value = summary.HasData ? InsightBuilder.FormatMoment(summary.LastEnd, start, end) : "—"
            },
            new()
            {
                Label = ResourceStrings.InsightPeakHour,
                Value = peakHour >= 0 ? $"{peakHour:00}:00" : "—"
            },
            new()
            {
                Label = activeCaption,
                Value = activeCount.ToString()
            }
        };

        var bandNames = new[]
        {
            ResourceStrings.InsightBandDawn,
            ResourceStrings.InsightBandMorning,
            ResourceStrings.InsightBandAfternoon,
            ResourceStrings.InsightBandEvening
        };
        var bands = InsightBuilder.BuildBands(hourOfDay, bandNames, BandMaxWidth);

        var (categorySeries, categoryTotalText) = BuildCategoryShare(currentColumns, visuals);

        var compare = new double[buckets.Length];
        for (var i = 0; i < compare.Length && i < prevBuckets.Length; i++) compare[i] = prevBuckets[i];

        var accent = ChartColorHelper.Resolve(null, true);
        var hourSeries = buckets.Length == 0
            ? []
            : new List<ChartsDataModel>
            {
                new()
                {
                    Name = ResourceStrings.CategoryTotalUsage,
                    Color = accent,
                    Values = buckets
                }
            };

        return new DataPageInsight
        {
            Cards = cards,
            Habits = habits,
            Bands = bands,
            Heatmap = BuildHeatmap(dataType, isWeb, spans, periodSpans, buckets, start, end),
            HourSeries = hourSeries,
            HourLabels = labels,
            CompareValues = compare,
            CompareCaption = CompareCaption(dataType),
            BucketTotalText = InsightBuilder.FormatDuration(totalSeconds),
            CategorySeries = categorySeries,
            CategoryTotalText = categoryTotalText,
            CategoryCaption = isWeb ? ResourceStrings.Website : ResourceStrings.App
        };
    }

    private static InsightStatCard BuildCard(string caption, string value,
        InsightBuilder.InsightDiff? diff, string? diffCaption, string? sub = null)
    {
        return new InsightStatCard
        {
            Caption = caption,
            Value = value,
            Sub = sub,
            DiffText = diff?.Text,
            DiffCaption = diff is null ? null : diffCaption,
            IsUp = diff?.IsUp ?? false,
            IsDown = diff?.IsDown ?? false,
            IsFlat = diff?.IsFlat ?? false
        };
    }

    private List<ChartsDataModel>? CurrentList(int dataType) => dataType switch
    {
        0 => Data,
        1 => WeekData,
        2 => MonthData,
        3 => YearData,
        _ => null
    };

    private static DateTime PreviousDate(DateTime date, int dataType) => dataType switch
    {
        0 => date.AddDays(-1),
        1 => date.AddDays(-7),
        2 => date.AddMonths(-1),
        3 => date.AddYears(-1),
        _ => date
    };

    private static string VersusCaption(int dataType) => dataType switch
    {
        0 => ResourceStrings.InsightVsYesterday,
        1 => ResourceStrings.InsightVsLastWeek,
        2 => ResourceStrings.InsightVsLastMonth,
        3 => ResourceStrings.InsightVsLastYear,
        _ => ResourceStrings.CategoryVsPrevious
    };

    private static string CompareCaption(int dataType) => dataType switch
    {
        0 => ResourceStrings.InsightCompareYesterday,
        1 => ResourceStrings.InsightCompareLastWeek,
        2 => ResourceStrings.InsightCompareLastMonth,
        3 => ResourceStrings.InsightCompareLastYear,
        _ => ResourceStrings.CategoryVsPrevious
    };

    /// <summary>
    /// 分类 × 时段矩阵。日 = 24 小时，周 / 月 = 按天，年 = 12 个月。
    /// 网站维度只有这一个接口，年的按天数据在这里折成月份
    /// </summary>
    private async Task<IReadOnlyList<ColumnDataModel>> GetCategoryColumnsAsync(DateTime date, int dataType,
        bool isWeb, CancellationToken ct)
    {
        if (isWeb)
        {
            var (start, end) = GetDateRange(date, dataType);
            return await _webDataService.GetBrowseDataByCategoryStatisticsAsync(start, end, ct);
        }

        return dataType switch
        {
            0 => await _dataService.GetCategoryHoursDataAsync(date, ct),
            1 or 2 => await _dataService.GetCategoryRangeDataAsync(GetDateRange(date, dataType).Start,
                GetDateRange(date, dataType).End, ct),
            3 => await _dataService.GetCategoryYearDataAsync(date, ct),
            _ => []
        };
    }

    /// <summary>把「分类 × 时段」矩阵按列求和，得到每个时段的总时长</summary>
    private static double[] BucketTotals(IReadOnlyList<ColumnDataModel> columns, int dataType, DateTime rangeStart)
    {
        if (columns.Count == 0) return [];

        var length = columns.Max(c => c.Values?.Length ?? 0);
        if (length == 0) return [];

        var sums = new double[length];
        foreach (var column in columns)
        {
            var values = column.Values;
            if (values == null) continue;
            for (var i = 0; i < values.Length && i < length; i++) sums[i] += values[i];
        }

        // 网站年的矩阵是按天的（365 列），柱图要的是 12 个月
        if (dataType != 3 || length <= 12) return sums;

        var months = new double[12];
        for (var i = 0; i < length; i++)
            months[rangeStart.AddDays(i).Month - 1] += sums[i];

        return months;
    }

    private static string[] BucketLabels(int dataType, int count, DateTime rangeStart)
    {
        var labels = new string[Math.Max(0, count)];
        for (var i = 0; i < labels.Length; i++)
        {
            labels[i] = dataType switch
            {
                0 => i.ToString(),
                1 => WeekdayName(i),
                2 => rangeStart.AddDays(i).Day.ToString(),
                3 => MonthName(i),
                _ => i.ToString()
            };
        }

        return labels;
    }

    private static string WeekdayName(int index) => index switch
    {
        0 => ResourceStrings.Monday,
        1 => ResourceStrings.Tuesday,
        2 => ResourceStrings.Wednesday,
        3 => ResourceStrings.Thursday,
        4 => ResourceStrings.Friday,
        5 => ResourceStrings.Saturday,
        6 => ResourceStrings.Sunday,
        _ => string.Empty
    };

    private static string MonthName(int index) =>
        Application.Current?.Resources[$"{index + 1}Month"] as string ?? $"{index + 1}";

    /// <summary>分类 id → 显示名 / 颜色（颜色已按当前主题算过对比度）</summary>
    private async Task<Dictionary<int, CategoryVisual>> GetCategoryVisualsAsync(bool isWeb, CancellationToken ct)
    {
        var map = new Dictionary<int, CategoryVisual>();

        if (isWeb)
        {
            foreach (var item in await _webDataService.GetWebSiteCategoriesAsync(ct))
                map[item.ID] = new CategoryVisual(item.Name ?? ResourceStrings.Uncategorized,
                    ChartColorHelper.Resolve(item.Color, item.IsSystem));
        }
        else
        {
            foreach (var item in await _categoryService.GetCategoriesAsync(ct))
                map[item.ID] = new CategoryVisual(item.Name ?? ResourceStrings.Uncategorized,
                    ChartColorHelper.Resolve(item.Color, item.IsSystem));
        }

        return map;
    }

    private static (IReadOnlyList<ChartsDataModel> Series, string TotalText) BuildCategoryShare(
        IReadOnlyList<ColumnDataModel> columns, IReadOnlyDictionary<int, CategoryVisual> visuals)
    {
        var totals = new Dictionary<int, double>();
        foreach (var column in columns)
        {
            var sum = column.Values?.Sum() ?? 0;
            if (sum <= 0) continue;

            totals[column.CategoryID] = totals.TryGetValue(column.CategoryID, out var existing)
                ? existing + sum
                : sum;
        }

        var total = totals.Values.Sum();
        if (total <= 0) return ([], string.Empty);

        var ordered = totals.OrderByDescending(x => x.Value).ToList();
        var series = new List<ChartsDataModel>();
        var unknown = new CategoryVisual(ResourceStrings.Uncategorized, ChartColorHelper.Resolve(null, true));

        foreach (var (id, value) in ordered.Take(CategoryShareTopCount))
        {
            var visual = visuals.TryGetValue(id, out var found) ? found : unknown;
            series.Add(new ChartsDataModel
            {
                Name = visual.Name,
                Color = visual.Color,
                Value = value,
                Tag = InsightBuilder.FormatDuration(value)
            });
        }

        var rest = ordered.Skip(CategoryShareTopCount).Sum(x => x.Value);
        if (rest > 0)
        {
            series.Add(new ChartsDataModel
            {
                Name = ResourceStrings.ClockLegendOther,
                Color = "#8b949e",
                Value = rest,
                Tag = InsightBuilder.FormatDuration(rest)
            });
        }

        return (series, InsightBuilder.FormatDuration(total));
    }

    #region 热力图

    private HeatmapData BuildHeatmap(int dataType, bool isWeb,
        IReadOnlyList<(DateTime Start, DateTime End, string Name)> namedSpans,
        IReadOnlyList<(DateTime Start, DateTime End)> spans,
        IReadOnlyList<double> dayBuckets,
        DateTime rangeStart, DateTime rangeEnd)
    {
        return dataType switch
        {
            0 => BuildNameHourHeatmap(namedSpans, rangeStart, rangeEnd, isWeb),
            2 => BuildMonthCalendarHeatmap(dayBuckets, rangeStart),
            3 => BuildMonthDayHeatmap(spans, rangeStart, rangeEnd),
            _ => BuildWeekHourHeatmap(spans, rangeStart, rangeEnd)
        };
    }

    /// <summary>
    /// 月视图：日历热力（行＝周、列＝星期、格子＝当天总时长）。
    /// 月里已经有「每天时长」柱图，但柱图看的是量级，日历看的是「哪几天连着头忙」。
    /// </summary>
    private static HeatmapData BuildMonthCalendarHeatmap(IReadOnlyList<double> dayTotals, DateTime rangeStart)
    {
        if (dayTotals.Count == 0) return HeatmapData.Empty;

        var max = dayTotals.Max();
        if (max <= 0) return HeatmapData.Empty;

        // 每周从周一算起
        var firstColumn = ((int)rangeStart.DayOfWeek + 6) % 7;
        var weeks = (int)Math.Ceiling((firstColumn + dayTotals.Count) / 7d);

        var rows = new List<HeatRow>(weeks);
        var peakLabel = string.Empty;
        var peakValue = 0d;

        for (var week = 0; week < weeks; week++)
        {
            var cells = new List<HeatCell>(7);
            for (var column = 0; column < 7; column++)
            {
                var index = week * 7 + column - firstColumn;
                if (index < 0 || index >= dayTotals.Count)
                {
                    cells.Add(new HeatCell { IsPlaceholder = true });
                    continue;
                }

                var day = rangeStart.AddDays(index);
                var value = dayTotals[index];
                cells.Add(new HeatCell
                {
                    Value = value,
                    Tip = $"{day:M/d} {WeekdayName(column)} · {InsightBuilder.FormatDuration(value)}"
                });

                if (value <= peakValue) continue;
                peakValue = value;
                peakLabel = $"{day:M/d} {WeekdayName(column)}";
            }

            rows.Add(new HeatRow
            {
                Label = string.Format(ResourceStrings.InsightWeekOfMonth, week + 1),
                Cells = cells
            });
        }

        return new HeatmapData
        {
            Rows = rows,
            ColumnLabels = Enumerable.Range(0, 7).Select(WeekdayName).ToArray(),
            Caption = ResourceStrings.InsightHeatmapByCalendar,
            PeakText = $"{peakLabel} · {InsightBuilder.FormatDuration(peakValue)}",
            MaxValue = max,
            IsCalendar = true
        };
    }

    /// <summary>日视图：当天用得最多的几个应用 / 站点 × 小时</summary>
    private static HeatmapData BuildNameHourHeatmap(
        IReadOnlyList<(DateTime Start, DateTime End, string Name)> spans,
        DateTime rangeStart, DateTime rangeEnd, bool isWeb)
    {
        var grid = InsightBuilder.NameHourGrid(spans, rangeStart, rangeEnd);
        if (grid.Count == 0) return HeatmapData.Empty;

        var rows = grid
            .Where(x => x.Value.Sum() > 0)
            .OrderByDescending(x => x.Value.Sum())
            .Take(HeatmapTopRows)
            .ToList();

        if (rows.Count == 0) return HeatmapData.Empty;

        var max = rows.Max(x => x.Value.Max());
        if (max <= 0) return HeatmapData.Empty;

        var result = new List<HeatRow>(rows.Count);
        var peakName = string.Empty;
        var peakHour = 0;
        var peakValue = 0d;

        foreach (var (name, values) in rows)
        {
            var label = ShortenName(name);
            var cells = new List<HeatCell>(24);
            for (var hour = 0; hour < 24; hour++)
            {
                var value = values[hour];
                cells.Add(new HeatCell
                {
                    Value = value,
                    Tip = $"{name} {hour:00}:00 · {InsightBuilder.FormatDuration(value)}"
                });

                if (value <= peakValue) continue;
                peakValue = value;
                peakHour = hour;
                peakName = name;
            }

            result.Add(new HeatRow { Label = label, Cells = cells });
        }

        return new HeatmapData
        {
            Rows = result,
            ColumnLabels = Enumerable.Range(0, 24).Select(i => i.ToString()).ToArray(),
            Caption = isWeb ? ResourceStrings.InsightHeatmapBySite : ResourceStrings.InsightHeatmapByApp,
            PeakText = $"{peakName} {peakHour:00}:00 · {InsightBuilder.FormatDuration(peakValue)}",
            MaxValue = max
        };
    }

    /// <summary>热力图左侧行名只有窄窄一列，长的应用名截断，免得压到格子上</summary>
    private static string ShortenName(string name, int max = 7)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= max) return name;
        return string.Concat(name.AsSpan(0, max), "…");
    }

    /// <summary>星期 × 小时：作息地图</summary>
    private static HeatmapData BuildWeekHourHeatmap(IReadOnlyList<(DateTime Start, DateTime End)> spans,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var grid = InsightBuilder.WeekHourGrid(spans, rangeStart, rangeEnd);
        var max = InsightBuilder.Max(grid);
        if (max <= 0) return HeatmapData.Empty;

        var rows = new List<HeatRow>(7);
        var peakLabel = string.Empty;
        var peakValue = 0d;

        for (var row = 0; row < 7; row++)
        {
            var weekday = WeekdayName(row);
            var cells = new List<HeatCell>(24);
            for (var hour = 0; hour < 24; hour++)
            {
                var value = grid[row, hour];
                cells.Add(new HeatCell
                {
                    Value = value,
                    Tip = $"{weekday} {hour:00}:00 · {InsightBuilder.FormatDuration(value)}"
                });

                if (value <= peakValue) continue;
                peakValue = value;
                peakLabel = $"{weekday} {hour:00}:00";
            }

            rows.Add(new HeatRow { Label = weekday, Cells = cells });
        }

        return new HeatmapData
        {
            Rows = rows,
            ColumnLabels = Enumerable.Range(0, 24).Select(i => i.ToString()).ToArray(),
            Caption = ResourceStrings.InsightHeatmapByWeekday,
            PeakText = $"{peakLabel} · {InsightBuilder.FormatDuration(peakValue)}",
            MaxValue = max
        };
    }

    /// <summary>月份 × 日期：一年的日历热力</summary>
    private static HeatmapData BuildMonthDayHeatmap(IReadOnlyList<(DateTime Start, DateTime End)> spans,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var grid = InsightBuilder.MonthDayGrid(spans, rangeStart, rangeEnd);
        var max = InsightBuilder.Max(grid);
        if (max <= 0) return HeatmapData.Empty;

        var rows = new List<HeatRow>(12);
        var peakLabel = string.Empty;
        var peakValue = 0d;

        for (var month = 0; month < 12; month++)
        {
            var name = MonthName(month);
            var cells = new List<HeatCell>(31);
            for (var day = 0; day < 31; day++)
            {
                var value = grid[month, day];
                cells.Add(new HeatCell
                {
                    Value = value,
                    Tip = $"{name}{day + 1}日 · {InsightBuilder.FormatDuration(value)}"
                });

                if (value <= peakValue) continue;
                peakValue = value;
                peakLabel = $"{name}{day + 1}日";
            }

            rows.Add(new HeatRow { Label = name, Cells = cells });
        }

        return new HeatmapData
        {
            Rows = rows,
            ColumnLabels = Enumerable.Range(1, 31).Select(i => i.ToString()).ToArray(),
            Caption = ResourceStrings.InsightHeatmapByMonth,
            PeakText = $"{peakLabel} · {InsightBuilder.FormatDuration(peakValue)}",
            MaxValue = max
        };
    }

    #endregion

    /// <summary>会话 / 浏览记录统一成「本地时间片段 + 名称」，热力图和使用习惯共用</summary>
    private async Task<IReadOnlyList<(DateTime Start, DateTime End, string Name)>> GetSpansAsync(
        DateTime start, DateTime end, bool isWeb, CancellationToken ct)
    {
        if (isWeb)
        {
            var logs = await GetBrowseLogsCachedAsync(start, end, ct);
            return logs
                .Select(log =>
                {
                    var logStart = ToLocal(log.LogTime);
                    var name = log.Site?.Title ?? log.Site?.Domain ?? string.Empty;
                    return (Start: logStart, End: logStart.AddSeconds(Math.Max(1, log.Duration)), Name: name);
                })
                .Where(x => x.End > x.Start)
                .ToList();
        }

        var sessions = await GetSessionsCachedAsync(start, end, ct);
        return sessions
            .Select(session => (Start: ToLocal(session.StartTime), End: ToLocal(session.EndTime),
                Name: session.AppModel?.GetDisplayName() ?? string.Empty))
            .Where(x => x.End > x.Start)
            .ToList();
    }

    private async Task<List<AppSessionModel>> GetSessionsCachedAsync(DateTime start, DateTime end,
        CancellationToken ct)
    {
        if (_sessionCache != null && _sessionCacheStart == start && _sessionCacheEnd == end)
            return _sessionCache;

        var list = (await _dataService.GetAppSessionsAsync(start, end, ct)).ToList();
        _sessionCacheStart = start;
        _sessionCacheEnd = end;
        _sessionCache = list;
        return list;
    }

    private async Task<List<WebBrowseLogModel>> GetBrowseLogsCachedAsync(DateTime start, DateTime end,
        CancellationToken ct)
    {
        if (_browseCache != null && _browseCacheStart == start && _browseCacheEnd == end)
            return _browseCache;

        var list = (await _webDataService.GetBrowseLogListAsync(start, end, cancellationToken: ct)).ToList();
        _browseCacheStart = start;
        _browseCacheEnd = end;
        _browseCache = list;
        return list;
    }

    #endregion

    private static (DateTime Start, DateTime End) GetDateRange(DateTime date, int dataType)
    {
        return dataType switch
        {
            0 => (date.Date, date.Date.AddDays(1).AddTicks(-1)),
            1 => GetWeekRange(date),
            2 => (new DateTime(date.Year, date.Month, 1), new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), 23, 59, 59)),
            3 => (new DateTime(date.Year, 1, 1), new DateTime(date.Year, 12, 31, 23, 59, 59)),
            _ => (date, date)
        };
    }

    private static (DateTime Start, DateTime End) GetWeekRange(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        dayOfWeek = dayOfWeek == 0 ? 7 : dayOfWeek;
        var start = date.Date.AddDays(-(dayOfWeek - 1));
        var end = start.AddDays(6).Date.AddHours(23).AddMinutes(59).AddSeconds(59);
        return (start, end);
    }

    private static string FormatDuration(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        if (ts.TotalMinutes < 1)
            return $"{ts.Seconds}s";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
    }
}
