using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia.Media;

namespace ShiJian.Client.Librarys.Image;

/// <summary>
/// Built-in 24x24 vector SVG category icon catalog (N10) replacing legacy Emoji bitmaps.
/// </summary>
public static class CategoryIconCatalog
{
    public sealed record CategoryIconEntry(string Key, string Label, string PathData);

    public const string DefaultIconKey = "svg:tag";
    public const string UncategorizedIconKey = "svg:inbox";

    private static readonly CategoryIconEntry[] Entries =
    [
        new("svg:code", "开发", "M9.4 16.6L4.8 12L9.4 7.4L8 6L2 12L8 18L9.4 16.6ZM14.6 16.6L19.2 12L14.6 7.4L16 6L22 12L16 18L14.6 16.6Z"),
        new("svg:briefcase", "办公", "M20 7H16V5C16 3.9 15.1 3 14 3H10C8.9 3 8 3.9 8 5V7H4C2.9 7 2 7.9 2 9V19C2 20.1 2.9 21 4 21H20C21.1 21 22 20.1 22 19V9C22 7.9 21.1 7 20 7ZM10 5H14V7H10V5Z"),
        new("svg:sparkles", "AI", "M12 2L14.4 8.6L21 11L14.4 13.4L12 20L9.6 13.4L3 11L9.6 8.6L12 2ZM19 16L20 18.5L22.5 19.5L20 20.5L19 23L18 20.5L15.5 19.5L18 18.5L19 16Z"),
        new("svg:gamepad", "娱乐", "M21 6H3C1.9 6 1 6.9 1 8V16C1 17.1 1.9 18 3 18H21C22.1 18 23 17.1 23 16V8C23 6.9 22.1 6 21 6ZM11 13H8V16H6V13H3V11H6V8H8V11H11V13ZM15.5 15C14.67 15 14 14.33 14 13.5C14 12.67 14.67 12 15.5 12C16.33 12 17 12.67 17 13.5C17 14.33 16.33 15 15.5 15ZM19.5 12C18.67 12 18 11.33 18 10.5C18 9.67 18.67 9 19.5 9C20.33 9 21 9.67 21 10.5C21 11.33 20.33 12 19.5 12Z"),
        new("svg:message", "社交", "M20 2H4C2.9 2 2 2.9 2 4V22L6 18H20C21.1 18 22 17.1 22 16V4C22 2.9 21.1 2 20 2ZM17 11H7V9H17V11ZM14 14H7V12H14V14ZM17 8H7V6H17V8Z"),
        new("svg:book", "学习", "M12 5.5C10.5 4.3 8.4 3.5 6 3.5C4.2 3.5 2.7 4 2 4.5V19C2.7 18.6 4.2 18 6 18C8.4 18 10.5 18.8 12 20C13.5 18.8 15.6 18 18 18C19.8 18 21.3 18.6 22 19V4.5C21.3 4 19.8 3.5 18 3.5C15.6 3.5 13.5 4.3 12 5.5Z"),
        new("svg:palette", "设计", "M12 2C6.49 2 2 6.49 2 12C2 17.51 6.49 22 12 22C13.38 22 14.5 20.88 14.5 19.5C14.5 18.89 14.27 18.33 13.89 17.9C13.54 17.5 13.33 16.98 13.33 16.41C13.33 15.08 14.41 14 15.74 14H17.5C20 14 22 12 22 9.5C22 5.36 17.52 2 12 2ZM6.5 12C5.67 12 5 11.33 5 10.5C5 9.67 5.67 9 6.5 9C7.33 9 8 9.67 8 10.5C8 11.33 7.33 12 6.5 12ZM9.5 8C8.67 8 8 7.33 8 6.5C8 5.67 8.67 5 9.5 5C10.33 5 11 5.67 11 6.5C11 7.33 10.33 8 9.5 8ZM14.5 8C13.67 8 13 7.33 13 6.5C13 5.67 13.67 5 14.5 5C15.33 5 16 5.67 16 6.5C16 7.33 15.33 8 14.5 8ZM17.5 12C16.67 12 16 11.33 16 10.5C16 9.67 16.67 9 17.5 9C18.33 9 19 9.67 19 10.5C19 11.33 18.33 12 17.5 12Z"),
        new("svg:film", "影音", "M4 4H20C21.1 4 22 4.9 22 6V18C22 19.1 21.1 20 20 20H4C2.9 20 2 19.1 2 18V6C2 4.9 2.9 4 4 4ZM10 15.5L16 12L10 8.5V15.5Z"),
        new("svg:wrench", "工具", "M22.7 19L13.6 9.9C14.5 7.6 14 4.9 12.1 3C10.1 1 7.1 0.6 4.7 1.7L9 6L6 9L1.6 4.7C0.4 7.1 0.9 10.1 2.9 12.1C4.8 14 7.5 14.5 9.8 13.6L18.9 22.7C19.3 23.1 19.9 23.1 20.3 22.7L22.6 20.4C23.1 20 23.1 19.3 22.7 19Z"),
        new("svg:tag", "标签", "M21.41 11.58L12.41 2.58C12.05 2.22 11.55 2 11 2H4C2.9 2 2 2.9 2 4V11C2 11.55 2.22 12.05 2.59 12.42L11.59 21.42C11.95 21.78 12.45 22 13 22C13.55 22 14.05 21.78 14.41 21.41L21.41 14.41C21.78 14.05 22 13.55 22 13C22 12.45 21.77 11.94 21.41 11.58ZM5.5 7C4.67 7 4 6.33 4 5.5C4 4.67 4.67 4 5.5 4C6.33 4 7 4.67 7 5.5C7 6.33 6.33 7 5.5 7Z"),
        new("svg:inbox", "收件箱", "M19 3H5C3.9 3 3 3.9 3 5V19C3 20.1 3.9 21 5 21H19C20.1 21 21 20.1 21 19V5C21 3.9 20.1 3 19 3ZM19 15H15C15 16.66 13.66 18 12 18C10.34 18 9 16.66 9 15H5V5H19V15Z"),
        new("svg:terminal", "终端", "M20 4H4C2.89 4 2 4.9 2 6V18C2 19.1 2.89 20 4 20H20C21.1 20 22 19.1 22 18V6C22 4.9 21.1 4 20 4ZM7.5 16L6.1 14.6L9.7 11L6.1 7.4L7.5 6L12.5 11L7.5 16ZM18 16H12V14H18V16Z"),
        new("svg:globe", "网络", "M12 2C6.48 2 2 6.48 2 12C2 17.52 6.48 22 12 22C17.52 22 22 17.52 22 12C22 6.48 17.52 2 12 2ZM11 19.93C7.05 19.44 4 16.08 4 12C4 11.38 4.08 10.79 4.21 10.21L9 15V16C9 17.1 9.9 18 11 18V19.93ZM17.9 17.39C17.64 16.58 16.9 16 16 16H15V13C15 12.45 14.55 12 14 12H8V10H10C10.55 10 11 9.55 11 9V7H13C14.1 7 15 6.1 15 5V4.59C17.93 5.78 20 8.65 20 12C20 14.08 19.2 15.97 17.9 17.39Z"),
        new("svg:music", "音乐", "M12 3V13.55C11.41 13.21 10.73 13 10 13C7.79 13 6 14.79 6 17C6 19.21 7.79 21 10 21C12.21 21 14 19.21 14 17V7H18V3H12Z"),
        new("svg:camera", "影像", "M9 2L7.17 4H4C2.9 4 2 4.9 2 6V18C2 19.1 2.9 20 4 20H20C21.1 20 22 19.1 22 18V6C22 4.9 21.1 4 20 4H16.83L15 2H9ZM12 17C9.24 17 7 14.76 7 12C7 9.24 9.24 7 12 7C14.76 7 17 9.24 17 12C17 14.76 14.76 17 12 17ZM12 9C10.34 9 9 10.34 9 12C9 13.66 10.34 15 12 15C13.66 15 15 13.66 15 12C15 10.34 13.66 9 12 9Z"),
        new("svg:headphones", "音频", "M12 3C7.03 3 3 7.03 3 12V19C3 20.1 3.9 21 5 21H9V13H5V12C5 8.13 8.13 5 12 5C15.87 5 19 8.13 19 12V13H15V21H19C20.1 21 21 20.1 21 19V12C21 7.03 16.97 3 12 3Z"),
        new("svg:shopping", "购物", "M7 18C5.9 18 5.01 18.9 5.01 20C5.01 21.1 5.9 22 7 22C8.1 22 9 21.1 9 20C9 18.9 8.1 18 7 18ZM1 2V4H3L6.6 11.59L5.25 14.04C5.09 14.32 5 14.65 5 15C5 16.1 5.9 17 7 17H19V15H7.42C7.28 15 7.17 14.89 7.17 14.75L7.2 14.63L8.1 13H15.55C16.3 13 16.96 12.59 17.3 11.97L20.88 5.48C20.96 5.34 21 5.17 21 5C21 4.45 20.55 4 20 4H5.21L4.27 2H1ZM17 18C15.9 18 15.01 18.9 15.01 20C15.01 21.1 15.9 22 17 22C18.1 22 19 21.1 19 20C19 18.9 18.1 18 17 18Z"),
        new("svg:heart", "生活", "M12 21.35L10.55 20.03C5.4 15.36 2 12.28 2 8.5C2 5.42 4.42 3 7.5 3C9.24 3 10.91 3.81 12 5.09C13.09 3.81 14.76 3 16.5 3C19.58 3 22 5.42 22 8.5C22 12.28 18.6 15.36 13.45 20.04L12 21.35Z"),
        new("svg:star", "精选", "M12 17.27L18.18 21L16.54 13.97L22 9.24L14.81 8.63L12 2L9.19 8.63L2 9.24L7.46 13.97L5.82 21L12 17.27Z"),
        new("svg:compass", "资讯", "M12 2C6.48 2 2 6.48 2 12C2 17.52 6.48 22 12 22C17.52 22 22 17.52 22 12C22 6.48 17.52 2 12 2ZM14.19 14.19L6 18L9.81 9.81L18 6L14.19 14.19ZM12 10.9C11.39 10.9 10.9 11.39 10.9 12C10.9 12.61 11.39 13.1 12 13.1C12.61 13.1 13.1 12.61 13.1 12C13.1 11.39 12.61 10.9 12 10.9Z"),
        new("svg:cloud", "云端", "M19.35 10.04C18.67 6.59 15.64 4 12 4C9.11 4 6.6 5.64 5.35 8.04C2.34 8.36 0 10.91 0 14C0 17.31 2.69 20 6 20H19C21.76 20 24 17.76 24 15C24 12.36 21.95 10.22 19.35 10.04Z"),
        new("svg:shield", "安全", "M12 2L4 5V11.09C4 16.14 7.41 20.85 12 22C16.59 20.85 20 16.14 20 11.09V5L12 2ZM10 17L6 13L7.41 11.59L10 14.17L16.59 7.58L18 9L10 17Z"),
        new("svg:file", "文档", "M14 2H6C4.9 2 4 2.9 4 4V20C4 21.1 4.9 22 6 22H18C19.1 22 20 21.1 20 20V8L14 2ZM16 18H8V16H16V18ZM16 14H8V12H16V14ZM13 9V3.5L18.5 9H13Z"),
        new("svg:clock", "日程", "M12 2C6.48 2 2 6.48 2 12C2 17.52 6.48 22 12 22C17.52 22 22 17.52 22 12C22 6.48 17.52 2 12 2ZM16.2 16.2L11 13V7H12.5V12.2L17 14.9L16.2 16.2Z"),
        new("svg:rocket", "效率", "M12 2C9.5 5 8 8.5 8 12.5L5 15.5V19L9 17.5C10 18.2 11 18.5 12 18.5C13 18.5 14 18.2 15 17.5L19 19V15.5L16 12.5C16 8.5 14.5 5 12 2ZM12 11.5C11.17 11.5 10.5 10.83 10.5 10C10.5 9.17 11.17 8.5 12 8.5C12.83 8.5 13.5 9.17 13.5 10C13.5 10.83 12.83 11.5 12 11.5ZM10 20.5H14V22H10V20.5Z"),
        new("svg:zap", "自动化", "M13 2L4 14H11L10 22L20 10H13L13 2Z"),
        new("svg:chart", "数据", "M5 9.2H8V19H5V9.2ZM10.6 5H13.4V19H10.6V5ZM16.2 13H19V19H16.2V13Z"),
        new("svg:folder", "项目", "M10 4H4C2.9 4 2 4.9 2 6V18C2 19.1 2.9 20 4 20H20C21.1 20 22 19.1 22 18V8C22 6.9 21.1 6 20 6H12L10 4Z"),
        new("svg:cpu", "算力", "M9 3V5H7C5.9 5 5 5.9 5 7V9H3V11H5V13H3V15H5V17C5 18.1 5.9 19 7 19H9V21H11V19H13V21H15V19H17C18.1 19 19 18.1 19 17V15H21V13H19V11H21V9H19V7C19 5.9 18.1 5 17 5H15V3H13V5H11V3H9ZM9 9H15V15H9V9Z"),
        new("svg:coffee", "休闲", "M18 8V10H19.5C20.05 10 20.5 9.55 20.5 9C20.5 8.45 20.05 8 19.5 8H18ZM4 6H19.5C21.16 6 22.5 7.34 22.5 9C22.5 10.66 21.16 12 19.5 12H18V13C18 16.87 14.87 20 11 20C7.13 20 4 16.87 4 13V6ZM2 21H20V22.5H2V21Z"),
        new("svg:layers", "架构", "M11.99 18.54L4.62 12.81L3 14.07L12 21.07L21 14.07L19.37 12.8L11.99 18.54ZM12 16L19.36 10.27L21 9L12 2L3 9L4.63 10.27L12 16Z"),
        new("svg:lightbulb", "灵感", "M9 21C9 21.55 9.45 22 10 22H14C14.55 22 15 21.55 15 21V20H9V21ZM12 2C8.14 2 5 5.14 5 9C5 11.38 6.19 13.47 8 14.74V17C8 17.55 8.45 18 9 18H15C15.55 18 16 17.55 16 17V14.74C17.81 13.47 19 11.38 19 9C19 5.14 15.86 2 12 2Z"),
    ];

    private static readonly Dictionary<string, CategoryIconEntry> ByKey;
    private static readonly ConcurrentDictionary<string, Geometry> ParsedGeometryCache = new(StringComparer.OrdinalIgnoreCase);

    static CategoryIconCatalog()
    {
        ByKey = new Dictionary<string, CategoryIconEntry>(Entries.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
            ByKey[entry.Key] = entry;
    }

    public static IReadOnlyList<CategoryIconEntry> All => Entries;

    public static IReadOnlyList<string> AllKeys
    {
        get
        {
            var keys = new string[Entries.Length];
            for (var i = 0; i < Entries.Length; i++)
                keys[i] = Entries[i].Key;
            return keys;
        }
    }

    /// <summary>
    /// Resolves an icon URL or legacy Emoji PNG URI into a canonical SVG icon key, if applicable.
    /// </summary>
    public static string? ResolveKey(string? rawIcon)
    {
        if (string.IsNullOrWhiteSpace(rawIcon))
            return null;

        var trimmed = rawIcon.Trim();
        if (ByKey.TryGetValue(trimmed, out var direct))
            return direct.Key;

        // Legacy Emoji asset compatibility: avares://ShiJian/Resources/Emoji/(N).png
        if (TryExtractLegacyEmojiIndex(trimmed, out var index))
            return MapLegacyEmojiIndex(index);

        return null;
    }

    public static bool IsSvgIcon(string? rawIcon) => ResolveKey(rawIcon) != null;

    public static string? GetPathData(string? rawIcon)
    {
        var key = ResolveKey(rawIcon);
        return key != null && ByKey.TryGetValue(key, out var entry) ? entry.PathData : null;
    }

    public static bool TryGetGeometry(string? rawIcon, out Geometry? geometry)
    {
        geometry = null;
        var key = ResolveKey(rawIcon);
        if (key == null || !ByKey.TryGetValue(key, out var entry))
            return false;

        geometry = ParsedGeometryCache.GetOrAdd(key, _ => Geometry.Parse(entry.PathData));
        return true;
    }

    public static string MapLegacyEmojiIndex(int index)
    {
        if (index <= 0) return DefaultIconKey;
        return Entries[(index - 1) % Entries.Length].Key;
    }

    private static bool TryExtractLegacyEmojiIndex(string path, out int index)
    {
        index = 0;
        const string prefix = "Emoji/(";
        var pos = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (pos < 0) return false;
        var start = pos + prefix.Length;
        var end = path.IndexOf(").png", start, StringComparison.OrdinalIgnoreCase);
        if (end <= start) return false;
        return int.TryParse(path.AsSpan(start, end - start), out index);
    }
}
