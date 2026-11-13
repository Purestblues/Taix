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
            if (isWeb ? row.Kind != "site" : row.Kind == "site") continue;

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

    public static string FormatEntityLoadError(Exception ex) =>
        ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
            ? ResourceStrings.ServerOutdated
            : ResourceStrings.EntityLoadFailed;
}
