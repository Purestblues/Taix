using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ShiJian.Client.Controls.Select;
using ShiJian.Client.PageState;

namespace ShiJian.Client.Models;

[GeneratePageState]
public partial class SettingPageModel : ModelBase
{
    private bool _checkUpdateBtnVisibility = true;
    private object? _data;
    private DateTime _delDataEndMonthDate;
    private DateTime _delDataStartMonthDate;
    private DateTime _exportDataEndMonthDate;
    private DateTime _exportDataStartMonthDate;
    private ObservableCollection<string> _tabbarData = [];
    private ObservableCollection<AppMergeGroupRow> _mergeGroups = [];
    private bool _hasMergeGroups;
    private bool _mergeEditorVisible;
    private bool _isMergeEditorCreate = true;
    private string _mergeEditorName = string.Empty;
    private string _mergeEditorSearch = string.Empty;
    private string _mergeEditorError = string.Empty;
    private ObservableCollection<MergeCandidateRow> _mergeEditorCandidates = [];
    private ObservableCollection<MergeCandidateRow> _mergeEditorSelectedMembers = [];
    private List<SelectItemModel> _browserOptions = [];
    private SelectItemModel? _selectedBrowser;
    private int _tabbarSelectedIndex;
    private string _version = string.Empty;

    public object? Data
    {
        get => _data;
        set
        {
            _data = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> TabbarData
    {
        get => _tabbarData;
        set
        {
            _tabbarData = value;
            OnPropertyChanged();
        }
    }

    [PageState]
    public int TabbarSelectedIndex
    {
        get => _tabbarSelectedIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 4);
            if (_tabbarSelectedIndex == normalized) return;
            _tabbarSelectedIndex = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTab0Selected));
            OnPropertyChanged(nameof(IsTab1Selected));
            OnPropertyChanged(nameof(IsTab2Selected));
            OnPropertyChanged(nameof(IsTab3Selected));
            OnPropertyChanged(nameof(IsTab4Selected));
        }
    }

    public bool IsTab0Selected => _tabbarSelectedIndex == 0;
    public bool IsTab1Selected => _tabbarSelectedIndex == 1;
    public bool IsTab2Selected => _tabbarSelectedIndex == 2;
    public bool IsTab3Selected => _tabbarSelectedIndex == 3;
    public bool IsTab4Selected => _tabbarSelectedIndex == 4;

    /// <summary>子母程序合并的自动组（N6）</summary>
    public ObservableCollection<AppMergeGroupRow> MergeGroups
    {
        get => _mergeGroups;
        set
        {
            _mergeGroups = value;
            OnPropertyChanged();
        }
    }

    public bool HasMergeGroups
    {
        get => _hasMergeGroups;
        set
        {
            _hasMergeGroups = value;
            OnPropertyChanged();
        }
    }

    /// <summary>默认浏览器候选项（第一项是「自动推荐」）</summary>
    public List<SelectItemModel> BrowserOptions
    {
        get => _browserOptions;
        set
        {
            _browserOptions = value;
            OnPropertyChanged();
        }
    }

    public SelectItemModel? SelectedBrowser
    {
        get => _selectedBrowser;
        set
        {
            if (ReferenceEquals(_selectedBrowser, value)) return;
            _selectedBrowser = value;
            OnPropertyChanged();
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            _version = value;
            OnPropertyChanged();
        }
    }

    public bool CheckUpdateBtnVisibility
    {
        get => _checkUpdateBtnVisibility;
        set
        {
            _checkUpdateBtnVisibility = value;
            OnPropertyChanged();
        }
    }

    public DateTime DelDataStartMonthDate
    {
        get => _delDataStartMonthDate;
        set
        {
            _delDataStartMonthDate = value;
            OnPropertyChanged();
        }
    }

    public DateTime DelDataEndMonthDate
    {
        get => _delDataEndMonthDate;
        set
        {
            _delDataEndMonthDate = value;
            OnPropertyChanged();
        }
    }

    public DateTime ExportDataStartMonthDate
    {
        get => _exportDataStartMonthDate;
        set
        {
            _exportDataStartMonthDate = value;
            OnPropertyChanged();
        }
    }

    public DateTime ExportDataEndMonthDate
    {
        get => _exportDataEndMonthDate;
        set
        {
            _exportDataEndMonthDate = value;
            OnPropertyChanged();
        }
    }

    public bool MergeEditorVisible
    {
        get => _mergeEditorVisible;
        set
        {
            _mergeEditorVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsMergeEditorCreate
    {
        get => _isMergeEditorCreate;
        set
        {
            _isMergeEditorCreate = value;
            OnPropertyChanged();
        }
    }

    public string MergeEditorName
    {
        get => _mergeEditorName;
        set
        {
            _mergeEditorName = value;
            OnPropertyChanged();
        }
    }

    public string MergeEditorSearch
    {
        get => _mergeEditorSearch;
        set
        {
            if (_mergeEditorSearch == value) return;
            _mergeEditorSearch = value;
            OnPropertyChanged();
        }
    }

    public string MergeEditorError
    {
        get => _mergeEditorError;
        set
        {
            _mergeEditorError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMergeEditorError));
        }
    }

    public bool HasMergeEditorError => !string.IsNullOrWhiteSpace(_mergeEditorError);

    public ObservableCollection<MergeCandidateRow> MergeEditorCandidates
    {
        get => _mergeEditorCandidates;
        set
        {
            _mergeEditorCandidates = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<MergeCandidateRow> MergeEditorSelectedMembers
    {
        get => _mergeEditorSelectedMembers;
        set
        {
            _mergeEditorSelectedMembers = value;
            OnPropertyChanged();
        }
    }
}
