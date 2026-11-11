using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Shared.Models;

public static class WebSiteModelExtensions
{
    // 优先别名，其次标题，最后域名
    public static string GetDisplayName(this WebSiteModel site)
    {
        if (!string.IsNullOrEmpty(site.Alias)) return site.Alias!;
        if (!string.IsNullOrEmpty(site.Title)) return site.Title!;
        return site.Domain ?? "Unknown";
    }
}
