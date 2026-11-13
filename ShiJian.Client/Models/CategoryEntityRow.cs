using ShiJian.Client.Shared.Librarys;

namespace ShiJian.Client.Models;

/// <summary>
/// 分类页「应用 / 网站明细视图（N5）」的一行：消费合并后的实体，支持单点击换分类。
/// </summary>
public class CategoryEntityRow : ModelBase
{
    private int _categoryId;
    private string _categoryName = string.Empty;
    private string _categoryColor = "#8A8A8A";
    private bool _isUncategorized;

    public bool IsSite { get; init; }

    public int PrimaryId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public int TodaySeconds { get; init; }

    public int TotalSeconds { get; init; }

    public int MemberCount { get; init; } = 1;

    public string? MemberTooltip { get; init; }

    public bool IsMerged => MemberCount > 1;

    public string MergedText => IsMerged ? string.Format(ResourceStrings.MergedItems, MemberCount) : string.Empty;

    public string TodayText => TodaySeconds > 0 ? Time.ToString(TodaySeconds) : "—";

    public string TotalText => TotalSeconds > 0 ? Time.ToString(TotalSeconds) : "—";

    public int CategoryId
    {
        get => _categoryId;
        set
        {
            if (_categoryId == value) return;
            _categoryId = value;
            OnPropertyChanged();
        }
    }

    public string CategoryName
    {
        get => _categoryName;
        set
        {
            if (_categoryName == value) return;
            _categoryName = value;
            OnPropertyChanged();
        }
    }

    public string CategoryColor
    {
        get => _categoryColor;
        set
        {
            if (_categoryColor == value) return;
            _categoryColor = value;
            OnPropertyChanged();
        }
    }

    public bool IsUncategorized
    {
        get => _isUncategorized;
        set
        {
            if (_isUncategorized == value) return;
            _isUncategorized = value;
            OnPropertyChanged();
        }
    }
}
