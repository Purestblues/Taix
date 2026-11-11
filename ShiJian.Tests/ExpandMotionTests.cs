using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ShiJian.Client.Librarys;
using Xunit;

namespace ShiJian.Tests;

/// <summary>
/// 展开 / 收起的高度动画：真的花时间、结束回到自动高度、关动效时零延迟直切。
/// 「回到自动高度」是硬约束——否则窗口缩放后内容不会自适应。
/// </summary>
public class ExpandMotionTests
{
    private static (Border Host, Window Window) CreateHost()
    {
        var content = new Border { Height = 80, Background = Avalonia.Media.Brushes.Red };
        var host = new Border { Child = content, IsVisible = false, Width = 200 };
        var window = new Window { Width = 300, Height = 300, Content = new StackPanel { Children = { host } } };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (host, window);
    }

    [AvaloniaFact]
    public async Task Expand_WhenMotionEnabled_AnimatesAndRestoresAutoHeight()
    {
        var (host, window) = CreateHost();

        try
        {
            Motion.SetEnabled(true);

            var watch = Stopwatch.StartNew();
            await ExpandMotion.SetExpandedAsync(host, true);
            watch.Stop();

            Assert.True(watch.ElapsedMilliseconds >= MotionTokens.M.TotalMilliseconds * 0.4,
                $"展开只用了 {watch.ElapsedMilliseconds}ms，可能没真的动画");
            Assert.True(host.IsVisible);
            Assert.True(double.IsNaN(host.Height), "展开结束后必须回到自动高度");
            Assert.False(host.ClipToBounds, "展开结束后应放开裁剪");
            Assert.Equal(1d, host.Opacity);
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Collapse_WhenMotionEnabled_HidesAndRestoresAutoHeight()
    {
        var (host, window) = CreateHost();

        try
        {
            Motion.SetEnabled(true);
            ExpandMotion.ApplyFinal(host, true);
            Dispatcher.UIThread.RunJobs();

            await ExpandMotion.SetExpandedAsync(host, false);

            Assert.False(host.IsVisible);
            Assert.True(double.IsNaN(host.Height));
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Toggle_WhenMotionDisabled_IsInstant()
    {
        var (host, window) = CreateHost();

        try
        {
            Motion.SetEnabled(false);

            var watch = Stopwatch.StartNew();
            await ExpandMotion.SetExpandedAsync(host, true);
            await ExpandMotion.SetExpandedAsync(host, false);
            watch.Stop();

            Assert.True(watch.ElapsedMilliseconds < 100, $"关闭动效后仍耗时 {watch.ElapsedMilliseconds}ms");
            Assert.False(host.IsVisible);
            Assert.True(double.IsNaN(host.Height));
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }
}
