using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class ThemeCatalogTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("nope")]
    public void Unknown_theme_resolves_to_default(string? id) =>
        Assert.Equal(ThemeCatalog.Default, ThemeCatalog.Resolve(id));

    [Fact]
    public void Known_theme_resolves_to_itself() => Assert.Equal("frost", ThemeCatalog.Resolve("frost"));

    [Fact]
    public void Validate_lists_missing_keys() =>
        Assert.Contains("scene", ThemeCatalog.Validate("{\"id\":\"x\",\"name\":\"X\",\"css\":{}}"));

    [Fact]
    public void Shipped_themes_are_valid()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Game.Web/wwwroot/themes"));
        Assert.True(Directory.Exists(dir), dir);
        Assert.Equal(ThemeCatalog.Ids.Count, Directory.GetFiles(dir, "*.json").Length);
        foreach (var id in ThemeCatalog.Ids)
            Assert.Empty(ThemeCatalog.Validate(File.ReadAllText(Path.Combine(dir, id + ".json"))));
    }

    [Fact]
    public void Static_swatches_match_the_theme_files()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Game.Web/wwwroot/themes"));
        Assert.Equal(ThemeCatalog.Ids, ThemeCatalog.Swatches.Select(s => s.Id));
        foreach (var s in ThemeCatalog.Swatches)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, s.Id + ".json")));
            var r = doc.RootElement; var css = r.GetProperty("css");
            Assert.Equal(r.GetProperty("name").GetString(), s.Name);
            Assert.Equal(css.GetProperty("--bg").GetString(), s.Bg);
            Assert.Equal(css.GetProperty("--panel").GetString(), s.Panel);
            Assert.Equal(css.GetProperty("--accent").GetString(), s.Accent);
            Assert.Equal(css.GetProperty("--glow").GetString(), s.Glow);
        }
    }
}
