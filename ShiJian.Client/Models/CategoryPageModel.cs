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
