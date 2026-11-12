using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Librarys;
using ShiJian.Client.Models.Category;
using ShiJian.Client.Shared.Models.Data;
using Xunit;

namespace ShiJian.Tests;

/// <summary>
/// N7-a 分类卡片上的时长 / 占比算术。
/// 这些数字出错的表现很隐蔽：占比之和不是 100%、0 时长的分类混在第一屏、总时长为 0 时除零崩掉。
/// </summary>
public class CategoryCardStatsTests
{
    private static ColumnDataModel Column(int categoryId, params double[] values) =>
        new() { CategoryID = categoryId, Values = values };

    private static CategoryModel Card(int id) =>
        new() { Data = new ShiJian.Client.Shared.Models.CategoryModel { ID = id, Name = "cat" + id } };

    [Fact]
    public void SumByCategory_MergesRowsAndSkipsEmpty()
    {
        var map = CategoryCardStats.SumByCategory(
        [
            Column(1, 10, 20),
            Column(1, 5),          // 同一分类的多行要相加
            Column(2, 0, 0),       // 全 0 的行不该占位
            Column(3)              // 没有 Values 的行也不能炸
        ]);

        Assert.Equal(35, map[1]);
        Assert.False(map.ContainsKey(2));
        Assert.False(map.ContainsKey(3));
    }

    [Fact]
    public void SumByCategory_NullInput_ReturnsEmpty()
    {
        Assert.Empty(CategoryCardStats.SumByCategory(null));
    }

    [Fact]
    public void Assign_WritesDurationAndRatio()
    {
        var cards = new List<CategoryModel> { Card(1), Card(2) };
        var map = new Dictionary<int, double> { [1] = 30, [2] = 10 };

        CategoryCardStats.Assign(cards, m => m.Data.ID, map);

        Assert.Equal(30, cards[0].Duration);
        Assert.Equal(0.75, cards[0].Ratio, 3);
        Assert.Equal(10, cards[1].Duration);
        Assert.Equal(0.25, cards[1].Ratio, 3);
    }

    [Fact]
    public void Assign_WhenTotalIsZero_DoesNotDivideByZero()
    {
        var cards = new List<CategoryModel> { Card(1) };
        CategoryCardStats.Assign(cards, m => m.Data.ID, new Dictionary<int, double>());

        Assert.Equal(0, cards[0].Duration);
        Assert.Equal(0, cards[0].Ratio);
        Assert.False(cards[0].HasDuration);
    }

    [Fact]
    public void Order_PutsCategoriesWithDataFirst()
    {
        var shortOne = Card(1);
        shortOne.Duration = 60;
        var zero = Card(2);
        var longOne = Card(3);
        longOne.Duration = 3600;

        var ordered = CategoryCardStats.Order([zero, shortOne, longOne], m => m.Data.Name);

        Assert.Equal([3, 1, 2], ordered.Select(m => m.Data.ID));
    }

    [Fact]
    public void Order_SortsZeroDurationByName()
    {
        var b = Card(2);
        var a = Card(1);
        a.Data = new ShiJian.Client.Shared.Models.CategoryModel { ID = 1, Name = "A" };
        b.Data = new ShiJian.Client.Shared.Models.CategoryModel { ID = 2, Name = "B" };

        var ordered = CategoryCardStats.Order([b, a], m => m.Data.Name);

        Assert.Equal([1, 2], ordered.Select(m => m.Data.ID));
    }

    [Fact]
    public void CardTexts_UseDashWhenNoDuration()
    {
        var card = Card(1);
        Assert.Equal("—", card.DurationText);
        Assert.Equal(string.Empty, card.RatioText);

        card.Duration = 5400;
        card.Ratio = 0.884;
        Assert.Equal("1h30m", card.DurationText);
        Assert.Equal("88%", card.RatioText);
    }

    [Fact]
    public void CountText_AlwaysHasMultiplierPrefix()
    {
        var card = Card(1);
        card.Count = 218;
        Assert.Equal("×218", card.CountText);
    }

    private static CategoryModel Named(int id, string name, int count = 0, double duration = 0)
    {
        var card = new CategoryModel
        {
            Data = new ShiJian.Client.Shared.Models.CategoryModel { ID = id, Name = name },
            Count = count,
            Duration = duration
        };
        return card;
    }

    [Fact]
    public void Sort_Duration_KeepsEmptyCategoriesAtTheEnd()
    {
        var cards = new[]
        {
            Named(1, ""),
            Named(2, "b", duration: 60),
            Named(3, "c", duration: 3600)
        };

        var sorted = CategoryCardStats.Sort(cards, CategorySortMode.Duration, m => m.Data.Name);

        Assert.Equal([3, 2, 1], sorted.Select(m => m.Data.ID));
    }

    [Fact]
    public void Sort_Name_IsPureAlphabeticalEvenWithEmptyCards()
    {
        var cards = new[]
        {
            Named(1, "C"),
            Named(2, "A", duration: 9999, count: 5),
            Named(3, "B")
        };

        var sorted = CategoryCardStats.Sort(cards, CategorySortMode.Name, m => m.Data.Name);

        Assert.Equal([2, 3, 1], sorted.Select(m => m.Data.ID));
    }

    [Fact]
    public void Sort_Count_PutsBiggestFirstAndEmptiesLast()
    {
        var cards = new[]
        {
            Named(1, "empty"),
            Named(2, "few", count: 2),
            Named(3, "many", count: 30),
            Named(4, "mid", count: 8)
        };

        var sorted = CategoryCardStats.Sort(cards, CategorySortMode.Count, m => m.Data.Name);

        Assert.Equal([3, 4, 2, 1], sorted.Select(m => m.Data.ID));
    }

    [Fact]
    public void Sort_Count_CountsDurationOnlyCardAsHavingData()
    {
        // 有网站/应用落在这个分类，但按分类取数失败时数量为 0、时长不为 0：不能当成空分类踢到末尾组
        var cards = new[]
        {
            Named(1, "empty"),
            Named(2, "durationOnly", duration: 120),
            Named(3, "one", count: 1)
        };

        var sorted = CategoryCardStats.Sort(cards, CategorySortMode.Count, m => m.Data.Name);

        Assert.Equal(1, sorted[^1].Data.ID);
    }

    [Fact]
    public void ReorderInPlace_MovesWithoutRebuilding()
    {
        var first = Named(1, "a");
        var second = Named(2, "b");
        var third = Named(3, "c");
        var target = new System.Collections.ObjectModel.ObservableCollection<CategoryModel> { first, second, third };

        CategoryCardStats.ReorderInPlace(target, [third, first, second]);

        Assert.Equal([3, 1, 2], target.Select(m => m.Data.ID));
        Assert.Same(third, target[0]);
        Assert.Same(first, target[1]);
        Assert.Same(second, target[2]);
    }

    [Fact]
    public void ReorderInPlace_IsNoOpWhenAlreadyOrdered()
    {
        var a = Named(1, "a");
        var b = Named(2, "b");
        var target = new System.Collections.ObjectModel.ObservableCollection<CategoryModel> { a, b };

        var raised = 0;
        target.CollectionChanged += (_, _) => raised++;

        CategoryCardStats.ReorderInPlace(target, [a, b]);

        Assert.Equal(0, raised);
    }
}
