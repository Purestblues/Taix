using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiJian.Client.Platform;

namespace ShiJian.Client.Views.Dialogs;

public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    private void LogHyperlinkClick(object sender, RoutedEventArgs e)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var currentLog = Path.Combine(baseDir, "Logs", $"shijian-client-{DateTime.Now:yyyy-MM-dd}.log");
        var legacyLog = Path.Combine(baseDir, "Log", $"{DateTime.Now:yyyy-MM-dd}.log");
        var targetLog = File.Exists(currentLog) ? currentLog : legacyLog;

        if (File.Exists(targetLog))
        {
            PlatformHelper.OpenFileInExplorer(targetLog);
            return;
        }

        var logsDir = Path.Combine(baseDir, "Logs");
        if (Directory.Exists(logsDir))
        {
            PlatformHelper.RunFile(logsDir);
        }
    }

    private void IssuesHyperlinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/Purestblues/shijian/issues/new")
                { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/Purestblues/shijian") { UseShellExecute = true });
        Close();
    }
}