using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ShiJian.Client.Librarys;
using Xunit;

namespace ShiJian.Tests;

public class SmoothScrollTests
{
    [Fact]
    public void ComputeNextOffset_ExponentialDecayConvergesAndSnaps()
    {
        var current = new Vector(0, 0);
        var target = new Vector(0, SmoothScroll.WheelStepPx);
        var snapped = false;

        for (var frame = 0; frame < 40; frame++)
        {
            var next = SmoothScroll.ComputeNextOffset(current, target, 1.0 / 60.0, out snapped);
            Assert.True(next.Y >= current.Y, "Offset should move monotonically toward target");
            Assert.True(next.Y <= target.Y, "Exponential decay must not overshoot target");
            current = next;
            if (snapped)
            {
                break;
            }
        }

        Assert.True(snapped, "Smooth scroll should snap to target within ~40 frames at 60Hz");
        Assert.Equal(target, current);
    }

    [Fact]
    public void ComputeNextOffset_AlreadyWithinThreshold_SnapsImmediately()
    {
        var target = new Vector(0, 120);
        var current = new Vector(0, 119.7);

        var next = SmoothScroll.ComputeNextOffset(current, target, 1.0 / 60.0, out var snapped);

        Assert.True(snapped);
        Assert.Equal(target, next);
    }

    [AvaloniaFact]
    public void CanScrollInDirection_RespectsBoundariesAndNestedScrollViewer()
    {
        var inner = new ScrollViewer
        {
            Height = 120,
            Content = new Border { Width = 200, Height = 500 }
        };
        var outer = new ScrollViewer
        {
            Height = 200,
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 100 },
                    inner,
                    new Border { Height = 400 }
                }
            }
        };
        var window = new Window
        {
            Width = 300,
            Height = 220,
            Content = outer
        };

        try
        {
            SmoothScroll.EnsureRegistered();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // 顶部位置：只能向下滚（delta.Y < 0），不能继续向上滚（delta.Y > 0）
            Assert.True(SmoothScroll.CanScrollInDirection(outer, new Vector(0, -1)));
            Assert.False(SmoothScroll.CanScrollInDirection(outer, new Vector(0, 1)));
            Assert.True(SmoothScroll.CanScrollInDirection(inner, new Vector(0, -1)));
            Assert.False(SmoothScroll.CanScrollInDirection(inner, new Vector(0, 1)));

            // 触发滚轮向下滚动内层 ScrollViewer，再将外层滚下、内层回到顶部后测试边界穿透判断
            var centerInInner = inner.TranslatePoint(new Point(60, 40), window);
            Assert.NotNull(centerInInner);
            window.MouseWheel(centerInInner!.Value, new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();

            // 将外层滚动到底部、内层归零后：向上的滚轮无法由内层消化，外层应能继续向上滚
            outer.Offset = new Vector(0, 100);
            inner.Offset = Vector.Zero;
            SmoothScroll.CancelAnimation(inner);
            SmoothScroll.CancelAnimation(outer);

            Assert.False(SmoothScroll.CanScrollInDirection(inner, new Vector(0, 1)));
            Assert.True(SmoothScroll.CanScrollInDirection(outer, new Vector(0, 1)));
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MotionDisabled_DoesNotInterceptWheelAndUsesDefaultJump()
    {
        var sv = new ScrollViewer
        {
            Height = 150,
            Content = new Border { Width = 200, Height = 600 }
        };
        var window = new Window
        {
            Width = 260,
            Height = 160,
            Content = sv
        };

        try
        {
            SmoothScroll.EnsureRegistered();
            Motion.SetEnabled(false);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();

            // 减动效关闭平滑插值后应由默认滚轮逻辑直接完成滚动
            Assert.True(sv.Offset.Y > 0);
        }
        finally
        {
            Motion.SetEnabled(true);
            window.Close();
        }
    }
}
