using System.Threading.Tasks;
using Avalonia.Controls;
using ShiJian.Client.Controls.Charts;
using ShiJian.Client.Controls.Charts.Model;

namespace ShiJian.Client.Servicers;

public interface IContextMenuServicer
{
    void Init();
    Task<ContextMenu> CreateContextMenuAsync(ContextMenuType type, ChartsDataModel data);
}
