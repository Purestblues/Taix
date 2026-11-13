using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using ShiJian.Client.Controls.Charts.Model;
using ShiJian.Client.Shared.Librarys;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Data;
using ShiJian.Client.Shared.Models.Entity;
using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Librarys;

public static class ChartDataMapper
{
    public static List<ChartsDataModel> MapFromDailyLogs(IEnumerable<DailyLogModel> logs, AppModel? app = null, bool includeBadges = false, bool orderByValue = true)
    {
        var result = new List<ChartsDataModel>();
        foreach (var item in logs)
        {
            var appModel = item.AppModel ?? app;
            if (appModel == null) continue;

            var category = appModel.Category;
            var categoryColor = category == null
                ? null
                : ChartColorHelper.Resolve(category.Color, category.IsSystem);

            var model = new ChartsDataModel
            {
                Data = item,
                Name = appModel.GetDisplayName(),
                Value = item.Time,
                Tag = Time.ToString(item.Time),
                PopupText = appModel.File,
                Icon = appModel.IconFile,
                FallbackText = appModel.GetDisplayName(),
                CategoryName = category?.Name,
                CategoryColor = categoryColor,
                DateTime = item.Date
            };

            if (includeBadges)
            {
                model.BadgeList = new List<ChartBadgeModel>();
                if (category != null)
                    model.BadgeList.Add(new ChartBadgeModel
                    {
                        Name = category.Name,
                        Color = ChartColorHelper.WithAlpha(categoryColor, 0x2E),
                        TextColor = categoryColor!,
                        CategoryId = category.ID,
                        IsWebCategory = false,
                        Type = ChartBadgeType.Category
                    });
            }

            result.Add(model);
        }
        return orderByValue ? result.OrderByDescending(x => x.Value).ToList() : result;
    }

    public static List<ChartsDataModel> MapFromWebSites(IEnumerable<WebSiteModel> sites, bool includeBadges = false)
    {
        var result = new List<ChartsDataModel>();
        foreach (var item in sites)
        {
            var category = item.Category;
            var categoryColor = category == null
                ? null
                : ChartColorHelper.Resolve(category.Color, category.IsSystem);

            var model = new ChartsDataModel
            {
                Data = item,
                Name = !string.IsNullOrEmpty(item.Alias) ? item.Alias : item.Title,
                Value = item.Duration,
                Tag = Time.ToString(item.Duration),
                PopupText = item.Domain,
                Icon = item.IconFile,
                FallbackText = item.Domain,
                CategoryName = category?.Name,
                CategoryColor = categoryColor
            };

            if (includeBadges)
            {
                model.BadgeList = new List<ChartBadgeModel>();
                if (category != null)
                    model.BadgeList.Add(new ChartBadgeModel
                    {
                        Name = category.Name,
                        Color = ChartColorHelper.WithAlpha(categoryColor, 0x2E),
                        TextColor = categoryColor!,
                        CategoryId = category.ID,
                        IsWebCategory = true,
                        Type = ChartBadgeType.Category
                    });
            }

            result.Add(model);
        }
        return result.OrderByDescending(x => x.Value).ToList();
    }

    public static List<ChartsDataModel> MapFromHoursLogs(IEnumerable<HoursLogModel> logs, bool includeBadges = false)
    {
        var result = new List<ChartsDataModel>();
        foreach (var item in logs)
        {
            var appModel = item.AppModel;
            if (appModel == null) continue;

            var category = appModel.Category;
            var categoryColor = category == null
                ? null
                : ChartColorHelper.Resolve(category.Color, category.IsSystem);

            var model = new ChartsDataModel
            {
                Data = item,
                Name = appModel.GetDisplayName(),
                Value = item.Time,
                Tag = Time.ToString(item.Time),
                PopupText = appModel.File,
                Icon = appModel.IconFile,
                FallbackText = appModel.GetDisplayName(),
                CategoryName = category?.Name,
                CategoryColor = categoryColor,
                DateTime = DateTime.SpecifyKind(item.DataTime, DateTimeKind.Utc).ToLocalTime()
            };

            if (includeBadges)
            {
                model.BadgeList = new List<ChartBadgeModel>();
                if (category != null)
                    model.BadgeList.Add(new ChartBadgeModel
                    {
                        Name = category.Name,
                        Color = ChartColorHelper.WithAlpha(categoryColor, 0x2E),
                        TextColor = categoryColor!,
                        CategoryId = category.ID,
                        IsWebCategory = false,
                        Type = ChartBadgeType.Category
                    });
            }

            result.Add(model);
        }
        return result.OrderByDescending(x => x.Value).ToList();
    }

    /// <summary>统一实体行 → 榜单/卡片模型；分类徽章与合并成员说明都挂在这里</summary>
    public static ChartsDataModel MapFromEntity(EntityModel entity, bool includeBadges = true)
    {
        var categoryColor = entity.CategoryColor == null
            ? null
            : ChartColorHelper.Resolve(entity.CategoryColor, false);

        var model = new ChartsDataModel
        {
            Data = entity,
            Name = entity.Name,
            Value = entity.Seconds,
            Tag = Time.ToString(entity.Seconds),
            Icon = entity.Icon ?? string.Empty,
            FallbackText = entity.Name,
            CategoryName = entity.CategoryName,
            CategoryColor = categoryColor,
            Color = categoryColor ?? string.Empty
        };

        if (entity.Members.Count > 0)
        {
            var memberText = string.Join(" / ", entity.Members.Select(member => member.Name));
            model.PopupText = entity.Kind == "other"
                ? string.Empty
                : string.Format(ResourceStrings.MergedItems, entity.Members.Count) + "：" + memberText;
        }

        if (includeBadges)
        {
            model.BadgeList = [];
            if (!string.IsNullOrWhiteSpace(entity.CategoryName) && entity.CategoryId > 0)
            {
                model.BadgeList.Add(new ChartBadgeModel
                {
                    Name = entity.CategoryName!,
                    Color = ChartColorHelper.WithAlpha(categoryColor, 0x2E),
                    TextColor = categoryColor!,
                    CategoryId = entity.CategoryId,
                    IsWebCategory = entity.CategoryKind == "site",
                    Type = ChartBadgeType.Category
                });
            }
        }

        return model;
    }

    public static List<ChartsDataModel> MapFromEntities(IEnumerable<EntityModel> entities, bool includeBadges = true) =>
        entities
            .Select(entity => MapFromEntity(entity, includeBadges))
            .OrderByDescending(x => x.Value)
            .ToList();

    /// <summary>把「实体 × 时段」矩阵按分类合并成「分类 × 时段」列集合（应用侧与网站侧各自按 Kind 切片）</summary>
    public static List<ColumnDataModel> AggregateColumnsByCategory(EntityHoursModel? hours, bool isWeb)
    {
        if (hours == null || hours.Rows.Count == 0) return [];

        var bucketCount = hours.BucketCount > 0
            ? hours.BucketCount
            : hours.Rows.Max(r => r.Values?.Length ?? 0);
        if (bucketCount <= 0) return [];

        var byCategory = new Dictionary<int, double[]>();
        foreach (var row in hours.Rows)
        {
            var isSiteCategory = row.Kind == "site" || row.CategoryKind == "site";
            if (isWeb != isSiteCategory) continue;

            if (!byCategory.TryGetValue(row.CategoryId, out var sums))
            {
                sums = new double[bucketCount];
                byCategory[row.CategoryId] = sums;
            }

            var values = row.Values;
            if (values == null) continue;
            var limit = Math.Min(values.Length, bucketCount);
            for (var i = 0; i < limit; i++)
                sums[i] += values[i];
        }

        return byCategory
            .Select(kvp => new ColumnDataModel
            {
                CategoryID = kvp.Key,
                Values = kvp.Value
            })
            .ToList();
    }

    public readonly record struct CategoryVisualInfo(string Name, string? Icon, string? Color, bool IsSystem);

    public static Dictionary<int, CategoryVisualInfo> ToVisualMap(
        IReadOnlyList<CategoryModel>? categories,
        string fallbackName)
    {
        var map = new Dictionary<int, CategoryVisualInfo>();
        if (categories == null) return map;
        foreach (var item in categories)
        {
            map[item.ID] = new CategoryVisualInfo(
                item.Name ?? fallbackName,
                item.IconFile,
                item.Color,
                item.IsSystem);
        }
        return map;
    }

    public static Dictionary<int, CategoryVisualInfo> ToVisualMap(
        IReadOnlyList<WebSiteCategoryModel>? categories,
        string fallbackName)
    {
        var map = new Dictionary<int, CategoryVisualInfo>();
        if (categories == null) return map;
        foreach (var item in categories)
        {
            map[item.ID] = new CategoryVisualInfo(
                item.Name ?? fallbackName,
                item.IconFile,
                item.Color,
                item.IsSystem);
        }
        return map;
    }

    /// <summary>
    /// 统一聚合实体矩阵中的所有分类（应用与网站共享统一分类 ID 空间）：同一非系统分类合并为同一序列，
    /// 未分类/系统分类合并成同一项。
    /// </summary>
    public static List<ChartsDataModel> BuildUnifiedCategorySeries(
        EntityHoursModel? hours,
        IReadOnlyDictionary<int, CategoryVisualInfo> categories,
        string fallbackUncategorizedName,
        string[]? columnNames = null)
    {
        return BuildUnifiedCategorySeriesCore(
            hours,
            row => (categories, row.CategoryId.ToString()),
            fallbackUncategorizedName,
            columnNames);
    }

    /// <summary>
    /// 兼容旧双表测试的分类聚合入口（当应用与网站分别传入独立视觉映射时分别分组）。
    /// </summary>
    public static List<ChartsDataModel> BuildUnifiedCategorySeries(
        EntityHoursModel? hours,
        IReadOnlyDictionary<int, CategoryVisualInfo> appCategories,
        IReadOnlyDictionary<int, CategoryVisualInfo> siteCategories,
        string fallbackUncategorizedName,
        string[]? columnNames = null)
    {
        return BuildUnifiedCategorySeriesCore(
            hours,
            row =>
            {
                var isSite = row.Kind == "site" || row.CategoryKind == "site";
                return (isSite ? siteCategories : appCategories, $"{(isSite ? "site" : "app")}:{row.CategoryId}");
            },
            fallbackUncategorizedName,
            columnNames);
    }

    private static List<ChartsDataModel> BuildUnifiedCategorySeriesCore(
        EntityHoursModel? hours,
        Func<EntityHoursRow, (IReadOnlyDictionary<int, CategoryVisualInfo> Map, string Key)> resolver,
        string fallbackUncategorizedName,
        string[]? columnNames)
    {
        if (hours == null || hours.Rows.Count == 0) return [];

        var bucketCount = hours.BucketCount > 0
            ? hours.BucketCount
            : hours.Rows.Max(r => r.Values?.Length ?? 0);
        if (bucketCount <= 0) return [];

        var buckets = new Dictionary<string, (CategoryVisualInfo Visual, double[] Sums)>(StringComparer.Ordinal);
        foreach (var row in hours.Rows)
        {
            var (map, rowBucketKey) = resolver(row);
            CategoryVisualInfo found = default;
            var hasVisual = row.CategoryId > 0 && map.TryGetValue(row.CategoryId, out found);
            var isUncategorized = !hasVisual || found.IsSystem;

            var bucketKey = isUncategorized ? "__uncategorized__" : rowBucketKey;

            if (!buckets.TryGetValue(bucketKey, out var entry))
            {
                var visual = isUncategorized
                    ? new CategoryVisualInfo(
                        hasVisual ? found.Name : fallbackUncategorizedName,
                        hasVisual ? found.Icon : null,
                        null,
                        true)
                    : found;
                entry = (visual, new double[bucketCount]);
                buckets[bucketKey] = entry;
            }

            var values = row.Values;
            if (values == null) continue;
            var limit = Math.Min(values.Length, bucketCount);
            for (var i = 0; i < limit; i++)
                entry.Sums[i] += values[i];
        }

        return buckets.Values
            .Select(entry =>
            {
                var total = entry.Sums.Sum();
                return new ChartsDataModel
                {
                    Name = entry.Visual.Name,
                    Icon = entry.Visual.Icon,
                    Color = ChartColorHelper.Resolve(entry.Visual.Color, entry.Visual.IsSystem),
                    Values = entry.Sums,
                    Value = total,
                    Tag = Time.ToString((int)total),
                    ColumnNames = columnNames ?? []
                };
            })
            .Where(m => m.Value > 0)
            .OrderByDescending(m => m.Value)
            .ToList();
    }

    /// <summary>按时段把统一实体矩阵的所有行加总（供总时段柱图与周期对比柱使用）</summary>
    public static double[] SumAllBuckets(EntityHoursModel? hours, int expectedLength = 0)
    {
        var maxRowLen = hours?.Rows.Count > 0 ? hours.Rows.Max(r => r.Values?.Length ?? 0) : 0;
        var count = Math.Max(expectedLength, Math.Max(hours?.BucketCount ?? 0, maxRowLen));
        if (count <= 0) return [];

        var totals = new double[count];
        if (hours == null) return totals;

        foreach (var row in hours.Rows)
        {
            var values = row.Values;
            if (values == null) continue;
            var limit = Math.Min(values.Length, count);
            for (var i = 0; i < limit; i++)
                totals[i] += values[i];
        }
        return totals;
    }

    public static string FormatEntityLoadError(Exception ex) =>
        ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
            ? ResourceStrings.ServerOutdated
            : ResourceStrings.EntityLoadFailed;
}
