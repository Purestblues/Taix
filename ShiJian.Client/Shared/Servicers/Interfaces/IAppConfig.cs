using System;
using System.Threading.Tasks;
using ShiJian.Client.Shared.Event;
using ShiJian.Client.Shared.Models.Config;

namespace ShiJian.Client.Shared.Servicers.Interfaces;

public interface IAppConfig
{
    Task LoadAsync();

    ConfigModel GetConfig();

    Task SaveAsync();

    event EventHandler<ConfigChangedEventArgs> ConfigChanged;
}
