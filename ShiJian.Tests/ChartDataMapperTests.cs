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
}
