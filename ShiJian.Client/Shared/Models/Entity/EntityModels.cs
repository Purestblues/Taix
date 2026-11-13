using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ShiJian.Client.Shared.Models.Entity;

/// <summary>
/// 统一实体表的一行（N2）。`Kind` 取值：`app` / `site` / `other`。
/// 数据口径（互斥归属、差额行）由服务端 `EntityService` 算好，客户端只负责渲染。
/// </summary>
public class EntityModel
{
    /// <summary>稳定标识：`app:12` / `site:5` / `other:3`</summary>
    public string Key { get; set; } = string.Empty;

    public string Kind { get; set; } = "app";

    public string Name { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public int Seconds { get; set; }

    public int CategoryId { get; set; }

    /// <summary>分类命名空间：应用分类 / 网站分类是两套自增 ID</summary>
    public string CategoryKind { get; set; } = "app";

    public string? CategoryName { get; set; }

    public string? CategoryColor { get; set; }

    public bool IsBrowser { get; set; }

    /// <summary>合并组成员 / 浏览器内被并入的站点（母成员不在里面）</summary>
    public List<EntityMemberModel> Members { get; set; } = [];

    /// <summary>行标识里的主键数字（导航用，不参与序列化）</summary>
    [JsonIgnore]
    public int EntityId
    {
        get
        {
            var index = Key.IndexOf(':');
            return index >= 0 && int.TryParse(Key[(index + 1)..], out var id) ? id : 0;
        }
    }
}

public class EntityMemberModel
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Seconds { get; set; }
    public string? Icon { get; set; }
}

public class EntityListModel
{
    public List<EntityModel> Rows { get; set; } = [];

    public int TotalSeconds { get; set; }

    public int AppSeconds { get; set; }

    public int SiteSeconds { get; set; }

    /// <summary>「浏览器内其他页面」合计</summary>
    public int BalanceSeconds { get; set; }

    /// <summary>站点时长超出浏览器会话覆盖的部分（数据异常计数）</summary>
    public int OverflowSeconds { get; set; }

    /// <summary>找不到归属浏览器的站点时长（默认浏览器也没跑）</summary>
    public int UnmatchedSeconds { get; set; }

    public bool BrowserMerged { get; set; }

    public string DefaultBrowser { get; set; } = string.Empty;
}

public class BrowserOptionModel
{
    public string Reference { get; set; } = string.Empty;
    public string? AppKey { get; set; }
    public string? AppName { get; set; }
    public int Seconds { get; set; }
    public bool IsDefault { get; set; }
}

public class MergeGroupModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "app";
    public bool IsAuto { get; set; }
    public string PrimaryRef { get; set; } = string.Empty;
    public List<string> Members { get; set; } = [];
}

public class MergeGroupListModel
{
    public List<MergeGroupModel> Groups { get; set; } = [];
    public List<string> Suppressed { get; set; } = [];
}

/// <summary>`/api/entity/hours` 的一行：一个实体在时间轴上的时长分布</summary>
public class EntityHoursRow
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = "app";
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int CategoryId { get; set; }
    public string CategoryKind { get; set; } = "app";
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public bool IsBrowser { get; set; }
    public double[] Values { get; set; } = [];
    public List<EntityMemberModel> Members { get; set; } = [];

    [JsonIgnore]
    public int EntityId
    {
        get
        {
            var index = Key.IndexOf(':');
            return index >= 0 && int.TryParse(Key[(index + 1)..], out var id) ? id : 0;
        }
    }

    public EntityModel ToEntityModel(int seconds) => new()
    {
        Key = Key,
        Kind = Kind,
        Name = Name,
        Icon = Icon,
        Seconds = seconds,
        CategoryId = CategoryId,
        CategoryKind = CategoryKind,
        CategoryName = CategoryName,
        CategoryColor = CategoryColor,
        IsBrowser = IsBrowser,
        Members = Members
    };
}

public class EntityHoursModel
{
    public List<string> Labels { get; set; } = [];
    public List<EntityHoursRow> Rows { get; set; } = [];
    public bool BrowserMerged { get; set; }
    public int BucketCount { get; set; }
}

/// <summary>`/api/entity/summary`：指标卡所需的区间聚合统计</summary>
public class EntitySummaryModel
{
    public long TotalSeconds { get; set; }
    public long AppSeconds { get; set; }
    public long AppNetSeconds { get; set; }
    public long SiteSeconds { get; set; }
    public int AppCount { get; set; }
    public int SiteCount { get; set; }
    public long SessionCount { get; set; }
    public long AverageSessionSeconds { get; set; }
    public long LongestSessionSeconds { get; set; }
    public DateTime? LongestSessionStart { get; set; }
    public DateTime? FirstSessionStart { get; set; }
    public DateTime? LastSessionEnd { get; set; }
    public long WebPageCount { get; set; }
    public long WebRecordCount { get; set; }
    public long LongestBrowseSeconds { get; set; }
    public DateTime? FirstBrowseStart { get; set; }
    public DateTime? LastBrowseEnd { get; set; }
    public EntityModel? TopApp { get; set; }
    public EntityModel? TopSite { get; set; }
}
