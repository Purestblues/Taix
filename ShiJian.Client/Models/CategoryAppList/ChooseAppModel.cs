using ShiJian.Client.Controls.Select;
using ShiJian.Client.Shared.Models;

namespace ShiJian.Client.Models.CategoryAppList;

public class ChooseAppModel
{
    public bool IsChoosed { get; set; }
    public AppModel App { get; set; }
    public SelectItemModel Value { get; set; } = new();
    public bool Visibility { get; set; } = true;
}