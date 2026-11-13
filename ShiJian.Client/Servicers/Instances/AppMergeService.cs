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
    private readonly IWebSiteData _webSiteData;
    private readonly object _gate = new();
    private IReadOnlyList<AppMergeGroup> _groups = [];
    private DateTime _loadedAt = DateTime.MinValue;

    public AppMergeService(IEntityData entityData, IAppData appData, IWebSiteData webSiteData)
    {
        _entityData = entityData;
        _appData = appData;
        _webSiteData = webSiteData;
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

    public async Task<AppMergeGroup?> CreateGroupAsync(string name, string primaryRef, IReadOnlyList<string> members)
    {
        var created = await _entityData.CreateGroupAsync(name, primaryRef, members).ConfigureAwait(false);
        var groups = await GetGroupsAsync(force: true).ConfigureAwait(false);
        return created != null
            ? groups.FirstOrDefault(g => g.Key == created.Id.ToString())
            : null;
    }

    public async Task<AppMergeGroup?> UpdateGroupAsync(int groupId, string? name = null, string? primaryRef = null, IReadOnlyList<string>? members = null)
    {
        var updated = await _entityData.UpdateGroupAsync(groupId, name, primaryRef, members).ConfigureAwait(false);
        var groups = await GetGroupsAsync(force: true).ConfigureAwait(false);
        return updated != null
            ? groups.FirstOrDefault(g => g.Key == updated.Id.ToString())
            : null;
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

        var appsTask = _appData.GetAllAppsAsync(cancellationToken);
        var sitesTask = _webSiteData.GetAllWebSitesAsync(cancellationToken);
        await Task.WhenAll(appsTask, sitesTask).ConfigureAwait(false);

        var byName = appsTask.Result
            .Where(app => !string.IsNullOrEmpty(app.Name))
            .GroupBy(app => app.Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var byDomain = sitesTask.Result
            .Where(site => !string.IsNullOrEmpty(site.Domain))
            .GroupBy(site => site.Domain!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<AppMergeGroup>(remote.Count);
        foreach (var model in remote)
        {
            var memberAppIds = new List<int>();
            var memberSiteIds = new List<int>();
            var memberNames = new List<string>();
            var memberRefs = new List<string>();

            foreach (var raw in model.Members)
            {
                var (isSite, value) = AppMergePlanner.ParseMemberRef(raw);
                if (string.IsNullOrEmpty(value)) continue;

                if (isSite)
                {
                    if (!byDomain.TryGetValue(value, out var site)) continue;
                    memberSiteIds.Add(site.ID);
                    memberNames.Add(AppMergePlanner.SiteDisplayName(site));
                    memberRefs.Add(AppMergePlanner.CanonicalMemberRef(true, site.Domain ?? value));
                }
                else
                {
                    if (!byName.TryGetValue(value, out var app)) continue;
                    memberAppIds.Add(app.ID);
                    memberNames.Add(AppMergePlanner.AppDisplayName(app));
                    memberRefs.Add(AppMergePlanner.CanonicalMemberRef(false, app.Name ?? value));
                }
            }

            if (memberAppIds.Count + memberSiteIds.Count < 2) continue;

            var (primaryIsSite, primaryValue) = AppMergePlanner.ParseMemberRef(model.PrimaryRef);
            int primaryAppId = 0;
            int primarySiteId = 0;
            string canonicalPrimary = memberRefs[0];
            if (primaryIsSite && byDomain.TryGetValue(primaryValue, out var primarySite))
            {
                primarySiteId = primarySite.ID;
                canonicalPrimary = AppMergePlanner.CanonicalMemberRef(true, primarySite.Domain ?? primaryValue);
            }
            else if (!primaryIsSite && byName.TryGetValue(primaryValue, out var primaryApp))
            {
                primaryAppId = primaryApp.ID;
                canonicalPrimary = AppMergePlanner.CanonicalMemberRef(false, primaryApp.Name ?? primaryValue);
            }
            else if (memberAppIds.Count > 0)
            {
                primaryAppId = memberAppIds[0];
            }
            else if (memberSiteIds.Count > 0)
            {
                primarySiteId = memberSiteIds[0];
            }

            result.Add(new AppMergeGroup
            {
                Key = model.Id.ToString(),
                Name = model.Name,
                Kind = model.Kind,
                IsAuto = model.IsAuto,
                PrimaryRef = canonicalPrimary,
                PrimaryAppId = primaryAppId,
                PrimarySiteId = primarySiteId,
                MemberAppIds = memberAppIds,
                MemberSiteIds = memberSiteIds,
                MemberNames = memberNames,
                MemberRefs = memberRefs,
                TotalTime = 0
            });
        }

        return result;
    }
}
