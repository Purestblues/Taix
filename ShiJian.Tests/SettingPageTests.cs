using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShiJian.Client;
using ShiJian.Client.Controls.SettingPanel;
using ShiJian.Client.Librarys;
using ShiJian.Client.Models;
using ShiJian.Client.Platform.Abstractions.Primitives;
using ShiJian.Client.Servicers.Updater;
using ShiJian.Client.Shared.Helpers;
using ShiJian.Client.Shared.Models.Config;
using Xunit;

namespace ShiJian.Tests;

public class SettingPageTests
{
    [Fact]
    public void AppVersionHelper_PreservesForkSuffixAndNormalizesTag()
    {
        var displayVersion = AppVersionHelper.GetDisplayVersion(typeof(AppVersionHelper).Assembly);
        Assert.False(string.IsNullOrWhiteSpace(displayVersion));
        Assert.DoesNotContain('+', displayVersion);

        Assert.Equal("1.3.4-fork.11", AppVersionHelper.NormalizeTagVersion("v1.3.4-fork.11"));
        Assert.Equal("2.0.0-dev", AppVersionHelper.NormalizeTagVersion("V2.0.0-dev"));
        Assert.Equal(string.Empty, AppVersionHelper.NormalizeTagVersion("   "));
    }

    [Fact]
    public void GithubRelease_IsCanUpdate_HandlesNormalizedVersionsAndEmptyInfo()
    {
        var same = new GithubRelease("https://example.invalid", "1.3.4-fork.11");
        same.Info.Version = "1.3.4-fork.11";
        Assert.False(same.IsCanUpdate());

        var sameWithVPrefix = new GithubRelease("https://example.invalid", "v1.3.4-fork.11");
        sameWithVPrefix.Info.Version = "1.3.4-fork.11";
        Assert.False(sameWithVPrefix.IsCanUpdate());

        var newer = new GithubRelease("https://example.invalid", "1.3.4-fork.11");
        newer.Info.Version = "1.3.4-fork.12";
        Assert.True(newer.IsCanUpdate());

        var emptyRemote = new GithubRelease("https://example.invalid", "1.3.4-fork.11");
        Assert.False(emptyRemote.IsCanUpdate());
    }

    [Fact]
    public void SettingPageModel_ClampsTabIndexAndUpdatesActiveFlags()
    {
        var model = new SettingPageModel();
        var changedProps = new List<string>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        Assert.True(model.IsTab0Selected);
        Assert.False(model.IsTab3Selected);

        model.TabbarSelectedIndex = 3;
        Assert.Equal(3, model.TabbarSelectedIndex);
        Assert.False(model.IsTab0Selected);
        Assert.True(model.IsTab3Selected);
        Assert.Contains(nameof(SettingPageModel.IsTab3Selected), changedProps);

        model.TabbarSelectedIndex = 99;
        Assert.Equal(4, model.TabbarSelectedIndex);
        Assert.True(model.IsTab4Selected);

        model.TabbarSelectedIndex = -5;
        Assert.Equal(0, model.TabbarSelectedIndex);
        Assert.True(model.IsTab0Selected);
    }

    [Fact]
    public void GeneralAndBehaviorModels_MapEveryConfigToDiscreteTabsAndDropRetentionUi()
    {
        var validGeneralGroups = new HashSet<string>(StringComparer.Ordinal)
        {
            "基础", "Basics",
            "概览页", "Overview",
            "外观", "Appearance",
            "采集与统计", "Tracking"
        };
        var validBehaviorGroups = new HashSet<string>(StringComparer.Ordinal)
        {
            "监测偏好", "Monitoring",
            "忽略应用", "Ignore Application",
            "忽略URL", "Ignore URL",
            "应用白名单", "Application Whitelist"
        };

        foreach (var pi in typeof(GeneralModel).GetProperties())
        {
            var attrs = pi.GetCustomAttributes<ConfigAttribute>().ToList();
            if (pi.Name == nameof(GeneralModel.DataRetentionDays))
            {
                Assert.Empty(attrs);
                continue;
            }

            foreach (var attr in attrs)
            {
                Assert.Contains(attr.Group, validGeneralGroups);
            }
        }

        foreach (var pi in typeof(BehaviorModel).GetProperties())
        {
            foreach (var attr in pi.GetCustomAttributes<ConfigAttribute>())
            {
                Assert.Contains(attr.Group, validBehaviorGroups);
            }
        }
    }

    [AvaloniaFact]
    public void SettingPanel_IncludeGroups_FiltersRenderedGroupsAndPreservesSiblingProperties()
    {
        var originalLang = SystemLanguage.CurrentLanguage;
        SystemLanguage.CurrentLanguage = CultureCode.ZhCn;

        var config = new GeneralModel
        {
            Language = 1,
            ThemeColor = "#FF112233",
            Theme = 2
        };

        var basicPanel = new SettingPanel
        {
            IncludeGroups = "基础|Basics|概览页|Overview",
            Data = config
        };
        var appearancePanel = new SettingPanel
        {
            IncludeGroups = "外观|Appearance",
            Data = config
        };

        var window = new Window
        {
            Width = 600,
            Height = 600,
            Content = new StackPanel
            {
                Children = { basicPanel, appearancePanel }
            }
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var basicHeaders = basicPanel.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text)
                .ToList();
            Assert.Contains("基础", basicHeaders);
            Assert.Contains("概览页", basicHeaders);
            Assert.DoesNotContain("外观", basicHeaders);

            var appearanceHeaders = appearancePanel.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text)
                .ToList();
            Assert.Contains("外观", appearanceHeaders);
            Assert.DoesNotContain("基础", appearanceHeaders);

            // 深拷贝并写回同一份数据对象的某一项时，不应破坏另一分页持有的字段值
            var updated = Assert.IsType<GeneralModel>(SettingPanel.DeepCopy(appearancePanel.Data, typeof(GeneralModel)));
            updated.Theme = 1;
            basicPanel.Data = updated;
            appearancePanel.Data = updated;

            var latestBasic = Assert.IsType<GeneralModel>(basicPanel.Data);
            Assert.Equal("#FF112233", latestBasic.ThemeColor);
            Assert.Equal(1, latestBasic.Language);
            Assert.Equal(1, latestBasic.Theme);
        }
        finally
        {
            SystemLanguage.CurrentLanguage = originalLang;
            window.Close();
        }
    }
}
