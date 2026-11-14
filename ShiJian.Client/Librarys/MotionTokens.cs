using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;


namespace ShiJian.Client.Librarys;

/// <summary>
/// 动效标尺（对齐 Super Productivity，见 docs/DESIGN-SP-ALIGN.md）。
///
/// 规则：所有动效只能引用这里的时长与曲线，代码里不允许再出现裸毫秒数。
/// 参考项目全场只用三档曲线、四档时长——档位少，观感才统一。
/// </summary>
public static class MotionTokens
{
    // ── 时长四档 ──
    /// <summary>微交互：hover 变色、图标显隐</summary>
    public static readonly TimeSpan Xs = TimeSpan.FromMilliseconds(90);

    /// <summary>小控件：按钮、输入框、行内反馈</summary>
    public static readonly TimeSpan S = TimeSpan.FromMilliseconds(150);

    /// <summary>默认：卡片、面板、展开收起、页面切换</summary>
    public static readonly TimeSpan M = TimeSpan.FromMilliseconds(225);

    /// <summary>大块移动 / 图表生长上限：严格控制在 300ms 以内</summary>
    public static readonly TimeSpan L = TimeSpan.FromMilliseconds(260);

    // ── 页面切换（三者并行，总时长 = 225ms，不是 225+180） ──
    /// <summary>旧页淡出：极快，只为不硬切</summary>
    public static readonly TimeSpan PageLeave = TimeSpan.FromMilliseconds(75);

    /// <summary>新页淡入 + 上移的时长</summary>
    public static readonly TimeSpan PageEnter = TimeSpan.FromMilliseconds(180);

    /// <summary>新页进场延迟：让旧页先淡掉一点，避免双重曝光</summary>
    public static readonly TimeSpan PageEnterDelay = TimeSpan.FromMilliseconds(45);

    /// <summary>页面进场位移，幅度刻意极小（做大了立刻显廉价）</summary>
    public const double PageOffsetY = 8;

    // ── 三条缓动曲线 ──
    /// <summary>Avalonia 12 没有公开的 CubicBezierEase，用 SplineEasing 表达同样的三次贝塞尔</summary>
    private static Easing Spline(double x1, double y1, double x2, double y2) =>
        new SplineEasing { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };

    /// <summary>通用状态切换</summary>
    public static Easing Standard() => Spline(0.40, 0, 0.20, 1);

    /// <summary>所有「出现」——减速进场</summary>
    public static Easing Enter() => Spline(0.00, 0, 0.20, 1);

    /// <summary>所有「离开」——加速退场</summary>
    public static Easing Leave() => Spline(0.40, 0, 1.00, 1);
}
