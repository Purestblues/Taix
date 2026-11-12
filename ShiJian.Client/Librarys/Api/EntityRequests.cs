namespace ShiJian.Client.Librarys.Api;

/// <summary>解散 / 改名子母程序合并组时只用组 ID</summary>
public class EntityGroupRequest
{
    public int GroupId { get; set; }
}

/// <summary>重命名合并组</summary>
public class EntityRenameRequest
{
    public int GroupId { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>恢复全部被解散的自动组（服务端不读 body，占位即可）</summary>
public class EntityRestoreRequest
{
}
