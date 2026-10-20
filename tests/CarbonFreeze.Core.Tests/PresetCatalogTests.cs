using CarbonFreeze.Core.Services;

namespace CarbonFreeze.Core.Tests;

public class PresetCatalogTests
{
    [Fact]
    public void Loads_ten_presets_sorted_by_name()
    {
        var catalog = new PresetCatalog();
        Assert.Equal(10, catalog.All.Count);
        var names = catalog.All.Select(p => p.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
        Assert.Contains(catalog.All, p => p.Name == "STAR WARS: Galactic Racer" && p.SteamAppId == "4078430");
        Assert.Contains(catalog.All, p => p.Name == "Hades" && p.Notes!.Contains("{Documents}"));
        Assert.All(catalog.All, p => Assert.False(string.IsNullOrWhiteSpace(p.SavePathTemplate)));
    }

    [Fact]
    public void Every_template_expands_with_a_fake_steam_user()
    {
        var expander = new PathExpander(new FakeSteamLocator());
        foreach (var preset in new PresetCatalog().All)
        {
            var path = expander.Expand(preset.SavePathTemplate);
            Assert.True(Path.IsPathRooted(path), preset.Name);
            Assert.DoesNotContain("{", path);
            Assert.DoesNotContain("%", path);
        }
    }
}
