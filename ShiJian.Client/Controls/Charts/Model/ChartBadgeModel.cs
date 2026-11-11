using System.Collections.Generic;

namespace ShiJian.Client.Controls.Charts.Model;

/// <summary>
/// 徽章模型
/// </summary>
public class ChartBadgeModel
{
    /// <summary>
    /// 忽略应用徽章
    /// </summary>
    public static ChartBadgeModel IgnoreBadge = new()
    {
        Name = ResourceStrings.Ignore,
        Color = "#f51837",
        Type = ChartBadgeType.Ignore
    };

    public static IReadOnlySet<string> IgnreLanguages = new HashSet<string>
    {
        "忽略",
        "ignore"
    };

    /// <summary>
    /// 徽章名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 徽章颜色
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// 徽章上的文字颜色（按底色算对比度，深色底白字、浅色底黑字）
    /// </summary>
    public string TextColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// 分类 Id（分类徽章才有，用于点击跳转）
    /// </summary>
    public int CategoryId { get; set; }

    /// <summary>
    /// 是否为网站分类（否则是应用分类）
    /// </summary>
    public bool IsWebCategory { get; set; }

    /// <summary>
    /// 徽章类型
    /// </summary>
    public ChartBadgeType Type { get; set; }
}