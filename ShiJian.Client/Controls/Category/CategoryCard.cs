using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace ShiJian.Client.Controls.Category;

/// <summary>
/// 分类页的一张卡（应用分类 / 网站分类共用）。
///
/// 抽成控件的原因是：应用与网站两处模板原本各写一遍，N7 又要多出「紧凑 / 卡片」两种形态，
/// 复制会变成四份。现在一份模板，形态由 <see cref="IsCard"/> 切换（由页面把
/// <c>ListBox.card</c> 类名挂在列表上，再用后代选择器把 IsCard 置真）。
///
/// 为什么不用两个 DataTemplate：换模板会让 ListBox 重建每一项的视觉树，
/// 滚动位置与选中态会丢；改属性只影响布局，容器还活着。
/// </summary>
public class CategoryCard : TemplatedControl
{
    public static readonly StyledProperty<bool> IsCardProperty =
        AvaloniaProperty.Register<CategoryCard, bool>(nameof(IsCard));

    /// <summary>true = 大卡片（图标与数字更大、方形），false = 紧凑扁卡</summary>
    public bool IsCard
    {
        get => GetValue(IsCardProperty);
        set => SetValue(IsCardProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(CategoryCard);
}
