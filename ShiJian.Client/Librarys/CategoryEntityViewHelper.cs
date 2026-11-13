using System;
using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Models;
using ShiJian.Client.Shared.Librarys;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Entity;
using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Librarys;

/// <summary>分类页实体视图筛选口径（N5）</summary>
public enum EntityFilterMode
{
    All = 0,
    Uncategorized = 1
}

/// <summary>分类页实体视图排序口径（N5）</summary>
public enum EntitySortMode
{
    TodayDuration = 0,
    TotalDuration = 1,
    Name = 2,
    UncategorizedFirst = 3
}

/// <summary>分类页快速打标签选项（供浮层共享列表渲染）</summary>
public sealed class CategoryTagOption
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = "#8A8A8A";
    public string? Icon { get; init; }
    public bool IsSystem { get; init; }
    public bool IsSelected { get; init; }
}

/// <summary>
/// N5 分类页实体视图的纯计算：组装应用/网站（含合并组折叠）明细行、过滤与排序。
/// 抽出纯函数便于直接单元测试，不依赖 Avalonia UI。
/// </summary>
public static class CategoryEntityViewHelper
{
    private static readonly Dictionary<string, string> AliasHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vscode"] = "Visual Studio Code",
        ["ps"] = "Photoshop",
    };

    public static List<CategoryEntityRow> BuildAppRows(
        IReadOnlyCollection<AppModel> apps,
        IReadOnlyList<CategoryModel> categories,
        IReadOnlyList<AppMergeGroup> groups,
        EntityListModel? todayEntities,
        string fallbackUncategorizedName = "未分类")
    {
        var categoryMap = categories.GroupBy(c => c.ID).ToDictionary(g => g.Key, g => g.First());
        var systemCategory = categories.FirstOrDefault(c => c.IsSystem);
        var todayById = BuildTodaySecondsMap(todayEntities, isSite: false);
        var appById = apps.GroupBy(a => a.ID).ToDictionary(g => g.Key, g => g.First());

        var rows = new List<CategoryEntityRow>(apps.Count);
        var consumedAppIds = new HashSet<int>();

        foreach (var group in groups)
        {
            if (group.MemberAppIds.Count == 0) continue;

            // 跨源混合组若母成员是网站，归在网站页签管理；但其应用子成员不再重复散落在应用列表里
            if (group.PrimarySiteId > 0 && group.PrimaryAppId == 0)
            {
                foreach (var appId in group.MemberAppIds) consumedAppIds.Add(appId);
                continue;
            }

            var members = group.MemberAppIds
                .Where(appById.ContainsKey)
                .Select(id => appById[id])
                .ToList();
            if (members.Count == 0) continue;

            foreach (var app in members) consumedAppIds.Add(app.ID);

            var primary = (group.PrimaryAppId > 0 && appById.TryGetValue(group.PrimaryAppId, out var p))
                ? p
                : members.OrderByDescending(m => m.TotalTime).First();

            var todaySec = todayById.TryGetValue(primary.ID, out var entitySec)
                ? entitySec
                : members.Sum(m => todayById.GetValueOrDefault(m.ID));

            var totalSec = members.Sum(m => m.TotalTime);
            var (catId, catName, catColor, isUncat) = ResolveCategory(
                primary.CategoryID, categoryMap, systemCategory, fallbackUncategorizedName);

            rows.Add(new CategoryEntityRow
            {
                IsSite = false,
                PrimaryId = primary.ID,
                Name = !string.IsNullOrWhiteSpace(group.Name) ? group.Name : AppMergePlanner.AppDisplayName(primary),
                Subtitle = primary.Name ?? string.Empty,
                Icon = primary.IconFile,
                TodaySeconds = todaySec,
                TotalSeconds = totalSec,
                MemberCount = Math.Max(group.MemberNames.Count, members.Count),
                MemberTooltip = group.MemberNames.Count > 1
                    ? string.Join(" / ", group.MemberNames)
                    : members.Count > 1
                        ? string.Join(" / ", members.Select(AppMergePlanner.AppDisplayName))
                        : null,
                CategoryId = catId,
                CategoryName = catName,
                CategoryColor = catColor,
                IsUncategorized = isUncat
            });
        }

        foreach (var app in apps)
        {
            if (consumedAppIds.Contains(app.ID)) continue;
            var (catId, catName, catColor, isUncat) = ResolveCategory(
                app.CategoryID, categoryMap, systemCategory, fallbackUncategorizedName);

            rows.Add(new CategoryEntityRow
            {
                IsSite = false,
                PrimaryId = app.ID,
                Name = AppMergePlanner.AppDisplayName(app),
                Subtitle = app.Name ?? string.Empty,
                Icon = app.IconFile,
                TodaySeconds = todayById.GetValueOrDefault(app.ID),
                TotalSeconds = app.TotalTime,
                MemberCount = 1,
                CategoryId = catId,
                CategoryName = catName,
                CategoryColor = catColor,
                IsUncategorized = isUncat
            });
        }

        return rows;
    }

    public static List<CategoryEntityRow> BuildSiteRows(
        IReadOnlyCollection<WebSiteModel> sites,
        IReadOnlyList<CategoryModel> categories,
        IReadOnlyList<AppMergeGroup> groups,
        EntityListModel? todayEntities,
        string fallbackUncategorizedName = "未分类")
    {
        var categoryMap = categories.GroupBy(c => c.ID).ToDictionary(g => g.Key, g => g.First());
        var systemCategory = categories.FirstOrDefault(c => c.IsSystem);
        return BuildSiteRowsCore(
            sites,
            catId => ResolveCategory(catId, categoryMap, systemCategory, fallbackUncategorizedName),
            groups,
            todayEntities);
    }

    public static List<CategoryEntityRow> BuildSiteRows(
        IReadOnlyCollection<WebSiteModel> sites,
        IReadOnlyList<WebSiteCategoryModel> categories,
        IReadOnlyList<AppMergeGroup> groups,
        EntityListModel? todayEntities,
        string fallbackUncategorizedName = "未分类")
    {
        var categoryMap = categories.GroupBy(c => c.ID).ToDictionary(g => g.Key, g => g.First());
        var systemCategory = categories.FirstOrDefault(c => c.IsSystem);
        return BuildSiteRowsCore(
            sites,
            catId => ResolveCategory(catId, categoryMap, systemCategory, fallbackUncategorizedName),
            groups,
            todayEntities);
    }

    private static List<CategoryEntityRow> BuildSiteRowsCore(
        IReadOnlyCollection<WebSiteModel> sites,
        Func<int, (int Id, string Name, string Color, bool IsUncategorized)> resolveCategory,
        IReadOnlyList<AppMergeGroup> groups,
        EntityListModel? todayEntities)
    {
        var todayById = BuildTodaySecondsMap(todayEntities, isSite: true);
        var siteById = sites.GroupBy(s => s.ID).ToDictionary(g => g.Key, g => g.First());

        var rows = new List<CategoryEntityRow>(sites.Count);
        var consumedSiteIds = new HashSet<int>();

        foreach (var group in groups)
        {
            if (group.MemberSiteIds.Count == 0) continue;

            // 若混合组以应用为母成员，则在应用页签展示，网站子成员从独立网站列表折叠隐藏
            if (group.PrimaryAppId > 0 && group.PrimarySiteId == 0)
            {
                foreach (var siteId in group.MemberSiteIds) consumedSiteIds.Add(siteId);
                continue;
            }

            var members = group.MemberSiteIds
                .Where(siteById.ContainsKey)
                .Select(id => siteById[id])
                .ToList();
            if (members.Count == 0) continue;

            foreach (var site in members) consumedSiteIds.Add(site.ID);

            var primary = (group.PrimarySiteId > 0 && siteById.TryGetValue(group.PrimarySiteId, out var p))
                ? p
                : members.OrderByDescending(m => m.Duration).First();

            var todaySec = todayById.TryGetValue(primary.ID, out var entitySec)
                ? entitySec
                : members.Sum(m => todayById.GetValueOrDefault(m.ID));

            var totalSec = members.Sum(m => m.Duration);
            var (catId, catName, catColor, isUncat) = resolveCategory(primary.CategoryID);

            rows.Add(new CategoryEntityRow
            {
                IsSite = true,
                PrimaryId = primary.ID,
                Name = !string.IsNullOrWhiteSpace(group.Name) ? group.Name : AppMergePlanner.SiteDisplayName(primary),
                Subtitle = primary.Domain ?? string.Empty,
                Icon = primary.IconFile,
                TodaySeconds = todaySec,
                TotalSeconds = totalSec,
                MemberCount = Math.Max(group.MemberNames.Count, members.Count),
                MemberTooltip = group.MemberNames.Count > 1
                    ? string.Join(" / ", group.MemberNames)
                    : members.Count > 1
                        ? string.Join(" / ", members.Select(AppMergePlanner.SiteDisplayName))
                        : null,
                CategoryId = catId,
                CategoryName = catName,
                CategoryColor = catColor,
                IsUncategorized = isUncat
            });
        }

        foreach (var site in sites)
        {
            if (consumedSiteIds.Contains(site.ID)) continue;
            var (catId, catName, catColor, isUncat) = resolveCategory(site.CategoryID);

            rows.Add(new CategoryEntityRow
            {
                IsSite = true,
                PrimaryId = site.ID,
                Name = AppMergePlanner.SiteDisplayName(site),
                Subtitle = site.Domain ?? string.Empty,
                Icon = site.IconFile,
                TodaySeconds = todayById.GetValueOrDefault(site.ID),
                TotalSeconds = site.Duration,
                MemberCount = 1,
                CategoryId = catId,
                CategoryName = catName,
                CategoryColor = catColor,
                IsUncategorized = isUncat
            });
        }

        return rows;
    }

    public static List<CategoryEntityRow> FilterAndSort(
        IEnumerable<CategoryEntityRow> rows,
        string? keyword,
        EntityFilterMode filterMode,
        EntitySortMode sortMode)
    {
        var query = (keyword ?? string.Empty).Trim();
        var aliased = !string.IsNullOrEmpty(query) && AliasHints.TryGetValue(query, out var mapped)
            ? mapped
            : null;

        IEnumerable<CategoryEntityRow> filtered = rows;
        if (filterMode == EntityFilterMode.Uncategorized)
        {
            filtered = filtered.Where(r => r.IsUncategorized);
        }

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(r => Matches(r, query) || (aliased != null && Matches(r, aliased)));
        }

        return sortMode switch
        {
            EntitySortMode.TotalDuration => filtered
                .OrderByDescending(r => r.TotalSeconds)
                .ThenByDescending(r => r.TodaySeconds)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            EntitySortMode.Name => filtered
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(r => r.TotalSeconds)
                .ToList(),
            EntitySortMode.UncategorizedFirst => filtered
                .OrderByDescending(r => r.IsUncategorized)
                .ThenByDescending(r => r.TodaySeconds)
                .ThenByDescending(r => r.TotalSeconds)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            _ => filtered
                .OrderByDescending(r => r.TodaySeconds)
                .ThenByDescending(r => r.TotalSeconds)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    private static bool Matches(CategoryEntityRow row, string keyword)
    {
        if (row.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        if (row.Subtitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        if (row.CategoryName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(row.MemberTooltip) &&
            row.MemberTooltip.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        return WildcardHelper.IsMatch(row.Name, keyword) || WildcardHelper.IsMatch(row.Subtitle, keyword);
    }

    private static Dictionary<int, int> BuildTodaySecondsMap(EntityListModel? entityList, bool isSite)
    {
        var dict = new Dictionary<int, int>();
        if (entityList?.Rows == null) return dict;

        foreach (var row in entityList.Rows)
        {
            if (row.Kind == "aggregate" || row.EntityId <= 0 || row.Seconds <= 0) continue;
            var rowIsSite = row.Kind == "site" || row.Key.StartsWith("site:", StringComparison.Ordinal);
            if (rowIsSite != isSite) continue;

            dict[row.EntityId] = dict.TryGetValue(row.EntityId, out var cur) ? cur + row.Seconds : row.Seconds;
        }
        return dict;
    }

    private static (int Id, string Name, string Color, bool IsUncategorized) ResolveCategory(
        int categoryId,
        Dictionary<int, CategoryModel> map,
        CategoryModel? systemCategory,
        string fallbackUncategorizedName)
    {
        if (categoryId > 0 && map.TryGetValue(categoryId, out var cat))
        {
            var color = string.IsNullOrWhiteSpace(cat.Color) ? "#8A8A8A" : cat.Color!;
            return (cat.ID, cat.Name ?? fallbackUncategorizedName, color, cat.IsSystem);
        }
        if (systemCategory != null)
        {
            var color = string.IsNullOrWhiteSpace(systemCategory.Color) ? "#8A8A8A" : systemCategory.Color!;
            return (systemCategory.ID, systemCategory.Name ?? fallbackUncategorizedName, color, true);
        }
        return (0, fallbackUncategorizedName, "#8A8A8A", true);
    }

    private static (int Id, string Name, string Color, bool IsUncategorized) ResolveCategory(
        int categoryId,
        Dictionary<int, WebSiteCategoryModel> map,
        WebSiteCategoryModel? systemCategory,
        string fallbackUncategorizedName)
    {
        if (categoryId > 0 && map.TryGetValue(categoryId, out var cat))
        {
            var color = string.IsNullOrWhiteSpace(cat.Color) ? "#8A8A8A" : cat.Color!;
            return (cat.ID, cat.Name ?? fallbackUncategorizedName, color, cat.IsSystem);
        }
        if (systemCategory != null)
        {
            var color = string.IsNullOrWhiteSpace(systemCategory.Color) ? "#8A8A8A" : systemCategory.Color!;
            return (systemCategory.ID, systemCategory.Name ?? fallbackUncategorizedName, color, true);
        }
        return (0, fallbackUncategorizedName, "#8A8A8A", true);
    }
}
