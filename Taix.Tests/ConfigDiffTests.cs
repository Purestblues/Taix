using System.Linq;
using Taix.Client.Servicers.Instances;
using Taix.Client.Shared.Models.Config;
using Xunit;

namespace Taix.Tests;

/// <summary>
/// 配置变更检测是手写清单：新增配置项忘加进去，监听方就收不到变更
/// （动效开关第一版就踩过这个坑，所以这里把每一条路径都钉住）
/// </summary>
public class ConfigDiffTests
{
    private static ConfigModel CreateConfig()
    {
        return new ConfigModel
        {
            General = new GeneralModel(),
            Behavior = new BehaviorModel(),
            Shortcut = new ShortcutModel()
        };
    }

    [Fact]
    public void SameConfig_ProducesNoChanges()
    {
        var config = CreateConfig();

        Assert.Empty(ApiAppConfig.DetectChanges(config, CreateConfig()));
    }

    [Fact]
    public void MotionMode_IsReported()
    {
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.General.MotionMode = 2;

        var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

        Assert.Contains("General.MotionMode", changes);
    }

    [Fact]
    public void EachGeneralField_HasItsOwnPath()
    {
        var cases = new (string Path, System.Action<GeneralModel> Mutate)[]
        {
            ("General.Language", g => g.Language = 2),
            ("General.Theme", g => g.Theme = 2),
            ("General.ThemeColor", g => g.ThemeColor = "#FF0000"),
            ("General.WindowGradientScheme", g => g.WindowGradientScheme = 1),
            ("General.StartPage", g => g.StartPage = 2),
            ("General.IsAutoUpdate", g => g.IsAutoUpdate = false),
            ("General.IsWebEnabled", g => g.IsWebEnabled = true),
            ("General.IsEnableTray", g => g.IsEnableTray = false),
            ("General.IsSaveWindowSize", g => g.IsSaveWindowSize = true),
            ("General.DataRetentionDays", g => g.DataRetentionDays = 90),
            ("General.MotionMode", g => g.MotionMode = 1),
        };

        foreach (var (path, mutate) in cases)
        {
            var oldConfig = CreateConfig();
            var newConfig = CreateConfig();
            mutate(newConfig.General);

            var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

            Assert.Contains(path, changes);
            Assert.Single(changes);
        }
    }

    [Fact]
    public void ShortcutChanges_AreReported()
    {
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.Shortcut.Refresh = "F9";
        newConfig.Shortcut.Search = "Ctrl+P";
        newConfig.Shortcut.NavigateBack = "Alt+Right";

        var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

        Assert.Contains("Shortcut.Refresh", changes);
        Assert.Contains("Shortcut.Search", changes);
        Assert.Contains("Shortcut.NavigateBack", changes);
    }

    [Fact]
    public void NullShortcut_DoesNotThrow()
    {
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.Shortcut = null!;

        var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

        Assert.NotNull(changes);
        Assert.DoesNotContain("Shortcut.Refresh", changes);
    }
}
