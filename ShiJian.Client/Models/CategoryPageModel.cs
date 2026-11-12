using System.Collections.Generic;
using System.Collections.ObjectModel;
using ShiJian.Client.Controls.Select;
using ShiJian.Client.Models.Category;
using ShiJian.Client.PageState;
using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Models;

[GeneratePageState]
public partial class CategoryPageModel : ModelBase
{
    /// <summary>
    /// 视图形态与排序口径需要默认值：<c>[PageState]</c> 只负责「返回导航时恢复」
    /// （<c>TryRestoreState</c> 第一句就是 <c>if (!navigation.IsNavigatingBack) return false;</c>），
    /// 从侧边栏进页面时不会写入任何值，开关就会停在一个「什么都没选」的空状态。
    /// </summary>
    public CategoryPageModel()
    {
        ViewMode = ViewModeOptions[0];
        SortMode = SortOptions[0];
    }

    private ObservableCollection<CategoryModel> _data = new();
    private string _editColor = string.Empty;
    private string _editErrorText = string.Empty;
    private string _editIconFile = string.Empty;
    private bool _editIsDirectoryMatch;
    private bool _editIsUrlMatch;
    private string _editName = string.Empty;
    private string _editSelectedDirectory = string.Empty;
    private string _editSelectedUrlPattern = string.Empty;
    private bool _editVisibility;
    private bool _isCreate;
    private bool _isEditError;
    private bool _isSysCategory;
    private CategoryModel? _selectedAppCategoryItem;
    private WebCategoryModel? _selectedWebCategoryItem;
    private ObservableCollection<WebCategoryModel> _webCategoryData = new();

    [PageState(LookupFrom = nameof(ShowTypeOptions))]
    public new SelectItemModel ShowType
    {
        get => base.ShowType;
        set => base.ShowType = value;
    }

    private SelectItemModel _viewMode;

    /// <summary>
    /// 紧凑 / 卡片视图（N7-b）。纯展示层开关：不重拉数据，也不重建列表项，
    /// 所以切换时滚动位置与选中态都不会丢。
    /// </summary>
    [PageState(LookupFrom = nameof(ViewModeOptions))]
    public SelectItemModel ViewMode
    {
        get => _viewMode;
        set
        {
            _viewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCardView));
        }
    }

    public List<SelectItemModel> ViewModeOptions { get; } =
    [
        new()
        {
            Id = 0,
            Name = ResourceStrings.ViewCompact
        },
        new()
        {
            Id = 1,
            Name = ResourceStrings.ViewCard
        }
    ];

    public bool IsCardView => ViewMode?.Id == 1;

    private SelectItemModel _sortMode;

    /// <summary>
    /// 排序口径（N7-c）：时长 / 名称 / 数量。只重排现有卡片，不重拉数据
    /// （卡片上的时长与数量已经是全量，排序纯属展示层）。
    /// </summary>
    [PageState(LookupFrom = nameof(SortOptions))]
    public SelectItemModel SortMode
    {
        get => _sortMode;
        set
        {
            _sortMode = value;
            OnPropertyChanged();
        }
    }

    public List<SelectItemModel> SortOptions { get; } =
    [
        new()
        {
            Id = 0,
            Name = ResourceStrings.SortByDuration
        },
        new()
        {
            Id = 1,
            Name = ResourceStrings.SortByName
        },
        new()
        {
            Id = 2,
            Name = ResourceStrings.SortByCount
        }
    ];

    public ObservableCollection<CategoryModel> Data
    {
        get => _data;
        set
        {
            _data = value;
            OnPropertyChanged();
        }
    }

    public CategoryModel? SelectedAppCategoryItem
    {
        get => _selectedAppCategoryItem;
        set
        {
            _selectedAppCategoryItem = value;
            OnPropertyChanged();
        }
    }

    public bool EditVisibility
    {
        get => _editVisibility;
        set
        {
            _editVisibility = value;
            OnPropertyChanged();
        }
    }

    public bool IsCreate
    {
        get => _isCreate;
        set
        {
            _isCreate = value;
            OnPropertyChanged();
        }
    }

    public string EditName
    {
        get => _editName;
        set
        {
            _editName = value;
            OnPropertyChanged();
        }
    }

    public string EditIconFile
    {
        get => _editIconFile;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                _editIconFile = value;
                OnPropertyChanged();
            }
        }
    }

    public bool EditIsDirectoryMatch
    {
        get => _editIsDirectoryMatch;
        set
        {
            _editIsDirectoryMatch = value;
            OnPropertyChanged();
        }
    }

    public bool EditIsUrlMatch
    {
        get => _editIsUrlMatch;
        set
        {
            _editIsUrlMatch = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> EditDirectories { get; set; } = new();

    public ObservableCollection<string> EditUrlPatterns { get; set; } = new();

    public string EditSelectedDirectory
    {
        get => _editSelectedDirectory;
        set
        {
            _editSelectedDirectory = value;
            OnPropertyChanged();
        }
    }

    public string EditSelectedUrlPattern
    {
        get => _editSelectedUrlPattern;
        set
        {
            _editSelectedUrlPattern = value;
            OnPropertyChanged();
        }
    }

    private string _editNewUrlPattern = string.Empty;
    public string EditNewUrlPattern
    {
        get => _editNewUrlPattern;
        set
        {
            _editNewUrlPattern = value;
            OnPropertyChanged();
        }
    }

    public string EditErrorText
    {
        get => _editErrorText;
        set
        {
            _editErrorText = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditError
    {
        get => _isEditError;
        set
        {
            _isEditError = value;
            OnPropertyChanged();
        }
    }

    public string EditColor
    {
        get => _editColor;
        set
        {
            _editColor = value;
            OnPropertyChanged();
        }
    }

    public bool IsSysCategory
    {
        get => _isSysCategory;
        set
        {
            _isSysCategory = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<WebCategoryModel> WebCategoryData
    {
        get => _webCategoryData;
        set
        {
            _webCategoryData = value;
            OnPropertyChanged();
        }
    }

    public WebCategoryModel? SelectedWebCategoryItem
    {
        get => _selectedWebCategoryItem;
        set
        {
            _selectedWebCategoryItem = value;
            OnPropertyChanged();
        }
    }

    public class WebCategoryModel : Category.CategoryCardModel
    {
        public WebSiteCategoryModel? Data { get; set; }

        public override string? Name => Data?.Name;

        public override string? IconFile => Data?.IconFile;

        protected override string? RawColor => Data?.Color;

        protected override bool IsSystemCategory => Data?.IsSystem ?? false;
    }
}
