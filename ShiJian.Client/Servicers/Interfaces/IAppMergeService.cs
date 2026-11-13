using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Librarys;
using ShiJian.Client.Shared.Models;

namespace ShiJian.Client.Servicers.Interfaces;

/// <summary>
/// 子母程序合并（N6）：负责「算组 + 记住用户解散过谁」，本身不含判定规则。
/// 判定在 <see cref="AppMergePlanner"/> 里，方便单测。
/// </summary>
public interface IAppMergeService
{
    /// <summary>当前生效的合并组（首次调用会拉应用表 + 读覆盖表）</summary>
    IReadOnlyList<AppMergeGroup> Groups { get; }

    /// <summary>用户对自动组的覆盖（解散 / 改名）</summary>
    IReadOnlyDictionary<string, AppMergeOverride> Overrides { get; }

    /// <summary>取合并组；<paramref name="force"/> 为真时忽略缓存</summary>
    Task<IReadOnlyList<AppMergeGroup>> GetGroupsAsync(bool force = false, CancellationToken cancellationToken = default);

    /// <summary>把子成员的行并到母成员（没有组时原样返回）</summary>
    List<DailyLogModel> Apply(IEnumerable<DailyLogModel> logs);

    /// <summary>给合并后的榜单行加「合并 N 项」徽章</summary>
    void Decorate(IEnumerable<Controls.Charts.Model.ChartsDataModel> rows);

    /// <summary>创建手动合并组（支持应用、网站或跨源）</summary>
    Task<AppMergeGroup?> CreateGroupAsync(string name, string primaryRef, IReadOnlyList<string> members);

    /// <summary>更新合并组（名称、母成员、成员列表）</summary>
    Task<AppMergeGroup?> UpdateGroupAsync(int groupId, string? name = null, string? primaryRef = null, IReadOnlyList<string>? members = null);

    /// <summary>解散一个自动组（写覆盖表，之后不再自动合并）</summary>
    Task DissolveAsync(string groupKey);

    /// <summary>清除全部覆盖，恢复自动合并</summary>
    Task ResetAsync();
}
