using System.Threading.Tasks;

namespace ShiJian.Client.Servicers.Interfaces;

public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}
