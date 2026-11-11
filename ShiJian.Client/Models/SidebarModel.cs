using ShiJian.Client.ViewModels;

namespace ShiJian.Client.Models;

public class SidebarModel
{
    public string Title { get; set; }

    public string IconPath { get; set; }

    public ViewModelBase NavigationViewModel { get; set; }
}