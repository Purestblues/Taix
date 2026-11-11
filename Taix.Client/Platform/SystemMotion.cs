using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Taix.Client.Platform;

/// <summary>
/// 读取系统的「减少动画 / Reduce Motion」偏好。
/// 读不到时一律按「允许动画」处理，避免因为平台差异把动效全部关掉。
/// </summary>
public static class SystemMotion
{
    /// <summary>系统是否允许播放动画</summary>
    public static bool IsAnimationAllowed()
    {
        try
        {
            if (PlatformHelper.IsWindows) return IsWindowsAnimationEnabled();
            if (PlatformHelper.IsMacOS) return !IsMacReduceMotionEnabled();
            return true;
        }
        catch
        {
            return true;
        }
    }

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAnimationEnabled()
    {
        // SPI_GETCLIENTAREAANIMATION 返回 BOOL（4 字节），对应「设置 → 辅助功能 → 视觉效果 → 动画效果」
        if (!SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0)) return true;
        return enabled != 0;
    }

    // ── macOS：NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceMotion ──
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ObjcMsgSendBool(IntPtr receiver, IntPtr selector);

    [SupportedOSPlatform("macos")]
    private static bool IsMacReduceMotionEnabled()
    {
        var workspaceClass = ObjcGetClass("NSWorkspace");
        if (workspaceClass == IntPtr.Zero) return false;

        var sharedSelector = SelRegisterName("sharedWorkspace");
        var workspace = ObjcMsgSendPtr(workspaceClass, sharedSelector);
        if (workspace == IntPtr.Zero) return false;

        var flagSelector = SelRegisterName("accessibilityDisplayShouldReduceMotion");
        return ObjcMsgSendBool(workspace, flagSelector);
    }
}
