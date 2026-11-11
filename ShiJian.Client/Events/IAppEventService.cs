using System;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Events;

public interface IAppEventService
{
    IObservable<AppChangedEvent> AppChanged { get; }
    IObservable<WebSiteChangedEvent> WebSiteChanged { get; }
    long ChangeVersion { get; }
    
    void PublishAppChanged(AppModel app, AppChangeType changeType);
    void PublishWebSiteChanged(WebSiteModel site, AppChangeType changeType);
}
