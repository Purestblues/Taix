using System;
using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Librarys;
using Xunit;

namespace ShiJian.Tests;

/// <summary>
/// 详细页看板的计算层。时间统一用本地时间，取一段固定的 2026-11 做基准，
/// 免得测试随「今天」漂移。
/// </summary>
public class InsightBuilderTests
{
    private static readonly DateTime DayStart = new(2026, 11, 11, 0, 0, 0);
    private static readonly DateTime DayEnd = new(2026, 11, 11, 23, 59, 59);

    private static List<(DateTime Start, DateTime End)> Spans(params (DateTime Start, DateTime End)[] items) =>
        items.ToList();

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(30, "30s")]
    [InlineData(90, "1m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(16920, "4h 42m")]
    public void FormatDuration_UsesCompactUnits(double seconds, string expected)
    {
        Assert.Equal(expected, InsightBuilder.FormatDuration(seconds));
    }

    [Fact]
    public void BuildDiff_ReturnsNullWhenPreviousIsEmpty()
    {
        Assert.Null(InsightBuilder.BuildDiff(120, 0));
    }

    [Fact]
    public void BuildDiff_ReportsDirectionAndPercent()
    {
        var up = InsightBuilder.BuildDiff(150, 100)!.Value;
        Assert.Equal("+50%", up.Text);
        Assert.True(up.IsUp);
        Assert.False(up.IsDown);

        var down = InsightBuilder.BuildDiff(80, 100)!.Value;
        Assert.Equal("-20%", down.Text);
        Assert.True(down.IsDown);

        var flat = InsightBuilder.BuildDiff(100, 100)!.Value;
        Assert.Equal("0%", flat.Text);
        Assert.True(flat.IsFlat);
    }

    [Fact]
    public void ForEachHourSlice_SplitsAtHourBoundary()
    {
        var slices = new List<(DateTime Hour, double Seconds)>();
        InsightBuilder.ForEachHourSlice(new DateTime(2026, 11, 11, 10, 30, 0),
            new DateTime(2026, 11, 11, 11, 20, 0), DayStart, DayEnd,
            (hour, seconds) => slices.Add((hour, seconds)));

        Assert.Equal(2, slices.Count);
        Assert.Equal(new DateTime(2026, 11, 11, 10, 0, 0), slices[0].Hour);
        Assert.Equal(1800, slices[0].Seconds);
        Assert.Equal(new DateTime(2026, 11, 11, 11, 0, 0), slices[1].Hour);
        Assert.Equal(1200, slices[1].Seconds);
    }

    [Fact]
    public void ForEachHourSlice_ClipsToRange()
    {
        var total = 0d;
        InsightBuilder.ForEachHourSlice(DayStart.AddHours(-3), DayStart.AddHours(2), DayStart, DayEnd,
            (_, seconds) => total += seconds);

        Assert.Equal(2 * 3600, total);
    }

    [Fact]
    public void HourOfDayHistogram_AccumulatesSameHourAcrossDays()
    {
        var histogram = InsightBuilder.HourOfDayHistogram(Spans(
                (new DateTime(2026, 11, 9, 15, 0, 0), new DateTime(2026, 11, 9, 16, 0, 0)),
                (new DateTime(2026, 11, 10, 15, 0, 0), new DateTime(2026, 11, 10, 15, 30, 0))),
            new DateTime(2026, 11, 9), new DateTime(2026, 11, 10, 23, 59, 59));

        Assert.Equal(5400, histogram[15]);
        Assert.Equal(0, histogram[16]);
    }

    [Fact]
    public void WeekHourGrid_UsesMondayAsFirstRow()
    {
        // 2026-11-11 是周三
        var wednesday = new DateTime(2026, 11, 11, 15, 0, 0);
        var grid = InsightBuilder.WeekHourGrid(
            Spans((wednesday, wednesday.AddMinutes(30))),
            new DateTime(2026, 11, 9), new DateTime(2026, 11, 15, 23, 59, 59));

        Assert.Equal(1800, grid[2, 15]);

        var monday = new DateTime(2026, 11, 9, 9, 0, 0);
        grid = InsightBuilder.WeekHourGrid(
            Spans((monday, monday.AddHours(1))),
            new DateTime(2026, 11, 9), new DateTime(2026, 11, 15, 23, 59, 59));

        Assert.Equal(3600, grid[0, 9]);
    }

    [Fact]
    public void MonthDayGrid_IndexesByMonthAndDay()
    {
        var span = (new DateTime(2026, 3, 5, 10, 0, 0), new DateTime(2026, 3, 5, 11, 0, 0));
        var grid = InsightBuilder.MonthDayGrid(Spans(span),
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31, 23, 59, 59));

        Assert.Equal(3600, grid[2, 4]);
        Assert.Equal(0, grid[1, 4]);
    }

    [Fact]
    public void Summarize_ClipsAndPicksLongest()
    {
        var summary = InsightBuilder.Summarize(Spans(
                (new DateTime(2026, 11, 11, 9, 0, 0), new DateTime(2026, 11, 11, 9, 10, 0)),
                (new DateTime(2026, 11, 11, 14, 0, 0), new DateTime(2026, 11, 11, 16, 0, 0))),
            DayStart, DayEnd);

        Assert.Equal(2, summary.Count);
        Assert.Equal(7800, summary.TotalSeconds);
        Assert.Equal(7200, summary.LongestSeconds);
        Assert.Equal(new DateTime(2026, 11, 11, 14, 0, 0), summary.LongestStart);
        Assert.Equal(new DateTime(2026, 11, 11, 9, 0, 0), summary.FirstStart);
        Assert.Equal(new DateTime(2026, 11, 11, 16, 0, 0), summary.LastEnd);
        Assert.Equal(3900, summary.AverageSeconds);
    }

    [Fact]
    public void Summarize_IgnoresSpansOutsideRange()
    {
        var summary = InsightBuilder.Summarize(
            Spans((DayStart.AddDays(2), DayStart.AddDays(2).AddHours(1))), DayStart, DayEnd);

        Assert.False(summary.HasData);
        Assert.Equal(0, summary.TotalSeconds);
    }

    [Fact]
    public void PeakHour_ReturnsBusiestHourOrMinusOne()
    {
        var histogram = new double[24];
        histogram[9] = 600;
        histogram[20] = 1200;

        Assert.Equal(20, InsightBuilder.PeakHour(histogram));
        Assert.Equal(-1, InsightBuilder.PeakHour(new double[24]));
    }

    [Fact]
    public void BuildBands_FoldsHoursIntoFourBands()
    {
        var histogram = new double[24];
        histogram[2] = 1000;  // 凌晨
        histogram[9] = 1000;  // 上午
        histogram[15] = 1500; // 下午
        histogram[21] = 500;  // 晚上

        var bands = InsightBuilder.BuildBands(histogram, ["凌晨", "上午", "下午", "晚上"], 100);

        Assert.Equal(4, bands.Count);
        Assert.Equal("25%", bands[0].ShareText);
        Assert.Equal("25%", bands[1].ShareText);
        Assert.Equal("38%", bands[2].ShareText);
        Assert.Equal("12%", bands[3].ShareText);
        Assert.Equal(38, bands[2].BarWidth);
    }

    [Fact]
    public void BuildBands_ReturnsEmptyWithoutData()
    {
        Assert.Empty(InsightBuilder.BuildBands(new double[24], ["a", "b", "c", "d"], 100));
    }

    [Fact]
    public void Max_FindsLargestCell()
    {
        Assert.Equal(42, InsightBuilder.Max(new double[,] { { 1, 42 }, { 3, 0 } }));
    }
}
