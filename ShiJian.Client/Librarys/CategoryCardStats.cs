using System;
using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Models.Category;
using ShiJian.Client.Shared.Models.Data;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 分类卡片上「时长 / 占比」的纯计算：把服务端的「分类 × 时段」矩阵压成每张卡的统计量。
///
/// 单独抽出来是为了能直接跑单测——分类页的手工验证成本太高（要起服务端 + 切明暗主题），
/// 而这一段只是算术，出错的后果是「占比之和不是 100%」「0 时长的分类混在前面」。
/// </summary>
public static class CategoryCardStats
{
    /// <summary>把「分类 × 时段」矩阵按分类求和，跳过空行与全 0 行</summary>
    public static Dictionary<int, double> SumByCategory(IEnumerable<ColumnDataModel>? columns)
    {
        var map = new Dictionary<int, double>();
        if (columns == null) return map;

        foreach (var column in columns)
        {
            var sum = column.Values?.Sum() ?? 0;
            if (sum <= 0) continue;
            map[column.CategoryID] = map.GetValueOrDefault(column.CategoryID) + sum;
        }

        return map;
    }

    /// <summary>按分类 ID 把时长与占比写进卡片；总时长为 0 时占比一律 0（不能除零）</summary>
    public static void Assign<T>(IEnumerable<T> cards, Func<T, int> categoryId,
        Dictionary<int, double> durationByCategory) where T : CategoryCardModel
    {
        var list = cards.ToList();
        var total = durationByCategory.Values.Sum();

        foreach (var card in list)
        {
            var duration = durationByCategory.GetValueOrDefault(categoryId(card));
            card.Duration = duration;
            card.Ratio = total > 0 ? duration / total : 0;
        }
    }

    /// <summary>
    /// 有数据的排前面（按时长降序），没数据的收在后面按名称排。
    /// 否则一堆 0 时长的分类会把真正在用的挤到需要滚动才能看到的位置。
    /// </summary>
    public static List<T> Order<T>(IEnumerable<T> cards, Func<T, string?> name) where T : CategoryCardModel
        => cards
            .OrderByDescending(m => m.HasDuration)
            .ThenByDescending(m => m.Duration)
            .ThenBy(m => name(m) ?? string.Empty, StringComparer.CurrentCulture)
            .ToList();
}
