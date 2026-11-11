using System;
using ShiJian.Client.Librarys;
using ShiJian.Client.Platform;
using ShiJian.Client.Shared.Event;
using ShiJian.Client.Shared.Models.Config;
using ShiJian.Client.Shared.Servicers.Interfaces;

namespace ShiJian.Client.Servicers;

public class MotionServicer : IMotionServicer
{
    private const int FollowSystem = 0;
    private const int AlwaysOn = 1;
    private const int AlwaysOff = 2;

    private readonly IAppConfig _appConfig;

    public MotionServicer(IAppConfig appConfig)
    {
        _appConfig = appConfig;
        _appConfig.ConfigChanged += OnConfigChanged;
    }

    public bool IsAnimationEnabled => Motion.IsEnabled;

    public void Init()
    {
        Apply();
    }

    public void RefreshFromSystem()
    {
        // 只有「跟随系统」时才需要重读系统设置，手动指定时系统改了也不该跟着变
        if (GetMode() != FollowSystem) return;
        Apply();
    }

    private void OnConfigChanged(object? sender, ConfigChangedEventArgs e)
    {
        if (e.HasChange("General.MotionMode")) Apply();
    }

    private int GetMode()
    {
        var mode = _appConfig.GetConfig().General.MotionMode;
        return mode is AlwaysOn or AlwaysOff ? mode : FollowSystem;
    }

    private void Apply()
    {
        var enabled = GetMode() switch
        {
            AlwaysOn => true,
            AlwaysOff => false,
            _ => SystemMotion.IsAnimationAllowed()
        };

        Motion.SetEnabled(enabled);
    }
}
