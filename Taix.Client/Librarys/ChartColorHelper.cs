using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Taix.Client.Librarys;

/// <summary>
/// 图表配色统一入口。
///
/// 分类颜色来自服务端，系统分类"未分类"默认是 #e4e3df（几乎等于白色背景），
/// 直接拿来画柱子/圆环等于什么都看不见。这里的规则：
/// 1. 系统分类一律用主题色——它本来就是"兜底桶"，用品牌色表达更合理
/// 2. 其他颜色如果过浅，按比例逐步压暗，保留色相的前提下保证可读
/// </summary>
public static class ChartColorHelper
{
    /// <summary>分类缺失时的兜底色</summary>
    private const string FallbackColor = "#C6C1D6";

    /// <summary>主题色拿不到时的兜底（与 Theme.axaml 的默认 ThemeColor 一致）</summary>
    private const string FallbackAccent = "#8780F7";

    private const double DarkenStep = 0.68;
    private const double LuminanceThreshold = 0.82;
    private const int MaxDarkenSteps = 3;

    /// <summary>
    /// 把分类色解析成可直接画图的高对比颜色
    /// </summary>
    /// <param name="color">分类原始颜色（#RRGGBB / #AARRGGBB）</param>
    /// <param name="isSystem">是否为系统分类（未分类）</param>
    public static string Resolve(string? color, bool isSystem = false)
    {
        if (isSystem) return AccentHex;

        if (string.IsNullOrWhiteSpace(color)) return FallbackColor;
        if (!Color.TryParse(color, out var parsed)) return FallbackColor;

        var factor = 1d;
        for (var i = 0; i < MaxDarkenSteps && Luminance(parsed) > LuminanceThreshold; i++)
        {
            factor *= DarkenStep;
            parsed = Color.FromArgb(
                parsed.A,
                (byte)(parsed.R * factor),
                (byte)(parsed.G * factor),
                (byte)(parsed.B * factor));
        }

        return ToHex(parsed);
    }

    /// <summary>
    /// 当前主题色（用户可在设置里改）
    /// </summary>
    public static string AccentHex
    {
        get
        {
            if (Application.Current?.FindResource("ThemeColor") is Color color)
                return ToHex(color);

            return FallbackAccent;
        }
    }

    /// <summary>
    /// 相对亮度（0~1），用于判断颜色是否和浅色背景糊在一起
    /// </summary>
    public static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
