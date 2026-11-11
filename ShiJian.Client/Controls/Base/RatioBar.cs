using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ShiJian.Client.Controls.Base;

/// <summary>
/// 按比例填充的横条（榜单里的时长占比条）。
///
/// 为什么不直接用矩形设置 Width：宽度要按容器宽度算，而容器宽度只有在布局完成后才知道。
/// 之前那套「Render 时算宽度 + SizeChanged 再补一次」的写法在虚拟化列表里会漏：
/// 行被复用时容器尺寸不一定再变化，于是宽度停在 0，条就永远不出现（切页、快速滚动都能复现）。
/// 这里改成在 <see cref="Render"/> 里按当前 <see cref="Bounds"/> 现算，绘制发生在布局之后，
/// 不存在时序问题，也不需要任何手写的尺寸订阅。
/// </summary>
public class RatioBar : Control
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<RatioBar, double>(nameof(Ratio));

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<RatioBar, IBrush?>(nameof(BarBrush));

    /// <summary>条的最小可见宽度：占比极小时也要能看见（否则像没画）</summary>
    public static readonly StyledProperty<double> MinWidthProperty =
        AvaloniaProperty.Register<RatioBar, double>(nameof(MinWidth), 3d);

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<RatioBar, CornerRadius>(nameof(CornerRadius), new CornerRadius(1.5));

    static RatioBar()
    {
        AffectsRender<RatioBar>(RatioProperty, BarBrushProperty, CornerRadiusProperty, MinWidthProperty);
    }

    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public double MinWidth
    {
        get => GetValue(MinWidthProperty);
        set => SetValue(MinWidthProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var ratio = Ratio;
        if (ratio <= 0 || BarBrush is not { } brush) return;

        if (ratio > 1) ratio = 1;

        var height = Bounds.Height;
        var width = Bounds.Width * ratio;
        if (width < MinWidth) width = MinWidth;
        if (width <= 0 || height <= 0) return;
        if (width > Bounds.Width) width = Bounds.Width;

        context.DrawRectangle(brush, null, new Rect(0, 0, width, height), CornerRadius.TopLeft, CornerRadius.TopLeft);
    }
}
