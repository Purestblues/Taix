using Taix.Client.Platform;
using Taix.Client.Shared.Models.Config;
using Xunit;

namespace Taix.Tests;

public class WindowStateTests
{
    [Theory]
    [InlineData(WindowStateKind.Normal)]
    [InlineData(WindowStateKind.Maximized)]
    public void ResolveRestoreState_KeepsNormalAndMaximized(WindowStateKind saved)
    {
        Assert.Equal(saved, WindowStatePolicy.ResolveRestoreState(saved, canRestoreFullScreen: true));
        Assert.Equal(saved, WindowStatePolicy.ResolveRestoreState(saved, canRestoreFullScreen: false));
    }

    [Fact]
    public void ResolveRestoreState_FullScreen_KeptWhenPlatformSupportsIt()
    {
        Assert.Equal(WindowStateKind.FullScreen,
            WindowStatePolicy.ResolveRestoreState(WindowStateKind.FullScreen, canRestoreFullScreen: true));
    }

    [Fact]
    public void ResolveRestoreState_FullScreen_DowngradedToMaximizedWhenUnsupported()
    {
        // macOS 上全屏是独立桌面空间，冷启动不能把用户切走
        Assert.Equal(WindowStateKind.Maximized,
            WindowStatePolicy.ResolveRestoreState(WindowStateKind.FullScreen, canRestoreFullScreen: false));
    }

    [Fact]
    public void Snapshot_WithPositiveSize_IsValid()
    {
        var snapshot = new WindowSnapshot(100, 200, 1200, 800, WindowStateKind.Normal);

        Assert.True(snapshot.IsValid);
    }

    [Theory]
    [InlineData(0, 800)]
    [InlineData(-1, 800)]
    [InlineData(1200, 0)]
    [InlineData(double.NaN, 800)]
    [InlineData(1200, double.PositiveInfinity)]
    public void Snapshot_WithBadSize_IsInvalid(double width, double height)
    {
        var snapshot = new WindowSnapshot(0, 0, width, height, WindowStateKind.Normal);

        Assert.False(snapshot.IsValid);
    }

    [Fact]
    public void Snapshot_WithNonFinitePosition_IsInvalid()
    {
        Assert.False(new WindowSnapshot(double.NaN, 0, 100, 100, WindowStateKind.Normal).IsValid);
        Assert.False(new WindowSnapshot(0, double.NegativeInfinity, 100, 100, WindowStateKind.Normal).IsValid);
    }
}
