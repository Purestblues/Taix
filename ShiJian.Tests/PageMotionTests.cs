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
/// 换页进场动效的验收线：能播（真的花时间）、播完一定回到终态、关掉动效时零延迟直切。
///
/// 「真的花时间」这一条不是凑数：Avalonia 12 不公开 RenderTransform 的动画器，关键帧里
/// 一旦带上它，<c>RunAsync</c> 会直接抛异常并被吞掉，页面瞬间就位——看起来和没加动效一样。
/// 早期 IndexPage 的进场动画就是这样一直是死的。
/// </summary>
public class PageMotionTests
{
    /// <summary>页面控件必须在树上，动画时钟才真的走</summary>
    private static (Control Page, Window Window) CreateHost()
    {
        var page = new Border { Opacity = 1 };
        var window = new Window { Width = 300, Height = 200, Content = page };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (page, window);
    }

    [AvaloniaFact]
    public async Task PlayEnter_WhenMotionDisabled_SnapsWithNoDelay()
    {
        var (page, window) = CreateHost();

        try
        {
            Motion.SetEnabled(false);

            var watch = Stopwatch.StartNew();
            await PageMotion.PlayEnterAsync(page);
            watch.Stop();

            Assert.Equal(1d, page.Opacity);
            Assert.Null(page.RenderTransform);
            // 关动效就该是「直切」，不能等一个动画时长
            Assert.True(watch.ElapsedMilliseconds < 100, $"关闭动效后仍耗时 {watch.ElapsedMilliseconds}ms");
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PlayEnter_StartsInvisible_ReallyAnimates_AndEndsVisible()
    {
        var (page, window) = CreateHost();

        try
        {
            Motion.SetEnabled(true);

            // 初始态在同步段就设好，否则第一帧会闪出完整页面
            PageMotion.Prepare(page);
            Assert.Equal(0d, page.Opacity);

            var watch = Stopwatch.StartNew();
            await PageMotion.PlayEnterAsync(page);
            watch.Stop();

            // 动画确实跑完了整个时长，而不是被异常跳过
            var minimum = PageMotion.EnterDuration.TotalMilliseconds * 0.6;
            Assert.True(watch.ElapsedMilliseconds >= minimum,
                $"进场动画只用了 {watch.ElapsedMilliseconds}ms（应 >= {minimum:F0}ms），动画可能被跳过");

            Assert.Equal(1d, page.Opacity);
            Assert.Null(page.RenderTransform);
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PlayEnter_ResetsStateLeftBehindByPreviousRun()
    {
        var (page, window) = CreateHost();

        try
        {
            Motion.SetEnabled(false);

            // 模拟上一轮动画被打断后残留的半透明状态
            page.Opacity = 0.3;

            await PageMotion.PlayEnterAsync(page);

            Assert.Equal(1d, page.Opacity);
            Assert.Null(page.RenderTransform);
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }
}
