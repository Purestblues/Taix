using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Librarys.Api;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Servicers.Interfaces;

namespace ShiJian.Client.Servicers.Instances;

public class ApiAppData : IAppData
{
    private readonly IShiJianApiClient _apiClient;

    public ApiAppData(IShiJianApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public Task<AppModel?> GetAppAsync(int id, CancellationToken cancellationToken = default)
    {
        return _apiClient.GetAppAsync(id);
    }

    public Task<AppModel?> GetAppByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return _apiClient.GetAppByNameAsync(name);
    }

    public async Task<IReadOnlyCollection<AppModel>> GetAllAppsAsync(CancellationToken cancellationToken = default)
    {
        var apps = await _apiClient.GetAllAppsAsync();
        return apps;
    }

    public async Task<IReadOnlyCollection<AppModel>> GetAppsByCategoryIDAsync(int categoryID, CancellationToken cancellationToken = default)
    {
        var apps = await _apiClient.GetAppsByCategoryAsync(categoryID);
        return apps;
    }

    public async Task<int> GetAppCountByCategoryIDAsync(int categoryID)
    {
        var apps = await _apiClient.GetAppsByCategoryAsync(categoryID);
        return apps.Count;
    }

    public async Task AddAppAsync(AppModel app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        await _apiClient.CreateAppAsync(app);
    }

    public async Task UpdateAppAsync(AppModel app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        await _apiClient.UpdateAppAsync(app);
    }
}
