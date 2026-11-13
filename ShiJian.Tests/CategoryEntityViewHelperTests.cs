using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Librarys;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Entity;
using ShiJian.Client.Shared.Models.Web;
using Xunit;

namespace ShiJian.Tests;

public class CategoryEntityViewHelperTests
{
    [Fact]
    public void BuildAppRows_FoldsGroups_MapsTodaySeconds_AndHandlesMixedGroups()
    {
        var devCat = new CategoryModel { ID = 10, Name = "开发", Color = "#2563EB" };
        var sysCat = new CategoryModel { ID = 1, Name = "系统默认", Color = "#8A8A8A", IsSystem = true };
        var categories = new[] { devCat, sysCat };

        var apps = new[]
        {
            new AppModel { ID = 1, Name = "Code", Description = "VS Code", CategoryID = 10, TotalTime = 1000 },
            new AppModel { ID = 2, Name = "CodeHelper", Description = "VS Code Helper", CategoryID = 10, TotalTime = 200 },
            new AppModel { ID = 3, Name = "FigmaDesktop", CategoryID = 0, TotalTime = 300 },
            new AppModel { ID = 4, Name = "Notepad", CategoryID = 1, TotalTime = 150 }
        };

        var groups = new[]
        {
            // 应用合并组：母成员为 app:1，折叠 app:2
            new AppMergeGroup
            {
                Key = "g_code",
                Kind = "app",
                Name = "VS Code Suite",
                PrimaryAppId = 1,
                MemberAppIds = [1, 2]
            },
            // 跨源合并组且母成员为网站：app:3 归网站侧管理，不再出现在应用明细里
            new AppMergeGroup
            {
                Key = "g_figma",
                Kind = "mixed",
                Name = "Figma",
                PrimarySiteId = 9,
                PrimaryAppId = 0,
                MemberAppIds = [3],
                MemberSiteIds = [9]
            }
        };

        var todayEntities = new EntityListModel
        {
            Rows =
            [
                new EntityModel { Key = "app:1", Kind = "app", Seconds = 600 },
                new EntityModel { Key = "app:4", Kind = "app", Seconds = 90 }
            ]
        };

        var rows = CategoryEntityViewHelper.BuildAppRows(apps, categories, groups, todayEntities);

        Assert.Equal(2, rows.Count);
        var vscode = Assert.Single(rows, r => r.PrimaryId == 1);
        Assert.Equal("VS Code Suite", vscode.Name);
        Assert.Equal(1200, vscode.TotalSeconds);
        Assert.Equal(600, vscode.TodaySeconds);
        Assert.Equal(2, vscode.MemberCount);
        Assert.Contains("VS Code Helper", vscode.MemberTooltip);
        Assert.False(vscode.IsUncategorized);
        Assert.Equal("开发", vscode.CategoryName);

        var notepad = Assert.Single(rows, r => r.PrimaryId == 4);
        Assert.True(notepad.IsUncategorized);
        Assert.Equal("系统默认", notepad.CategoryName);

        Assert.DoesNotContain(rows, r => r.PrimaryId == 2);
        Assert.DoesNotContain(rows, r => r.PrimaryId == 3);
    }

    [Fact]
    public void BuildSiteRows_IncludesMixedGroupWhenSiteIsPrimary()
    {
        var readCat = new WebSiteCategoryModel { ID = 20, Name = "阅读", Color = "#10B981" };
        var sites = new[]
        {
            new WebSiteModel { ID = 9, Title = "Figma Web", Domain = "figma.com", CategoryID = 20, Duration = 400 },
            new WebSiteModel { ID = 10, Title = "Docs", Domain = "docs.figma.com", CategoryID = 0, Duration = 100 }
        };
        var groups = new[]
        {
            new AppMergeGroup
            {
                Key = "g_figma",
                Kind = "mixed",
                Name = "Figma",
                PrimarySiteId = 9,
                MemberSiteIds = [9, 10],
                MemberAppIds = [3]
            }
        };
        var todayEntities = new EntityListModel
        {
            Rows = [new EntityModel { Key = "site:9", Kind = "mixed", Seconds = 250 }]
        };

        var rows = CategoryEntityViewHelper.BuildSiteRows(sites, [readCat], groups, todayEntities);

        var row = Assert.Single(rows);
        Assert.True(row.IsSite);
        Assert.Equal(9, row.PrimaryId);
        Assert.Equal("Figma", row.Name);
        Assert.Equal(500, row.TotalSeconds);
        Assert.Equal(250, row.TodaySeconds);
        Assert.Equal(2, row.MemberCount);
    }

    [Fact]
    public void FilterAndSort_SupportsUncategorizedFilter_AliasSearch_AndAllSortModes()
    {
        var rows = new[]
        {
            new ShiJian.Client.Models.CategoryEntityRow
            {
                PrimaryId = 1,
                Name = "Visual Studio Code",
                Subtitle = "code.exe",
                TodaySeconds = 120,
                TotalSeconds = 5000,
                IsUncategorized = false
            },
            new ShiJian.Client.Models.CategoryEntityRow
            {
                PrimaryId = 2,
                Name = "Alpha Tool",
                Subtitle = "alpha.exe",
                TodaySeconds = 300,
                TotalSeconds = 400,
                IsUncategorized = true
            },
            new ShiJian.Client.Models.CategoryEntityRow
            {
                PrimaryId = 3,
                Name = "Beta Browser",
                Subtitle = "beta.exe",
                MemberTooltip = "betahelper.exe",
                TodaySeconds = 50,
                TotalSeconds = 900,
                IsUncategorized = false
            }
        };

        // 仅未分类
        var uncategorizedOnly = CategoryEntityViewHelper.FilterAndSort(
            rows, null, EntityFilterMode.Uncategorized, EntitySortMode.TodayDuration);
        Assert.Equal([2], uncategorizedOnly.Select(r => r.PrimaryId));

        // 别名触发（vscode -> Visual Studio Code）
        var byAlias = CategoryEntityViewHelper.FilterAndSort(
            rows, "vscode", EntityFilterMode.All, EntitySortMode.TodayDuration);
        Assert.Equal([1], byAlias.Select(r => r.PrimaryId));

        // 合并子成员提示匹配
        var byMember = CategoryEntityViewHelper.FilterAndSort(
            rows, "betahelper", EntityFilterMode.All, EntitySortMode.TodayDuration);
        Assert.Equal([3], byMember.Select(r => r.PrimaryId));

        // 四种排序模式
        Assert.Equal([2, 1, 3], CategoryEntityViewHelper.FilterAndSort(rows, null, EntityFilterMode.All, EntitySortMode.TodayDuration).Select(r => r.PrimaryId));
        Assert.Equal([1, 3, 2], CategoryEntityViewHelper.FilterAndSort(rows, null, EntityFilterMode.All, EntitySortMode.TotalDuration).Select(r => r.PrimaryId));
        Assert.Equal([2, 3, 1], CategoryEntityViewHelper.FilterAndSort(rows, null, EntityFilterMode.All, EntitySortMode.Name).Select(r => r.PrimaryId));
        Assert.Equal([2, 1, 3], CategoryEntityViewHelper.FilterAndSort(rows, null, EntityFilterMode.All, EntitySortMode.UncategorizedFirst).Select(r => r.PrimaryId));
    }
}
