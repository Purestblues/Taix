using System;
using Avalonia;

namespace Taix.Client;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // macOS 弹层默认是独立 NSPanel，选完下拉窗口会挪动且悬浮全部失效；
            // 改成在窗口内渲染弹层绕开。此选项只有 macOS 后端会读，Windows 不受影响
            .With(new AvaloniaNativePlatformOptions { OverlayPopups = true });

#if DEBUG
        // AvaloniaUI.DiagnosticsSupport 2.2.3 的样式里用了嵌套选择器（^），
        // 与 Avalonia 12.1.1 一起会抛 "Nesting selector ... cannot determine parent selector"，
        // 启动即弹错误框、主界面空白。默认不挂，需要时用环境变量 TAIX_DEVTOOLS=1 打开。
        if (Environment.GetEnvironmentVariable("TAIX_DEVTOOLS") == "1")
        {
            builder.WithDeveloperTools();
        }
#endif
        return builder;
    }
}
