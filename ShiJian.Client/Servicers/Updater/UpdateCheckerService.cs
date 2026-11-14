using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using ShiJian.Client.Librarys;
using ShiJian.Client.Logging;
using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Servicers.Updater;

public class UpdateCheckerService
{
    private readonly IUIServicer _uiServicer;
    private readonly MainViewModel _mainViewModel;

    public UpdateCheckerService(IUIServicer uiServicer, MainViewModel mainViewModel)
    {
        _uiServicer = uiServicer;
        _mainViewModel = mainViewModel;
    }

    public async Task AutoCheckForUpdatesAsync()
    {
        try
        {
            var (release, info) = await GetReleaseInfoAsync();
            if (info is null || !release.IsCanUpdate())
                return;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ShowUpdateDialogAsync();
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Auto update check failed: {ex.Message}", ex);
        }
    }

    public async Task ManualCheckForUpdatesAsync()
    {
        try
        {
            var (release, info) = await GetReleaseInfoAsync();
            if (info is null)
            {
                _mainViewModel.Error(GetResourceString("UpdateCheckFailed"));
                return;
            }

            if (release.IsCanUpdate())
                await ShowUpdateDialogAsync();
            else
                _mainViewModel.Info(GetResourceString("NoUpdateAvailable"));
        }
        catch (Exception ex)
        {
            Logger.Error($"Manual update check failed: {ex.Message}", ex);
            _mainViewModel.Error(GetResourceString("UpdateCheckFailed"));
        }
    }

    private async Task<(GithubRelease release, GithubRelease.VersionInfo? info)> GetReleaseInfoAsync()
    {
        var currentVersion = GetCurrentVersion();
        var release = new GithubRelease("https://api.github.com/repos/Purestblues/shijian/releases/latest", currentVersion);
        var info = await release.GetRequestAsync();
        return (release, info);
    }

    private static string GetCurrentVersion()
    {
#if DEBUG
        var envVersion = Environment.GetEnvironmentVariable("SHIJIAN_DEBUG_VERSION");
        if (!string.IsNullOrWhiteSpace(envVersion))
            return AppVersionHelper.NormalizeTagVersion(envVersion);
#endif
        return AppVersionHelper.GetDisplayVersion();
    }

    private async Task ShowUpdateDialogAsync()
    {
        var title = GetResourceString("NewVersionAvailable");
        var message = GetResourceString("WantGoDownloadPage");

        var result = await _uiServicer.ShowConfirmDialogAsync(title, message);

        if (result)
        {
            Process.Start(new ProcessStartInfo("https://github.com/Purestblues/shijian/releases/latest")
            {
                UseShellExecute = true
            });
        }
    }

    private static string GetResourceString(string key)
    {
        Application.Current!.Resources.TryGetResource(key, null, out var value);
        return (value as string) ?? string.Empty;
    }
}
