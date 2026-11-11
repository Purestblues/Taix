using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 展开 / 收起的高度动画（对应参考项目的 <c>height: 0 ↔ *</c>）。
///
/// <c>Height</c> 是 double，Avalonia 有内置动画器，所以这里不需要像位移那样自己插值；
/// 但「目标高度」得先量出来：先把 <c>Height</c> 清成 NaN（自动），量完再逐帧写回去，
/// 结束后必须还原成 NaN，否则窗口缩放时内容不会自适应。
///
/// 用法：容器外层套一个 <c>Border</c>（作为被动画的宿主），内容放里面；
/// 收起完成后再把宿主 <c>IsVisible</c> 置 false，避免它继续参与布局与 Tab 序列。
/// </summary>
public static class ExpandMotion
{
    /// <summary>同一个控件被连续调用时，只有最后一次生效（快速连点不会打架）</summary>
    private static readonly Dictionary<Control, int> Versions = new();

    /// <summary>展开 / 收起。返回时控件已处于目标状态。</summary>
    public static async Task SetExpandedAsync(Control host, bool expanded)
    {
        Versions.TryGetValue(host, out var version);
        version++;
        Versions[host] = version;

        bool IsStale() => !Versions.TryGetValue(host, out var current) || current != version;

        if (!Motion.IsEnabled)
        {
            ApplyFinal(host, expanded);
            return;
        }

        try
        {
            if (expanded) await ExpandAsync(host, IsStale);
            else await CollapseAsync(host, IsStale);
        }
        catch
        {
            // 动画失败（控件被卸载等）不能让内容停在半高
            if (!IsStale()) ApplyFinal(host, expanded);
        }
        finally
        {
            if (Versions.TryGetValue(host, out var current) && current == version) Versions.Remove(host);
        }
    }

    /// <summary>直接用最终状态（动效关闭时走这里）</summary>
    public static void ApplyFinal(Control host, bool expanded)
    {
        host.Height = double.NaN;
        host.ClipToBounds = false;
        host.IsVisible = expanded;
        host.Opacity = 1;
    }

    private static async Task ExpandAsync(Control host, Func<bool> isStale)
    {
        host.IsVisible = true;
        host.Height = double.NaN;      // 先还原成自动，才能量到真实高度
        host.ClipToBounds = true;

        var targetHeight = MeasureHeight(host);
        if (targetHeight <= 0)
        {
            ApplyFinal(host, true);
            return;
        }

        host.Height = 0;
        host.Opacity = 0;

        await Task.WhenAll(
            AnimateHeightAsync(host, 0, targetHeight, MotionTokens.M, MotionTokens.Enter()),
            AnimateOpacityAsync(host, 0, 1, MotionTokens.M, MotionTokens.Enter()));

        if (isStale()) return;

        // 还原成自动高度：窗口缩放、内容变化时才能自适应
        host.Height = double.NaN;
        host.ClipToBounds = false;
        host.Opacity = 1;
    }

    private static async Task CollapseAsync(Control host, Func<bool> isStale)
    {
        if (!host.IsVisible)
        {
            ApplyFinal(host, false);
            return;
        }

        var currentHeight = host.Bounds.Height > 0 ? host.Bounds.Height : MeasureHeight(host);
        host.Height = currentHeight;
        host.ClipToBounds = true;

        await Task.WhenAll(
            AnimateHeightAsync(host, currentHeight, 0, MotionTokens.M, MotionTokens.Leave()),
            AnimateOpacityAsync(host, 1, 0, MotionTokens.M, MotionTokens.Leave()));

        if (isStale()) return;

        ApplyFinal(host, false);
    }

    private static double MeasureHeight(Control host)
    {
        var width = host.Bounds.Width;
        if (width <= 0 && host.Parent is Layoutable parent) width = parent.Bounds.Width;

        host.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
        var height = host.DesiredSize.Height;
        return double.IsNaN(height) || height < 0 ? 0 : height;
    }

    /// <summary>单纯把高度从 from 动画到 to（供自带布局的控件复用，比如 Expander 的占位 Canvas）</summary>
    public static Task AnimateHeightAsync(Control host, double from, double to, TimeSpan duration, Avalonia.Animation.Easings.Easing easing)
    {
        var animation = new Animation
        {
            Duration = duration,
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Layoutable.HeightProperty, from) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Layoutable.HeightProperty, to) } }
            }
        };

        return animation.RunAsync(host);
    }

    /// <summary>单纯把不透明度从 from 动画到 to</summary>
    public static Task AnimateOpacityAsync(Control host, double from, double to, TimeSpan duration, Avalonia.Animation.Easings.Easing easing)
    {
        var animation = new Animation
        {
            Duration = duration,
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, to) } }
            }
        };

        return animation.RunAsync(host);
    }
}
