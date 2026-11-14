namespace ShiJian.Client.Shared.Models.Config;

/// <summary>WebDAV 云同步配置（N15）</summary>
public class WebDavModel
{
    public bool Enabled { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string RemoteFolder { get; set; } = "ShiJian";

    /// <summary>同步策略：manual | startup | hourly | daily</summary>
    public string SyncMode { get; set; } = "manual";

    public bool MergeOnSync { get; set; } = true;

    public string LastSyncAtUtc { get; set; } = string.Empty;

    public string LastSyncStatus { get; set; } = string.Empty;
}
