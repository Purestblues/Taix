using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using ShiJian.Client.Librarys;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Views;

public partial class IndexPage : UserControl
{
    /// <summary>总览卡错峰进场（比整页动得轻一点）。整页淡入由 PageContainer 统一播放</summary>
    private static readonly Animation CardAnimation = CreateFadeSlide(220, 8);

    /// <summary>相邻两张卡的开始间隔，太小看不出错峰，太大整页显得拖拉</summary>
    private const int CardStaggerMs = 36;

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

        await StaggerStatCardsAsync();
    }

    /// <summary>
    /// 四张总览卡逐张进场，避免整排"啪"地一起出现
    /// </summary>
    private async Task StaggerStatCardsAsync()
    {
        var panel = StatCardsPanel;
        if (panel == null) return;

        var cards = panel.Children.OfType<Control>().ToList();
        if (!Motion.IsEnabled)
        {
            foreach (var card in cards)
            {
                card.Opacity = 1;
                card.RenderTransform = null;
            }

            return;
        }
        var running = new List<Task>(cards.Count);

        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            card.Opacity = 0;
            card.RenderTransform = TransformOperations.Parse("translateY(8px)");
            running.Add(AnimateCardAsync(card, TimeSpan.FromMilliseconds(CardStaggerMs * index)));
        }

        await Task.WhenAll(running);
    }

    private static async Task AnimateCardAsync(Control card, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay);

        try
        {
            await CardAnimation.RunAsync(card);
        }
        catch
        {
            card.Opacity = 1;
            card.RenderTransform = null;
        }
    }

    private static Animation CreateFadeSlide(int durationMs, double offsetY)
    {
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
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
                        new Setter(RenderTransformProperty, TransformOperations.Parse($"translateY({offsetY}px)"))
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
    }
}
