using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Librarys.Api;
using ShiJian.Client.Shared.Models.Web;
using ShiJian.Client.Shared.Servicers.Interfaces;

namespace ShiJian.Client.Servicers.Instances;

public class ApiWebSiteData : IWebSiteData
{
    private readonly IShiJianApiClient _apiClient;

    public ApiWebSiteData(IShiJianApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public Task<WebSiteModel?> GetWebSiteAsync(int id, CancellationToken cancellationToken = default)
    {
        return _apiClient.GetWebSiteAsync(id);
    }

    public Task<WebSiteModel?> GetWebSiteByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        return _apiClient.GetWebSiteByDomainAsync(domain);
    }

    public async Task<IReadOnlyCollection<WebSiteModel>> GetAllWebSitesAsync(CancellationToken cancellationToken = default)
    {
        var sites = await _apiClient.GetWebSitesAsync();
        return sites;
    }

    public async Task<IReadOnlyCollection<WebSiteModel>> GetWebSitesByCategoryIDAsync(int categoryID, CancellationToken cancellationToken = default)
    {
        var sites = await _apiClient.GetWebSitesAsync(categoryId: categoryID);
        return sites;
    }

    public async Task<WebSiteModel?> UpdateWebSiteAsync(WebSiteModel site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);
        return await _apiClient.UpdateWebSiteAsync(site);
    }
}
