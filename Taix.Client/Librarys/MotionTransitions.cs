using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Taix.Client.Logging;

namespace Taix.Client.Librarys;

/// <summary>
/// 动效关闭时把界面里的 <see cref="Transitions"/> 全部摘掉。
///
/// 为什么要运行时处理：XAML 里内联写的 Transitions 是「本地值」，
/// 样式选择器的 Setter 覆盖不了它，所以只能在运行时摘下来、重新开启时还原。
/// </summary>
public static class MotionTransitions
{
    private static readonly ConditionalWeakTable<Animatable, Transitions> SavedValues = new();
    private static readonly List<WeakReference<Animatable>> SavedTargets = [];

    private static Window? _window;

    /// <summary>首次扫描的间隔；列表虚拟化会不断实例化新行，需要重复扫描</summary>
    private const long MinIntervalMs = 400;

    /// <summary>连续多轮没有新元素时退避到这个间隔，避免一直空转</summary>
    private const long MaxIntervalMs = 5000;

    private static long _interval = MinIntervalMs;
    private static long _lastWalk;

    /// <summary>挂在主窗口上，跟随 <see cref="Motion"/> 开关工作</summary>
    public static void Attach(Window window)
    {
        if (ReferenceEquals(_window, window)) return;
        Detach();

        _window = window;
        Motion.Changed += OnMotionChanged;
        window.LayoutUpdated += OnLayoutUpdated;

        Apply();
    }

    public static void Detach()
    {
        Motion.Changed -= OnMotionChanged;
        if (_window != null)
        {
            _window.LayoutUpdated -= OnLayoutUpdated;
            _window = null;
        }
        RestoreAll();
    }

    private static void OnMotionChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    private static void Apply()
    {
        if (Motion.IsEnabled)
        {
            var restored = SavedTargets.Count;
            RestoreAll();
            Logger.Info($"动效已开启（还原 {restored} 处过渡）");
            return;
        }

        _interval = MinIntervalMs;
        _lastWalk = 0;
        Logger.Info($"动效已关闭（摘除 {SuppressAll()} 处过渡）");
    }

    /// <summary>
    /// 动效关闭期间持续扫描：切换页面、列表滚动、模板延迟实例化都会带来新元素，
    /// 它们的内联 Transitions 不在第一次扫描的范围内。
    /// </summary>
    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (Motion.IsEnabled || _window == null) return;

        var now = Environment.TickCount64;
        if (now - _lastWalk < _interval) return;
        _lastWalk = now;

        var added = SuppressAll();

        if (added > 0)
        {
            _interval = MinIntervalMs;
        }
        else if (_interval < MaxIntervalMs)
        {
            _interval = Math.Min(_interval * 2, MaxIntervalMs);
        }
    }

    private static int SuppressAll()
    {
        return _window == null ? 0 : SuppressAll(_window);
    }

    private static int SuppressAll(Visual root)
    {
        var count = SuppressOne(root);
        foreach (var descendant in root.GetVisualDescendants()) count += SuppressOne(descendant);
        return count;
    }

    private static int SuppressOne(Visual visual)
    {
        if (visual is not Animatable animatable) return 0;
        if (SavedValues.TryGetValue(animatable, out _)) return 0;

        var transitions = animatable.Transitions;
        if (transitions == null || transitions.Count == 0) return 0;

        SavedValues.Add(animatable, transitions);
        SavedTargets.Add(new WeakReference<Animatable>(animatable));
        animatable.Transitions = null;
        return 1;
    }

    private static void RestoreAll()
    {
        if (SavedTargets.Count == 0) return;

        foreach (var reference in SavedTargets)
        {
            if (!reference.TryGetTarget(out var target)) continue;
            if (!SavedValues.TryGetValue(target, out var transitions)) continue;

            try
            {
                target.Transitions = transitions;
            }
            catch
            {
                // 元素已销毁 / 属性不可写，忽略即可
            }
        }

        SavedTargets.Clear();
        SavedValues.Clear();
    }
}
