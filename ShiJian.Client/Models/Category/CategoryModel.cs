using ShiJian.Client.Librarys;

namespace ShiJian.Client.Models.Category;

public class CategoryModel
{
    public int Count { get; set; }
    public Shared.Models.CategoryModel Data { get; set; }

    /// <summary>
    /// 列表里展示的颜色：和图表同一套对比度规则，换主题后重建列表即可生效
    /// </summary>
    public string DisplayColor => ChartColorHelper.Resolve(Data?.Color, Data?.IsSystem ?? false);
}
