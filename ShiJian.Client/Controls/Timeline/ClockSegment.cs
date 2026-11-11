using System;
using System.Collections.Generic;

namespace ShiJian.Client.Controls.Timeline;

/// <summary>时钟环上的刻度类型：日看小时、周/月看天、年看月</summary>
public enum ClockTickMode
{
    Hour,
    Day,
    Month
}

/// <summary>
/// 时钟环上的一段使用记录。
/// 位置用「占整个圆周的比例」表达（0 = 周期起点，顺时针），
/// 这样日 / 周 / 月 / 年可以共用一套绘制逻辑。
/// </summary>
public sealed class ClockSegment
{
    public double StartFraction { get; init; }
    public double EndFraction { get; init; }

    /// <summary>应用名</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>分类名</summary>
    public string? CategoryName { get; init; }

    /// <summary>应用色（时间轴调色板）</summary>
    public string Color { get; init; } = "#7C6BF0";

    /// <summary>分类色（已按主题背景算过对比度）</summary>
    public string CategoryColor { get; init; } = "#7C6BF0";

    /// <summary>悬停时显示的时间文案，例如 09:12 ~ 09:48</summary>
    public string RangeText { get; init; } = string.Empty;

    /// <summary>这段的时长（秒）</summary>
    public double Seconds { get; init; }

    /// <summary>跳转用的数据（AppModel）</summary>
    public object? Data { get; init; }

    /// <summary>分类跳转用的数据（CategoryModel）</summary>
    public object? CategoryData { get; init; }
}

/// <summary>环旁边的图例条目（前 N 名 + 其他）</summary>
public sealed class ClockLegendItem
{
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = "#7C6BF0";
    public string DurationText { get; init; } = string.Empty;
    public string ShareText { get; init; } = string.Empty;
    public object? Data { get; init; }
    public object? CategoryData { get; init; }
    public bool IsOther { get; init; }
}

/// <summary>一个页签的环形时钟数据</summary>
public sealed class ClockRingData
{
    public static ClockRingData Empty => new();

    public List<ClockSegment> Segments { get; init; } = [];
    public List<ClockLegendItem> Legend { get; init; } = [];
    public string CenterText { get; init; } = string.Empty;
    public string CenterCaption { get; init; } = string.Empty;
    public ClockTickMode TickMode { get; init; } = ClockTickMode.Hour;

    /// <summary>周期起点（只用于生成刻度文案）</summary>
    public DateTime RangeStart { get; init; }

    /// <summary>周期终点</summary>
    public DateTime RangeEnd { get; init; }

    public bool HasData => Segments.Count > 0;
}
