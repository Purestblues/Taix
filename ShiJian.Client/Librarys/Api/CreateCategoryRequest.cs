namespace ShiJian.Client.Librarys.Api;

internal sealed class CreateCategoryRequest
{
    public string? Name { get; set; }
    public string? IconFile { get; set; }
    public string? Color { get; set; }
    public bool IsDirectoryMatch { get; set; }
    public string? Directories { get; set; }
    public bool IsUrlMatch { get; set; }
    public string? UrlPatterns { get; set; }
}
