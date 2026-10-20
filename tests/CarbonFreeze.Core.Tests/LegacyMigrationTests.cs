using CarbonFreeze.Core.Services;

namespace CarbonFreeze.Core.Tests;

public class LegacyMigrationTests
{
    [Fact]
    public void Moves_config_and_store_once()
    {
        using var tmp = new TempDir();
        var oldCfg = tmp.MkDir("roaming", "SaveSwitch");
        var newCfg = Path.Combine(tmp.Path, "roaming", "CarbonFreeze");
        var oldLocal = tmp.MkDir("local", "SaveSwitch");
        var newLocal = Path.Combine(tmp.Path, "local", "CarbonFreeze");

        File.WriteAllText(Path.Combine(oldCfg, "config.json"), "{\"Version\":1}");
        Directory.CreateDirectory(Path.Combine(oldLocal, "Store", "game", "profiles", "main"));
        File.WriteAllText(Path.Combine(oldLocal, "Store", "game", "profiles", "main", "save.dat"), "x");

        var first = LegacyMigration.Run(oldCfg, newCfg, oldLocal, newLocal);
        Assert.Equal((true, true), first);
        Assert.True(File.Exists(Path.Combine(newCfg, "config.json")));
        Assert.False(File.Exists(Path.Combine(oldCfg, "config.json")));
        Assert.True(File.Exists(Path.Combine(newLocal, "Store", "game", "profiles", "main", "save.dat")));
        Assert.False(Directory.Exists(Path.Combine(oldLocal, "Store")));

        var second = LegacyMigration.Run(oldCfg, newCfg, oldLocal, newLocal);
        Assert.Equal((false, false), second);
    }

    [Fact]
    public void Never_overwrites_data_the_new_name_already_has()
    {
        using var tmp = new TempDir();
        var oldCfg = tmp.MkDir("roaming", "SaveSwitch");
        var newCfg = tmp.MkDir("roaming", "CarbonFreeze");
        var oldLocal = tmp.MkDir("local", "SaveSwitch");
        var newLocal = tmp.MkDir("local", "CarbonFreeze");

        File.WriteAllText(Path.Combine(oldCfg, "config.json"), "old");
        File.WriteAllText(Path.Combine(newCfg, "config.json"), "new");
        Directory.CreateDirectory(Path.Combine(oldLocal, "Store"));
        Directory.CreateDirectory(Path.Combine(newLocal, "Store"));

        Assert.Equal((false, false), LegacyMigration.Run(oldCfg, newCfg, oldLocal, newLocal));
        Assert.Equal("new", File.ReadAllText(Path.Combine(newCfg, "config.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(oldCfg, "config.json")));
        Assert.True(Directory.Exists(Path.Combine(oldLocal, "Store")));
    }

    [Fact]
    public void Nothing_to_do_when_old_locations_are_absent()
    {
        using var tmp = new TempDir();
        var r = LegacyMigration.Run(
            Path.Combine(tmp.Path, "a"), Path.Combine(tmp.Path, "b"),
            Path.Combine(tmp.Path, "c"), Path.Combine(tmp.Path, "d"));
        Assert.Equal((false, false), r);
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "b")));
    }
}
