using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Shared.Models.Entity;

namespace ShiJian.Client.Shared.Servicers.Interfaces;

/// <summary>
/// 统一实体表（N2）数据源。互斥归属、差额行、合并组都在服务端算，
/// 客户端拿到的就已经是「可以直接渲染的一行一行」。
/// </summary>
public interface IEntityData
{
    /// <param name="browserMerged">true = 网站并入浏览器（站点行消失，浏览器拿回完整时长）</param>
    Task<EntityListModel?> GetEntityListAsync(DateTime start, DateTime end, bool browserMerged,
        CancellationToken cancellationToken = default);

    Task<EntityHoursModel?> GetEntityHoursAsync(DateTime start, DateTime end, string buckets, bool browserMerged,
        CancellationToken cancellationToken = default);

    Task<EntitySummaryModel?> GetEntitySummaryAsync(DateTime start, DateTime end, bool browserMerged,
        CancellationToken cancellationToken = default);

    /// <summary>设置页「默认浏览器」的候选（浏览日志里出现过的 + 应用侧识别到的）</summary>
    Task<IReadOnlyList<BrowserOptionModel>> GetBrowsersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MergeGroupModel>> GetMergeGroupsAsync(CancellationToken cancellationToken = default);

    Task DissolveGroupAsync(int groupId);

    Task RenameGroupAsync(int groupId, string name);

    Task RestoreGroupsAsync();
}
