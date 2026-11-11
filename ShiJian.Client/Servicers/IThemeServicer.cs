using ShiJian.Client.Shared.Models.Config;
using ShiJian.Client.Views;

namespace ShiJian.Client.Servicers;

public interface IThemeServicer
{
    void Init();
    void LoadTheme(AppTheme theme);
    void SetMainWindow(MainWindow mainWindow);
}
