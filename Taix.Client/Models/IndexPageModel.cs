using System.Collections.Generic;
using Avalonia.Controls;
using Taix.Client.Controls.Charts.Model;
using Taix.Client.Controls.Select;
using Taix.Client.PageState;

namespace Taix.Client.Models;

[GeneratePageState]
public partial class IndexPageModel : ModelBase
{
    private List<ChartsDataModel> _appMoreData;
    private List<ChartsDataModel> _appFrequentUseData;

    private int _frequentUseNum;

    private int _moreNum;
    private SelectItemModel _moreType;

    private int _tabbarSelectedIndex;

    private List<ChartsDataModel> _webFrequentUseData;
    private List<ChartsDataModel> _webMoreData;

    private List<ChartsDataModel> _weekData;

    private SelectItemModel _selectedPeriod;
    private List<SelectItemModel> _periodOptions;

    private string _compareCaption = string.Empty;

    /// <summary>
    /// 对比文字的周期描述（比昨日 / 比上周）
    /// </summary>
    [PageState]
    public string CompareCaption
    {
        get => _compareCaption;
        set
        {
            _compareCaption = value;
            OnPropertyChanged();
        }
    }

    #region 概览卡片

    private double _totalTime;
    private double _lastTotalTime;
    private string _totalTimeText = "0";
    private double _appCount;
    private double _lastAppCount;
    private double _webTime;
    private double _lastWebTime;
    private string _webTimeText = "0";
    private double _webSiteCount;
    private double _lastWebSiteCount;
    private double _webPageCount;
    private double _lastWebPageCount;

    /// <summary>
    /// 总使用时长（应用+网站，秒）
    /// </summary>
    [PageState]
    public double TotalTime
    {
        get => _totalTime;
        set
        {
            _totalTime = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 上一个周期的总使用时长（用于对比）
    /// </summary>
    [PageState]
    public double LastTotalTime
    {
        get => _lastTotalTime;
        set
        {
            _lastTotalTime = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public string TotalTimeText
    {
        get => _totalTimeText;
        set
        {
            _totalTimeText = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double AppCount
    {
        get => _appCount;
        set
        {
            _appCount = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double LastAppCount
    {
        get => _lastAppCount;
        set
        {
            _lastAppCount = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double WebTime
    {
        get => _webTime;
        set
        {
            _webTime = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double LastWebTime
    {
        get => _lastWebTime;
        set
        {
            _lastWebTime = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public string WebTimeText
    {
        get => _webTimeText;
        set
        {
            _webTimeText = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double WebSiteCount
    {
        get => _webSiteCount;
        set
        {
            _webSiteCount = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double LastWebSiteCount
    {
        get => _lastWebSiteCount;
        set
        {
            _lastWebSiteCount = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double WebPageCount
    {
        get => _webPageCount;
        set
        {
            _webPageCount = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public double LastWebPageCount
    {
        get => _lastWebPageCount;
        set
        {
            _lastWebPageCount = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 图表展示的是应用还是网站
    /// </summary>
    [PageState(LookupFrom = nameof(ChartTypeOptions))]
    public SelectItemModel ChartType
    {
        get => _chartType;
        set
        {
            _chartType = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 图表类型选项（0=应用/1=网站）
    /// </summary>
    public List<SelectItemModel> ChartTypeOptions { get; set; } = [];

    /// <summary>
    /// 环形图中心标注（应用 / 网站）
    /// </summary>
    [PageState]
    public string ChartDonutCaption
    {
        get => _chartDonutCaption;
        set
        {
            _chartDonutCaption = value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region 图表

    private string _chartDonutCaption = string.Empty;
    private SelectItemModel _chartType;

    private List<ChartsDataModel> _hourSeries = [];
    private string[] _hourLabels = [];
    private string _hourTotalText = "0";
    private List<ChartsDataModel> _categorySeries = [];
    private string _categoryTotalText = "0";

    /// <summary>
    /// 时段分布序列（每项为一条分类，Values 与 HourLabels 对齐）
    /// </summary>
    [PageState(DataCache = true)]
    public List<ChartsDataModel> HourSeries
    {
        get => _hourSeries;
        set
        {
            _hourSeries = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 时段分布底部刻度
    /// </summary>
    [PageState(DataCache = true)]
    public string[] HourLabels
    {
        get => _hourLabels;
        set
        {
            _hourLabels = value;
            OnPropertyChanged();
        }
    }

    [PageState(DataCache = true)]
    public string HourTotalText
    {
        get => _hourTotalText;
        set
        {
            _hourTotalText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 分类占比序列（Value 为分类总时长）
    /// </summary>
    [PageState(DataCache = true)]
    public List<ChartsDataModel> CategorySeries
    {
        get => _categorySeries;
        set
        {
            _categorySeries = value;
            OnPropertyChanged();
        }
    }

    [PageState(DataCache = true)]
    public string CategoryTotalText
    {
        get => _categoryTotalText;
        set
        {
            _categoryTotalText = value;
            OnPropertyChanged();
        }
    }

    #endregion

    /// <summary>
    /// 周期选项（今日/本周）
    /// </summary>
    public List<SelectItemModel> PeriodOptions
    {
        get => _periodOptions;
        set
        {
            _periodOptions = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 选中的周期
    /// </summary>
    [PageState(LookupFrom = nameof(PeriodOptions))]
    public SelectItemModel SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            _selectedPeriod = value;
            OnPropertyChanged();
            if (value != null)
                TabbarSelectedIndex = value.Id;
        }
    }

    /// <summary>
    /// tabbar selected item index (内部使用)
    /// </summary>
    [PageState]
    public int TabbarSelectedIndex
    {
        get => _tabbarSelectedIndex;
        set
        {
            _tabbarSelectedIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// week data
    /// </summary>
    [PageState(DataCache = true)]
    public List<ChartsDataModel> WeekData
    {
        get => _weekData;
        set
        {
            _weekData = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 最为频繁条数
    /// </summary>
    public int FrequentUseNum
    {
        get => _frequentUseNum;
        set
        {
            _frequentUseNum = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 更多条数
    /// </summary>
    public int MoreNum
    {
        get => _moreNum;
        set
        {
            _moreNum = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 网页最为频繁数据
    /// </summary>
    [PageState(DataCache = true)]
    public List<ChartsDataModel> WebFrequentUseData
    {
        get => _webFrequentUseData;
        set
        {
            _webFrequentUseData = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 更多展示数据类型（0=应用/1=网页）
    /// </summary>
    [PageState(LookupFrom = nameof(MoreTypeOptions))]
    public SelectItemModel MoreType
    {
        get => _moreType;
        set
        {
            _moreType = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 应用最为频繁数据
    /// </summary>
    [PageState(DataCache = true)]
    public List<ChartsDataModel> AppFrequentUseData
    {
        get => _appFrequentUseData;
        set
        {
            _appFrequentUseData = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 应用更多数据
    /// </summary>
    [PageState(DataCache = true)]
    public List<ChartsDataModel> AppMoreData
    {
        get => _appMoreData;
        set
        {
            _appMoreData = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 网页更多数据
    /// </summary>
    [PageState(DataCache = true)]
    public List<ChartsDataModel> WebMoreData
    {
        get => _webMoreData;
        set
        {
            _webMoreData = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 更多展示数据类型选项（应用/网页）
    /// </summary>
    public List<SelectItemModel> MoreTypeOptions { get; set; } = [];
}
