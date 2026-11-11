using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Shared.Servicers.Interfaces;

public interface IWebSiteData
{
    Task<WebSiteModel?> GetWebSiteAsync(int id, CancellationToken cancellationToken = default);

    Task<WebSiteModel?> GetWebSiteByDomainAsync(string domain, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WebSiteModel>> GetAllWebSitesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WebSiteModel>> GetWebSitesByCategoryIDAsync(int categoryID, CancellationToken cancellationToken = default);

    Task<WebSiteModel?> UpdateWebSiteAsync(WebSiteModel site, CancellationToken cancellationToken = default);
}
