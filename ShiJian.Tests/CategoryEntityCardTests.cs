using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ShiJian.Client.Librarys;
using ShiJian.Client.Models;
using Xunit;

namespace ShiJian.Tests;

public class CategoryEntityCardTests
{
    [AvaloniaFact]
    public void TagOptionButton_PointerOverChangesBackgroundUnderSimpleTheme()
    {
        var hoverBrush = new SolidColorBrush(Color.Parse("#22FFFFFF"));
        var button = new Button
        {
            Classes = { "tagOption" },
            Content = new TextBlock { Text = "开发" }
        };

        var normalStyle = new Style(x => x.OfType<Button>().Class("tagOption"));
        normalStyle.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        normalStyle.Setters.Add(new Setter(Button.TemplateProperty, new Avalonia.Controls.Templates.FuncControlTemplate<Button>((ctrl, scope) =>
            new Border
            {
                [!Border.BackgroundProperty] = ctrl[!Button.BackgroundProperty],
                Child = new ContentPresenter
                {
                    [!ContentPresenter.ContentProperty] = ctrl[!Button.ContentProperty]
                }
            })));

        var hoverStyle = new Style(x => x.OfType<Button>().Class("tagOption").Class(":pointerover"));
        hoverStyle.Setters.Add(new Setter(Button.BackgroundProperty, hoverBrush));

        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = button
        };
        window.Styles.Add(normalStyle);
        window.Styles.Add(hoverStyle);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Same(Brushes.Transparent, button.Background);

            ((IPseudoClasses)button.Classes).Set(":pointerover", true);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(hoverBrush, button.Background);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WrapPanel_LaysOutEntityCardsInMultiColumnGrid()
    {
        var items = new[]
        {
            new CategoryEntityRow { PrimaryId = 1, Name = "App1", CategoryName = "开发" },
            new CategoryEntityRow { PrimaryId = 2, Name = "Site2", IsSite = true, CategoryName = "未分类", IsUncategorized = true },
            new CategoryEntityRow { PrimaryId = 3, Name = "App3", CategoryName = "办公" }
        };

        var panel = new WrapPanel { Width = 700 };
        foreach (var item in items)
        {
            panel.Children.Add(new Border
            {
                Width = 198,
                Height = 156,
                Margin = new Thickness(4),
                DataContext = item
            });
        }

        var window = new Window { Width = 800, Height = 400, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var first = panel.Children[0];
            var second = panel.Children[1];
            var third = panel.Children[2];

            // All 3 cards (3 * 206 = 618 <= 700) sit on the same horizontal row instead of wide single-column strips
            Assert.Equal(first.Bounds.Y, second.Bounds.Y);
            Assert.Equal(first.Bounds.Y, third.Bounds.Y);
            Assert.True(second.Bounds.X > first.Bounds.X);
            Assert.True(third.Bounds.X > second.Bounds.X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EntityCardLayout_DoesNotClipDurationRowWithin156Height()
    {
        var todayText = new TextBlock { Text = "今日 1h29m", FontSize = 11 };
        var statsGrid = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { todayText }
        };
        Grid.SetRow(statsGrid, 2);

        var header = new Border { Height = 32 };
        Grid.SetRow(header, 0);

        var titleStack = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "Antigravity", FontSize = 13 },
                new TextBlock { Text = "Antigravity", FontSize = 11 }
            }
        };
        Grid.SetRow(titleStack, 1);

        var tagPill = new Button
        {
            Margin = new Thickness(0, 8, 0, 0),
            Height = 28,
            VerticalAlignment = VerticalAlignment.Bottom,
            Content = new TextBlock { Text = "AI", FontSize = 11 }
        };
        Grid.SetRow(tagPill, 3);

        var rootGrid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,*"),
            Children = { header, titleStack, statsGrid, tagPill }
        };

        var card = new Border
        {
            Width = 198,
            Height = 156,
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(1),
            Child = rootGrid
        };

        var window = new Window { Width = 300, Height = 260, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.True(statsGrid.Bounds.Height >= todayText.DesiredSize.Height);
            Assert.True(statsGrid.Bounds.Bottom <= tagPill.Bounds.Y);
            Assert.True(tagPill.Bounds.Bottom <= rootGrid.Bounds.Height + 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task SlideMotion_AnimateYAndOpacityAsync_AppliesTargetState()
    {
        var banner = new Border
        {
            Width = 220,
            Height = 40,
            Opacity = 0,
            RenderTransform = new TranslateTransform { Y = -44 }
        };

        await SlideMotion.AnimateYAndOpacityAsync(
            banner,
            fromY: -44,
            toY: 0,
            fromOpacity: 0,
            toOpacity: 1,
            duration: TimeSpan.Zero,
            easing: new CubicEaseOut(),
            resetTransformOnComplete: false);

        Assert.Equal(1d, banner.Opacity);
        Assert.Equal(0d, SlideMotion.GetTranslateY(banner));

        await SlideMotion.AnimateYAndOpacityAsync(
            banner,
            fromY: 0,
            toY: -32,
            fromOpacity: 1,
            toOpacity: 0,
            duration: TimeSpan.Zero,
            easing: new CubicEaseIn(),
            resetTransformOnComplete: false);

        Assert.Equal(0d, banner.Opacity);
        Assert.Equal(-32d, SlideMotion.GetTranslateY(banner));
    }
}
