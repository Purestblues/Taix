using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 全局应用内平滑滚动拦截器（N13）。
/// 为应用内 <see cref="ScrollViewer"/> 提供基于帧同步的指数阻尼滚轮平滑滚动，
/// 遵循 <see cref="Motion.IsEnabled"/> 系统减动效偏好，并正确处理嵌套滚动边界穿透。
/// </summary>
public static class SmoothScroll
{
    /// <summary>单格鼠标滚轮的滚动步长（像素）</summary>
    public const double WheelStepPx = 84.0;

    /// <summary>指数衰减速率（1/秒）：约 180~220ms 自然收尾</summary>
    public const double DecayRate = 18.0;

    /// <summary>距离目标小于该阈值（像素）时直接对齐并结束动画</summary>
    public const double SnapThresholdPx = 0.5;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, ScrollViewer, bool>(
            "IsEnabled",
            defaultValue: true,
            inherits: true);

    public static bool GetIsEnabled(AvaloniaObject obj) => obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(AvaloniaObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private sealed class ScrollAnimState
    {
        public Vector TargetOffset;
        public Vector LastAppliedOffset;
        public TimeSpan LastFrameTimestamp;
        public bool IsAnimating;
        public TopLevel? AttachedTopLevel;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollAnimState> States = new();
    private static readonly List<WeakReference<ScrollViewer>> ActiveViewers = [];
    private static readonly object RegistrationLock = new();
    private static bool _isRegistered;
    private static bool _frameScheduled;

    /// <summary>幂等注册全局 <see cref="ScrollViewer"/> 滚轮平滑处理与减动效监听</summary>
    public static void EnsureRegistered()
    {
        lock (RegistrationLock)
        {
            if (_isRegistered) return;
            _isRegistered = true;

            InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(
                OnScrollViewerPointerWheelChanged,
                RoutingStrategies.Tunnel,
                handledEventsToo: false);

            Motion.Changed += OnMotionChanged;
        }
    }

    /// <summary>取消指定 ScrollViewer 正在进行的平滑惯性（用于编程式 ScrollToHome 等场景）</summary>
    public static void CancelAnimation(ScrollViewer? viewer)
    {
        if (viewer == null) return;
        if (States.TryGetValue(viewer, out var state))
        {
            state.IsAnimating = false;
            state.TargetOffset = viewer.Offset;
            state.LastAppliedOffset = viewer.Offset;
            state.LastFrameTimestamp = TimeSpan.Zero;
        }
    }

    /// <summary>计算指数阻尼的下一帧滚动偏移量</summary>
    public static Vector ComputeNextOffset(Vector current, Vector target, double dtSeconds, out bool snapped)
    {
        var dx = target.X - current.X;
        var dy = target.Y - current.Y;

        var snapX = Math.Abs(dx) <= SnapThresholdPx;
        var snapY = Math.Abs(dy) <= SnapThresholdPx;

        if (snapX && snapY)
        {
            snapped = true;
            return target;
        }

        // dt 上限钳位到 100ms，避免切窗/卡顿回来出现瞬跳
        var dt = Math.Clamp(dtSeconds, 0.0, 0.1);
        var factor = 1.0 - Math.Exp(-dt * DecayRate);

        var nextX = snapX ? target.X : current.X + dx * factor;
        var nextY = snapY ? target.Y : current.Y + dy * factor;

        snapped = Math.Abs(target.X - nextX) <= SnapThresholdPx
                  && Math.Abs(target.Y - nextY) <= SnapThresholdPx;

        return snapped ? target : new Vector(nextX, nextY);
    }

    /// <summary>判断 ScrollViewer 在指定滚轮方向上是否仍有可滚动空间</summary>
    public static bool CanScrollInDirection(ScrollViewer sv, Vector wheelDelta)
    {
        var baseOffset = States.TryGetValue(sv, out var s) && s.IsAnimating
            ? s.TargetOffset
            : sv.Offset;

        var maxOffset = GetMaxOffset(sv);
        var canVertical = sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                          && maxOffset.Y > 0.5
                          && Math.Abs(wheelDelta.Y) > 1e-6;
        if (canVertical)
        {
            if (wheelDelta.Y > 0 && baseOffset.Y > 0.5) return true;
            if (wheelDelta.Y < 0 && baseOffset.Y < maxOffset.Y - 0.5) return true;
        }

        var canHorizontal = sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled
                            && maxOffset.X > 0.5
                            && Math.Abs(wheelDelta.X) > 1e-6;
        if (canHorizontal)
        {
            if (wheelDelta.X > 0 && baseOffset.X > 0.5) return true;
            if (wheelDelta.X < 0 && baseOffset.X < maxOffset.X - 0.5) return true;
        }

        return false;
    }

    private static void OnMotionChanged(object? sender, EventArgs e)
    {
        if (Motion.IsEnabled) return;

        Dispatcher.UIThread.Post(SnapAllActiveToTarget);
    }

    private static void SnapAllActiveToTarget()
    {
        for (int i = ActiveViewers.Count - 1; i >= 0; i--)
        {
            if (ActiveViewers[i].TryGetTarget(out var sv) && States.TryGetValue(sv, out var state))
            {
                if (state.IsAnimating)
                {
                    sv.Offset = ClampOffset(sv, state.TargetOffset);
                }
                state.IsAnimating = false;
                state.LastAppliedOffset = sv.Offset;
                state.LastFrameTimestamp = TimeSpan.Zero;
            }
            ActiveViewers.RemoveAt(i);
        }
        _frameScheduled = false;
    }

    private static void OnScrollViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv) return;
        if (!sv.IsEnabled || !sv.IsEffectivelyVisible) return;
        if (!Motion.IsEnabled || !GetIsEnabled(sv)) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        // 优先让更内层的可滚动容器消化本次滚轮；仅当内层无法在该方向继续滚动时才由外层接管
        if (HasInnerScrollableConsumer(sv, e.Source as Visual, e.Delta))
        {
            return;
        }

        if (!CanScrollInDirection(sv, e.Delta))
        {
            return;
        }

        var state = States.GetOrCreateValue(sv);

        // 若外部操作（拖拽滚动条或编程重置）修改了 Offset，则从最新 Offset 重新起算
        if (!state.IsAnimating || HasExternalOffsetChange(sv, state))
        {
            state.TargetOffset = sv.Offset;
            state.LastAppliedOffset = sv.Offset;
            state.LastFrameTimestamp = TimeSpan.Zero;
        }

        var deltaOffset = new Vector(
            sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled ? -e.Delta.X * WheelStepPx : 0,
            sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled ? -e.Delta.Y * WheelStepPx : 0);

        var nextTarget = ClampOffset(sv, state.TargetOffset + deltaOffset);
        if ((nextTarget - sv.Offset).Length <= SnapThresholdPx)
        {
            return;
        }

        state.TargetOffset = nextTarget;
        state.LastAppliedOffset = sv.Offset;
        state.AttachedTopLevel = TopLevel.GetTopLevel(sv);

        if (!state.IsAnimating)
        {
            state.IsAnimating = true;
            state.LastFrameTimestamp = TimeSpan.Zero;
            TrackActiveViewer(sv);
        }

        ScheduleNextFrame(state.AttachedTopLevel);
        e.Handled = true;
    }

    private static bool HasInnerScrollableConsumer(ScrollViewer outer, Visual? source, Vector delta)
    {
        var cursor = source;
        while (cursor != null && !ReferenceEquals(cursor, outer))
        {
            if (cursor is ScrollViewer inner
                && inner.IsEnabled
                && inner.IsEffectivelyVisible
                && CanScrollInDirection(inner, delta))
            {
                return true;
            }
            cursor = cursor.GetVisualParent();
        }
        return false;
    }

    private static bool HasExternalOffsetChange(ScrollViewer sv, ScrollAnimState state)
    {
        return (sv.Offset - state.LastAppliedOffset).Length > 1.5;
    }

    private static Vector GetMaxOffset(ScrollViewer sv)
    {
        var maxX = Math.Max(0.0, sv.Extent.Width - sv.Viewport.Width);
        var maxY = Math.Max(0.0, sv.Extent.Height - sv.Viewport.Height);
        return new Vector(maxX, maxY);
    }

    private static Vector ClampOffset(ScrollViewer sv, Vector offset)
    {
        var max = GetMaxOffset(sv);
        return new Vector(
            Math.Clamp(offset.X, 0.0, max.X),
            Math.Clamp(offset.Y, 0.0, max.Y));
    }

    private static void TrackActiveViewer(ScrollViewer sv)
    {
        for (int i = ActiveViewers.Count - 1; i >= 0; i--)
        {
            if (!ActiveViewers[i].TryGetTarget(out var existing))
            {
                ActiveViewers.RemoveAt(i);
            }
            else if (ReferenceEquals(existing, sv))
            {
                return;
            }
        }
        ActiveViewers.Add(new WeakReference<ScrollViewer>(sv));
    }

    private static void ScheduleNextFrame(TopLevel? preferredTopLevel)
    {
        if (_frameScheduled) return;

        var topLevel = preferredTopLevel ?? FindActiveTopLevel();
        if (topLevel == null)
        {
            SnapAllActiveToTarget();
            return;
        }

        _frameScheduled = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private static TopLevel? FindActiveTopLevel()
    {
        for (int i = ActiveViewers.Count - 1; i >= 0; i--)
        {
            if (ActiveViewers[i].TryGetTarget(out var sv) && States.TryGetValue(sv, out var state))
            {
                var tl = TopLevel.GetTopLevel(sv) ?? state.AttachedTopLevel;
                if (tl != null) return tl;
            }
        }
        return null;
    }

    private static void OnAnimationFrame(TimeSpan timestamp)
    {
        _frameScheduled = false;

        if (!Motion.IsEnabled)
        {
            SnapAllActiveToTarget();
            return;
        }

        TopLevel? nextTopLevel = null;

        for (int i = ActiveViewers.Count - 1; i >= 0; i--)
        {
            if (!ActiveViewers[i].TryGetTarget(out var sv)
                || !States.TryGetValue(sv, out var state)
                || !state.IsAnimating)
            {
                ActiveViewers.RemoveAt(i);
                continue;
            }

            if (!sv.IsEffectivelyVisible || TopLevel.GetTopLevel(sv) == null)
            {
                state.IsAnimating = false;
                ActiveViewers.RemoveAt(i);
                continue;
            }

            if (HasExternalOffsetChange(sv, state))
            {
                // 用户拖拽了滚动条或业务代码显式复位 Offset，停止惯性追踪
                state.IsAnimating = false;
                state.LastAppliedOffset = sv.Offset;
                ActiveViewers.RemoveAt(i);
                continue;
            }

            var target = ClampOffset(sv, state.TargetOffset);
            state.TargetOffset = target;

            var dt = state.LastFrameTimestamp == TimeSpan.Zero
                ? 1.0 / 60.0
                : Math.Max(0.001, (timestamp - state.LastFrameTimestamp).TotalSeconds);
            state.LastFrameTimestamp = timestamp;

            var next = ComputeNextOffset(sv.Offset, target, dt, out var snapped);
            sv.Offset = next;
            state.LastAppliedOffset = sv.Offset;

            if (snapped)
            {
                state.IsAnimating = false;
                state.LastFrameTimestamp = TimeSpan.Zero;
                ActiveViewers.RemoveAt(i);
            }
            else
            {
                nextTopLevel ??= TopLevel.GetTopLevel(sv) ?? state.AttachedTopLevel;
            }
        }

        if (ActiveViewers.Count > 0)
        {
            ScheduleNextFrame(nextTopLevel);
        }
    }
}
