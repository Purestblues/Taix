namespace ShiJian.Client.Servicers;

/// <summary>
/// 动效开关服务：按「配置 + 系统偏好」算出当前是否播放动效，并广播给界面。
/// </summary>
public interface IMotionServicer
{
    /// <summary>读取配置与系统偏好，须在配置加载完成后调用</summary>
    void Init();

    /// <summary>重新读取系统偏好（例如用户刚在系统设置里改过，窗口重新激活时）</summary>
    void RefreshFromSystem();

    /// <summary>当前是否播放动效</summary>
    bool IsAnimationEnabled { get; }
}
