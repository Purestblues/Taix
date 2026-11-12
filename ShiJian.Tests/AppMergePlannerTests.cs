using System;
using System.Collections.Generic;
using System.Linq;
using ShiJian.Client.Controls.Charts.Model;
using ShiJian.Client.Librarys;
using ShiJian.Client.Shared.Models;
using Xunit;

namespace ShiJian.Tests;

/// <summary>
/// N6 子母程序合并的判定与折叠。这些用例就是「不许误判」的护栏：
/// 并错了的表现是「A 的时长跑到 B 头上」，而且用户很难自己发现。
/// </summary>
public class AppMergePlannerTests
{
    private static AppModel App(int id, string name, string? file, int totalTime = 0, string? description = null) => new()
    {
        ID = id,
        Name = name,
        File = file,
        TotalTime = totalTime,
        Description = description
    };

    private static DailyLogModel Log(int appId, int time, DateTime date, string name = "app") => new()
    {
        AppModelID = appId,
        Time = time,
        Date = date,
        AppModel = App(appId, name, $"C:\\Tools\\{name}\\{name}.exe")
    };

    [Theory]
    [InlineData(@"C:\Program Files\WPS Office\public-ip\office6\wps.exe", "c:/program files/wps office/public-ip/office6")]
    [InlineData(@"C:\Program Files\Foo\bin\foo.exe", "c:/program files/foo")]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\Foo\1.2.0\Foo.exe", "c:/users/me/appdata/local/programs/foo")]
    [InlineData(@"/Applications/Safari.app/Contents/MacOS/Safari", "/applications/safari.app")]
    [InlineData("just_a_name", null)]
    [InlineData(@"C:\evil.exe", null)]
    public void AnchorOf_NormalizesInstallDirectory(string file, string? expected)
    {
        Assert.Equal(expected, AppMergePlanner.AnchorOf(file));
    }

    [Theory]
    [InlineData("PiDeck-0.9.5-setup", "PiDeck")]
    [InlineData("ShiJian-1.3.4-fork.6-Setup", "ShiJian")]
    [InlineData("Clash.Verge_2.5.8_x64-setup", "Clash.Verge")]
    [InlineData("TextFold-win-x64-setup", "TextFold")]
    [InlineData("kimi_3.2.20", "kimi")]
    [InlineData("FooUpdater", "Foo")]
    [InlineData("setup", null)]
    [InlineData("_1.11.0_x64-setup", null)]
    public void BrandOf_KeepsOnlyTheProductName(string name, string? expected)
    {
        Assert.Equal(expected, AppMergePlanner.BrandOf(name));
    }

    [Fact]
    public void Build_MergesVersionVariantsInTheSameFolder()
    {
        var apps = new[]
        {
            App(1, "PiDeck-0.9.5-setup", @"D:\Downloads\PiDeck-0.9.5-setup.exe", 44),
            App(2, "PiDeck-0.11.3-setup", @"D:\Downloads\PiDeck-0.11.3-setup.exe", 19),
            App(3, "Unrelated", @"D:\Downloads\Unrelated.exe", 100)
        };

        var groups = AppMergePlanner.Build(apps);

        var group = Assert.Single(groups);
        Assert.Equal("PiDeck", group.Name);
        Assert.Equal(1, group.PrimaryAppId);
        Assert.Equal([1, 2], group.MemberAppIds);
        Assert.Equal(63, group.TotalTime);
    }

    [Fact]
    public void Build_DoesNotMergeDifferentProductsInTheSameFolder()
    {
        var apps = new[]
        {
            App(1, "Bionic-1.1.10-8-x64", @"D:\Downloads\Bionic-1.1.10-8-x64.exe", 53),
            App(2, "LM-Studio-0.4.27-1-x64", @"D:\Downloads\LM-Studio-0.4.27-1-x64.exe", 34)
        };

        Assert.Empty(AppMergePlanner.Build(apps));
    }

    [Fact]
    public void Build_NeverMergesBrowsersOrSystemComponents()
    {
        var apps = new[]
        {
            App(1, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 100),
            App(2, "chrome_crashpad", @"C:\Program Files\Google\Chrome\Application\chrome_crashpad.exe", 5),
            App(3, "svchost", @"C:\Windows\System32\svchost.exe", 100),
            App(4, "svchost-2", @"C:\Windows\System32\svchost-2.exe", 40)
        };

        Assert.Empty(AppMergePlanner.Build(apps));
    }

    [Fact]
    public void Build_PrimaryIsTheLongestUsedAndTiesPreferShorterPath()
    {
        var apps = new[]
        {
            App(1, "Foo-1.0", @"C:\Program Files\Foo\bin\Foo-1.0.exe", 10),
            App(2, "Foo-2.0", @"C:\Program Files\Foo\Foo-2.0.exe", 10),
            App(3, "Foo-3.0", @"C:\Program Files\Foo\Foo-3.0.exe", 99)
        };

        var group = Assert.Single(AppMergePlanner.Build(apps));
        Assert.Equal(3, group.PrimaryAppId);

        var tie = AppMergePlanner.Build(apps.Where(a => a.ID != 3));
        Assert.Equal(2, Assert.Single(tie).PrimaryAppId);
    }

    [Fact]
    public void Build_HonoursDissolveAndRenameOverrides()
    {
        var apps = new[]
        {
            App(1, "PiDeck-0.9.5-setup", @"D:\Downloads\PiDeck-0.9.5-setup.exe", 44),
            App(2, "PiDeck-0.11.3-setup", @"D:\Downloads\PiDeck-0.11.3-setup.exe", 19)
        };

        var groups = AppMergePlanner.Build(apps);
        var key = Assert.Single(groups).Key;

        var dissolved = new Dictionary<string, AppMergeOverride>
        {
            [key] = new() { IsSuppressed = true }
        };
        Assert.Empty(AppMergePlanner.Build(apps, dissolved));

        var renamed = new Dictionary<string, AppMergeOverride>
        {
            [key] = new() { DisplayName = "派德克" }
        };
        Assert.Equal("派德克", Assert.Single(AppMergePlanner.Build(apps, renamed)).Name);
    }

    [Fact]
    public void Apply_FoldsChildrenIntoThePrimaryOfTheSameDay()
    {
        var day = new DateTime(2026, 11, 12);
        var next = day.AddDays(1);
        var apps = new[]
        {
            App(1, "PiDeck-0.9.5-setup", @"D:\Downloads\PiDeck-0.9.5-setup.exe", 44),
            App(2, "PiDeck-0.11.3-setup", @"D:\Downloads\PiDeck-0.11.3-setup.exe", 19)
        };
        var groups = AppMergePlanner.Build(apps);

        var logs = new List<DailyLogModel>
        {
            Log(2, 30, day, "PiDeck-0.11.3-setup"),
            Log(1, 60, day, "PiDeck-0.9.5-setup"),
            Log(2, 5, next, "PiDeck-0.11.3-setup")
        };

        var merged = AppMergePlanner.Apply(logs, groups);

        Assert.Equal(2, merged.Count);
        Assert.Equal(90, merged.Single(m => m.Date == day).Time);
        // 另一天的母成员行缺席：子成员那天的时间不能丢（会加回母成员自己的行）
        Assert.Equal(5, merged.Single(m => m.Date == next).Time);
        Assert.Equal(95, merged.Sum(m => m.Time));
    }

    [Fact]
    public void Apply_KeepsEverythingWhenThereAreNoGroups()
    {
        var logs = new List<DailyLogModel> { Log(1, 10, DateTime.Today), Log(2, 20, DateTime.Today) };

        var merged = AppMergePlanner.Apply(logs, []);

        Assert.Equal(2, merged.Count);
        Assert.Equal(30, merged.Sum(m => m.Time));
    }

    [Fact]
    public void Apply_DoesNotTouchAppsOutsideGroups()
    {
        var day = DateTime.Today;
        var apps = new[]
        {
            App(1, "PiDeck-0.9.5-setup", @"D:\Downloads\PiDeck-0.9.5-setup.exe"),
            App(2, "PiDeck-0.11.3-setup", @"D:\Downloads\PiDeck-0.11.3-setup.exe")
        };
        var groups = AppMergePlanner.Build(apps);

        var logs = new List<DailyLogModel> { Log(1, 10, day), Log(2, 20, day), Log(3, 7, day) };
        var merged = AppMergePlanner.Apply(logs, groups);

        Assert.Equal(2, merged.Count);
        Assert.Equal(7, merged.Single(m => m.AppModelID == 3).Time);
    }

    [Fact]
    public void Decorate_RenamesTheRowAndAddsAMergedBadge()
    {
        var apps = new[]
        {
            App(1, "PiDeck-0.9.5-setup", @"D:\Downloads\PiDeck-0.9.5-setup.exe", 44),
            App(2, "PiDeck-0.11.3-setup", @"D:\Downloads\PiDeck-0.11.3-setup.exe", 19)
        };
        var groups = AppMergePlanner.Build(apps);

        var row = new ChartsDataModel
        {
            Data = Log(1, 60, DateTime.Today, "PiDeck-0.9.5-setup"),
            Name = "PiDeck-0.9.5-setup",
            Value = 60
        };

        AppMergePlanner.Decorate([row], groups);

        Assert.Equal("PiDeck", row.Name);
        var badge = Assert.Single(row.BadgeList);
        Assert.Equal(0, badge.CategoryId); // 点徽章不会跳到分类 0
        Assert.Contains("0.11.3", row.PopupText);
    }
}
