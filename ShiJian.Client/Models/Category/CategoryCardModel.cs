using System;
using ShiJian.Client.Shared.Librarys;

namespace ShiJian.Client.Models.Category;

/// <summary>
/// 分类卡片上要展示的统计量：应用数 + 周期时长 + 占比。
///
/// 应用分类（<see cref="CategoryModel"/>）与网站分类（<c>WebCategoryModel</c>）是两套 Data 模型，
/// 但卡片长得一模一样，格式化规则抽在这里，避免两份各写一遍、以后一边改一边漏。
/// </summary>
public abstract class CategoryCardModel
{
    /// <summary>分类下的应用数 / 站点数</summary>
    public int Count { get; set; }

    /// <summary>周期内时长（秒）</summary>
    public double Duration { get; set; }

    /// <summary>占同维度时长合计的比例（0..1），给占比条用</summary>
    public double Ratio { get; set; }

    /// <summary>不足 1 秒按「没有数据」处理，避免卡片上出现 0s 和一条看不见的进度条</summary>
    public bool HasDuration => Duration >= 1;

    public string DurationText => HasDuration ? Time.ToString((int)Duration) : "—";

    public string RatioText => HasDuration ? Math.Round(Ratio * 100).ToString("f0") + "%" : string.Empty;

    public string CountText => "×" + Count;
}
