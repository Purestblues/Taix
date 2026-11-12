using System;
using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Controls.Charts.Model;
using ShiJian.Client.Shared.Models;

namespace ShiJian.Client.Librarys;

/// <summary>
/// N6 子母程序合并（视图期映射，不改数据库）。
///
/// 判定输入只有应用表里已有的两样东西：可执行路径 + 进程名，配上一份本地的「解散 / 改名」覆盖表。
/// 产出是一组 <see cref="AppMergeGroup"/>：每个组有一个母成员（时长最长的那个），
/// 其余成员在列表里被折叠掉，时长累加到母成员上。
///
/// 为什么把口径放在这里（而不是服务端）：
/// - 合并是纯展示层的映射，`AppModels` / `DailyLogModels` 一行不改，随时可拆回原样；
/// - 判定规则必须能单测，误判的代价是「时长凭空并到别的应用上」，放在纯函数里才好钉住。
///
/// 判定规则（按顺序）：
/// 1. 锚点 = 安装目录（去掉末尾的版本号段 / bin / x64 之类打包层级；macOS 取 `.app` 包）；
/// 2. 品牌 = 进程名里「第一个版本号之前的那些词」（`PiDeck-0.9.5-setup` → `PiDeck`）；
/// 3. 同一个锚点 + 同一个品牌 → 一组，成员少于 2 个不算组；
/// 4. 排除浏览器、系统目录、以及 `setup` / `uninstall` 这类不具辨识度的品牌；
/// 5. 用户的解散（suppress）优先于自动判定。
/// </summary>
public static class AppMergePlanner
{
    /// <summary>不参与自动合并的浏览器（它们的子进程是渲染器，合并了看不出谁在用）</summary>
    private static readonly HashSet<string> BrowserStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "safari", "opera", "brave", "vivaldi", "iexplore",
        "chromium", "thorium", "tor", "arc", "360se", "360chrome", "qqbrowser"
    };

    /// <summary>打包产物层级，锚点要往上收一层</summary>
    private static readonly HashSet<string> BuilderDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "x64", "x86", "win64", "win32", "app"
    };

    /// <summary>出现在品牌尾部的安装器 / 平台词，判断品牌时直接截断</summary>
    private static readonly HashSet<string> PackagingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "win", "win32", "win64", "x64", "x86", "amd64", "user", "setup", "installer",
        "beta", "rc", "cn", "en", "app"
    };

    /// <summary>完全没有辨识度的品牌：这些词不构成合并依据（否则同一个下载目录会乱并）</summary>
    private static readonly HashSet<string> GenericBrands = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup", "uninstall", "install", "installer", "update", "updater", "helper",
        "application", "launcher", "service", "main", "index", "run", "start"
    };

    /// <summary>子进程常见后缀：`FooUpdater` 归到 `Foo`</summary>
    private static readonly HashSet<string> RoleSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "updater", "update", "helper", "service", "tray", "launcher", "broker",
        "daemon", "watchdog", "crashpad", "notifier", "srv", "systray"
    };

    /// <summary>安装目录锚点；拿不到（没有路径 / 相对路径）时返回 null</summary>
    public static string? AnchorOf(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        var path = file.Trim().Replace('\\', '/');
        var lower = path.ToLowerInvariant();

        // macOS：以 .app 包为锚点，包内层级（Contents/MacOS）不参与判定
        var appIndex = lower.IndexOf(".app/", StringComparison.Ordinal);
        if (appIndex >= 0) return lower[..(appIndex + 4)];

        var slash = lower.LastIndexOf('/');
        if (slash <= 0) return null;

        var segments = lower[..slash].Split('/').Where(s => s.Length > 0).ToList();
        if (segments.Count <= 1) return null; // c:/x.exe 这种根目录文件不参与

        while (segments.Count > 1)
        {
            var last = segments[^1];
            if (IsVersionSegment(last) || BuilderDirs.Contains(last)) segments.RemoveAt(segments.Count - 1);
            else break;
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static bool IsVersionSegment(string segment)
    {
        if (segment.Length == 0) return false;
        var trimmed = segment[0] is 'v' or 'V' ? segment[1..] : segment;
        if (trimmed.Length == 0) return false;
        if (!trimmed.Any(char.IsAsciiDigit)) return false;
        return trimmed.All(c => char.IsAsciiDigit(c) || c is '.' or '-' or '_');
    }

    /// <summary>系统目录不参与（界面里它们本来就该各占一行）</summary>
    private static bool IsSystemAnchor(string anchor)
        => anchor.StartsWith("c:/windows", StringComparison.Ordinal)
           || anchor.Contains("/windowsapps/", StringComparison.Ordinal)
           || anchor.StartsWith("/system/", StringComparison.Ordinal)
           || anchor.StartsWith("/usr/", StringComparison.Ordinal)
           || anchor.StartsWith("/bin/", StringComparison.Ordinal)
           || anchor.StartsWith("/sbin/", StringComparison.Ordinal);

    /// <summary>
    /// 品牌：取进程名里第一个版本号之前的词。
    /// `PiDeck-0.9.5-setup` → `PiDeck`；`Clash.Verge_2.5.8_x64-setup` → `Clash.Verge`；
    /// `TextFold-win-x64-setup` → `TextFold`；`kimi_3.2.20` → `kimi`。
    /// </summary>
    public static string? BrandOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var stem = name.Trim();
        if (stem.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];

        var tokens = stem.Split(['_', '-', '+', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        foreach (var token in tokens)
        {
            if (PackagingWords.Contains(token)) break;
            if (token.Any(char.IsAsciiDigit)) break; // 版本号开始
            kept.Add(token);
        }

        while (kept.Count > 0 && RoleSuffixes.Contains(kept[^1])) kept.RemoveAt(kept.Count - 1);

        var brand = string.Join(' ', kept).Trim();

        // 单 token 情况：`FooUpdater` 没有分隔符，直接按后缀裁掉
        if (kept.Count == 1)
        {
            foreach (var suffix in RoleSuffixes)
                if (brand.Length > suffix.Length + 1 &&
                    brand.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                    !char.IsAsciiDigit(brand[brand.Length - suffix.Length - 1]))
                {
                    brand = brand[..^suffix.Length].Trim();
                    break;
                }
        }
        if (brand.Length < 2) return null;
        if (GenericBrands.Contains(brand)) return null;
        return brand;
    }

    private static string StemOf(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return string.Empty;
        var path = file.Trim().Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>
    /// 从应用表算出合并组。`suppressed` / `renamed` 来自本地覆盖表。
    /// 返回的组按组时长降序，组内第一个成员是母成员。
    /// </summary>
    public static List<AppMergeGroup> Build(
        IEnumerable<AppModel> apps,
        IReadOnlyDictionary<string, AppMergeOverride>? overrides = null)
    {
        var buckets = new Dictionary<string, List<AppModel>>(StringComparer.Ordinal);

        foreach (var app in apps)
        {
            var anchor = AnchorOf(app.File);
            if (anchor == null || IsSystemAnchor(anchor)) continue;
            if (BrowserStems.Contains(StemOf(app.File))) continue;

            var brand = BrandOf(StemOf(app.File)) ?? BrandOf(app.Name);
            if (brand == null) continue;

            var key = anchor + "|" + brand.ToLowerInvariant();
            if (overrides != null && overrides.TryGetValue(key, out var ov) && ov.IsSuppressed) continue;

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(app);
        }

        var groups = new List<AppMergeGroup>();
        foreach (var (key, members) in buckets)
        {
            if (members.Count < 2) continue;

            // 母成员 = 时长最长；平局取路径更短（通常是主程序），再平局取 ID 小的
            var ordered = members
                .OrderByDescending(m => m.TotalTime)
                .ThenBy(m => m.File?.Length ?? int.MaxValue)
                .ThenBy(m => m.ID)
                .ToList();

            var primary = ordered[0];
            var name = BrandOf(StemOf(primary.File)) ?? BrandOf(primary.Name) ?? primary.Name ?? string.Empty;
            if (overrides != null && overrides.TryGetValue(key, out var ov) && !string.IsNullOrWhiteSpace(ov.DisplayName))
                name = ov.DisplayName!;

            groups.Add(new AppMergeGroup
            {
                Key = key,
                Name = name,
                PrimaryAppId = primary.ID,
                MemberAppIds = ordered.Select(m => m.ID).ToList(),
                MemberNames = ordered.Select(m => m.Name ?? m.Description ?? string.Empty).ToList(),
                TotalTime = ordered.Sum(m => m.TotalTime)
            });
        }

        groups.Sort((a, b) => b.TotalTime.CompareTo(a.TotalTime));
        return groups;
    }

    /// <summary>
    /// 把子成员的行并进母成员（按「应用 + 日期」对齐，避免把不同日期的桶揉成一行）。
    /// 总时长守恒：只是把同一段时间从多行挪到一行。
    /// </summary>
    public static List<DailyLogModel> Apply(IEnumerable<DailyLogModel> logs, IReadOnlyList<AppMergeGroup> groups)
    {
        var list = logs as IList<DailyLogModel> ?? logs.ToList();
        if (groups.Count == 0 || list.Count == 0) return list.ToList();

        var groupByApp = new Dictionary<int, AppMergeGroup>();
        foreach (var group in groups)
        foreach (var appId in group.MemberAppIds)
            groupByApp[appId] = group;

        if (!list.Any(log => groupByApp.ContainsKey(LogAppId(log)))) return list.ToList();

        // 先收齐「母成员在哪一天有行」：子成员那一行才能折叠过去，
        // 否则（那天母成员没有记录）原样保留，不能把时长吞掉。
        var primaryLogs = new Dictionary<(int AppId, DateTime Date), DailyLogModel>();
        foreach (var log in list)
        {
            var appId = LogAppId(log);
            if (groupByApp.TryGetValue(appId, out var group) && appId == group.PrimaryAppId)
                primaryLogs[(group.PrimaryAppId, log.Date.Date)] = log;
        }

        var result = new List<DailyLogModel>(list.Count);
        foreach (var log in list)
        {
            var appId = LogAppId(log);
            if (!groupByApp.TryGetValue(appId, out var group))
            {
                result.Add(log);
                continue;
            }

            if (appId == group.PrimaryAppId
                || !primaryLogs.TryGetValue((group.PrimaryAppId, log.Date.Date), out var primary))
            {
                result.Add(log);
                continue;
            }

            primary.Time += log.Time;
        }

        return result;
    }

    private static int LogAppId(DailyLogModel log) => log.AppModel?.ID ?? log.AppModelID;

    /// <summary>
    /// 给合并后的榜单行加上「合并 N 项」徽章，并把成员名放进悬停说明。
    /// 徽章 `CategoryId = 0`，点击会被分类徽章的命令忽略。
    /// </summary>
    public static void Decorate(IEnumerable<ChartsDataModel> rows, IReadOnlyList<AppMergeGroup> groups)
    {
        if (groups.Count == 0) return;

        var byPrimary = groups
            .GroupBy(g => g.PrimaryAppId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var row in rows)
        {
            var appId = row.Data switch
            {
                DailyLogModel daily => daily.AppModel?.ID ?? daily.AppModelID,
                Shared.Models.HoursLogModel hours => hours.AppModel?.ID ?? hours.AppModelID,
                AppModel app => app.ID,
                _ => 0
            };
            if (appId <= 0 || !byPrimary.TryGetValue(appId, out var group)) continue;

            row.Name = group.Name;
            row.PopupText = string.IsNullOrWhiteSpace(row.PopupText)
                ? $"合并 {group.MemberNames.Count} 项：{string.Join(" / ", group.MemberNames)}"
                : $"{row.PopupText}{Environment.NewLine}合并 {group.MemberNames.Count} 项：{string.Join(" / ", group.MemberNames)}";

            row.BadgeList ??= [];
            row.BadgeList.Add(new ChartBadgeModel
            {
                Name = string.Format(ResourceStrings.MergedItems, group.MemberNames.Count),
                Color = "#2E8A8A8A",
                TextColor = "#8A8A8A",
                CategoryId = 0,
                IsWebCategory = false,
                Type = ChartBadgeType.Ignore
            });
        }
    }
}

/// <summary>一个合并组：母成员保留原行，其余成员折叠进来</summary>
public sealed class AppMergeGroup
{
    /// <summary>稳定锚点（`安装目录|品牌`），覆盖表按它记录</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>组名（品牌，或用户改的名字）</summary>
    public string Name { get; init; } = string.Empty;

    public int PrimaryAppId { get; init; }

    public IReadOnlyList<int> MemberAppIds { get; init; } = [];

    public IReadOnlyList<string> MemberNames { get; init; } = [];

    public int TotalTime { get; init; }
}

/// <summary>用户对某个自动组的覆盖（解散 / 改名）</summary>
public sealed class AppMergeOverride
{
    public string? DisplayName { get; set; }

    public bool IsSuppressed { get; set; }
}
