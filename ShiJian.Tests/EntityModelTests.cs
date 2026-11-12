using System.Linq;
using ShiJian.Client.Servicers.Instances;
using ShiJian.Client.Shared.Models.Config;
using ShiJian.Client.Shared.Models.Entity;
using Xunit;

namespace ShiJian.Tests;

/// <summary>
/// N2 统一实体表的客户端约定：实体行的标识解析、以及两个新配置项的变更检测。
/// 实体表本身的互斥归属与差额行在服务端（`shijian-server/src/services/entity.rs`）有单测。
/// </summary>
public class EntityModelTests
{
    [Theory]
    [InlineData("app:12", 12)]
    [InlineData("site:5", 5)]
    [InlineData("other:3", 3)]
    [InlineData("bad", 0)]
    [InlineData("app:", 0)]
    [InlineData("app:abc", 0)]
    public void EntityId_ParsesKeySuffix(string key, int expected)
    {
        var model = new EntityModel { Key = key };

        Assert.Equal(expected, model.EntityId);
    }

    [Fact]
    public void MergedMemberCount_ComesFromMembers()
    {
        var model = new EntityModel
        {
            Key = "app:1",
            Members =
            [
                new EntityMemberModel { Key = "app:2", Name = "PiDeck-0.9.5-setup", Seconds = 60 },
                new EntityMemberModel { Key = "app:3", Name = "PiDeck-helper", Seconds = 10 }
            ]
        };

        Assert.Equal(2, model.Members.Count);
        Assert.Equal(70, model.Members.Sum(member => member.Seconds));
    }

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
    public void WebBrowserMerged_IsReported()
    {
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.General.WebBrowserMerged = true;

        var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

        Assert.Contains("General.WebBrowserMerged", changes);
    }

    [Fact]
    public void DefaultBrowser_IsReported()
    {
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.General.DefaultBrowser = "chrome";

        var changes = ApiAppConfig.DetectChanges(oldConfig, newConfig);

        Assert.Contains("General.DefaultBrowser", changes);
    }
}
