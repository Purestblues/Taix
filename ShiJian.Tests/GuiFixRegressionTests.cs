using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShiJian.Client.Controls.Charts;
using ShiJian.Client.Controls.Charts.Model;
using ShiJian.Client.Controls.Select;
using ShiJian.Client.Librarys;
using ShiJian.Client.Librarys.Api;
using ShiJian.Client.Librarys.Image;
using ShiJian.Client.Models;
using ShiJian.Client.Resources.Localization;
using ShiJian.Client.Servicers.Interfaces;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Models.Config;
using ShiJian.Client.Shared.Models.Web;
using ShiJian.Client.Shared.Servicers.Interfaces;
using ShiJian.Client.ViewModels;
using ShiJian.Client.Views;
using Xunit;

namespace ShiJian.Tests;

public class GuiFixRegressionTests
{
    [Fact]
    public void IndexPageViewModel_BuildOverviewRankings_HandlesEmptyAndShortListsWithoutThrow()
    {
        var shortApps = new List<ChartsDataModel>
        {
            new() { Name = "Terminal", Value = 1800 }
        };
        var webs = new List<ChartsDataModel>
        {
            new() { Name = "GitHub", Value = 1500 },
            new() { Name = "Docs", Value = 1200 },
            new() { Name = "Bilibili", Value = 900 },
            new() { Name = "Zhihu", Value = 300 }
        };

        var (appFreq, webFreq, more) = IndexPageViewModel.BuildOverviewRankings(shortApps, webs, frequentUseNum: 2, moreNum: 11);

        Assert.Single(appFreq);
        Assert.Equal("Terminal", appFreq[0].Name);
        Assert.Equal(2, webFreq.Count);
        Assert.Equal(["Bilibili", "Zhihu"], more.Select(m => m.Name));

        var (appOnly, webEmpty, moreApps) = IndexPageViewModel.BuildOverviewRankings(
            [
                new() { Name = "A1", Value = 500 },
                new() { Name = "A2", Value = 400 },
                new() { Name = "A3", Value = 300 },
                new() { Name = "A4", Value = 100 }
            ],
            [],
            frequentUseNum: 2,
            moreNum: 2);

        Assert.Equal(2, appOnly.Count);
        Assert.Empty(webEmpty);
        Assert.Equal(["A3", "A4"], moreApps.Select(m => m.Name));

        var (negApp, negWeb, negMore) = IndexPageViewModel.BuildOverviewRankings(shortApps, webs, frequentUseNum: -2, moreNum: -1);
        Assert.Empty(negApp);
        Assert.Empty(negWeb);
        Assert.Empty(negMore);
    }

    [AvaloniaFact]
    public void ColumnChart_StackedMultiRowCategoryLegend_DoesNotClipBelowMinHeight()
    {
        var data = Enumerable.Range(1, 14)
            .Select(i => new ChartsDataModel
            {
                Name = $"分类条目{i:D2}",
                Color = i % 2 == 0 ? "#FF4A90E2" : "#FF2ECC71",
                Values = [100 + i * 10, 80, 120, 60, 90, 70, 50]
            })
            .ToList();

        var chart = new ColumnChart
        {
            IsStack = true,
            IsShowCategory = true,
            MinHeight = 310,
            Width = 480,
            Data = data
        };

        var window = new Window
        {
            Width = 600,
            Height = 900,
            Content = new StackPanel { Children = { chart } }
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(600, 900));
            window.Arrange(new Rect(0, 0, 600, 900));
            Dispatcher.UIThread.RunJobs();

            var legendGrid = chart.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "CategoryLegend");
            var chartBorder = chart.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ChartBorder");
            var wrapPanel = legendGrid.GetVisualDescendants().OfType<WrapPanel>().First();

            Assert.True(chart.Bounds.Height >= 310);
            Assert.True(chartBorder.Bounds.Height >= 230);
            Assert.True(legendGrid.Bounds.Height > 70, $"Expected multi-row legend height > 70, got {legendGrid.Bounds.Height}");
            Assert.True(wrapPanel.Bounds.Height <= legendGrid.Bounds.Height + 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ColumnChartCanvas_ResolveReferenceLabelPositions_PreventsVerticalOverlapAndBottomOverflow()
    {
        var clustered = new[]
        {
            new ColumnChartCanvas.ReferenceLabelSlot(LineY: 100, Height: 14, Priority: 0),
            new ColumnChartCanvas.ReferenceLabelSlot(LineY: 102, Height: 14, Priority: 1),
            new ColumnChartCanvas.ReferenceLabelSlot(LineY: 104, Height: 14, Priority: 2)
        };

        var tops = ColumnChartCanvas.ResolveReferenceLabelPositions(clustered, minY: 0, maxY: 200, minGap: 2.0);
        Assert.Equal(3, tops.Length);
        var sorted = tops.OrderBy(t => t).ToArray();
        Assert.True(sorted[1] >= sorted[0] + 16.0 - 1e-6);
        Assert.True(sorted[2] >= sorted[1] + 16.0 - 1e-6);

        var bottomCluster = new[]
        {
            new ColumnChartCanvas.ReferenceLabelSlot(LineY: 194, Height: 14, Priority: 0),
            new ColumnChartCanvas.ReferenceLabelSlot(LineY: 197, Height: 14, Priority: 1),
            new ColumnChartCanvas.ReferenceLabelSlot(LineY: 200, Height: 14, Priority: 2)
        };

        var clamped = ColumnChartCanvas.ResolveReferenceLabelPositions(bottomCluster, minY: 0, maxY: 200, minGap: 2.0);
        Assert.All(clamped, top =>
        {
            Assert.True(top >= 0);
            Assert.True(top + 14 <= 200 + 1e-6);
        });
        var clampedSorted = clamped.OrderBy(t => t).ToArray();
        Assert.True(clampedSorted[1] >= clampedSorted[0] + 16.0 - 1e-6);
        Assert.True(clampedSorted[2] >= clampedSorted[1] + 16.0 - 1e-6);
    }

    [AvaloniaFact]
    public void HeatGrid_YearTwelveMonthHeatmap_PreservesReadableRowHeightInsideScrollViewer()
    {
        var measured = HeatGrid.MeasurePreferredSize(
            new Size(368, double.PositiveInfinity),
            rowCount: 12,
            columnCount: 31,
            maxCellHeight: 16,
            squareCells: false,
            rowLabelWidth: 28);

        Assert.True(measured.Height >= 12 * 14 + 11 * 2 + 18);
        Assert.Equal(1, HeatGrid.ComputeRowLabelStep(rowPitch: 16, labelHeight: 11));
        Assert.True(HeatGrid.ComputeRowLabelStep(rowPitch: 7, labelHeight: 11) >= 2);

        var rows = Enumerable.Range(1, 12)
            .Select(month => new HeatRow
            {
                Label = $"{month}月",
                Cells = Enumerable.Range(1, 31)
                    .Select(day => new HeatCell { Value = day * month * 60 })
                    .ToList()
            })
            .ToList();

        var grid = new HeatGrid
        {
            Rows = rows,
            ColumnLabels = ["1", "", "", "", "", "15", "", "", "", "", "31"],
            MaxValue = 12 * 31 * 60,
            RowLabelWidth = 28,
            MaxCellHeight = 16
        };

        var scrollViewer = new ScrollViewer
        {
            Width = 372,
            Height = 180,
            Content = grid
        };

        var window = new Window
        {
            Width = 420,
            Height = 300,
            Content = scrollViewer
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(grid.Bounds.Height >= 200, $"Expected 12-month HeatGrid height >= 200 inside ScrollViewer, got {grid.Bounds.Height}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void CategorySearch_FiltersByTrimmedSubstringAliasAndDeduplicatesBlankEntries()
    {
        Assert.True(CategoryEntityViewHelper.MatchesSearchWithAlias("  git  ", "GitHub"));
        Assert.True(CategoryEntityViewHelper.MatchesSearchWithAlias("gh", "GitHub (gh)"));
        Assert.True(CategoryEntityViewHelper.MatchesSearchWithAlias("*.org", "docs.python.org"));
        Assert.False(CategoryEntityViewHelper.MatchesSearchWithAlias("*.com", "docs.python.org"));

        var unsetSites = new[]
        {
            new WebSiteModel { ID = 1, Title = " ", Domain = " " },
            new WebSiteModel { ID = 2, Title = "GitHub", Domain = "github.com", Alias = "代码托管", CategoryID = 0 },
            new WebSiteModel { ID = 3, Title = "example.com", Domain = "example.com", CategoryID = 0 }
        };
        var currentSites = new[]
        {
            new WebSiteModel { ID = 2, Title = "GitHub", Domain = "github.com", Alias = "代码托管", CategoryID = 5 },
            new WebSiteModel { ID = 4, Title = "Ant Design", Domain = "ant.design", CategoryID = 5 }
        };

        var siteOptions = CategoryWebSiteListPageViewModel.BuildChooserOptions(unsetSites, currentSites, categoryId: 5);
        Assert.Equal(3, siteOptions.Count);
        Assert.Equal("example.com", siteOptions.First(o => o.WebSite?.ID == 3).OptionValue.Name);

        var filteredSites = CategoryWebSiteListPageViewModel.FilterChooserOptions(siteOptions, "  代码 ");
        Assert.Single(filteredSites);
        Assert.Equal(2, filteredSites[0].WebSite?.ID);

        var sysCat = new CategoryModel { ID = 0, IsSystem = true };
        var apps = new[]
        {
            new AppModel { ID = 10, Name = "   ", CategoryID = 0, Category = sysCat, TotalTime = 100 },
            new AppModel { ID = 11, Name = "Code", Description = "Visual Studio Code", CategoryID = 0, Category = sysCat, TotalTime = 500 },
            new AppModel { ID = 12, Name = "Code", Description = "Visual Studio Code", CategoryID = 3, TotalTime = 100 },
            new AppModel { ID = 13, Name = "wt", Description = "Windows Terminal", Alias = "终端", CategoryID = 0, Category = sysCat, TotalTime = 800 }
        };

        var appOptions = CategoryAppListPageViewModel.BuildCandidateOptions(apps, categoryId: 3);
        Assert.Equal(2, appOptions.Count);
        Assert.Contains(appOptions, o => o.App.ID == 12 && o.IsChoosed);

        var filteredApps = CategoryAppListPageViewModel.FilterCandidateOptions(appOptions, " 终端 ");
        Assert.Single(filteredApps);
        Assert.Equal(13, filteredApps[0].App.ID);
    }

    [Fact]
    public void MainViewModel_SyncNavigationSelection_TracksNavSettingAndSubPages()
    {
        var appConfig = StubProxy<IAppConfig>.Create(new()
        {
            [nameof(IAppConfig.GetConfig)] = _ => new ConfigModel()
        });
        var vm = new MainViewModel(
            StubProxy<IServiceProvider>.Create(new()),
            appConfig,
            StubProxy<IShiJianApiClient>.Create(new()),
            StubProxy<IProcessService>.Create(new()));

        try
        {
            vm.Uri = nameof(IndexPage);
            Assert.Equal(nameof(IndexPage), vm.NavSelectedItem?.Uri);
            Assert.False(vm.IsSettingSelected);

            vm.Uri = nameof(SettingPage);
            Assert.Null(vm.NavSelectedItem);
            Assert.True(vm.IsSettingSelected);

            vm.Uri = nameof(DetailPage);
            Assert.Null(vm.NavSelectedItem);
            Assert.False(vm.IsSettingSelected);

            vm.Uri = nameof(ChartPage);
            Assert.Equal(nameof(ChartPage), vm.NavSelectedItem?.Uri);
            Assert.False(vm.IsSettingSelected);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [AvaloniaFact]
    public void DateSelect_InsideInitiallyHiddenContainer_OpensPopupWhenCenterClicked()
    {
        var dateSelect = new DateSelect
        {
            SelectType = DateSelectType.Date,
            Date = new DateTime(2026, 11, 14)
        };
        var host = new Border
        {
            IsVisible = false,
            Child = dateSelect
        };
        var window = new Window
        {
            Width = 500,
            Height = 300,
            Content = host
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            host.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(500, 300));
            window.Arrange(new Rect(0, 0, 500, 300));
            Dispatcher.UIThread.RunJobs();

            var triggerButton = dateSelect.GetVisualDescendants()
                .OfType<Button>()
                .First(b => ReferenceEquals(b.Command, dateSelect.ShowSelectCommand));

            triggerButton.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(dateSelect.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CategoryEntityRowAndImager_ProvideFallbackTextAndCacheInvalidFaviconsAcrossWidths()
    {
        var siteRow = new CategoryEntityRow
        {
            IsSite = true,
            Name = "GitHub Docs",
            Subtitle = "docs.github.com"
        };
        var appRow = new CategoryEntityRow
        {
            IsSite = false,
            Name = "Visual Studio Code",
            Subtitle = "Code"
        };
        Assert.Equal("docs.github.com", siteRow.FallbackText);
        Assert.Equal("Visual Studio Code", appRow.FallbackText);

        Assert.True(Imager.IsSupportedImageHeader([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.True(Imager.IsSupportedImageHeader([0x00, 0x00, 0x01, 0x00, 0x01, 0x00]));
        Assert.False(Imager.IsSupportedImageHeader([0x00, 0x00, 0x01, 0x00, 0x00, 0x00]));
        Assert.False(Imager.IsSupportedImageHeader("<!DOCTYPE html><html>404</html>"u8));
        Assert.False(Imager.IsSupportedImageHeader("404 page not found."u8));
        Assert.False(Imager.IsSupportedImageHeader([]));

        var tempFile = Path.Combine(Path.GetTempPath(), $"shijian_bad_favicon_{Guid.NewGuid():N}.ico");
        try
        {
            await File.WriteAllTextAsync(tempFile, "<!DOCTYPE html><html><body>not an icon</body></html>");

            var bmp = await Imager.LoadAsync(tempFile, decodeWidth: 24);
            Assert.NotNull(bmp);
            Assert.True(Imager.IsFailed(tempFile, decodeWidth: 24));
            Assert.True(Imager.IsFailed(tempFile, decodeWidth: 64));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LocalizationResources_ProvideChineseToggleTextAndNonDuplicatedSearchTips()
    {
        var cn = new StringResourcesCn();
        var en = new StringResourcesEn();

        Assert.Equal("开", cn["ToggleOn"]);
        Assert.Equal("关", cn["ToggleOff"]);
        Assert.Equal("On", en["ToggleOn"]);
        Assert.Equal("Off", en["ToggleOff"]);

        var cnNav = Assert.IsType<string>(cn["SearchTipNav"]);
        var cnClose = Assert.IsType<string>(cn["SearchTipClose"]);
        var enNav = Assert.IsType<string>(en["SearchTipNav"]);
        var enClose = Assert.IsType<string>(en["SearchTipClose"]);

        Assert.DoesNotContain("↑↓", cnNav);
        Assert.DoesNotContain("Esc", cnClose);
        Assert.DoesNotContain("↑↓", enNav);
        Assert.DoesNotContain("Esc", enClose);
    }

    private class StubProxy<T> : DispatchProxy where T : class
    {
        private Dictionary<string, Func<object?[], object?>> _handlers = [];

        public static T Create(Dictionary<string, Func<object?[], object?>> handlers)
        {
            var proxy = DispatchProxy.Create<T, StubProxy<T>>();
            ((StubProxy<T>)(object)proxy)._handlers = handlers;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod != null && _handlers.TryGetValue(targetMethod.Name, out var fn))
                return fn(args ?? []);
            if (targetMethod?.ReturnType == typeof(void))
                return null;
            if (targetMethod?.ReturnType == typeof(Task))
                return Task.CompletedTask;
            if (targetMethod?.ReturnType.IsValueType == true)
                return Activator.CreateInstance(targetMethod.ReturnType);
            return null;
        }
    }
}
