using System;

namespace ShiJian.Client.Librarys;

/// <summary>
/// 全局动效开关。所有代码驱动的动画（页面进场、图表生长、数字滚动）统一读这里，
/// 不要在各地自己判断系统设置，否则开关和系统偏好会打架。
/// 由 <see cref="Servicers.MotionServicer"/> 根据配置与系统偏好写入。
/// </summary>
public static class Motion
{
    private static bool _isEnabled = true;

    /// <summary>当前是否允许播放动效</summary>
    public static bool IsEnabled => _isEnabled;

    /// <summary>开关变化（仅从允许变成禁止时控制器才需要处理，反向同理）</summary>
    public static event EventHandler? Changed;

    internal static void SetEnabled(bool enabled)
    {
        if (_isEnabled == enabled) return;
        _isEnabled = enabled;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
