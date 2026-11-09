using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using Taix.Client.Base.Color;
using Taix.Client.Controls.Charts.Model;
using Taix.Client.Controls.Timeline;
using Taix.Client.Events;
using Taix.Client.Foundation;
using Taix.Client.Foundation.Rx;
using Taix.Client.Librarys;
using Taix.Client.Models;
using Taix.Client.Models.Navigation;
using Taix.Client.Servicers;
using Taix.Client.Servicers.Interfaces;
using Taix.Client.Shared.Models;
using Taix.Client.Shared.Models.Web;
using Taix.Client.Shared.Servicers.Interfaces;
using Taix.Client.Views;
using AppModel = Taix.Client.Shared.Models.AppModel;

namespace Taix.Client.ViewModels;

public partial class DataPageViewModel : DataPageModel
{
    private readonly IWebData _webDataService;
    private readonly IData _dataService;
    private readonly INavigationService _navigationService;
    private readonly IStateService _stateService;
    private readonly IAppEventService _appEventService;



    public DataPageViewModel(
        IData data,
        IWebData webData,
        INavigationService navigationService,
        IStateService stateService,
        IAppEventService appEventService)
    {
        _dataService = data;
        _webDataService = webData;
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

        var sessions = await _dataService.GetAppSessionsAsync(dayStart, dayEnd.AddSeconds(-1), ct);
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
