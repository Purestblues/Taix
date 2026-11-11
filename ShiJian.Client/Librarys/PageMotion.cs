using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 换页进场动效。统一由 <see cref="Controls.PageContainer"/> 在页面挂上之后调用，
/// 各页面不要再自己写一套，否则两层淡入叠在一起会显得发虚。
///
/// 只做「新页淡入 + 轻移」，不做旧页淡出：换页流程会先把旧页 VM <c>Dispose()</c>、把
/// <c>DataContext</c> 置空（见 <c>PageContainer.LoadPage</c> 的注释，那里是为了避开 Avalonia 12
/// 的逻辑树竞态）。想让旧页淡出就必须推迟这两步，代价大于收益。
/// </summary>
public static class PageMotion
{
    /// <summary>进场时长。克制优先：超过 300ms 就开始影响「点了就换」的手感</summary>
    public static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(260);

    /// <summary>只上移这么一点，避免整页「飞进来」</summary>
    private const double EnterOffsetY = 8;

    private static readonly Animation EnterAnimation = BuildEnterAnimation();

    /// <summary>动画结束态 / 动效关闭时的直切态：一定要把初始值清干净，否则页面会停在半透明</summary>
    public static void SnapToRest(Control control)
    {
        control.Opacity = 1;
        control.RenderTransform = null;
    }

    /// <summary>进场初始态。单独拆出来是为了能直接断言「第一帧不会闪出完整页面」</summary>
    internal static void Prepare(Control control)
    {
        control.Opacity = 0;
        control.RenderTransform = TransformOperations.Parse($"translateY({EnterOffsetY}px)");
    }

    /// <summary>播放进场动效。动效关闭时同步置为终态，不产生任何延迟</summary>
    public static async Task PlayEnterAsync(Control control)
    {
        if (!Motion.IsEnabled)
        {
            SnapToRest(control);
            return;
        }

        try
        {
            Prepare(control);
            await EnterAnimation.RunAsync(control);
        }
        catch
        {
            // 页面在播放中途被卸载 / 时钟不可用，都不能让页面停在半透明
        }
        finally
        {
            SnapToRest(control);
        }
    }

    private static Animation BuildEnterAnimation()
    {
        return new Animation
        {
            Duration = EnterDuration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(Visual.RenderTransformProperty, TransformOperations.Parse($"translateY({EnterOffsetY}px)"))
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("translateY(0px)"))
                    }
                }
            }
        };
    }
}
