using System.Collections.Generic;

namespace ShiJian.Client.Shared.Models.Data;

public class BackupCounts
{
    public long Apps { get; set; }
    public long Categories { get; set; }
    public long DailyLogs { get; set; }
    public long HoursLogs { get; set; }
    public long AppSessions { get; set; }
    public long WebSites { get; set; }
    public long WebUrls { get; set; }
    public long WebBrowseLogs { get; set; }
    public long MergeGroups { get; set; }
    public long PluginRecords { get; set; }
}

public class BackupManifest
{
    public int FormatVersion { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public int MigrationCount { get; set; }
    public List<string> AppliedMigrations { get; set; } = [];
    public string DbFileName { get; set; } = string.Empty;
    public string DbSha256 { get; set; } = string.Empty;
    public long DbSizeBytes { get; set; }
    public bool IncludesIcons { get; set; }
    public int IconCount { get; set; }
    public BackupCounts Counts { get; set; } = new();
}

public class BackupInspectResult
{
    public bool Valid { get; set; }
    public bool Compatible { get; set; }
    public string CompatibilityNote { get; set; } = string.Empty;
    public bool Sha256Verified { get; set; }
    public bool SqliteIntegrityOk { get; set; }
    public BackupManifest Manifest { get; set; } = new();
    public int CurrentSchemaVersion { get; set; }
}

public class BackupImportResult
{
    public string Mode { get; set; } = string.Empty;
    public string RollbackFile { get; set; } = string.Empty;
    public int ImportedSchemaVersion { get; set; }
    public bool MigratedFromOlder { get; set; }
    public int RestoredIcons { get; set; }
    public BackupCounts Counts { get; set; } = new();
}

public class RollbackItem
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ModifiedAtUtc { get; set; } = string.Empty;
}

public class RollbackRequest
{
    public string FileName { get; set; } = string.Empty;
}

public class WebDavTestRequest
{
    public string Url { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RemoteFolder { get; set; } = "ShiJian";
}

public class WebDavTestResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RemoteUrl { get; set; } = string.Empty;
    public int BackupCount { get; set; }
}

public class RemoteBackupItem
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string LastModified { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}

public class WebDavSyncRequest
{
    public bool? MergeOnSync { get; set; }
    public string? AppVersion { get; set; }
}

public class WebDavSyncResult
{
    public string UploadedFile { get; set; } = string.Empty;
    public List<string> MergedFiles { get; set; } = [];
    public int SkippedFiles { get; set; }
    public string SyncedAtUtc { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class WebDavRestoreRequest
{
    public string FileName { get; set; } = string.Empty;
    public string Mode { get; set; } = "merge";
}
