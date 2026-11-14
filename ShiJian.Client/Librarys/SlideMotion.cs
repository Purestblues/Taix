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

        var transform = GetOrCreateTranslateTransform(target);
        transform.Y = fromY;

        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);

            // 延迟期间控件可能已被卸载，不能再拿旧 topLevel 启动动画
            var currentTopLevel = TopLevel.GetTopLevel(target);
            if (currentTopLevel == null) return;

            await AnimateAsync(target, currentTopLevel, duration, easing, progress =>
            {
                transform.Y = fromY + (toY - fromY) * progress;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 控件在动画途中被取消：直接落到终态，不能留在半路
        }
        finally
        {
            Reset(target);
        }
    }

    /// <summary>
    /// 逐帧同时插值 Y 位移与透明度（用于顶部横幅通知滑入/滑出等连续动效）。
    /// </summary>
    public static async Task AnimateYAndOpacityAsync(
        Control target,
        double fromY,
        double toY,
        double fromOpacity,
        double toOpacity,
        TimeSpan duration,
        IEasing easing,
        bool resetTransformOnComplete = false,
        CancellationToken cancellationToken = default)
    {
        var topLevel = TopLevel.GetTopLevel(target);
        if (!Motion.IsEnabled || topLevel == null || duration <= TimeSpan.Zero)
        {
            ApplyYAndOpacity(target, toY, toOpacity, resetTransformOnComplete);
            return;
        }

        var transform = GetOrCreateTranslateTransform(target);
        transform.Y = fromY;
        target.Opacity = fromOpacity;

        await AnimateAsync(target, topLevel, duration, easing, progress =>
        {
            transform.Y = fromY + (toY - fromY) * progress;
            target.Opacity = fromOpacity + (toOpacity - fromOpacity) * progress;
        }, cancellationToken);

        if (!cancellationToken.IsCancellationRequested && TopLevel.GetTopLevel(target) != null)
        {
            ApplyYAndOpacity(target, toY, toOpacity, resetTransformOnComplete);
        }
    }

    public static double GetTranslateY(Control target)
        => target.RenderTransform is TranslateTransform transform ? transform.Y : 0d;

    private static TranslateTransform GetOrCreateTranslateTransform(Control target)
    {
        if (target.RenderTransform is TranslateTransform existing)
            return existing;

        var transform = new TranslateTransform();
        target.RenderTransform = transform;
        return transform;
    }

    private static void ApplyYAndOpacity(
        Control target,
        double y,
        double opacity,
        bool resetTransformOnComplete)
    {
        target.Opacity = opacity;
        if (resetTransformOnComplete)
        {
            Reset(target);
        }
        else
        {
            GetOrCreateTranslateTransform(target).Y = y;
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
        Control target,
        TopLevel topLevel,
        TimeSpan duration,
        IEasing easing,
        Action<double> apply,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = -1d;
        var totalMs = duration.TotalMilliseconds;

        void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            completion.TrySetResult();
        }

        void OnTopLevelClosed(object? sender, EventArgs e)
        {
            completion.TrySetResult();
        }

        void Step(TimeSpan timestamp)
        {
            if (completion.Task.IsCompleted) return;

            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            // 控件在帧回调前被卸载时直接结束，不能在 RequestAnimationFrame 回调里抛异常
            if (TopLevel.GetTopLevel(target) == null)
            {
                completion.TrySetResult();
                return;
            }

            try
            {
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
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        target.DetachedFromVisualTree += OnDetached;
        topLevel.Closed += OnTopLevelClosed;
        using var cancellationRegistration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken))
            : default;

        try
        {
            topLevel.RequestAnimationFrame(Step);
            await completion.Task;
        }
        finally
        {
            target.DetachedFromVisualTree -= OnDetached;
            topLevel.Closed -= OnTopLevelClosed;
        }
    }
}
