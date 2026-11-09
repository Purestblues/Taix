using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Taix.Client.Librarys;

/// <summary>
/// 图表配色统一入口。
///
/// 分类颜色来自服务端，系统分类"未分类"默认是 #e4e3df（几乎等于白色背景），
/// 直接拿来画柱子/圆环等于什么都看不见。这里的规则：
/// 1. 系统分类一律用主题色——它本来就是"兜底桶"，用品牌色表达更合理
/// 2. 其他颜色按"和当前主题背景的对比度"调整：浅色背景下太浅的压暗，深色背景下太暗的提亮，
///    只调到刚好够看（WCAG 图形元素 3:1），尽量保留原来的色相与明暗关系
///
/// 判定基准是当前主题实际使用的窗口背景色，所以同一套分类色在浅色 / 深色主题下会自动
/// 收敛到各自可读的版本，不再依赖写死的亮度阈值。
/// </summary>
public static class ChartColorHelper
{
    /// <summary>分类缺失时的兜底色</summary>
    private const string FallbackColor = "#C6C1D6";

    /// <summary>主题色拿不到时的兜底（与 Theme.axaml 的默认 ThemeColor 一致）</summary>
    private const string FallbackAccent = "#8780F7";

    /// <summary>图形元素的最低对比度（WCAG 2.1 AA 对非文本图形的要求）</summary>
    public const double TargetContrast = 3d;

    /// <summary>主题背景拿不到时的浅色 / 深色兜底（与 ThemeServicer 的实色背景一致）</summary>
    private static readonly Color LightBackground = Color.Parse("#EDEDF0");
    private static readonly Color DarkBackground = Color.Parse("#131315");

    private const int AdjustIterations = 14;

    /// <summary>主题（背景色 / 主题色）变化后触发，页面据此重新解析缓存里的颜色</summary>
    public static event Action? ThemeChanged;

    /// <summary>由 <see cref="Servicers.ThemeServicer"/> 在应用完主题后调用</summary>
    public static void NotifyThemeChanged() => ThemeChanged?.Invoke();

    /// <summary>
    /// 把分类色解析成可直接画图的高对比颜色
    /// </summary>
    /// <param name="color">分类原始颜色（#RRGGBB / #AARRGGBB）</param>
    /// <param name="isSystem">是否为系统分类（未分类）</param>
    public static string Resolve(string? color, bool isSystem = false) =>
        ResolveFor(color, isSystem, BackgroundColor);

    /// <summary>
    /// 指定背景色的解析版本，便于单测与"按图片取色"等场景
    /// </summary>
    public static string ResolveFor(string? color, bool isSystem, Color background)
    {
        if (isSystem)
            return ToHex(EnsureContrast(ParseOr(AccentHex, FallbackAccent), background));

        var parsed = ParseOr(color, FallbackColor);
        return ToHex(EnsureContrast(parsed, background));
    }

    /// <summary>
    /// 当前图表背景色：优先用主题实际设置的窗口背景（实色时），否则按明暗变体取兜底
    /// </summary>
    public static Color BackgroundColor
    {
        get
        {
            var app = Application.Current;
            if (app != null &&
                app.Resources.TryGetResource("WindowBackground", app.ActualThemeVariant, out var value))
            {
                switch (value)
                {
                    case ISolidColorBrush solid:
                        return solid.Color;
                    case Color color:
                        return color;
                }
            }

            return app?.ActualThemeVariant == ThemeVariant.Dark ? DarkBackground : LightBackground;
        }
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
    /// WCAG 相对亮度（0~1），已按 sRGB 做伽马还原
    /// </summary>
    public static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);

    /// <summary>
    /// 两个颜色的对比度（1~21）
    /// </summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static Color EnsureContrast(Color color, Color background)
    {
        if (ContrastRatio(color, background) >= TargetContrast)
            return color;

        // 背景暗就往白里提，背景亮就往黑里压——这样才能拉开对比度
        var towardsWhite = RelativeLuminance(background) < 0.5;
        var extreme = towardsWhite ? Color.FromRgb(255, 255, 255) : Color.FromRgb(0, 0, 0);

        if (ContrastRatio(extreme, background) < TargetContrast)
            return extreme;

        // 二分找"刚好达到目标对比度"的最小改动量，避免把颜色调过头
        double low = 0, high = 1;
        var result = extreme;
        for (var i = 0; i < AdjustIterations; i++)
        {
            var mid = (low + high) / 2;
            var candidate = Blend(color, extreme, mid);
            if (ContrastRatio(candidate, background) >= TargetContrast)
            {
                result = candidate;
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return result;
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static double Linearize(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color ParseOr(string? color, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(color) && Color.TryParse(color, out var parsed))
            return parsed;

        return Color.Parse(fallback);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
