using System;
using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Controls.Charts.Model;
using ShiJian.Client.Shared.Librarys;
using ShiJian.Client.Shared.Models;
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
}
