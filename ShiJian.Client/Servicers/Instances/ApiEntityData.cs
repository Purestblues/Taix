using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Librarys.Api;
using ShiJian.Client.Shared.Models.Entity;
using ShiJian.Client.Shared.Servicers.Interfaces;

namespace ShiJian.Client.Servicers.Instances;

public class ApiEntityData : IEntityData
{
    private readonly IShiJianApiClient _apiClient;

    public ApiEntityData(IShiJianApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public Task<EntityListModel?> GetEntityListAsync(DateTime start, DateTime end, bool browserMerged,
        CancellationToken cancellationToken = default) =>
        _apiClient.GetEntityListAsync(start, end, browserMerged, cancellationToken);

    public Task<EntityHoursModel?> GetEntityHoursAsync(DateTime start, DateTime end, string buckets, bool browserMerged,
        CancellationToken cancellationToken = default) =>
        _apiClient.GetEntityHoursAsync(start, end, buckets, browserMerged, cancellationToken);

    public Task<EntitySummaryModel?> GetEntitySummaryAsync(DateTime start, DateTime end, bool browserMerged,
        CancellationToken cancellationToken = default) =>
        _apiClient.GetEntitySummaryAsync(start, end, browserMerged, cancellationToken);

    public async Task<IReadOnlyList<BrowserOptionModel>> GetBrowsersAsync(CancellationToken cancellationToken = default) =>
        await _apiClient.GetEntityBrowsersAsync(cancellationToken);

    public async Task<IReadOnlyList<MergeGroupModel>> GetMergeGroupsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _apiClient.GetEntityMergeGroupsAsync(cancellationToken);
        return result?.Groups ?? [];
    }

    public async Task<MergeGroupModel?> CreateGroupAsync(string name, string primaryRef, IReadOnlyList<string> members) =>
        await _apiClient.CreateEntityMergeGroupAsync(name, primaryRef, members);

    public async Task<MergeGroupModel?> UpdateGroupAsync(int groupId, string? name = null, string? primaryRef = null, IReadOnlyList<string>? members = null) =>
        await _apiClient.UpdateEntityMergeGroupAsync(groupId, name, primaryRef, members);

    public Task DissolveGroupAsync(int groupId) => _apiClient.DissolveEntityMergeGroupAsync(groupId);

    public Task RenameGroupAsync(int groupId, string name) => _apiClient.RenameEntityMergeGroupAsync(groupId, name);

    public Task RestoreGroupsAsync() => _apiClient.RestoreEntityMergeGroupsAsync();
}
