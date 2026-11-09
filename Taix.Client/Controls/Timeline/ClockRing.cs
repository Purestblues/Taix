using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Taix.Client.Controls.Timeline;

/// <summary>
/// 环形时钟：把一段周期画成表盘，扇区就是真实的使用时段。
/// 日 = 24 小时表盘，周 = 7 天表盘，月 = 当月天数表盘，年 = 12 个月表盘。
/// 悬停显示该段的名称与时间，点击交给 <see cref="ClickCommand"/>。
/// </summary>
public class ClockRing : Control, ICustomHitTest
{
    public static readonly StyledProperty<IEnumerable<ClockSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<ClockRing, IEnumerable<ClockSegment>?>(nameof(Segments));

    public static readonly StyledProperty<ClockTickMode> TickModeProperty =
        AvaloniaProperty.Register<ClockRing, ClockTickMode>(nameof(TickMode));

    public static readonly StyledProperty<DateTime> RangeStartProperty =
        AvaloniaProperty.Register<ClockRing, DateTime>(nameof(RangeStart), DateTime.Today);

    public static readonly StyledProperty<DateTime> RangeEndProperty =
        AvaloniaProperty.Register<ClockRing, DateTime>(nameof(RangeEnd), DateTime.Today.AddDays(1));

    /// <summary>是否按分类着色（false = 按应用着色）</summary>
    public static readonly StyledProperty<bool> UseCategoryColorProperty =
        AvaloniaProperty.Register<ClockRing, bool>(nameof(UseCategoryColor));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ClockRing, double>(nameof(Thickness), 22d);

    public static readonly StyledProperty<string?> CenterTextProperty =
        AvaloniaProperty.Register<ClockRing, string?>(nameof(CenterText));

    public static readonly StyledProperty<string?> CenterCaptionProperty =
        AvaloniaProperty.Register<ClockRing, string?>(nameof(CenterCaption));

    public static readonly StyledProperty<string?> EmptyTextProperty =
        AvaloniaProperty.Register<ClockRing, string?>(nameof(EmptyText));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<ClockRing, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<IBrush?> MutedTextBrushProperty =
        AvaloniaProperty.Register<ClockRing, IBrush?>(nameof(MutedTextBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ClockRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> HoverBackgroundBrushProperty =
        AvaloniaProperty.Register<ClockRing, IBrush?>(nameof(HoverBackgroundBrush));

    public static readonly StyledProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<ClockRing, ICommand?>(nameof(ClickCommand));

    public static readonly DirectProperty<ClockRing, ClockSegment?> HoveredSegmentProperty =
        AvaloniaProperty.RegisterDirect<ClockRing, ClockSegment?>(
            nameof(HoveredSegment),
            o => o.HoveredSegment,
            (o, v) => o.HoveredSegment = v);

    /// <summary>小于这个角度也要画出来，否则几秒的碎片会完全看不见</summary>
    private const double MinSweepDegrees = 1.1;

    private const double LabelSpace = 26;

    private readonly List<(ClockSegment Segment, double Start, double Sweep)> _hitMap = [];
    private readonly Dictionary<string, IBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);
    private ClockSegment? _hoveredSegment;
    private IBrush? _fallbackBrush;

    static ClockRing()
    {
        AffectsRender<ClockRing>(SegmentsProperty, TickModeProperty, RangeStartProperty, RangeEndProperty,
            UseCategoryColorProperty, ThicknessProperty, CenterTextProperty, CenterCaptionProperty,
            EmptyTextProperty, TextBrushProperty, MutedTextBrushProperty, TrackBrushProperty,
            HoverBackgroundBrushProperty);
    }

    public IEnumerable<ClockSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public ClockTickMode TickMode
    {
        get => GetValue(TickModeProperty);
        set => SetValue(TickModeProperty, value);
    }

    public DateTime RangeStart
    {
        get => GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    public DateTime RangeEnd
    {
        get => GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
    }

    public bool UseCategoryColor
    {
        get => GetValue(UseCategoryColorProperty);
        set => SetValue(UseCategoryColorProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public string? CenterText
    {
        get => GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public string? CenterCaption
    {
        get => GetValue(CenterCaptionProperty);
        set => SetValue(CenterCaptionProperty, value);
    }

    public string? EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? MutedTextBrush
    {
        get => GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? HoverBackgroundBrush
    {
        get => GetValue(HoverBackgroundBrushProperty);
        set => SetValue(HoverBackgroundBrushProperty, value);
    }

    public ICommand? ClickCommand
    {
        get => GetValue(ClickCommandProperty);
        set => SetValue(ClickCommandProperty, value);
    }

    public ClockSegment? HoveredSegment
    {
        get => _hoveredSegment;
        set
        {
            if (ReferenceEquals(_hoveredSegment, value)) return;
            SetAndRaise(HoveredSegmentProperty, ref _hoveredSegment, value);
            InvalidateVisual();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SegmentsProperty || change.Property == UseCategoryColorProperty)
        {
            _brushCache.Clear();
            HoveredSegment = null;
        }
    }

    /// <summary>
    /// 自绘控件默认只在自己画过的像素上参与命中测试，表盘中间是空心的，
    /// 悬停/点击会时灵时不灵。这里整个矩形都接受指针，具体命中哪个扇区由内部按角度算。
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var thickness = Math.Max(6, Thickness);
        var radius = Math.Min(width, height) / 2 - thickness / 2 - LabelSpace;
        if (radius <= 4) return;

        var center = new Point(width / 2, height / 2);

        DrawTicks(context, center, radius, thickness);

        context.DrawEllipse(null,
            new Pen(TrackBrush ?? new SolidColorBrush(Color.FromArgb(36, 128, 128, 128)), thickness),
            center, radius, radius);

        var segments = (Segments ?? []).Where(s => s != null).OrderBy(s => s.StartFraction).ToList();
        _hitMap.Clear();

        foreach (var segment in segments)
        {
            var rawSweep = Math.Max(0, segment.EndFraction - segment.StartFraction) * 360d;
            if (rawSweep <= 0) continue;

            var sweep = Math.Max(MinSweepDegrees, rawSweep);
            var start = -90d + segment.StartFraction * 360d;
            var isHover = ReferenceEquals(segment, HoveredSegment);

            var pen = new Pen(GetBrush(segment), isHover ? thickness + 5 : thickness)
            {
                LineCap = PenLineCap.Flat
            };

            DrawArc(context, center, radius, start, sweep, pen);
            _hitMap.Add((segment, start, sweep));
        }

        DrawCenter(context, center, segments.Count > 0);
    }

    #region 刻度

    private void DrawTicks(DrawingContext context, Point center, double radius, double thickness)
    {
        var outer = radius + thickness / 2;
        var tickBrush = MutedTextBrush ?? new SolidColorBrush(Color.FromArgb(120, 128, 128, 128));
        var pen = new Pen(tickBrush, 1);

        switch (TickMode)
        {
            case ClockTickMode.Hour:
                for (var hour = 0; hour < 24; hour++)
                {
                    var major = hour % 3 == 0;
                    DrawTickLine(context, pen, center, outer + 2, outer + (major ? 7 : 4), hour / 24d);
                    if (!major) continue;

                    DrawLabel(context, center, outer + 13, hour / 24d, $"{hour:00}");
                }

                break;

            case ClockTickMode.Day:
            {
                // 两端都算一天：一周是 7 格，不是 6 格
                var days = Math.Max(1, (RangeEnd.Date - RangeStart.Date).Days + 1);
                // 一个月 30 天，每天都写标签会糊成一团：超过 7 天就每 5 天标一次
                var labelStep = days <= 7 ? 1 : 5;

                for (var i = 0; i < days; i++)
                {
                    var fraction = (double)i / days;
                    DrawTickLine(context, pen, center, outer + 2, outer + 5, fraction);

                    if (i % labelStep != 0) continue;
                    DrawLabel(context, center, outer + 13, fraction, RangeStart.AddDays(i).ToString("M/d"));
                }

                break;
            }

            default:
                for (var month = 0; month < 12; month++)
                {
                    var fraction = month / 12d;
                    DrawTickLine(context, pen, center, outer + 2, outer + 6, fraction);
                    DrawLabel(context, center, outer + 13, fraction,
                        RangeStart.AddMonths(month).ToString("MMM", CultureInfo.CurrentCulture));
                }

                break;
        }
    }

    private static void DrawTickLine(DrawingContext context, IPen pen, Point center,
        double from, double to, double fraction)
    {
        var angle = (-90d + fraction * 360d) * Math.PI / 180d;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        context.DrawLine(pen,
            new Point(center.X + from * cos, center.Y + from * sin),
            new Point(center.X + to * cos, center.Y + to * sin));
    }

    private void DrawLabel(DrawingContext context, Point center, double distance, double fraction, string text)
    {
        var formatted = MakeText(text, 10, MutedTextBrush ?? TextBrush);
        var angle = (-90d + fraction * 360d) * Math.PI / 180d;
        var x = center.X + distance * Math.Cos(angle) - formatted.Width / 2;
        var y = center.Y + distance * Math.Sin(angle) - formatted.Height / 2;
        context.DrawText(formatted, new Point(x, y));
    }

    #endregion

    private void DrawCenter(DrawingContext context, Point center, bool hasData)
    {
        var hovered = HoveredSegment;

        string? main;
        string? caption;

        if (hovered != null)
        {
            main = hovered.Name;
            caption = string.IsNullOrEmpty(hovered.RangeText)
                ? hovered.CategoryName
                : hovered.RangeText;
        }
        else if (!hasData)
        {
            main = EmptyText;
            caption = null;
        }
        else
        {
            main = CenterText;
            caption = CenterCaption;
        }

        if (!string.IsNullOrEmpty(main))
        {
            var text = MakeText(main, hovered != null ? 15 : 22, TextBrush, FontWeight.SemiBold);
            var y = center.Y - text.Height / 2 - (string.IsNullOrEmpty(caption) ? 0 : 8);
            context.DrawText(text, new Point(center.X - text.Width / 2, y));
        }

        if (string.IsNullOrEmpty(caption)) return;

        var captionText = MakeText(caption, 11, MutedTextBrush ?? TextBrush);
        var captionY = string.IsNullOrEmpty(main) ? center.Y - captionText.Height / 2 : center.Y + 6;
        context.DrawText(captionText, new Point(center.X - captionText.Width / 2, captionY));
    }

    #region 交互

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var segment = HitTestSegment(e.GetPosition(this));
        HoveredSegment = segment;
        Cursor = segment?.Data != null || segment?.CategoryData != null
            ? new Cursor(StandardCursorType.Hand)
            : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredSegment = null;
        Cursor = Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var segment = HitTestSegment(e.GetPosition(this));
        if (segment == null) return;

        var command = ClickCommand;
        if (command == null) return;

        if (command.CanExecute(segment)) command.Execute(segment);
        e.Handled = true;
    }

    private ClockSegment? HitTestSegment(Point position)
    {
        if (_hitMap.Count == 0) return null;

        var thickness = Math.Max(6, Thickness);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - thickness / 2 - LabelSpace;
        if (radius <= 4) return null;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var dx = position.X - center.X;
        var dy = position.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < radius - thickness / 2 - 3 || distance > radius + thickness / 2 + 3)
            return null;

        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        angle = (angle + 90 + 360) % 360;

        foreach (var (segment, start, sweep) in _hitMap)
        {
            var offset = (angle - start + 360) % 360;
            if (offset <= sweep) return segment;
        }

        return null;
    }

    #endregion

    #region 绘制辅助

    private IBrush GetBrush(ClockSegment segment)
    {
        var color = UseCategoryColor && !string.IsNullOrWhiteSpace(segment.CategoryColor)
            ? segment.CategoryColor
            : segment.Color;

        _fallbackBrush ??= new SolidColorBrush(Color.Parse("#8780F7"));

        if (string.IsNullOrWhiteSpace(color)) return _fallbackBrush;
        if (_brushCache.TryGetValue(color, out var cached)) return cached;

        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(color));
        }
        catch
        {
            brush = _fallbackBrush;
        }

        _brushCache[color] = brush;
        return brush;
    }

    private static void DrawArc(DrawingContext context, Point center, double radius,
        double startAngleDegrees, double sweepDegrees, IPen pen)
    {
        if (sweepDegrees >= 359.5)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var start = PointOnCircle(center, radius, startAngleDegrees);
        var end = PointOnCircle(center, radius, startAngleDegrees + sweepDegrees);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static FormattedText MakeText(string? text, double size, IBrush? brush,
        FontWeight weight = FontWeight.Normal)
    {
        return new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight),
            size,
            brush ?? Brushes.Gray);
    }

    #endregion
}
