using System;
using System.Collections.Generic;
using ShiJian.Client.Librarys;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Web;
using Xunit;

namespace ShiJian.Tests;

public class ChartDataMapperTests
{
    private static AppModel CreateApp(string name, int categoryId = 0, string? categoryName = null)
    {
        return new AppModel
        {
            ID = categoryId + 1,
            Name = name,
            File = $"/apps/{name}.exe",
            CategoryID = categoryId,
            Category = categoryName == null
                ? null
                : new ShiJian.Client.Shared.Models.CategoryModel
                {
                    ID = categoryId,
                    Name = categoryName,
                    Color = "#123456",
                    IsSystem = false
                }
        };
    }

    private static DailyLogModel CreateLog(AppModel app, int timeSeconds, DateTime? date = null)
    {
        return new DailyLogModel
        {
            ID = app.ID,
            Date = date ?? new DateTime(2026, 11, 11),
            Time = timeSeconds,
            AppModelID = app.ID,
            AppModel = app
        };
    }

    [Fact]
    public void MapFromDailyLogs_OrdersByValueDescending()
    {
        var logs = new[]
        {
            CreateLog(CreateApp("small"), 10),
            CreateLog(CreateApp("big"), 300),
            CreateLog(CreateApp("mid"), 60)
        };

        var result = ChartDataMapper.MapFromDailyLogs(logs);

        Assert.Equal(new[] { "big", "mid", "small" }, result.ConvertAll(x => x.Name));
    }

    [Fact]
    public void MapFromDailyLogs_CanKeepInputOrder()
    {
        var logs = new[]
        {
            CreateLog(CreateApp("first"), 10),
            CreateLog(CreateApp("second"), 300)
        };

        var result = ChartDataMapper.MapFromDailyLogs(logs, orderByValue: false);

        Assert.Equal(new[] { "first", "second" }, result.ConvertAll(x => x.Name));
    }

    [Fact]
    public void MapFromDailyLogs_SkipsEntriesWithoutApp()
    {
        var logs = new List<DailyLogModel>
        {
            new() { ID = 1, Time = 10, AppModel = null! },
            CreateLog(CreateApp("kept"), 20)
        };

        var result = ChartDataMapper.MapFromDailyLogs(logs);

        Assert.Single(result);
        Assert.Equal("kept", result[0].Name);
    }

    [Fact]
    public void MapFromDailyLogs_FallsBackToSharedApp()
    {
        var shared = CreateApp("shared");
        var logs = new List<DailyLogModel>
        {
            new() { ID = 1, Time = 42, AppModel = null! }
        };

        var result = ChartDataMapper.MapFromDailyLogs(logs, shared);

        Assert.Single(result);
        Assert.Equal("shared", result[0].Name);
        Assert.Equal(42, result[0].Value);
    }

    [Fact]
    public void MapFromDailyLogs_WithBadges_ProducesCategoryBadge()
    {
        var logs = new[] { CreateLog(CreateApp("code", 1, "写代码"), 120) };

        var result = ChartDataMapper.MapFromDailyLogs(logs, includeBadges: true);

        var model = Assert.Single(result);
        Assert.Equal("写代码", model.CategoryName);
        Assert.NotNull(model.CategoryColor);
        Assert.NotNull(model.BadgeList);
        var badge = Assert.Single(model.BadgeList!);
        Assert.Equal("写代码", badge.Name);
        Assert.Equal(model.CategoryColor, badge.TextColor);
    }

    [Fact]
    public void MapFromDailyLogs_WithoutCategory_HasNoBadge()
    {
        var logs = new[] { CreateLog(CreateApp("plain"), 5) };

        var result = ChartDataMapper.MapFromDailyLogs(logs, includeBadges: true);

        var model = Assert.Single(result);
        Assert.Null(model.CategoryName);
        Assert.Empty(model.BadgeList!);
    }

    [Fact]
    public void MapFromWebSites_UsesAliasOverTitle()
    {
        var sites = new[]
        {
            new WebSiteModel { ID = 1, Title = "Bilibili", Alias = "B站", Domain = "bilibili.com", Duration = 30 },
            new WebSiteModel { ID = 2, Title = "GitHub", Domain = "github.com", Duration = 90 }
        };

        var result = ChartDataMapper.MapFromWebSites(sites);

        Assert.Equal("GitHub", result[0].Name);
        Assert.Equal("B站", result[1].Name);
    }

    [Fact]
    public void MapFromHoursLogs_MapsTimeAndDate()
    {
        var stamp = new DateTime(2026, 11, 11, 9, 0, 0);
        var app = CreateApp("hourly");
        var logs = new[]
        {
            new HoursLogModel { ID = 1, DataTime = stamp, Time = 55, AppModelID = app.ID, AppModel = app }
        };

        var result = ChartDataMapper.MapFromHoursLogs(logs);

        var model = Assert.Single(result);
        Assert.Equal(55, model.Value);
        // 服务端返回的是 UTC，映射时会转成本地时间
        Assert.Equal(DateTime.SpecifyKind(stamp, DateTimeKind.Utc).ToLocalTime(), model.DateTime);
        Assert.Equal("hourly", model.Name);
    }

    [Fact]
    public void BuildUnifiedCategorySeries_SeparatesAppAndSiteIdsAndMergesUncategorized()
    {
        var hours = new ShiJian.Client.Shared.Models.Entity.EntityHoursModel
        {
            BucketCount = 2,
            Rows =
            [
                // 应用 ID=1（非系统：开发）
                new() { Key = "app:1", Kind = "app", CategoryId = 1, Values = [100, 50] },
                // 网站 ID=1（非系统：影音 — 与应用共享 ID=1 但命名空间不同）
                new() { Key = "site:1", Kind = "site", CategoryKind = "site", CategoryId = 1, Values = [60, 0] },
                // 未分类应用 + 系统分类网站：应合并进同一「未分类」桶
                new() { Key = "app:2", Kind = "app", CategoryId = 0, Values = [30, 10] },
                new() { Key = "site:2", Kind = "site", CategoryKind = "site", CategoryId = 99, Values = [20, 30] },
                // 时长为 0 的行不应生成孤立 0 秒分类
                new() { Key = "app:3", Kind = "app", CategoryId = 2, Values = [0, 0] }
            ]
        };

        var appVisuals = ChartDataMapper.ToVisualMap(
            new[]
            {
                new CategoryModel { ID = 1, Name = "开发", Color = "#16a34a", IsSystem = false },
                new CategoryModel { ID = 2, Name = "办公", Color = "#2563eb", IsSystem = false }
            },
            "未分类");
        var siteVisuals = ChartDataMapper.ToVisualMap(
            new[]
            {
                new WebSiteCategoryModel { ID = 1, Name = "影音", Color = "#ea580c", IsSystem = false },
                new WebSiteCategoryModel { ID = 99, Name = "未分类", Color = "#999999", IsSystem = true }
            },
            "未分类");

        var result = ChartDataMapper.BuildUnifiedCategorySeries(
            hours, appVisuals, siteVisuals, "未分类", ["10", "11"]);

        Assert.Equal(new[] { "开发", "未分类", "影音" }, result.ConvertAll(x => x.Name));
        Assert.Equal(150, result[0].Value);
        Assert.Equal(90, result[1].Value);
        Assert.Equal(60, result[2].Value);
        Assert.Equal(new[] { 50d, 40d }, result[1].Values);
        Assert.Equal(300, result[0].Value + result[1].Value + result[2].Value);
    }

    [Fact]
    public void SumAllBuckets_SumsAllEntitiesAndPadsExpectedLength()
    {
        var hours = new ShiJian.Client.Shared.Models.Entity.EntityHoursModel
        {
            BucketCount = 3,
            Rows =
            [
                new() { Key = "app:1", Kind = "app", Values = [10, 20, 0] },
                new() { Key = "site:1", Kind = "site", Values = [5, 15, 30] }
            ]
        };

        var totals = ChartDataMapper.SumAllBuckets(hours, expectedLength: 4);

        Assert.Equal(new[] { 15d, 35d, 30d, 0d }, totals);
    }

    [Fact]
    public void BuildUnifiedCategorySeries_WithUnifiedVisualMap_MergesAppAndSiteIntoSingleSeries()
    {
        var hours = new ShiJian.Client.Shared.Models.Entity.EntityHoursModel
        {
            BucketCount = 2,
            Rows =
            [
                new() { Key = "app:1", Kind = "app", CategoryId = 10, Values = [100, 20] },
                new() { Key = "site:1", Kind = "site", CategoryId = 10, Values = [30, 50] },
                new() { Key = "site:2", Kind = "site", CategoryId = 0, Values = [10, 5] }
            ]
        };

        var unifiedVisuals = ChartDataMapper.ToVisualMap(
            new[]
            {
                new CategoryModel { ID = 10, Name = "开发", Color = "#22c55e", IsSystem = false },
                new CategoryModel { ID = 99, Name = "未分类", Color = "#999999", IsSystem = true }
            },
            "未分类");

        var result = ChartDataMapper.BuildUnifiedCategorySeries(
            hours, unifiedVisuals, "未分类", ["10", "11"]);

        Assert.Equal(new[] { "开发", "未分类" }, result.ConvertAll(x => x.Name));
        Assert.Equal(200, result[0].Value);
        Assert.Equal(new[] { 130d, 70d }, result[0].Values);
        Assert.Equal(15, result[1].Value);
        Assert.Equal(new[] { 10d, 5d }, result[1].Values);
    }
}
