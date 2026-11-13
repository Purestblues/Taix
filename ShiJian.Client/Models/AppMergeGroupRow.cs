using System;

namespace ShiJian.Client.Models;

/// <summary>
/// 设置页「子母程序合并」列表里的一行。只是展示用的快照，
/// 真正的判定结果在 <see cref="Librarys.AppMergeGroup"/>。
/// </summary>
public class AppMergeGroupRow
{
    /// <summary>覆盖表按它记录解散（稳定锚点）</summary>
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>成员名（折叠成一行，过长由界面截断）</summary>
    public string MembersText { get; init; } = string.Empty;

    public int Count { get; init; }

    public string CountText => string.Format(ResourceStrings.MergeGroupMembers, Count);

    /// <summary>是否被用户改过名（改过名的不显示「恢复」按钮的语义差别，仅作标识）</summary>
    public bool IsRenamed { get; init; }

    public string Kind { get; init; } = "app";

    public bool IsAuto { get; init; } = true;

    public string KindLabel { get; init; } = string.Empty;

    public bool HasKindBadge => !string.IsNullOrEmpty(KindLabel);
}

/// <summary>设置页手动/跨源合并弹窗里的候选项（应用或网站）</summary>
public class MergeCandidateRow : ModelBase
{
    private bool _isSelected;
    private bool _isPrimary;

    public string Ref { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public bool IsSite { get; init; }

    public string TypeBadge => IsSite ? ResourceStrings.MergeCandidateSite : ResourceStrings.MergeCandidateApp;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsPrimary
    {
        get => _isPrimary;
        set
        {
            if (_isPrimary == value) return;
            _isPrimary = value;
            OnPropertyChanged();
        }
    }
}
