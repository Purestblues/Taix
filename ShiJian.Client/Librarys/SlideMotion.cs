using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 逐帧插值做「位移」。
///
/// 为什么不用 Animation：Avalonia 12 移除了 <c>Animation.Animators</c>，
/// <c>TransformAnimator</c> / <c>TransformOperationsAnimator</c> 都是 internal，
/// 关键帧里只要出现 <see cref="Visual.RenderTransformProperty"/>，
/// <c>RunAsync</c> 会直接抛「No animator registered for the property RenderTransform」
/// 并把整个动画跳过（详见 docs/ROADMAP.md 的 N4 备注）。
///
/// 这里只改 <see cref="TranslateTransform.Y"/>：走渲染变换，不触发 layout，
/// 也不重采样内容，成本就是一次属性写入 + 一次重绘。
/// </summary>
public static class SlideMotion
{
    /// <summary>一次位移动画。结束后把变换清干净，绝不让控件停在偏移位置。</summary>
    public static async Task SlideYAsync(
        Control target,
        double fromY,
        double toY,
        TimeSpan duration,
        IEasing easing,
        TimeSpan delay = default,
        CancellationToken cancellationToken = default)
    {
        if (!Motion.IsEnabled)
        {
            Reset(target);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(target);
        if (topLevel == null || duration <= TimeSpan.Zero)
        {
            Reset(target);
            return;
        }

        var transform = target.RenderTransform as TranslateTransform;
        if (transform == null)
        {
            transform = new TranslateTransform();
            target.RenderTransform = transform;
        }

        transform.Y = fromY;

        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);

            await AnimateAsync(topLevel, duration, easing, progress =>
            {
                if (TopLevel.GetTopLevel(target) == null) throw new OperationCanceledException();
                transform.Y = fromY + (toY - fromY) * progress;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 控件在动画途中被卸载：直接落到终态，不能留在半路
        }
        finally
        {
            Reset(target);
        }
    }

    /// <summary>把位移清掉（动效关闭 / 动画结束 / 控件卸载）</summary>
    public static void Reset(Control target)
    {
        if (target.RenderTransform is TranslateTransform transform)
        {
            transform.Y = 0;
            target.RenderTransform = null;
        }
    }

    /// <summary>
    /// 用渲染帧驱动进度回调。<see cref="TopLevel.RequestAnimationFrame"/> 与渲染循环同步，
    /// 比 DispatcherTimer 稳（不会在掉帧时堆回调）。
    /// </summary>
    private static async Task AnimateAsync(
        TopLevel topLevel,
        TimeSpan duration,
        IEasing easing,
        Action<double> apply,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource();
        var startedAt = -1d;
        var totalMs = duration.TotalMilliseconds;

        void Step(TimeSpan timestamp)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            var now = timestamp.TotalMilliseconds;
            if (startedAt < 0) startedAt = now;

            var elapsed = now - startedAt;
            var raw = totalMs <= 0 ? 1d : Math.Clamp(elapsed / totalMs, 0d, 1d);

            apply(easing.Ease(raw));

            if (raw >= 1d)
            {
                completion.TrySetResult();
                return;
            }

            topLevel.RequestAnimationFrame(Step);
        }

        topLevel.RequestAnimationFrame(Step);
        await completion.Task;
    }
}
