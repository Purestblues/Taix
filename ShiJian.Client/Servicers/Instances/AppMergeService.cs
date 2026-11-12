using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Librarys;
using ShiJian.Client.Logging;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Servicers.Interfaces;
using ShiJian.Client.Servicers.Interfaces;

namespace ShiJian.Client.Servicers.Instances;

public class AppMergeService : IAppMergeService
{
    /// <summary>应用表变化不频繁，缓存一会儿够用；设置页改完覆盖会强制刷新</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IAppData _appData;
    private readonly object _gate = new();
    private readonly Dictionary<string, AppMergeOverride> _overrides = new(StringComparer.Ordinal);
    private IReadOnlyList<AppMergeGroup> _groups = [];
    private DateTime _loadedAt = DateTime.MinValue;
    private bool _overridesLoaded;

    public AppMergeService(IAppData appData)
    {
        _appData = appData;
    }

    public IReadOnlyList<AppMergeGroup> Groups
    {
        get
        {
            lock (_gate) return _groups;
        }
    }

    public IReadOnlyDictionary<string, AppMergeOverride> Overrides
    {
        get
        {
            lock (_gate) return new Dictionary<string, AppMergeOverride>(_overrides, StringComparer.Ordinal);
        }
    }

    public async Task<IReadOnlyList<AppMergeGroup>> GetGroupsAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!force && _groups.Count > 0 && DateTime.UtcNow - _loadedAt < CacheTtl) return _groups;
        }

        try
        {
            EnsureOverridesLoaded();

            var apps = await _appData.GetAllAppsAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, AppMergeOverride> snapshot;
            lock (_gate) snapshot = new Dictionary<string, AppMergeOverride>(_overrides, StringComparer.Ordinal);

            var groups = AppMergePlanner.Build(apps, snapshot);
            lock (_gate)
            {
                _groups = groups;
                _loadedAt = DateTime.UtcNow;
            }
            return groups;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 合并是增强功能：服务端连不上时不能让页面跟着失败
            Logger.Warn($"AppMergeService: 加载合并组失败，按不合并处理：{ex.Message}");
            lock (_gate) return _groups;
        }
    }

    public List<DailyLogModel> Apply(IEnumerable<DailyLogModel> logs)
    {
        var groups = Groups;
        return groups.Count == 0 ? logs.ToList() : AppMergePlanner.Apply(logs, groups);
    }

    public void Decorate(IEnumerable<Controls.Charts.Model.ChartsDataModel> rows)
    {
        var groups = Groups;
        if (groups.Count == 0) return;
        AppMergePlanner.Decorate(rows, groups);
    }

    public async Task DissolveAsync(string groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey)) return;

        lock (_gate) _overrides[groupKey] = new AppMergeOverride { IsSuppressed = true };

        await SaveOverridesAsync().ConfigureAwait(false);
        await GetGroupsAsync(force: true).ConfigureAwait(false);
    }

    public async Task ResetAsync()
    {
        lock (_gate)
        {
            _overrides.Clear();
        }

        await SaveOverridesAsync().ConfigureAwait(false);
    }

    private void EnsureOverridesLoaded()
    {
        lock (_gate)
        {
            if (_overridesLoaded) return;
            _overridesLoaded = true;
            _overrides.Clear();

            try
            {
                if (!File.Exists(OverridesFilePath)) return;
                var json = File.ReadAllText(OverridesFilePath);
                var state = JsonSerializer.Deserialize(json, ClientJsonContext.Default.AppMergeState);
                if (state?.Overrides == null) return;
                foreach (var (key, value) in state.Overrides)
                    if (!string.IsNullOrWhiteSpace(key) && value != null)
                        _overrides[key] = value;
            }
            catch (Exception ex)
            {
                Logger.Warn($"AppMergeService: 读取合并覆盖失败，按无覆盖处理：{ex.Message}");
            }
        }
    }

    private async Task SaveOverridesAsync()
    {
        try
        {
            AppMergeState state;
            lock (_gate) state = new AppMergeState { Overrides = new Dictionary<string, AppMergeOverride>(_overrides, StringComparer.Ordinal) };

            var directory = Path.GetDirectoryName(OverridesFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(state, ClientJsonContext.Default.AppMergeState);
            await File.WriteAllTextAsync(OverridesFilePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"AppMergeService: 保存合并覆盖失败：{ex.Message}");
        }
    }

    private static string OverridesFilePath => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShiJian", "app.merge.json")
        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.merge.json");
}

/// <summary>本地覆盖表的 JSON 形态（`app.merge.json`）</summary>
public sealed class AppMergeState
{
    public Dictionary<string, AppMergeOverride> Overrides { get; set; } = new(StringComparer.Ordinal);
}
