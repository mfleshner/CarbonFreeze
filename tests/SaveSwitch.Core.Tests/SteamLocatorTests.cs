using SaveSwitch.Core.Services;

namespace SaveSwitch.Core.Tests;

public class SteamLocatorTests
{
    private static (TempDir Tmp, string SteamRoot, SteamLocator Locator) MakeSteam()
    {
        var tmp = new TempDir();
        var root = tmp.MkDir("Steam");
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        return (tmp, root, new SteamLocator(() => root));
    }

    [Fact]
    public void FindInstall_returns_null_when_path_missing()
    {
        var locator = new SteamLocator(() => @"C:\definitely\not\here\Steam");
        Assert.Null(locator.FindInstall());
        Assert.Null(new SteamLocator(() => null).FindInstall());
    }

    [Fact]
    public void FindInstall_reads_library_folders_and_dedupes()
    {
        var (tmp, root, locator) = MakeSteam();
        using (tmp)
        {
            var lib2 = tmp.MkDir("Lib2");
            var escapedRoot = root.Replace(@"\", @"\\");
            var escapedLib2 = lib2.Replace(@"\", @"\\");
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), $$"""
                "libraryfolders"
                {
                    "0" { "path" "{{escapedRoot}}\\" }
                    "1" { "path" "{{escapedLib2}}" }
                    "2" { "path" "{{escapedLib2.ToUpperInvariant()}}" }
                }
                """);

            var install = locator.FindInstall();
            Assert.NotNull(install);
            Assert.Equal(root, install!.SteamPath);
            Assert.Equal(2, install.LibraryPaths.Count);
            Assert.Equal(root, install.LibraryPaths[0]);
            Assert.Equal(lib2, install.LibraryPaths[1], ignoreCase: true);
        }
    }

    [Fact]
    public void FindInstall_tolerates_old_library_format()
    {
        var (tmp, root, locator) = MakeSteam();
        using (tmp)
        {
            var lib2 = tmp.MkDir("Old");
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), $$"""
                "LibraryFolders"
                {
                    "TimeNextStatsReport"  "1"
                    "1"  "{{lib2.Replace(@"\", @"\\")}}"
                }
                """);
            var install = locator.FindInstall()!;
            Assert.Equal(new[] { root, lib2 }, install.LibraryPaths);
        }
    }

    [Fact]
    public void ListInstalledGames_skips_redist_and_sorts()
    {
        var (tmp, root, locator) = MakeSteam();
        using (tmp)
        {
            var apps = Path.Combine(root, "steamapps");
            File.WriteAllText(Path.Combine(apps, "appmanifest_1245620.acf"), Samples.AppManifest);
            File.WriteAllText(Path.Combine(apps, "appmanifest_228980.acf"), """
                "AppState" { "appid" "228980" "name" "Steamworks Common Redistributables" "installdir" "Steamworks Shared" }
                """);
            File.WriteAllText(Path.Combine(apps, "appmanifest_4078430.acf"), """
                "AppState" { "appid" "4078430" "name" "Galactic Racer" "installdir" "GR" }
                """);
            File.WriteAllText(Path.Combine(apps, "appmanifest_1.acf"), """
                "AppState" { "appid" "1" "installdir" "noname" }
                """);
            File.WriteAllText(Path.Combine(apps, "appmanifest_2.acf"), "garbage {{{");

            var games = locator.ListInstalledGames();
            Assert.Equal(new[] { "ELDEN RING", "Galactic Racer" }, games.Select(g => g.Name));
            Assert.Equal("1245620", games[0].AppId);
            Assert.Equal("ELDEN RING", games[0].InstallDir);
            Assert.Equal(root, games[0].LibraryPath);
        }
    }

    [Fact]
    public void ListUsers_and_MostRecent_from_loginusers()
    {
        var (tmp, root, locator) = MakeSteam();
        using (tmp)
        {
            File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), Samples.LoginUsers);
            var users = locator.ListUsers();
            Assert.Equal(2, users.Count);
            var alice = users.Single(u => u.AccountName == "alice");
            Assert.Equal("76561198012345678", alice.SteamId64);
            Assert.Equal("52079950", alice.SteamId3);
            Assert.True(alice.MostRecent);
            Assert.Equal("alice", locator.MostRecentUser()!.AccountName);
        }
    }

    [Fact]
    public void ListUsers_falls_back_to_userdata_dirs_and_cloud_heuristic_works()
    {
        var (tmp, root, locator) = MakeSteam();
        using (tmp)
        {
            Directory.CreateDirectory(Path.Combine(root, "userdata", "52079950", "4078430"));
            Directory.CreateDirectory(Path.Combine(root, "userdata", "notanid"));
            var users = locator.ListUsers();
            var u = Assert.Single(users);
            Assert.Equal("52079950", u.SteamId3);
            Assert.Equal("76561198012345678", u.SteamId64);
            Assert.Equal("", u.AccountName);
            Assert.Equal(u.SteamId64, locator.MostRecentUser()!.SteamId64);

            Assert.False(locator.AppUsesSteamCloud("4078430"));
            File.WriteAllText(Path.Combine(root, "userdata", "52079950", "4078430", "remotecache.vdf"), "\"4078430\" {}");
            Assert.True(locator.AppUsesSteamCloud("4078430"));
            Assert.False(locator.AppUsesSteamCloud("999"));
            Assert.False(locator.AppUsesSteamCloud(""));
        }
    }

    [Fact]
    public void Everything_is_empty_when_steam_missing()
    {
        var locator = new SteamLocator(() => null);
        Assert.Empty(locator.ListInstalledGames());
        Assert.Empty(locator.ListUsers());
        Assert.Null(locator.MostRecentUser());
        Assert.False(locator.AppUsesSteamCloud("1"));
    }

    [Fact]
    public void Real_locator_does_not_throw()
    {
        var locator = new SteamLocator();
        var install = locator.FindInstall();
        if (install is not null)
        {
            Assert.True(Directory.Exists(install.SteamPath));
            Assert.Contains(install.SteamPath, install.LibraryPaths);
        }
        _ = locator.ListInstalledGames();
        _ = locator.ListUsers();
        _ = locator.MostRecentUser();
        _ = locator.AppUsesSteamCloud("4078430");
    }
}
