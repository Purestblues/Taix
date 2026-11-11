using System;
using Taix.Client.Shared.Models.Config;

namespace Taix.Client.Platform;

internal static class WindowStatePolicy
{
    /// <summary>
    /// 是否必须等窗口完成显示之后再设置窗口状态
    /// macOS 的 NSWindow 在尚未完成 show 时，zoom / fullscreen 会被平台层忽略；
    /// Windows 在 Show 之前设置即可生效
    /// </summary>
    public static bool MustApplyStateAfterOpened => OperatingSystem.IsMacOS();

    /// <summary>
    /// 是否在启动时恢复全屏状态
    /// macOS 的全屏是独立的桌面空间，冷启动时自动切空间会把用户从当前工作区拽走，
    /// 因此降级为最大化。Windows 的全屏就是最大化，照原样恢复
    /// </summary>
    public static bool CanRestoreFullScreen => !OperatingSystem.IsMacOS();

    /// <summary>
    /// 冷启动要恢复成什么状态：平台不支持恢复全屏时降级为最大化
    /// （纯函数，便于单测；平台判断走 <see cref="CanRestoreFullScreen"/>）
    /// </summary>
    public static WindowStateKind ResolveRestoreState(WindowStateKind saved, bool canRestoreFullScreen) =>
        saved == WindowStateKind.FullScreen && !canRestoreFullScreen
            ? WindowStateKind.Maximized
            : saved;
}
