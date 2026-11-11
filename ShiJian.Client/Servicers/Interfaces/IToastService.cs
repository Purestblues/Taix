using ShiJian.Client.Controls.Base;
using ShiJian.Client.Controls.Window;

namespace ShiJian.Client.Servicers.Interfaces;

public interface IToastService
{
    void Toast(string content, ToastType type = ToastType.Info, IconTypes icon = IconTypes.Accept);
    void Error(string message);
    void Info(string message);
    void Success(string message);
}
