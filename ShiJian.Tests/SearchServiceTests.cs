using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Librarys;
using ShiJian.Client.Servicers.Instances;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Entity;
using ShiJian.Client.Shared.Models.Search;
using ShiJian.Client.Shared.Models.Web;
using Xunit;

namespace ShiJian.Tests;

public class SearchServiceTests
{
    [Fact]
    public void SearchCore_CollapsesMergedGroup_AndMatchesChildMemberName()
    {
        var apps = new[]
        {
            new AppModel { ID = 1, Name = "WeChat", Description = "微信" },
            new AppModel { ID = 2, Name = "WeChatAppEx", Description = "微信小程序运行时" },
            new AppModel { ID = 3, Name = "Notepad", Description = "记事本" }
        };
        var sites = new[]
        {
            new WebSiteModel { ID = 10, Title = "WeChat Read", Domain = "weread.qq.com" }
        };
        var groups = new[]
        {
            new AppMergeGroup
            {
                Key = "g_wechat",
                Kind = "mixed",
                Name = "微信生态",
                PrimaryAppId = 1,
                MemberAppIds = [1, 2],
                MemberSiteIds = [10]
            }
        };

        // 搜索子成员域名，应只命中一条合并后的主实体，且不会展开重复子项
        var results = SearchService.SearchCore(
            "weread",
            apps,
            sites,
            [],
            [],
            groups);

        var hit = Assert.Single(results);
        Assert.Equal(SearchResultType.App, hit.Type);
        Assert.Equal(1, hit.Id);
        Assert.Equal("微信生态", hit.DisplayName);
        Assert.Contains("3", hit.SecondaryText);
    }

    [Fact]
    public void BuildEntityHighlightItems_SkipsZeroAndAggregate_AndAddsMergedBadge()
    {
        var appById = new Dictionary<int, AppModel>
        {
            [1] = new AppModel { ID = 1, Name = "Code", IconFile = "code.png" }
        };
        var siteById = new Dictionary<int, WebSiteModel>
        {
            [5] = new WebSiteModel { ID = 5, Title = "GitHub", Domain = "github.com", IconFile = "gh.png" }
        };

        var rows = new[]
        {
            new EntityModel { Key = "aggregate:browser", Kind = "aggregate", Name = "浏览器汇总", Seconds = 999 },
            new EntityModel { Key = "app:99", Kind = "app", Name = "Idle", Seconds = 0 },
            new EntityModel
            {
                Key = "app:1",
                Kind = "mixed",
                Name = "VS Code Suite",
                Seconds = 600,
                Members =
                [
                    new EntityMemberModel { Key = "app:2", Name = "CodeHelper", Seconds = 100 }
                ]
            },
            new EntityModel
            {
                Key = "site:5",
                Kind = "site",
                Name = "GitHub",
                Seconds = 300
            }
        };

        var highlights = SearchService.BuildEntityHighlightItems(rows, appById, siteById, max: 8);

        Assert.Equal(2, highlights.Count);
        Assert.Equal(SearchResultType.App, highlights[0].Type);
        Assert.Equal("VS Code Suite", highlights[0].DisplayName);
        Assert.Contains("2", highlights[0].SecondaryText);

        Assert.Equal(SearchResultType.WebSite, highlights[1].Type);
        Assert.Equal(5, highlights[1].Id);
        Assert.Equal("github.com", highlights[1].SecondaryText);
    }
}
