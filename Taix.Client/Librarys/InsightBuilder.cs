using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Taix.Client.Models;

namespace Taix.Client.Librarys;

/// <summary>
/// 详细页看板的纯计算层。
///
/// 这里只做「区间 → 数字」的换算，不碰 Avalonia、不碰网络：
/// 会话 / 浏览记录统一拆成 (开始, 结束) 的本地时间片段再进来，
/// 这样单测可以直接喂手写的时间段，不用起服务端。
/// </summary>
public static class InsightBuilder
{
    /// <summary>环比结果</summary>
    public readonly record struct InsightDiff(string Text, bool IsUp, bool IsDown, bool IsFlat);

    /// <summary>一段周期内的使用会话汇总（全部按本地时间、已裁剪到区间内）</summary>
    public readonly record struct SessionSummary(
        int Count,
        double TotalSeconds,
        double LongestSeconds,
        DateTime FirstStart,
        DateTime LastEnd,
        DateTime LongestStart)
    {
        public bool HasData => Count > 0 && TotalSeconds > 0;

        public double AverageSeconds => Count > 0 ? TotalSeconds / Count : 0;
    }

    /// <summary>
    /// 环比：上一周期为 0（或本期为 0）时不显示，避免出现「+∞%」。
    /// </summary>
    public static InsightDiff? BuildDiff(double current, double previous)
    {
        if (previous <= 0 || current < 0) return null;

        var ratio = (current - previous) / previous;
        var percent = (int)Math.Round(ratio * 100);
        var text = percent > 0 ? $"+{percent}%" : percent < 0 ? $"{percent}%" : "0%";
        return new InsightDiff(text, percent > 0, percent < 0, percent == 0);
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 1) return "0s";

        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m";
        return $"{ts.Seconds}s";
    }

    /// <summary>日视图按小时、期视图按天，时长格式统一走这里</summary>
    public static string FormatClock(DateTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>跨周期的首末时间：同一天只显示时刻，跨天带上日期</summary>
    public static string FormatMoment(DateTime time, DateTime rangeStart, DateTime rangeEnd)
    {
        var sameDay = rangeStart.Date == rangeEnd.Date;
        return sameDay ? FormatClock(time) : time.ToString("M/d HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 把一个时间段切片，逐个小时回调（已裁剪到 [rangeStart, rangeEnd)）。
    /// 会话跨小时、跨天都靠它拆开。
    /// </summary>
    public static void ForEachHourSlice(DateTime start, DateTime end, DateTime rangeStart, DateTime rangeEnd,
        Action<DateTime, double> sink)
    {
        var from = start < rangeStart ? rangeStart : start;
        var to = end > rangeEnd ? rangeEnd : end;
        if (to <= from) return;

        var cursor = from;
        while (cursor < to)
        {
            var hourStart = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0);
            var sliceEnd = hourStart.AddHours(1);
            if (sliceEnd > to) sliceEnd = to;

            var seconds = (sliceEnd - cursor).TotalSeconds;
            if (seconds > 0) sink(hourStart, seconds);

            cursor = sliceEnd;
        }
    }

    /// <summary>同上，逐天回调</summary>
    public static void ForEachDaySlice(DateTime start, DateTime end, DateTime rangeStart, DateTime rangeEnd,
        Action<DateTime, double> sink)
    {
        var from = start < rangeStart ? rangeStart : start;
        var to = end > rangeEnd ? rangeEnd : end;
        if (to <= from) return;

        var cursor = from;
        while (cursor < to)
        {
            var dayStart = cursor.Date;
            var sliceEnd = dayStart.AddDays(1);
            if (sliceEnd > to) sliceEnd = to;

            var seconds = (sliceEnd - cursor).TotalSeconds;
            if (seconds > 0) sink(dayStart, seconds);

            cursor = sliceEnd;
        }
    }

    /// <summary>按「几点」汇总（0~23），跨天、跨月都一样用</summary>
    public static double[] HourOfDayHistogram(IEnumerable<(DateTime Start, DateTime End)> spans,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var values = new double[24];
        foreach (var span in spans)
            ForEachHourSlice(span.Start, span.End, rangeStart, rangeEnd,
                (hourStart, seconds) => values[hourStart.Hour] += seconds);

        return values;
    }

    /// <summary>
    /// 名称 × 小时（日视图的「哪几个应用 / 站点在几点用」）。
    /// 只返回出现过的名称，排序交给调用方。
    /// </summary>
    public static Dictionary<string, double[]> NameHourGrid(
        IEnumerable<(DateTime Start, DateTime End, string Name)> spans, DateTime rangeStart, DateTime rangeEnd)
    {
        var grid = new Dictionary<string, double[]>(StringComparer.Ordinal);

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Name)) continue;
            if (!grid.TryGetValue(span.Name, out var row))
            {
                row = new double[24];
                grid[span.Name] = row;
            }

            var target = row;
            ForEachHourSlice(span.Start, span.End, rangeStart, rangeEnd,
                (hourStart, seconds) => target[hourStart.Hour] += seconds);
        }

        return grid;
    }

    /// <summary>星期 × 小时（周一 = 第 0 行）</summary>
    public static double[,] WeekHourGrid(IEnumerable<(DateTime Start, DateTime End)> spans,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var grid = new double[7, 24];
        foreach (var span in spans)
            ForEachHourSlice(span.Start, span.End, rangeStart, rangeEnd, (hourStart, seconds) =>
            {
                var index = ((int)hourStart.DayOfWeek + 6) % 7;
                grid[index, hourStart.Hour] += seconds;
            });

        return grid;
    }

    /// <summary>月 × 日（1 月 = 第 0 行，1 号 = 第 0 列）</summary>
    public static double[,] MonthDayGrid(IEnumerable<(DateTime Start, DateTime End)> spans,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var grid = new double[12, 31];
        foreach (var span in spans)
            ForEachDaySlice(span.Start, span.End, rangeStart, rangeEnd, (dayStart, seconds) =>
            {
                var row = dayStart.Month - 1;
                var column = dayStart.Day - 1;
                if (row is < 0 or > 11 || column is < 0 or > 30) return;
                grid[row, column] += seconds;
            });

        return grid;
    }

    /// <summary>会话次数 / 总时长 / 最长单次 / 首次·末次时刻</summary>
    public static SessionSummary Summarize(IEnumerable<(DateTime Start, DateTime End)> spans,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var count = 0;
        var total = 0d;
        var longest = 0d;
        var first = DateTime.MaxValue;
        var last = DateTime.MinValue;
        var longestStart = DateTime.MinValue;

        foreach (var span in spans)
        {
            var from = span.Start < rangeStart ? rangeStart : span.Start;
            var to = span.End > rangeEnd ? rangeEnd : span.End;
            var seconds = (to - from).TotalSeconds;
            if (seconds <= 0) continue;

            count++;
            total += seconds;
            if (from < first) first = from;
            if (to > last) last = to;
            if (seconds > longest)
            {
                longest = seconds;
                longestStart = from;
            }
        }

        if (count == 0) return new SessionSummary(0, 0, 0, default, default, default);

        return new SessionSummary(count, total, longest, first, last, longestStart);
    }

    /// <summary>最忙的那个小时，没有数据返回 -1</summary>
    public static int PeakHour(double[] histogram)
    {
        var peak = -1;
        var max = 0d;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] <= max) continue;
            max = histogram[i];
            peak = i;
        }

        return peak;
    }

    /// <summary>
    /// 把 24 小时直方图折成四段（凌晨 / 上午 / 下午 / 晚上）。
    /// 条宽在这里算好，XAML 直接绑。
    /// </summary>
    public static IReadOnlyList<InsightBand> BuildBands(double[] hourOfDay, IReadOnlyList<string> names,
        double maxBarWidth)
    {
        var total = hourOfDay.Sum();
        if (total <= 0 || names.Count < 4) return [];

        var ranges = new (int From, int To)[] { (0, 6), (6, 12), (12, 18), (18, 24) };
        var bands = new List<InsightBand>(4);

        for (var i = 0; i < 4; i++)
        {
            var (from, to) = ranges[i];
            var seconds = 0d;
            for (var hour = from; hour < to; hour++) seconds += hourOfDay[hour];

            var share = seconds / total;
            bands.Add(new InsightBand
            {
                Name = names[i],
                Share = share,
                ShareText = $"{(int)Math.Round(share * 100)}%",
                BarWidth = Math.Max(2, Math.Round(share * maxBarWidth))
            });
        }

        return bands;
    }

    public static double Max(double[,] grid)
    {
        var max = 0d;
        foreach (var value in grid)
            if (value > max) max = value;

        return max;
    }
}
