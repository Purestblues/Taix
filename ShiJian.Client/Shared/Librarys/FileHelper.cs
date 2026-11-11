using System;
using System.IO;

namespace ShiJian.Client.Shared.Librarys;

/// <summary>
/// 文件操作帮助类
/// </summary>
public class FileHelper
{
    private static string? _rootDirectory;

    /// <summary>
    /// 获取根目录
    /// </summary>
    /// <returns></returns>
    public static string GetRootDirectory()
    {
        if (_rootDirectory != null)
            return _rootDirectory;

#if MACOS
        _rootDirectory = "/Applications/ShiJianTools";
#else
        _rootDirectory = AppContext.BaseDirectory;
#endif
        return _rootDirectory;
    }

    public static string GetIconsDirectory()
    {
#if MACOS
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", "ShiJian");
#else
        return AppContext.BaseDirectory;
#endif
    }
}