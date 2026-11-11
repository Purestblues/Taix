using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Taix.Client.Librarys;
using Xunit;

namespace Taix.Tests;

public class MotionTransitionsTests
{
    private static Transitions CreateTransitions() =>
        new() { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) } };

    private static (StackPanel Panel, Border Target, Window Window) CreateHost()
    {
        var target = new Border { Transitions = CreateTransitions() };
        var panel = new StackPanel { Children = { target } };
        var window = new Window { Width = 200, Height = 200, Content = panel };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (panel, target, window);
    }

    /// <summary>把「节流窗口」推过去，让 Attach 的布局扫描真正跑一轮</summary>
    private static void PumpLayout()
    {
        Dispatcher.UIThread.RunJobs();
        System.Threading.Thread.Sleep(450);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void DisablingMotion_StripsInlineTransitions_AndRestoresThem()
    {
        var (_, target, window) = CreateHost();
        var original = target.Transitions;

        try
        {
            MotionTransitions.Attach(window);

            Motion.SetEnabled(false);
            Dispatcher.UIThread.RunJobs();

            // 内联 Transitions 是本地值，样式盖不掉，只能运行时摘掉
            Assert.Null(target.Transitions);

            Motion.SetEnabled(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, target.Transitions);
        }
        finally
        {
            MotionTransitions.Detach();
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DisablingMotion_AlsoStripsTransitionsOfLaterAttachedChildren()
    {
        var (panel, target, window) = CreateHost();

        try
        {
            MotionTransitions.Attach(window);

            Motion.SetEnabled(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(target.Transitions);

            // 模拟列表虚拟化：关闭动效之后才实例化出来的行
            var late = new Border { Transitions = CreateTransitions() };
            panel.Children.Add(late);

            PumpLayout();

            Assert.Null(late.Transitions);
        }
        finally
        {
            MotionTransitions.Detach();
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AttachedChildren_KeepTransitionsWhenMotionIsEnabled()
    {
        var (panel, target, window) = CreateHost();

        try
        {
            MotionTransitions.Attach(window);

            var late = new Border { Transitions = CreateTransitions() };
            panel.Children.Add(late);

            PumpLayout();

            Assert.NotNull(target.Transitions);
            Assert.NotNull(late.Transitions);
        }
        finally
        {
            MotionTransitions.Detach();
            Motion.SetEnabled(true);
            window.Close();
        }
    }
}
