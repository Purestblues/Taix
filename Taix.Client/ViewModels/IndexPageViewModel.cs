using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    /// 一次取齐榜单 + 概览 + 图表所需数据（全部并发，避免串行请求把首页拖慢）
    /// </summary>
    private async Task LoadRangeAsync(DateTime start, DateTime end,
        DateTime prevStart, DateTime prevEnd, bool isWeek, CancellationToken cancellationToken)
    {
        var appListTask = _dataService.GetDateRangelogListAsync(start, end, cancellationToken: cancellationToken);
        var webListTask = _webDataService.GetDateRangeWebSiteListAsync(start, end, cancellationToken: cancellationToken);

        var appTotalTask = _dataService.GetRangeTotalDataAsync(start, end, cancellationToken);
        var prevAppTotalTask = _dataService.GetRangeTotalDataAsync(prevStart, prevEnd, cancellationToken);
        var appCountTask = _dataService.GetDateRangeAppCountAsync(start, end, cancellationToken);
        var prevAppCountTask = _dataService.GetDateRangeAppCountAsync(prevStart, prevEnd, cancellationToken);

        var webTimeTask = _webDataService.GetBrowseDurationTotalAsync(start, end, cancellationToken);
        var prevWebTimeTask = _webDataService.GetBrowseDurationTotalAsync(prevStart, prevEnd, cancellationToken);
        var siteCountTask = _webDataService.GetBrowseSitesTotalAsync(start, end, cancellationToken);
        var prevSiteCountTask = _webDataService.GetBrowseSitesTotalAsync(prevStart, prevEnd, cancellationToken);
        var pageCountTask = _webDataService.GetBrowsePagesTotalAsync(start, end, cancellationToken);
        var prevPageCountTask = _webDataService.GetBrowsePagesTotalAsync(prevStart, prevEnd, cancellationToken);

        var columnTask = isWeek
            ? _dataService.GetCategoryRangeDataAsync(start, end, cancellationToken)
            : _dataService.GetCategoryHoursDataAsync(start, cancellationToken);
        var categoriesTask = _categoryService.GetCategoriesAsync(cancellationToken);

        await Task.WhenAll(appListTask, webListTask, appTotalTask, prevAppTotalTask, appCountTask, prevAppCountTask,
            webTimeTask, prevWebTimeTask, siteCountTask, prevSiteCountTask, pageCountTask, prevPageCountTask,
            columnTask, categoriesTask);
        cancellationToken.ThrowIfCancellationRequested();

        // 榜单
        CompareCaption = isWeek ? ResourceStrings.CategoryVsLastWeek : ResourceStrings.CategoryVsYesterday;

        var appChartData = ChartDataMapper.MapFromDailyLogs(await appListTask);
        WeekData = appChartData;
        AppFrequentUseData = appChartData.Take(FrequentUseNum).ToList();
        AppMoreData = appChartData.Skip(FrequentUseNum).Take(MoreNum).ToList();

        var webChartData = ChartDataMapper.MapFromWebSites(await webListTask);
        WebFrequentUseData = webChartData.Take(FrequentUseNum).ToList();
        WebMoreData = webChartData.Skip(FrequentUseNum).Take(MoreNum).ToList();

        // 概览卡片
        var appTotal = (await appTotalTask)?.Sum() ?? 0;
        var prevAppTotal = (await prevAppTotalTask)?.Sum() ?? 0;
        var webTime = await webTimeTask;
        var prevWebTime = await prevWebTimeTask;

        TotalTime = appTotal + webTime;
        LastTotalTime = prevAppTotal + prevWebTime;
        TotalTimeText = Time.ToString((int)TotalTime);

        AppCount = await appCountTask;
        LastAppCount = await prevAppCountTask;

        WebTime = webTime;
        LastWebTime = prevWebTime;
        WebTimeText = Time.ToString((int)webTime);

        WebSiteCount = await siteCountTask;
        LastWebSiteCount = await prevSiteCountTask;
        WebPageCount = await pageCountTask;
        LastWebPageCount = await prevPageCountTask;

        BuildChartSeries(await columnTask, await categoriesTask, isWeek);
    }

    /// <summary>
    /// 把服务端的分类×时段矩阵拆成“时段堆叠序列”与“分类占比序列”
    /// </summary>
    private void BuildChartSeries(IReadOnlyList<ColumnDataModel> columns, IReadOnlyList<CategoryModel> categories, bool isWeek)
    {
        var unknownName = Application.Current?.FindResource("Uncategorized") as string ?? "Unknown";

        var categoryMap = new Dictionary<int, CategoryModel>();
        if (categories != null)
            foreach (var item in categories)
                categoryMap[item.ID] = item;

        var series = new List<ChartsDataModel>(columns?.Count ?? 0);
        if (columns != null)
        {
            foreach (var column in columns)
            {
                if (!categoryMap.TryGetValue(column.CategoryID, out var category) || category == null)
                    category = new CategoryModel { Name = unknownName, Color = "#C6C1D6", IconFile = string.Empty };

                series.Add(new ChartsDataModel
                {
                    Name = category.Name,
                    Icon = category.IconFile,
                    Color = ResolveSeriesColor(category),
                    Values = column.Values ?? []
                });
            }
        }

        series = series.OrderByDescending(m => m.Values.Sum()).ToList();

        HourSeries = series;
        HourLabels = isWeek ? WeekDayLabels() : HourLabels24();
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
    }

    private static string[] HourLabels24()
    {
        var labels = new string[24];
        for (var i = 0; i < 24; i++) labels[i] = i.ToString();
        return labels;
    }

    /// <summary>
    /// 分类色太浅时图形基本看不见（系统分类“未分类”默认是 #e4e3df），
    /// 这里把系统分类换成主题色、其他过浅颜色逐步压暗，保证图表可读
    /// </summary>
    private static string ResolveSeriesColor(CategoryModel category)
    {
        if (category.IsSystem &&
            Application.Current?.TryFindResource("ThemeColor", out var accent) == true &&
            accent is Color themeColor)
        {
            return $"#{themeColor.R:X2}{themeColor.G:X2}{themeColor.B:X2}";
        }

        var raw = category.Color;
        if (string.IsNullOrWhiteSpace(raw)) return "#C6C1D6";
        if (!Color.TryParse(raw, out var color)) return raw;

        var factor = 1d;
        for (var i = 0; i < 3 && Luminance(color) > 0.82; i++)
        {
            factor *= 0.68;
            color = Color.FromArgb(color.A,
                (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
        }

        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;

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
