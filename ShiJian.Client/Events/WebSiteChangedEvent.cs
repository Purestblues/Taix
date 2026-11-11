using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Events;

public record WebSiteChangedEvent(WebSiteModel WebSite, AppChangeType ChangeType);