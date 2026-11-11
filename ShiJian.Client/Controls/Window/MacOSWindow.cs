using System;
using Avalonia;
using Avalonia.Controls;

namespace ShiJian.Client.Controls.Window;

public class MacOSWindow : DefaultWindow
{
    public MacOSWindow()
    {
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        WindowDecorations = WindowDecorations.Full;
    }

    protected override Type StyleKeyOverride => typeof(MacOSWindow);
}