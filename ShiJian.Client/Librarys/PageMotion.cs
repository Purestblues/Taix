using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 换页进场动效。统一由 <see cref="Controls.PageContainer"/> 在页面挂上之后调用，
/// 各页面不要再自己写一套，否则两层淡入叠在一起会显得发虚。
///
/// 时间线（对齐 Super Productivity，三者并行，总计 225ms）：
///   t=0    页面 opacity = 0，translateY = 8px（同步设好，避免第一帧闪出完整页面）
///   t=45   开始 180ms 淡入 + 上移归零（enter 曲线）
///   t=225  结束，清理终态
///
/// 不做旧页淡出：换页流程会先把旧页 VM <c>Dispose()</c>、把 <c>DataContext</c> 置空
/// （见 <c>PageContainer.LoadPage</c> 的注释，那里是为了避开 Avalonia 12 的逻辑树竞态），
/// 想让旧页淡出就必须推迟这两步，代价大于收益。
/// </summary>
public static class PageMotion
{
    /// <summary>进场总时长（含 45ms 延迟）。动效默认时长的一半以上必须真的花掉，否则说明动画被跳过了。</summary>
    public static readonly TimeSpan EnterDuration = MotionTokens.PageEnterDelay + MotionTokens.PageEnter;

    private static readonly Animation EnterAnimation = BuildEnterAnimation();

    /// <summary>动画结束态 / 动效关闭时的直切态：一定要把初始值清干净，否则页面会停在半透明或偏移位置</summary>
    public static void SnapToRest(Control control)
    {
        control.Opacity = 1;
        SlideMotion.Reset(control);
    }

    /// <summary>进场初始态。单独拆出来是为了能直接断言「第一帧不会闪出完整页面」</summary>
    internal static void Prepare(Control control)
    {
        control.Opacity = 0;
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

            // 淡入走原生动画（Opacity 是 double，有内置动画器）
            var fade = EnterAnimation.RunAsync(control);

            // 位移只能自己插值（Avalonia 12 没有 RenderTransform 动画器）
            var slide = SlideMotion.SlideYAsync(
                control,
                MotionTokens.PageOffsetY,
                0,
                MotionTokens.PageEnter,
                MotionTokens.Enter(),
                MotionTokens.PageEnterDelay);

            await Task.WhenAll(fade, slide);
        }
        catch (Exception ex)
        {
            // 动画抛异常时 Avalonia 会整段跳过（关键帧里有它不认识的属性就会这样），
            // 页面随即被 finally 拉到终态——所以这里必须留下日志，否则「没动效」查无对证。
            Logging.Logger.Warn($"[PageMotion] 进场动画失败，已跳过: {ex.GetType().Name}: {ex.Message}");
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
            Duration = MotionTokens.PageEnter,
            Delay = MotionTokens.PageEnterDelay,
            Easing = MotionTokens.Enter(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, 1d) }
                }
            }
        };
    }
}
