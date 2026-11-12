using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using ShiJian.Client.Controls.Base;
using ShiJian.Client.Controls.Select;
using ShiJian.Client.Controls.Window;
using ShiJian.Client.Foundation;
using ShiJian.Client.Foundation.Rx;
using ShiJian.Client.Logging;
using ShiJian.Client.Models;
using ShiJian.Client.Platform.Abstractions.Primitives;
using ShiJian.Client.Servicers.Interfaces;
using ShiJian.Client.Servicers.Updater;
using ShiJian.Client.Shared.Event;
using ShiJian.Client.Shared.Models.Config;
using ShiJian.Client.Shared.Models.Data;
using ShiJian.Client.Shared.Servicers.Interfaces;

namespace ShiJian.Client.ViewModels;

public class SettingPageViewModel : SettingPageModel
{
    private readonly IDialogService _dialogService;
    private readonly UpdateCheckerService _updateCheckerService;
    private readonly IWebData _webDataService;
    private readonly IAppConfig _appConfig;
    private readonly IData _dataService;
    private readonly IProcessService _processService;
    private readonly IAppMergeService _appMergeService;
    private readonly IEntityData _entityData;

    /// <summary>与 <c>BrowserOptions</c> 一一对应的浏览器标识（第一项是空串 = 自动推荐）</summary>
    private readonly List<string> _browserReferences = [];
    private bool _isLoadingBrowsers;
    private readonly IToastService _toastService;
    private readonly ConfigModel _config;

    public SettingPageViewModel(
        IAppConfig appConfig,
        IToastService toastService,
        IData data,
        IWebData webData,
        IDialogService dialogService,
        IProcessService processService,
        UpdateCheckerService updateCheckerService,
        IAppMergeService appMergeService,
        IEntityData entityData)
    {
        _appConfig = appConfig;
        _toastService = toastService;
        _dataService = data;
        _webDataService = webData;
        _dialogService = dialogService;
        _processService = processService;
        _updateCheckerService = updateCheckerService;
        _appMergeService = appMergeService;
        _entityData = entityData;
        _config = appConfig.GetConfig();

        OpenURL = AsyncRelayCommand.Create<object>(OnOpenURL).DisposeWith(Disposables);
        DelDataCommand = AsyncRelayCommand.CreateFromTask<object>(OnDelDataAsync).DisposeWith(Disposables);
        ExportDataCommand = AsyncRelayCommand.CreateFromTask<object>(OnExportDataAsync).DisposeWith(Disposables);
        CheckUpdate = AsyncRelayCommand.CreateFromTask(OnCheckUpdateAsync).DisposeWith(Disposables);
        DissolveMergeGroupCommand = AsyncRelayCommand.CreateFromTask<object>(OnDissolveMergeGroupAsync).DisposeWith(Disposables);
        ResetMergeGroupsCommand = AsyncRelayCommand.CreateFromTask<object>(OnResetMergeGroupsAsync).DisposeWith(Disposables);

        appConfig.ConfigChanged += OnConfigChanged;

        Initialize();

        // 默认浏览器下拉框换项即保存（没有「保存」按钮，和其他设置项一致）
        WhenPropertyChanged(this, x => x.SelectedBrowser, _ => OnSelectedBrowserChangedAsync());
    }

    public ICommand DissolveMergeGroupCommand { get; }
    public ICommand ResetMergeGroupsCommand { get; }

    /// <summary>进设置页时刷新一次（应用表可能在别的页面里改过）</summary>
    public override async Task OnNavigatedToAsync()
    {
        await LoadBrowsersAsync();
        await LoadMergeGroupsAsync();
        await base.OnNavigatedToAsync();
    }

    /// <summary>
    /// 默认浏览器：扩展早期版本上报的站点记录没有浏览器标识（`BrowserRef` 为 NULL），
    /// 这些历史记录统一归给这里选的浏览器。第一项「自动推荐」= 不指定，由服务端取出现最多的标识。
    /// </summary>
    private async Task LoadBrowsersAsync()
    {
        _isLoadingBrowsers = true;
        try
        {
            var browsers = await _entityData.GetBrowsersAsync();

            _browserReferences.Clear();
            var options = new List<SelectItemModel>
            {
                new() { Id = 0, Name = ResourceStrings.BrowserAuto }
            };
            _browserReferences.Add(string.Empty);

            foreach (var browser in browsers)
            {
                var label = string.IsNullOrWhiteSpace(browser.AppName)
                    ? browser.Reference
                    : $"{browser.AppName} ({browser.Reference})";
                options.Add(new SelectItemModel { Id = options.Count, Name = label });
                _browserReferences.Add(browser.Reference);
            }

            BrowserOptions = options;

            var current = _config.General.DefaultBrowser ?? string.Empty;
            var index = _browserReferences.FindIndex(reference =>
                string.Equals(reference, current, StringComparison.OrdinalIgnoreCase));
            SelectedBrowser = options[index < 0 ? 0 : index];
        }
        catch (Exception ex)
        {
            Logger.Warn($"加载默认浏览器候选失败：{ex.Message}");
        }
        finally
        {
            _isLoadingBrowsers = false;
        }
    }

    /// <summary>下拉框换项即落配置（没有「保存」按钮，与其他设置项一致）</summary>
    public async Task OnSelectedBrowserChangedAsync()
    {
        if (_isLoadingBrowsers) return;

        var option = SelectedBrowser;
        if (option == null) return;

        var reference = option.Id >= 0 && option.Id < _browserReferences.Count
            ? _browserReferences[option.Id]
            : string.Empty;

        if (string.Equals(_config.General.DefaultBrowser, reference, StringComparison.Ordinal)) return;

        _config.General.DefaultBrowser = reference;
        try
        {
            await _appConfig.SaveAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"保存默认浏览器失败：{ex.Message}", ex);
        }
    }

    private async Task LoadMergeGroupsAsync()
    {
        try
        {
            var groups = await _appMergeService.GetGroupsAsync(force: true);
            MergeGroups.Clear();
            foreach (var group in groups)
                MergeGroups.Add(new AppMergeGroupRow
                {
                    Key = group.Key,
                    Name = group.Name,
                    Count = group.MemberNames.Count,
                    MembersText = string.Join(" · ", group.MemberNames),
                    IsRenamed = group.Name != group.MemberNames.FirstOrDefault()
                });

            HasMergeGroups = MergeGroups.Count > 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"加载子母程序合并组失败：{ex.Message}", ex);
        }
    }

    private async Task OnDissolveMergeGroupAsync(object obj)
    {
        if (obj is not AppMergeGroupRow row) return;

        try
        {
            await _appMergeService.DissolveAsync(row.Key);
            _toastService.Success(ResourceStrings.MergeGroupDissolved);
            await LoadMergeGroupsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message, ex);
            _toastService.Error(ResourceStrings.OperationFailedRetry);
        }
    }

    private async Task OnResetMergeGroupsAsync(object obj)
    {
        try
        {
            await _appMergeService.ResetAsync();
            _toastService.Success(ResourceStrings.MergeGroupRestored);
            await LoadMergeGroupsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message, ex);
            _toastService.Error(ResourceStrings.OperationFailedRetry);
        }
    }

    public object GeneralSettings
    {
        get => _config.General;
        set
        {
            if (value is GeneralModel general && _config.General != general)
            {
                _config.General = general;
                _ = _appConfig.SaveAsync();
                OnPropertyChanged();
            }
        }
    }

    public object BehaviorSettings
    {
        get => _config.Behavior;
        set
        {
            if (value is BehaviorModel behavior && _config.Behavior != behavior)
            {
                _config.Behavior = behavior;
                _ = _appConfig.SaveAsync();
                OnPropertyChanged();
            }
        }
    }

    public string RefreshShortcut
    {
        get => _config.Shortcut?.Refresh ?? string.Empty;
        set
        {
            _config.Shortcut ??= new ShortcutModel();
            if (_config.Shortcut.Refresh == value) return;
            _config.Shortcut.Refresh = value ?? string.Empty;
            _ = _appConfig.SaveAsync();
            OnPropertyChanged();
        }
    }

    public string SearchShortcut
    {
        get => _config.Shortcut?.Search ?? string.Empty;
        set
        {
            _config.Shortcut ??= new ShortcutModel();
            if (_config.Shortcut.Search == value) return;
            _config.Shortcut.Search = value ?? string.Empty;
            _ = _appConfig.SaveAsync();
            OnPropertyChanged();
        }
    }

    // 鼠标后退键始终可用，与此配置无关
    public string NavigateBackShortcut
    {
        get => _config.Shortcut?.NavigateBack ?? string.Empty;
        set
        {
            _config.Shortcut ??= new ShortcutModel();
            if (_config.Shortcut.NavigateBack == value) return;
            _config.Shortcut.NavigateBack = value ?? string.Empty;
            _ = _appConfig.SaveAsync();
            OnPropertyChanged();
        }
    }

    private int _dataRetentionDays;
    public int DataRetentionDays
    {
        get => _dataRetentionDays;
        set
        {
            if (_dataRetentionDays != value)
            {
                _dataRetentionDays = value;
                _config.General.DataRetentionDays = value;
                _ = _appConfig.SaveAsync();
                OnPropertyChanged();
                UpdateSelectedRetentionOption();
            }
        }
    }

    private List<SelectItemModel> _retentionOptions;
    public List<SelectItemModel> RetentionOptions
    {
        get => _retentionOptions;
        set
        {
            _retentionOptions = value;
            OnPropertyChanged();
        }
    }

    private SelectItemModel _selectedRetentionOption;
    public SelectItemModel SelectedRetentionOption
    {
        get => _selectedRetentionOption;
        set
        {
            if (_selectedRetentionOption != value && value != null)
            {
                _selectedRetentionOption = value;
                OnPropertyChanged();

                if (value.Data is int days)
                {
                    if (days == -1)
                    {
                        // 自定义选项，显示输入框
                        IsCustomRetention = true;
                    }
                    else
                    {
                        IsCustomRetention = false;
                        DataRetentionDays = days;
                    }
                }
            }
        }
    }

    private bool _isCustomRetention;
    public bool IsCustomRetention
    {
        get => _isCustomRetention;
        set
        {
            _isCustomRetention = value;
            OnPropertyChanged();
        }
    }

    private void UpdateSelectedRetentionOption()
    {
        if (_retentionOptions == null) return;

        var matchingOption = _retentionOptions.Find(o => o.Data is int d && d == _dataRetentionDays);
        if (matchingOption != null)
        {
            IsCustomRetention = false;
            _selectedRetentionOption = matchingOption;
        }
        else
        {
            IsCustomRetention = true;
            _selectedRetentionOption = _retentionOptions.Find(o => o.Data is int d && d == -1);
        }
        OnPropertyChanged(nameof(SelectedRetentionOption));
    }

    public ICommand OpenURL { get; }
    public ICommand CheckUpdate { get; }
    public ICommand DelDataCommand { get; }
    public ICommand ExportDataCommand { get; }

    private void OnConfigChanged(object? sender, ConfigChangedEventArgs e)
    {
        if (e.HasChange("General.Language"))
        {
            if (TabbarData.Count > 0) TabbarData[0] = ResourceStrings.General;
            if (TabbarData.Count > 1) TabbarData[1] = ResourceStrings.Behavior;
            if (TabbarData.Count > 2) TabbarData[2] = ResourceStrings.Data;
            if (TabbarData.Count > 3) TabbarData[3] = ResourceStrings.Shortcuts;
            if (TabbarData.Count > 4) TabbarData[4] = ResourceStrings.About;
            OnPropertyChanged(nameof(TabbarData));
        }

        // 别处编辑了 Shortcut 也同步刷新本地输入框
        if (e.HasAnyChange("Shortcut.Refresh", "Shortcut.Search", "Shortcut.NavigateBack"))
        {
            OnPropertyChanged(nameof(RefreshShortcut));
            OnPropertyChanged(nameof(SearchShortcut));
            OnPropertyChanged(nameof(NavigateBackShortcut));
        }
    }

    private void Initialize()
    {
        Data = _config.General;
        TabbarData = [ResourceStrings.General, ResourceStrings.Behavior, ResourceStrings.Data, ResourceStrings.Shortcuts, ResourceStrings.About];
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;
        DelDataStartMonthDate = DateTime.Now;
        DelDataEndMonthDate = DateTime.Now;
        ExportDataStartMonthDate = DateTime.Now;
        ExportDataEndMonthDate = DateTime.Now;
        _dataRetentionDays = _config.General.DataRetentionDays;

        var days = ResourceStrings.Days;
        RetentionOptions =
        [
            new SelectItemModel { Name = $"30{days}", Data = 30 },
            new SelectItemModel { Name = $"60{days}", Data = 60 },
            new SelectItemModel { Name = $"90{days}", Data = 90 },
            new SelectItemModel { Name = $"180{days}", Data = 180 },
            new SelectItemModel { Name = $"365{days}", Data = 365 },
            new SelectItemModel { Name = ResourceStrings.Custom, Data = -1 }
        ];
        UpdateSelectedRetentionOption();
    }

    private async Task OnDelDataAsync(object obj)
    {
        if (DelDataStartMonthDate > DelDataEndMonthDate)
        {
            _toastService.Toast(ResourceStrings.TimeRangeSelectionError, ToastType.Error, IconTypes.IncidentTriangle);
            return;
        }

        var isConfirm = await _dialogService.ShowConfirmDialogAsync(
            ResourceStrings.DeleteConfirmation,
            ResourceStrings.WantPerformAction);
        if (isConfirm)
        {
            await _dataService.ClearRangeAsync(DelDataStartMonthDate, DelDataEndMonthDate);
            await _webDataService.ClearAsync(DelDataStartMonthDate, DelDataEndMonthDate);
            _toastService.Success(ResourceStrings.OperationCompleted);
        }
    }

    private async Task OnExportDataAsync(object obj)
    {
        try
        {
            var folder = await _dialogService.ShowFolderPickerAsync();
            if (string.IsNullOrEmpty(folder)) return;

            var exportStart = new DateTime(ExportDataStartMonthDate.Year, ExportDataStartMonthDate.Month, 1);
            var exportEnd = new DateTime(ExportDataEndMonthDate.Year, ExportDataEndMonthDate.Month, DateTime.DaysInMonth(ExportDataEndMonthDate.Year, ExportDataEndMonthDate.Month));
            await _dataService.ExportToExcelAsync(folder, exportStart, exportEnd);
            await _webDataService.ExportAsync(folder, exportStart, exportEnd);
            _toastService.Success(ResourceStrings.DataExportCompleted);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message, ex);
            _toastService.Toast(ResourceStrings.DataExportFailed, ToastType.Error, IconTypes.IncidentTriangle);
        }
    }

    private Task OnCheckUpdateAsync() => _updateCheckerService.ManualCheckForUpdatesAsync();

    private void OnOpenURL(object obj)
    {
        var url = obj?.ToString();
        if (!string.IsNullOrEmpty(url))
            _processService.OpenUrl(url);
    }

    public override void Dispose()
    {
        _appConfig.ConfigChanged -= OnConfigChanged;
        base.Dispose();
    }
}
