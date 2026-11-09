using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Taix.Client.Librarys.Image;

namespace Taix.Client.Controls.Base;

/// <summary>
/// 带兜底的图片控件：绘制前先确认位图没被释放，已释放就换成默认图标。
/// </summary>
/// <remarks>
/// 图标位图由 <see cref="Imager"/> 的缓存统一持有，理论上不会在控件还挂着它的时候被释放，
/// 但一旦发生，Avalonia 自带的 <see cref="Image"/> 会在 Render 里读 Bitmap.Size 抛
/// ObjectDisposedException（Ref&lt;IBitmapImpl&gt;），异常直接冒到渲染循环把进程带崩，
/// 用户侧看到的就是「点一下闪退」。自带 Image 的 Render 是 sealed，包不住，
/// 所以这里自己画，把异常挡在控件内部。
/// </remarks>
public class SafeImage : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<SafeImage, IImage?>(nameof(Source));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<SafeImage, Stretch>(nameof(Stretch), Stretch.Uniform);

    static SafeImage()
    {
        AffectsRender<SafeImage>(SourceProperty, StretchProperty);
        AffectsMeasure<SafeImage>(SourceProperty);
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var source = Source;
        if (source == null) return;

        var imageSize = TryGetSize(source);
        if (imageSize == null)
        {
            // 位图已经被释放：换成默认图标，下一帧再画
            Source = Imager.GetDefaultBitmap();
            return;
        }

        var dest = CalculateDestRect(Bounds.Size, imageSize.Value, Stretch);
        if (dest.Width <= 0 || dest.Height <= 0) return;

        try
        {
            source.Draw(context, new Rect(imageSize.Value), dest);
        }
        catch (ObjectDisposedException)
        {
            Source = Imager.GetDefaultBitmap();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var source = Source;
        var size = source == null ? default(Size?) : TryGetSize(source);
        if (size == null) return default;

        return new Size(
            double.IsInfinity(availableSize.Width) ? size.Value.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? size.Value.Height : availableSize.Height);
    }

    private static Size? TryGetSize(IImage source)
    {
        try
        {
            var size = source.Size;
            return size.Width > 0 && size.Height > 0 ? size : null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static Rect CalculateDestRect(Size available, Size source, Stretch stretch)
    {
        if (available.Width <= 0 || available.Height <= 0) return default;

        switch (stretch)
        {
            case Stretch.None:
                return new Rect(0, 0, Math.Min(source.Width, available.Width), Math.Min(source.Height, available.Height));
            case Stretch.Fill:
                return new Rect(available);
            case Stretch.UniformToFill:
            {
                var scale = Math.Max(available.Width / source.Width, available.Height / source.Height);
                var width = source.Width * scale;
                var height = source.Height * scale;
                return new Rect((available.Width - width) / 2, (available.Height - height) / 2, width, height);
            }
            default:
            {
                var scale = Math.Min(available.Width / source.Width, available.Height / source.Height);
                var width = source.Width * scale;
                var height = source.Height * scale;
                return new Rect((available.Width - width) / 2, (available.Height - height) / 2, width, height);
            }
        }
    }
}
