using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using ShiJian.Client;
using ShiJian.Client.Librarys;
using ShiJian.Client.Servicers.Instances;
using ShiJian.Client.Shared.Models.Config;
using ShiJian.Client.Shared.Models.Entity;
using Xunit;

namespace ShiJian.Tests;

/// <summary>
/// N2 统一实体表的客户端约定：实体行的标识解析、以及两个新配置项的变更检测。
/// 实体表本身的互斥归属与差额行在服务端（`shijian-server/src/services/entity.rs`）有单测。
/// </summary>
public class EntityModelTests
{
    [Theory]
    [InlineData("app:12", 12)]
    [InlineData("site:5", 5)]
    [InlineData("other:3", 3)]
    [InlineData("bad", 0)]
    [InlineData("app:", 0)]
    [InlineData("app:abc", 0)]
    public void EntityId_ParsesKeySuffix(string key, int expected)
    {
        var model = new EntityModel { Key = key };

        Assert.Equal(expected, model.EntityId);
    }

    [Fact]
    public void MergedMemberCount_ComesFromMembers()
    {
        var model = new EntityModel
        {
            Key = "app:1",
            Members =
            [
                new EntityMemberModel { Key = "app:2", Name = "PiDeck-0.9.5-setup", Seconds = 60 },
                new EntityMemberModel { Key = "app:3", Name = "PiDeck-helper", Seconds = 10 }
            ]
        };

        Assert.Equal(2, model.Members.Count);
        Assert.Equal(70, model.Members.Sum(member => member.Seconds));
    }

    private static ConfigModel CreateConfig()
    {
        return new ConfigModel
        {
            General = new GeneralModel(),
            Behavior = new BehaviorModel(),
            Shortcut = new ShortcutModel()
        };
    }

    [Fact]
    public void WebBrowserMerged_IsReported()
    {
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.General.WebBrowserMerged = true;

        var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

        Assert.Contains("General.WebBrowserMerged", changes);
    }

    [Fact]
    public void DefaultBrowser_IsReported()
    {
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.General.DefaultBrowser = "chrome";

        var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

        Assert.Contains("General.DefaultBrowser", changes);
    }

    [Fact]
    public void AggregateColumnsByCategory_SplitsAppAndSiteSidesAndIncludesBalanceRow()
    {
        var hours = new EntityHoursModel
        {
            BucketCount = 3,
            Rows =
            [
                new EntityHoursRow
                {
                    Key = "app:1",
                    Kind = "app",
                    CategoryId = 10,
                    Values = [100, 200, 0]
                },
                new EntityHoursRow
                {
                    Key = "other:2",
                    Kind = "other",
                    CategoryId = 10,
                    IsBrowser = true,
                    Values = [50, 0, 30]
                },
                new EntityHoursRow
                {
                    Key = "site:5",
                    Kind = "site",
                    CategoryId = 10, // 同 ID 但在网站分类命名空间，不能混进应用侧
                    Values = [40, 60, 10]
                }
            ]
        };

        var appColumns = ChartDataMapper.AggregateColumnsByCategory(hours, isWeb: false);
        var siteColumns = ChartDataMapper.AggregateColumnsByCategory(hours, isWeb: true);

        Assert.Single(appColumns);
        Assert.Equal(10, appColumns[0].CategoryID);
        Assert.Equal([150d, 200d, 30d], appColumns[0].Values);

        Assert.Single(siteColumns);
        Assert.Equal(10, siteColumns[0].CategoryID);
        Assert.Equal([40d, 60d, 10d], siteColumns[0].Values);
    }

    [Fact]
    public void MapFromEntity_HidesMemberPopupOnOtherRowAndMapsBadges()
    {
        var other = new EntityModel
        {
            Key = "other:3",
            Kind = "other",
            Name = "浏览器内其他页面",
            Seconds = 900,
            CategoryId = 2,
            CategoryKind = "app",
            CategoryName = "浏览器",
            CategoryColor = "#4285F4",
            Members = [new EntityMemberModel { Key = "site:1", Name = "example.com", Seconds = 300 }]
        };

        var mapped = ChartDataMapper.MapFromEntity(other, includeBadges: true);

        Assert.Equal(string.Empty, mapped.PopupText);
        Assert.Single(mapped.BadgeList);
        Assert.Equal(2, mapped.BadgeList[0].CategoryId);
        Assert.False(mapped.BadgeList[0].IsWebCategory);
    }

    [Fact]
    public void FormatEntityLoadError_DistinguishesNotFoundFromGeneralError()
    {
        var notFound = new HttpRequestException("404", null, HttpStatusCode.NotFound);
        var serverErr = new InvalidOperationException("boom");

        Assert.Equal(ResourceStrings.ServerOutdated, ChartDataMapper.FormatEntityLoadError(notFound));
        Assert.Equal(ResourceStrings.EntityLoadFailed, ChartDataMapper.FormatEntityLoadError(serverErr));
    }

    [Fact]
    public void AggregateColumnsByCategory_SlicesMixedGroupsByCategoryKind()
    {
        var hours = new EntityHoursModel
        {
            BucketCount = 2,
            Rows =
            [
                new EntityHoursRow
                {
                    Key = "app:1",
                    Kind = "mixed",
                    CategoryId = 5,
                    CategoryKind = "app",
                    Values = [100, 50]
                },
                new EntityHoursRow
                {
                    Key = "site:2",
                    Kind = "mixed",
                    CategoryId = 5,
                    CategoryKind = "site",
                    Values = [40, 20]
                }
            ]
        };

        var appCols = ChartDataMapper.AggregateColumnsByCategory(hours, isWeb: false);
        var siteCols = ChartDataMapper.AggregateColumnsByCategory(hours, isWeb: true);

        Assert.Equal([100d, 50d], Assert.Single(appCols).Values);
        Assert.Equal([40d, 20d], Assert.Single(siteCols).Values);
    }
}
