using SaveSwitch.Core.Models;
using SaveSwitch.Core.Services;

namespace SaveSwitch.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Load_returns_default_when_file_missing()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Sub("nested", "config.json"));
        var cfg = store.Load();
        Assert.NotNull(cfg);
        Assert.Empty(cfg.Games);
        Assert.Equal(20, cfg.MaxBackupsPerGame);
    }

    [Fact]
    public void Round_trips_config_and_creates_directory()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("deep", "er", "config.json");
        var store = new ConfigStore(path);

        var cfg = new AppConfig
        {
            StoreRoot = @"D:\Saves",
            MaxBackupsPerGame = 7,
            Games =
            {
                new GameEntry
                {
                    Name = "Test Game",
                    SavePathTemplate = @"%LOCALAPPDATA%\Test\{SteamId64}",
                    SteamAppId = "123",
                    ActiveProfile = "kid",
                    LastSwitchedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    Notes = "hi",
                },
            },
        };

        store.Save(cfg);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));

        var loaded = new ConfigStore(path).Load();
        Assert.Equal(@"D:\Saves", loaded.StoreRoot);
        Assert.Equal(7, loaded.MaxBackupsPerGame);
        var g = Assert.Single(loaded.Games);
        Assert.Equal(cfg.Games[0].Id, g.Id);
        Assert.Equal("Test Game", g.Name);
        Assert.Equal(@"%LOCALAPPDATA%\Test\{SteamId64}", g.SavePathTemplate);
        Assert.Equal("123", g.SteamAppId);
        Assert.Equal("kid", g.ActiveProfile);
        Assert.Equal(cfg.Games[0].LastSwitchedUtc, g.LastSwitchedUtc);
        Assert.Equal("hi", g.Notes);
    }

    [Fact]
    public void Save_overwrites_existing_file()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Sub("config.json"));
        store.Save(new AppConfig { MaxBackupsPerGame = 1 });
        store.Save(new AppConfig { MaxBackupsPerGame = 2 });
        Assert.Equal(2, store.Load().MaxBackupsPerGame);
    }

    [Fact]
    public void Load_tolerates_unknown_fields()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("config.json", """
            {
              "Version": 1,
              "FutureSetting": true,
              "Games": [ { "Name": "X", "SavePathTemplate": "C:\\x", "Extra": { "a": 1 } } ],
              "MaxBackupsPerGame": 3
            }
            """);
        var cfg = new ConfigStore(path).Load();
        Assert.Equal(3, cfg.MaxBackupsPerGame);
        Assert.Equal("X", Assert.Single(cfg.Games).Name);
    }
}
