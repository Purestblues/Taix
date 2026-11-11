using System.Collections.Generic;
using Taix.Client.Controls.Charts.Model;

namespace Taix.Client.Models;

/// <summary>
/// 看板里的一张指标卡（上半部分大数字 + 下半部分环比）。
/// 图标不放在模型里：卡片位置固定，XAML 里直接写死更省事。
/// </summary>
public sealed class InsightStatCard
{
    /// <summary>卡片标题，如「总使用时长」</summary>
    public string Caption { get; init; } = string.Empty;

    /// <summary>主数值，已格式化</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>副标题（可为空）</summary>
    public string? Sub { get; init; }

    /// <summary>环比文本，如「+12%」；为空表示这一项不给环比</summary>
    public string? DiffText { get; init; }

    /// <summary>环比说明，如「比昨日」</summary>
    public string? DiffCaption { get; init; }

    /// <summary>环比方向，供箭头图标用</summary>
    public bool IsUp { get; init; }

    public bool IsDown { get; init; }

    public bool IsFlat { get; init; }

    public bool HasDiff => !string.IsNullOrEmpty(DiffText);

    public bool HasSub => !string.IsNullOrEmpty(Sub);
}

/// <summary>使用习惯里的一行「标签 → 值」</summary>
public sealed class InsightHabitRow
{
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// 时段偏好里的一根横条。宽度在计算层算好，
/// 免得 XAML 里为了一个百分比再写转换器。
/// </summary>
public sealed class InsightBand
{
    public string Name { get; init; } = string.Empty;

    public string ShareText { get; init; } = string.Empty;

    /// <summary>条宽（像素），按占比算</summary>
    public double BarWidth { get; init; }

    /// <summary>占比（0~1），用来给条做透明度</summary>
    public double Share { get; init; }
}

/// <summary>热力图单元格</summary>
public sealed class HeatCell
{
    public double Value { get; init; }

    /// <summary>悬停文案</summary>
    public string Tip { get; init; } = string.Empty;

    /// <summary>
    /// 占位格：日历里「这个月没有这一天」的位置，不画也不响应悬停，
    /// 不然月初月末会多出一串假的 0 时长格子
    /// </summary>
    public bool IsPlaceholder { get; init; }
}

/// <summary>热力图一行（星期 / 分类 / 月份）</summary>
public sealed class HeatRow
{
    public string Label { get; init; } = string.Empty;

    public IReadOnlyList<HeatCell> Cells { get; init; } = [];
}

/// <summary>一整张热力图</summary>
public sealed class HeatmapData
{
    public static HeatmapData Empty => new();

    public IReadOnlyList<HeatRow> Rows { get; init; } = [];

    /// <summary>列刻度（小时 / 日）</summary>
    public string[] ColumnLabels { get; init; } = [];

    /// <summary>一句话说明，如「行＝星期，列＝小时」</summary>
    public string Caption { get; init; } = string.Empty;

    /// <summary>峰值说明，如「周三 15:00 · 1h 20m」</summary>
    public string PeakText { get; init; } = string.Empty;

    /// <summary>全图最大值，供控件换算颜色深浅</summary>
    public double MaxValue { get; init; }

    /// <summary>日历式（周 × 星期）：控件把格子画成方块，而不是拉长的条</summary>
    public bool IsCalendar { get; init; }

    public bool HasData => Rows.Count > 0 && MaxValue > 0;
}

/// <summary>
/// 一个周期（日 / 周 / 月 / 年）的详细页看板数据。
/// 切换周期时整体替换，页面只绑 <c>CurrentInsight</c> 一份。
/// </summary>
public sealed class DataPageInsight
{
    public static DataPageInsight Empty => new();

    public IReadOnlyList<InsightStatCard> Cards { get; init; } = [];

    // 卡片位置固定，直接给四个命名属性：XAML 里绑 Cards[0] 在列表为空时会直接炸
    private static readonly InsightStatCard EmptyCard = new();

    public InsightStatCard Card1 => Cards.Count > 0 ? Cards[0] : EmptyCard;

    public InsightStatCard Card2 => Cards.Count > 1 ? Cards[1] : EmptyCard;

    public InsightStatCard Card3 => Cards.Count > 2 ? Cards[2] : EmptyCard;

    public InsightStatCard Card4 => Cards.Count > 3 ? Cards[3] : EmptyCard;

    public IReadOnlyList<InsightHabitRow> Habits { get; init; } = [];

    public IReadOnlyList<InsightBand> Bands { get; init; } = [];

    public HeatmapData Heatmap { get; init; } = HeatmapData.Empty;

    /// <summary>时段分布柱图的数据（单序列）</summary>
    public IReadOnlyList<ChartsDataModel> HourSeries { get; init; } = [];

    public string[] HourLabels { get; init; } = [];

    /// <summary>上一周期同粒度总量，画成淡色对比柱</summary>
    public IReadOnlyList<double> CompareValues { get; init; } = [];

    /// <summary>对比说明，如「浅色为昨日」</summary>
    public string CompareCaption { get; init; } = string.Empty;

    /// <summary>柱图标题右侧的总计文本</summary>
    public string BucketTotalText { get; init; } = string.Empty;

    /// <summary>分类占比环形图</summary>
    public IReadOnlyList<ChartsDataModel> CategorySeries { get; init; } = [];

    public string CategoryTotalText { get; init; } = string.Empty;

    /// <summary>占比环下面的标题（应用 / 网站）</summary>
    public string CategoryCaption { get; init; } = string.Empty;

    public bool HasData => HourSeries.Count > 0 || Cards.Count > 0;
}
