using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
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
                Height = 142,
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
}
