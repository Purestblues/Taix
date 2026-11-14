using System;
using System.Reflection;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 应用版本号与 Release 标签标准化工具（N12）。
/// </summary>
public static class AppVersionHelper
{
    /// <summary>
    /// 提取用于界面展示和更新比对的标准化版本号（保留 <c>-fork.X</c> 等预发布后缀，剥离 <c>+gitHash</c> 元数据）。
    /// </summary>
    public static string GetDisplayVersion(Assembly? assembly = null)
    {
        var target = assembly ?? typeof(AppVersionHelper).Assembly;
        var infoVersion = target
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            var cleaned = plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
            cleaned = NormalizeTagVersion(cleaned);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }
        }

        return target.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    /// <summary>
    /// 标准化 GitHub Release Tag（仅移除开头的 <c>v</c> / <c>V</c>，避免误删预发布后缀中的字母）。
    /// </summary>
    public static string NormalizeTagVersion(string? rawTag)
    {
        return string.IsNullOrWhiteSpace(rawTag)
            ? string.Empty
            : rawTag.Trim().TrimStart('v', 'V');
    }
}
