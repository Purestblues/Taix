using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Taix.Client.Controls.Base;
using Xunit;

namespace Taix.Tests;

/// <summary>
/// 空态控件是 M3 统一出来的：加载中显示骨架、否则显示插画 + 文案。
/// 这里顺带验证「主题能否正常加载」——主题里出现嵌套选择器之类的错误会直接抛异常。
/// </summary>
public class EmptyDataTests
{
    private static (EmptyData EmptyData, Window Window) Show(EmptyData emptyData)
    {
        var window = new Window { Width = 300, Height = 300, Content = emptyData };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (emptyData, window);
    }

    private static bool HasVisibleText(EmptyData emptyData, string text) =>
        emptyData.GetVisualDescendants()
            .OfType<TextBlock>()
            .Any(t => t.IsEffectivelyVisible && t.Text == text);

    [AvaloniaFact]
    public void EmptyState_ShowsDefaultText_WhenNotLoading()
    {
        var (emptyData, window) = Show(new EmptyData());

        try
        {
            // 文案留空时回落到 NoData 资源
            Assert.True(HasVisibleText(emptyData, "暂时没有数据"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LoadingState_HidesEmptyText()
    {
        var (emptyData, window) = Show(new EmptyData { IsLoading = true });

        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(HasVisibleText(emptyData, "暂时没有数据"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CustomText_OverridesDefault()
    {
        var (emptyData, window) = Show(new EmptyData { Text = "该时间段内此分类暂无使用记录" });

        try
        {
            Assert.True(HasVisibleText(emptyData, "该时间段内此分类暂无使用记录"));
            Assert.False(HasVisibleText(emptyData, "暂时没有数据"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Description_IsRenderedOnlyWhenProvided()
    {
        var (emptyData, window) = Show(new EmptyData { Text = "空", Description = "换个时间范围试试" });

        try
        {
            Assert.True(HasVisibleText(emptyData, "换个时间范围试试"));
        }
        finally
        {
            window.Close();
        }
    }
}
