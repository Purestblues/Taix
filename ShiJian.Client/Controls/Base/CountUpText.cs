using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ShiJian.Client.Librarys;
using ShiJian.Client.Shared.Librarys;

namespace ShiJian.Client.Controls.Base;

/// <summary>
/// 会滚动的数字文本：Value 变化时从当前显示值补间到目标值。
/// 时长类（Duration）复用 Time.ToString，输出 2h53m 这种形式。
/// </summary>
public class CountUpText : TextBlock
{
    public enum ValueKind
    {
        /// <summary>纯数字（应用量、站点数…）</summary>
        Number,

        /// <summary>秒数，按 43m20s / 2h53m 显示</summary>
        Duration
    }

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CountUpText, double>(nameof(Value));

    public static readonly StyledProperty<ValueKind> KindProperty =
        AvaloniaProperty.Register<CountUpText, ValueKind>(nameof(Kind));

    /// <summary>补间时长（毫秒）</summary>
    public static readonly StyledProperty<double> DurationMsProperty =
        AvaloniaProperty.Register<CountUpText, double>(nameof(DurationMs), 620d);

    private DispatcherTimer? _timer;
    private DateTime _startedAt;
    private double _from;
    private double _to;
    private double _shown;

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ValueKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double DurationMs
    {
        get => GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty || change.Property == KindProperty)
            StartTween();
    }

    private void StartTween()
    {
        _timer?.Stop();
        _timer = null;

        var target = Value;
        if (double.IsNaN(target) || double.IsInfinity(target)) target = 0;

        var from = _shown;

        // 关掉动效时直接落到目标值；差值太小也别动画了
        if (!Motion.IsEnabled || Math.Abs(target - from) < 0.5 || DurationMs <= 0)
        {
            Apply(target);
            return;
        }

        _from = from;
        _to = target;
        _startedAt = DateTime.UtcNow;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _startedAt).TotalMilliseconds;
        var t = Math.Clamp(elapsed / DurationMs, 0, 1);

        // ease-out cubic
        var eased = 1 - Math.Pow(1 - t, 3);
        Apply(_from + (_to - _from) * eased);

        if (t < 1) return;

        _timer?.Stop();
        _timer = null;
        Apply(_to);
    }

    private void Apply(double value)
    {
        _shown = value;

        Text = Kind == ValueKind.Duration
            ? Time.ToString((int)Math.Round(value))
            : Math.Round(value).ToString();
    }
}
