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
}
