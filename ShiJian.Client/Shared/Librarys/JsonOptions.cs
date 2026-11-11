using System.Text.Json;

namespace ShiJian.Client.Shared.Librarys;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
