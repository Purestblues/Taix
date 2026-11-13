using System.Linq;
using Avalonia.Headless.XUnit;
using ShiJian.Client.Librarys.Image;
using Xunit;

namespace ShiJian.Tests;

public class CategoryIconCatalogTests
{
    [AvaloniaFact]
    public void AllCatalogIcons_ParseAsValidAvaloniaGeometry()
    {
        Assert.Equal(32, CategoryIconCatalog.AllKeys.Count);
        Assert.Equal(32, CategoryIconCatalog.AllKeys.Distinct().Count());

        foreach (var key in CategoryIconCatalog.AllKeys)
        {
            Assert.True(CategoryIconCatalog.IsSvgIcon(key));
            Assert.True(CategoryIconCatalog.TryGetGeometry(key, out var geometry), $"Failed to parse geometry for {key}");
            Assert.NotNull(geometry);
        }
    }

    [AvaloniaFact]
    public void LegacyEmojiPaths_AndFallbackIcons_ResolvePredictably()
    {
        for (var i = 1; i <= 44; i++)
        {
            var legacyPath = $"avares://ShiJian/Resources/Emoji/({i}).png";
            var resolved = CategoryIconCatalog.ResolveKey(legacyPath);
            Assert.NotNull(resolved);
            Assert.StartsWith("svg:", resolved);
            Assert.True(CategoryIconCatalog.TryGetGeometry(legacyPath, out var geometry));
            Assert.NotNull(geometry);
        }

        Assert.Equal("svg:inbox", CategoryIconCatalog.UncategorizedIconKey);
        Assert.True(CategoryIconCatalog.TryGetGeometry(CategoryIconCatalog.UncategorizedIconKey, out var uncatGeom));
        Assert.NotNull(uncatGeom);

        // Non-category icons (app icons / custom PNGs on disk) do not resolve to vector SVG keys
        Assert.Null(CategoryIconCatalog.ResolveKey("avares://ShiJian/Resources/Icons/shijian.ico"));
        Assert.Null(CategoryIconCatalog.ResolveKey("C:/custom/icons/my.png"));
        Assert.False(CategoryIconCatalog.TryGetGeometry("C:/custom/icons/my.png", out _));
        Assert.Null(CategoryIconCatalog.ResolveKey(null));
        Assert.False(CategoryIconCatalog.TryGetGeometry("", out _));
    }
}
