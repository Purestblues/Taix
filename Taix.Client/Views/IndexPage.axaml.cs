using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Taix.Client.ViewModels;

namespace Taix.Client.Views;

public partial class IndexPage : UserControl
{
    private bool _entered;

    public IndexPage()
    {
        InitializeComponent();
        DataContext = ServiceLocator.GetRequiredService<IndexPageViewModel>();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_entered) return;
        _entered = true;

        var root = PageRoot;
        if (root == null) return;

        // 页面进场：淡入 + 轻微上移，避免切页时"啪"地一下整块出现
        root.RenderTransform = TransformOperations.Parse("translateY(12px)");
        root.Opacity = 0;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(320),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(RenderTransformProperty, TransformOperations.Parse("translateY(12px)"))
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(RenderTransformProperty, TransformOperations.Parse("translateY(0px)"))
                    }
                }
            }
        };

        try
        {
            await animation.RunAsync(root);
        }
        catch
        {
            // 动画失败不能影响页面显示
            root.Opacity = 1;
            root.RenderTransform = null;
        }
    }
}
