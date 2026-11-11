using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using ShiJian.Client.Librarys;
using Xunit;

namespace ShiJian.Tests;

/// <summary>
/// 位移只能自己插值（Avalonia 12 没有 RenderTransform 动画器），
/// 这里锁住三件事：真的花时间、结束时归零、关动效时零延迟。
/// </summary>
public class SlideMotionTests
{
    private static (Control Target, Window Window) CreateHost()
    {
        var target = new Border { Width = 100, Height = 100 };
        var window = new Window { Width = 300, Height = 300, Content = target };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (target, window);
    }

    [AvaloniaFact]
    public async Task Slide_WhenMotionEnabled_AnimatesAndResets()
    {
        var (target, window) = CreateHost();

        try
        {
            Motion.SetEnabled(true);

            var watch = Stopwatch.StartNew();
            await SlideMotion.SlideYAsync(target, 8, 0, MotionTokens.S, MotionTokens.Enter());
            watch.Stop();

            Assert.True(watch.ElapsedMilliseconds >= MotionTokens.S.TotalMilliseconds * 0.5,
                $"位移只用了 {watch.ElapsedMilliseconds}ms，可能没真的跑");
            // 结束后必须清干净，不能让控件停在偏移位置
            Assert.Null(target.RenderTransform);
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Slide_WhenMotionDisabled_IsInstant()
    {
        var (target, window) = CreateHost();

        try
        {
            Motion.SetEnabled(false);

            var watch = Stopwatch.StartNew();
            await SlideMotion.SlideYAsync(target, 8, 0, MotionTokens.L, MotionTokens.Enter());
            watch.Stop();

            Assert.True(watch.ElapsedMilliseconds < 50, $"关闭动效后仍耗时 {watch.ElapsedMilliseconds}ms");
            Assert.Null(target.RenderTransform);
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Tokens_HaveExpectedShape()
    {
        Assert.True(MotionTokens.Xs < MotionTokens.S);
        Assert.True(MotionTokens.S < MotionTokens.M);
        Assert.True(MotionTokens.M < MotionTokens.L);

        // 三条曲线在两端都必须落在 0 与 1，否则动画会留下残值
        foreach (var easing in new[] { MotionTokens.Standard(), MotionTokens.Enter(), MotionTokens.Leave() })
        {
            Assert.Equal(0d, easing.Ease(0d), 3);
            Assert.Equal(1d, easing.Ease(1d), 3);
        }
    }
}
