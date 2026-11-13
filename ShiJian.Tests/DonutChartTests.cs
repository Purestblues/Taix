using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using ShiJian.Client.Controls.Charts;
using ShiJian.Client.Controls.Charts.Model;
using Xunit;

namespace ShiJian.Tests;

public class DonutChartTests
{
    private static void AssertFitsInsideInnerCircle(
        FormattedText text,
        Point origin,
        Point center,
        double innerRadius)
    {
        var hasMaxWidth = double.IsFinite(text.MaxTextWidth) && text.MaxTextWidth > 0;
        var effectiveWidth = hasMaxWidth ? Math.Min(text.Width, text.MaxTextWidth) : text.Width;
        var boxWidth = hasMaxWidth ? text.MaxTextWidth : text.Width;

        // 无论单行居中还是限宽居中，实际绘制区域均在 [origin.X, origin.X + boxWidth] 内
        var left = origin.X + (boxWidth - effectiveWidth) / 2;
        var right = left + effectiveWidth;
        var top = origin.Y;
        var bottom = origin.Y + text.Height;

        var corners = new[]
        {
            new Point(left, top),
            new Point(right, top),
            new Point(left, bottom),
            new Point(right, bottom)
        };

        foreach (var corner in corners)
        {
            var dx = corner.X - center.X;
            var dy = corner.Y - center.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            Assert.True(distance <= innerRadius,
                $"文本边界角 ({corner.X:F1}, {corner.Y:F1}) 距圆心 {distance:F1}px，超出内环半径 {innerRadius:F1}px（文本宽 {text.Width:F1}, 高 {text.Height:F1}）");
        }
    }

    [AvaloniaTheory]
    [InlineData(128, 13, "暂时没有数据")]
    [InlineData(104, 12, "暂时没有数据")]
    [InlineData(128, 13, "No data")]
    [InlineData(104, 12, "No data")]
    [InlineData(104, 12, "该时间段内此分类暂无使用记录")]
    public void EmptyText_FitsInsideInnerRing(double size, double thickness, string emptyText)
    {
        var center = new Point(size / 2, size / 2);
        var radius = size / 2 - thickness / 2 - 2;
        var innerRadius = radius - thickness / 2;
        var safeRadius = DonutChart.ComputeSafeRadius(innerRadius);

        var (text, origin) = DonutChart.LayoutCenteredText(
            emptyText, center, safeRadius, verticalShift: 0,
            maxFontSize: 12, minFontSize: 9.5, step: 0.5,
            Brushes.Gray, FontWeight.Normal, allowWrap: true);

        AssertFitsInsideInnerCircle(text, origin, center, innerRadius);
    }

    [AvaloniaTheory]
    [InlineData(128, 13, "123h45m", "总计")]
    [InlineData(104, 12, "123h45m", "总计")]
    [InlineData(104, 12, "12h30m", "即时通讯与社交娱乐")]
    public void CenterAndCaption_FitInsideInnerRing(double size, double thickness, string main, string caption)
    {
        var center = new Point(size / 2, size / 2);
        var radius = size / 2 - thickness / 2 - 2;
        var innerRadius = radius - thickness / 2;
        var safeRadius = DonutChart.ComputeSafeRadius(innerRadius);

        var maxMainFont = Math.Min(20, Math.Max(12, safeRadius * 0.52));
        var (mainText, mainOrigin) = DonutChart.LayoutCenteredText(
            main, center, safeRadius, verticalShift: -7,
            maxMainFont, minFontSize: 11, step: 1,
            Brushes.Black, FontWeight.SemiBold, allowWrap: false);

        var (captionText, captionOrigin) = DonutChart.LayoutCenteredText(
            caption, center, safeRadius, verticalShift: 12,
            maxFontSize: 11, minFontSize: 9.5, step: 0.5,
            Brushes.Gray, FontWeight.Normal, allowWrap: false);

        AssertFitsInsideInnerCircle(mainText, mainOrigin, center, innerRadius);
        AssertFitsInsideInnerCircle(captionText, captionOrigin, center, innerRadius);
    }

    [AvaloniaFact]
    public void DonutChart_RendersEmptyAndPopulatedWithoutError()
    {
        var chart = new DonutChart
        {
            Width = 104,
            Height = 104,
            Thickness = 12,
            EmptyText = "暂时没有数据",
            TextBrush = Brushes.Black,
            MutedTextBrush = Brushes.Gray,
            TrackBrush = Brushes.LightGray
        };

        var window = new Window { Width = 200, Height = 200, Content = chart };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            chart.Series = new[]
            {
                new ChartsDataModel { Name = "开发工具与文档", Value = 3600, Color = "#7E57C2" }
            };
            chart.CenterText = "1h";
            chart.CenterCaption = "总计";
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    }
}
