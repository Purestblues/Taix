using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Librarys;
using ShiJian.Client.Logging;
using ShiJian.Client.Servicers.Interfaces;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Entity;
using ShiJian.Client.Shared.Servicers.Interfaces;

namespace ShiJian.Client.Servicers.Instances;

/// <summary>
/// 子母程序合并组（N6 / N2）。
///
/// `fork.10` 起判定与组表都在服务端（`AppModels.File + Publisher` 推出稳定锚点，
/// 组存在 `EntityMergeGroups`），客户端只做两件事：
/// 1. 把服务端的组翻成界面用的 <see cref="AppMergeGroup"/>（顺便解析成应用 ID）；
/// 2. 统计页仍是「按应用列表」的旧数据源，继续用同一套规则做视图期折叠。
///
/// 为什么不在客户端自己判定：页面（概览）已经直接消费服务端的实体行，
/// 客户端再判定一次就是同一份规则两处实现，改一处漏一处。
/// </summary>
public class AppMergeService : IAppMergeService
{
    /// <summary>组变化不频繁，缓存一会儿够用；设置页改完会强制刷新</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IEntityData _entityData;
    private readonly IAppData _appData;
    private readonly object _gate = new();
    private IReadOnlyList<AppMergeGroup> _groups = [];
    private DateTime _loadedAt = DateTime.MinValue;

    public AppMergeService(IEntityData entityData, IAppData appData)
    {
        _entityData = entityData;
        _appData = appData;
    }

    public IReadOnlyList<AppMergeGroup> Groups
    {
        get
        {
            lock (_gate) return _groups;
        }
    }

    public IReadOnlyDictionary<string, AppMergeOverride> Overrides { get; } =
        new Dictionary<string, AppMergeOverride>(StringComparer.Ordinal);

    public async Task<IReadOnlyList<AppMergeGroup>> GetGroupsAsync(bool force = false,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!force && _groups.Count > 0 && DateTime.UtcNow - _loadedAt < CacheTtl) return _groups;
        }

        try
        {
            var remote = await _entityData.GetMergeGroupsAsync(cancellationToken).ConfigureAwait(false);
            var groups = await ToGroupsAsync(remote, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _groups = groups;
                _loadedAt = DateTime.UtcNow;
            }
            return groups;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 合并是增强功能：服务端连不上时不能让页面跟着失败
            Logger.Warn($"AppMergeService: 加载合并组失败，按不合并处理：{ex.Message}");
            lock (_gate) return _groups;
        }
    }

    public List<DailyLogModel> Apply(IEnumerable<DailyLogModel> logs)
    {
        var groups = Groups;
        return groups.Count == 0 ? logs.ToList() : AppMergePlanner.Apply(logs, groups);
    }

    public void Decorate(IEnumerable<Controls.Charts.Model.ChartsDataModel> rows)
    {
        var groups = Groups;
        if (groups.Count == 0) return;
        AppMergePlanner.Decorate(rows, groups);
    }

    /// <param name="groupKey">组标识（服务端组 ID 的字符串形式）</param>
    public async Task DissolveAsync(string groupKey)
    {
        if (!int.TryParse(groupKey, out var groupId) || groupId <= 0) return;

        await _entityData.DissolveGroupAsync(groupId).ConfigureAwait(false);
        await GetGroupsAsync(force: true).ConfigureAwait(false);
    }

    public async Task ResetAsync()
    {
        await _entityData.RestoreGroupsAsync().ConfigureAwait(false);
        await GetGroupsAsync(force: true).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AppMergeGroup>> ToGroupsAsync(
        IReadOnlyList<MergeGroupModel> remote, CancellationToken cancellationToken)
    {
        if (remote.Count == 0) return [];

        var apps = await _appData.GetAllAppsAsync(cancellationToken).ConfigureAwait(false);
        var byName = apps
            .Where(app => !string.IsNullOrEmpty(app.Name))
            .GroupBy(app => app.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var result = new List<AppMergeGroup>(remote.Count);
        foreach (var model in remote)
        {
            if (model.Kind != "app") continue;

            var memberIds = new List<int>();
            var memberNames = new List<string>();
            foreach (var member in model.Members)
            {
                if (!byName.TryGetValue(member, out var app)) continue;
                memberIds.Add(app.ID);
                memberNames.Add(app.Name);
            }

            if (memberIds.Count < 2) continue;

            var primaryId = byName.TryGetValue(model.PrimaryRef, out var primary) ? primary.ID : memberIds[0];
            result.Add(new AppMergeGroup
            {
                Key = model.Id.ToString(),
                Name = model.Name,
                PrimaryAppId = primaryId,
                MemberAppIds = memberIds,
                MemberNames = memberNames,
                TotalTime = 0
            });
        }

        return result;
    }
}
