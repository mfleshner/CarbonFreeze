using System.IO.Compression;
using SaveSwitch.Core.Models;
using SaveSwitch.Core.Services;

namespace SaveSwitch.Core.Tests;

public class ProfileServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly AppConfig _config;
    private readonly ProfileService _svc;
    private readonly GameEntry _game;
    private readonly string _savePath;

    public ProfileServiceTests()
    {
        _config = new AppConfig { StoreRoot = _tmp.Sub("Store"), MaxBackupsPerGame = 20 };
        _svc = new ProfileService(new PathExpander(new FakeSteamLocator()), _config);
        _tmp.MkDir("Game", "SaveGames");
        _savePath = _tmp.Sub("Game", "SaveGames", "{SteamId64}");
        _game = new GameEntry { Name = "Test Game", SavePathTemplate = _savePath };
        _savePath = _savePath.Replace("{SteamId64}", "76561198012345678");
    }

    public void Dispose() => _tmp.Dispose();

    private void SeedSave(params (string Rel, string Content)[] files)
    {
        Directory.CreateDirectory(_savePath);
        foreach (var (rel, content) in files)
        {
            var p = Path.Combine(_savePath, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
        }
    }

    private string Store => _svc.GetStorePath(_game);
    private string Backups => _svc.GetBackupPath(_game);

    // ---------- paths ----------

    [Fact]
    public void Paths_use_slug_with_id_suffix_and_store_root()
    {
        Assert.Equal(_savePath, _svc.GetSavePath(_game));
        var slug = "test-game-" + _game.Id.ToString("N")[..8];
        Assert.Equal(Path.Combine(_config.StoreRoot!, slug, "profiles"), Store);
        Assert.Equal(Path.Combine(_config.StoreRoot!, slug, "backups"), Backups);

        _game.StoreRootOverride = _tmp.Sub("Override");
        Assert.Equal(Path.Combine(_tmp.Sub("Override"), slug, "profiles"), _svc.GetStorePath(_game));
        _game.StoreRootOverride = null;
    }

    [Fact]
    public void Slug_strips_invalid_chars()
    {
        var g = new GameEntry { Name = "  STAR WARS: Galactic / Racer  " };
        var slug = ProfileService.GetSlug(g);
        Assert.StartsWith("star-wars-galactic-racer-", slug);
        Assert.DoesNotContain(" ", slug);
        Assert.DoesNotContain(":", slug);
        Assert.Equal(-1, slug.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void Default_store_root_is_localappdata()
    {
        var svc = new ProfileService(new PathExpander(new FakeSteamLocator()), new AppConfig());
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveSwitch", "Store");
        Assert.StartsWith(expected + Path.DirectorySeparatorChar, svc.GetStorePath(_game));
    }

    // ---------- validation ----------

    [Theory]
    [InlineData("main")]
    [InlineData("kid 2")]
    [InlineData("a")]
    [InlineData("Dad_run.v2-final")]
    [InlineData("1234567890123456789012345678901234567890")]
    public void Valid_names_pass(string name) => Assert.Null(_svc.ValidateProfileName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..\\evil")]
    [InlineData("../evil")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData(" lead")]
    [InlineData("-lead")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    [InlineData("NUL.txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("12345678901234567890123456789012345678901")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("tab\tname")]
    public void Invalid_names_rejected(string name) => Assert.NotNull(_svc.ValidateProfileName(name));

    [Fact]
    public void Switch_rejects_invalid_name()
    {
        SeedSave(("a.sav", "x"));
        var ex = Assert.Throws<ProfileException>(() => _svc.Switch(_game, "..\\evil"));
        Assert.Contains("Invalid profile name", ex.Message);
        Assert.True(Directory.Exists(_savePath));
    }

    // ---------- switch ----------

    [Fact]
    public void First_switch_parks_main_and_creates_empty_target()
    {
        SeedSave(("Slot0.sav", "hello"), (@"sub\deep\x.bin", "world"));

        var result = _svc.Switch(_game, "kid");

        Assert.Equal("main", result.PreviousProfile);
        Assert.Equal("kid", result.NewProfile);
        Assert.True(result.NewProfileWasCreated);
        Assert.NotNull(result.BackupZipPath);
        Assert.True(File.Exists(result.BackupZipPath));

        Assert.Equal("kid", _game.ActiveProfile);
        Assert.NotNull(_game.LastSwitchedUtc);
        Assert.True(Directory.Exists(_savePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_savePath));

        var parked = Path.Combine(Store, "main");
        Assert.True(Directory.Exists(parked));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(parked, "Slot0.sav")));
        Assert.Equal("world", File.ReadAllText(Path.Combine(parked, "sub", "deep", "x.bin")));
    }

    [Fact]
    public void Switch_back_restores_every_file_and_subfolder()
    {
        SeedSave(("Slot0.sav", "main-data"), (@"sub\deep\x.bin", "main-deep"), ("empty.sav", ""));
        _svc.Switch(_game, "kid");
        File.WriteAllText(Path.Combine(_savePath, "Slot0.sav"), "kid-data");

        var result = _svc.Switch(_game, "main");

        Assert.Equal("kid", result.PreviousProfile);
        Assert.Equal("main", result.NewProfile);
        Assert.False(result.NewProfileWasCreated);
        Assert.Equal("main", _game.ActiveProfile);
        Assert.Equal("main-data", File.ReadAllText(Path.Combine(_savePath, "Slot0.sav")));
        Assert.Equal("main-deep", File.ReadAllText(Path.Combine(_savePath, "sub", "deep", "x.bin")));
        Assert.True(File.Exists(Path.Combine(_savePath, "empty.sav")));
        Assert.Equal(3, Directory.GetFiles(_savePath, "*", SearchOption.AllDirectories).Length);

        Assert.False(Directory.Exists(Path.Combine(Store, "main")));
        Assert.Equal("kid-data", File.ReadAllText(Path.Combine(Store, "kid", "Slot0.sav")));
    }

    [Fact]
    public void Switch_uses_on_disk_casing_of_parked_profile()
    {
        SeedSave(("a.sav", "1"));
        _svc.Switch(_game, "Kid");
        var r = _svc.Switch(_game, "main");
        Assert.Equal("Kid", r.PreviousProfile);
        var r2 = _svc.Switch(_game, "KID");
        Assert.Equal("Kid", r2.NewProfile);
        Assert.Equal("Kid", _game.ActiveProfile);
        Assert.False(r2.NewProfileWasCreated);
    }

    [Fact]
    public void Backup_zip_contains_files()
    {
        SeedSave(("Slot0.sav", "hello"), (@"sub\x.bin", "world"));
        var result = _svc.Switch(_game, "kid");

        Assert.Equal(Backups, Path.GetDirectoryName(result.BackupZipPath));
        Assert.Matches(@"^main-\d{8}-\d{6}\.zip$", Path.GetFileName(result.BackupZipPath!));
        using var zip = ZipFile.OpenRead(result.BackupZipPath!);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Slot0.sav", "sub/x.bin" }, names);
        using var reader = new StreamReader(zip.GetEntry("Slot0.sav")!.Open());
        Assert.Equal("hello", reader.ReadToEnd());
    }

    [Fact]
    public void Empty_save_folder_is_not_zipped_but_is_parked()
    {
        Directory.CreateDirectory(_savePath);
        var result = _svc.Switch(_game, "kid");
        Assert.Null(result.BackupZipPath);
        Assert.True(Directory.Exists(Path.Combine(Store, "main")));
        Assert.False(Directory.Exists(Backups) && Directory.EnumerateFiles(Backups).Any());
    }

    [Fact]
    public void Missing_save_folder_still_switches()
    {
        var result = _svc.Switch(_game, "kid");
        Assert.True(result.NewProfileWasCreated);
        Assert.Null(result.BackupZipPath);
        Assert.True(Directory.Exists(_savePath));
        Assert.False(Directory.Exists(Path.Combine(Store, "main")));
    }

    [Fact]
    public void Same_name_switch_throws()
    {
        SeedSave(("a.sav", "1"));
        var ex = Assert.Throws<ProfileException>(() => _svc.Switch(_game, "main"));
        Assert.Contains("already", ex.Message);
        Assert.Throws<ProfileException>(() => _svc.Switch(_game, "MAIN"));
        Assert.True(File.Exists(Path.Combine(_savePath, "a.sav")));
    }

    [Fact]
    public void Missing_parent_folder_throws()
    {
        var g = new GameEntry { Name = "Never Run", SavePathTemplate = _tmp.Sub("nope", "deeper", "saves") };
        var ex = Assert.Throws<ProfileException>(() => _svc.Switch(g, "kid"));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Consistency_guard_throws_when_both_exist()
    {
        SeedSave(("a.sav", "1"));
        Directory.CreateDirectory(Path.Combine(Store, "main"));
        File.WriteAllText(Path.Combine(Store, "main", "b.sav"), "2");

        var ex = Assert.Throws<ProfileException>(() => _svc.Switch(_game, "kid"));
        Assert.Contains("Both", ex.Message);
        Assert.Contains("by hand", ex.Message);

        // nothing touched
        Assert.True(File.Exists(Path.Combine(_savePath, "a.sav")));
        Assert.True(File.Exists(Path.Combine(Store, "main", "b.sav")));
        Assert.Equal("main", _game.ActiveProfile);
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void Crash_recovery_completes_when_save_missing_and_active_parked()
    {
        // simulate: previous run parked "main" then died before bringing "kid" in
        Directory.CreateDirectory(Path.Combine(Store, "main"));
        File.WriteAllText(Path.Combine(Store, "main", "a.sav"), "main");
        Directory.CreateDirectory(Path.Combine(Store, "kid"));
        File.WriteAllText(Path.Combine(Store, "kid", "a.sav"), "kid");
        Assert.False(Directory.Exists(_savePath));

        var result = _svc.Switch(_game, "kid");

        Assert.Null(result.BackupZipPath);
        Assert.False(result.NewProfileWasCreated);
        Assert.Equal("kid", _game.ActiveProfile);
        Assert.Equal("kid", File.ReadAllText(Path.Combine(_savePath, "a.sav")));
        Assert.True(Directory.Exists(Path.Combine(Store, "main")));
        Assert.False(Directory.Exists(Path.Combine(Store, "kid")));
    }

    [Fact]
    public void Store_inside_save_folder_is_refused()
    {
        SeedSave(("a.sav", "1"));
        _game.StoreRootOverride = Path.Combine(_savePath, "store");
        Assert.Throws<ProfileException>(() => _svc.Switch(_game, "kid"));
        _game.StoreRootOverride = null;
    }

    [Fact]
    public void Multiple_profiles_round_trip()
    {
        SeedSave(("a.sav", "main"));
        _svc.Switch(_game, "kid");
        File.WriteAllText(Path.Combine(_savePath, "a.sav"), "kid");
        _svc.Switch(_game, "guest");
        File.WriteAllText(Path.Combine(_savePath, "a.sav"), "guest");

        _svc.Switch(_game, "kid");
        Assert.Equal("kid", File.ReadAllText(Path.Combine(_savePath, "a.sav")));
        _svc.Switch(_game, "main");
        Assert.Equal("main", File.ReadAllText(Path.Combine(_savePath, "a.sav")));
        _svc.Switch(_game, "guest");
        Assert.Equal("guest", File.ReadAllText(Path.Combine(_savePath, "a.sav")));

        var parked = Directory.GetDirectories(Store).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "kid", "main" }, parked);
    }

    // ---------- cross-volume fallback ----------

    [Fact]
    public void CopyVerifyDelete_moves_tree_and_removes_source()
    {
        var src = _tmp.MkDir("src");
        File.WriteAllText(Path.Combine(src, "a.txt"), "aaa");
        Directory.CreateDirectory(Path.Combine(src, "sub", "deep"));
        File.WriteAllText(Path.Combine(src, "sub", "deep", "b.txt"), "bb");
        Directory.CreateDirectory(Path.Combine(src, "emptydir"));
        var dst = _tmp.Sub("dst");

        ProfileService.CopyVerifyDelete(src, dst);

        Assert.False(Directory.Exists(src));
        Assert.Equal("aaa", File.ReadAllText(Path.Combine(dst, "a.txt")));
        Assert.Equal("bb", File.ReadAllText(Path.Combine(dst, "sub", "deep", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(dst, "emptydir")));
    }

    [Fact]
    public void CopyVerifyDelete_refuses_existing_destination()
    {
        var src = _tmp.MkDir("src2");
        var dst = _tmp.MkDir("dst2");
        Assert.Throws<ProfileException>(() => ProfileService.CopyVerifyDelete(src, dst));
        Assert.True(Directory.Exists(src));
    }

    [Fact]
    public void SameVolume_compares_roots_case_insensitively()
    {
        Assert.True(ProfileService.SameVolume(@"c:\a\b", @"C:\x"));
        Assert.False(ProfileService.SameVolume(@"C:\a", @"D:\a"));
    }

    // ---------- rename ----------

    [Fact]
    public void Rename_parked_profile_moves_directory()
    {
        SeedSave(("a.sav", "1"));
        _svc.Switch(_game, "kid");
        _svc.Rename(_game, "main", "dad");
        Assert.False(Directory.Exists(Path.Combine(Store, "main")));
        Assert.True(File.Exists(Path.Combine(Store, "dad", "a.sav")));
        Assert.Equal("kid", _game.ActiveProfile);
    }

    [Fact]
    public void Rename_active_profile_only_updates_entry()
    {
        SeedSave(("a.sav", "1"));
        _svc.Rename(_game, "main", "dad");
        Assert.Equal("dad", _game.ActiveProfile);
        Assert.True(File.Exists(Path.Combine(_savePath, "a.sav")));
        Assert.False(Directory.Exists(Store));

        // case-only change of the active name
        _svc.Rename(_game, "dad", "Dad");
        Assert.Equal("Dad", _game.ActiveProfile);
    }

    [Fact]
    public void Rename_refuses_clashes_and_invalid_names()
    {
        SeedSave(("a.sav", "1"));
        _svc.Switch(_game, "kid");
        Assert.Throws<ProfileException>(() => _svc.Rename(_game, "main", "kid"));   // clashes with active
        Assert.Throws<ProfileException>(() => _svc.Rename(_game, "main", "KID"));   // case-insensitive clash
        Assert.Throws<ProfileException>(() => _svc.Rename(_game, "kid", "main"));   // active -> clashes with parked
        Assert.Throws<ProfileException>(() => _svc.Rename(_game, "main", "CON"));
        Assert.Throws<ProfileException>(() => _svc.Rename(_game, "ghost", "x"));    // no such profile
        Assert.True(Directory.Exists(Path.Combine(Store, "main")));
    }

    [Fact]
    public void Rename_parked_profile_case_only()
    {
        SeedSave(("a.sav", "1"));
        _svc.Switch(_game, "kid");
        _svc.Rename(_game, "main", "Main");
        var name = Directory.GetDirectories(Store).Select(Path.GetFileName).Single();
        Assert.Equal("Main", name);
    }

    // ---------- delete ----------

    [Fact]
    public void Delete_active_refused()
    {
        SeedSave(("a.sav", "1"));
        var ex = Assert.Throws<ProfileException>(() => _svc.DeleteProfile(_game, "main"));
        Assert.Contains("active", ex.Message);
        Assert.Throws<ProfileException>(() => _svc.DeleteProfile(_game, "MAIN"));
        Assert.True(File.Exists(Path.Combine(_savePath, "a.sav")));
    }

    [Fact]
    public void Delete_parked_zips_then_removes()
    {
        SeedSave(("a.sav", "main-data"));
        _svc.Switch(_game, "kid");

        var zip = _svc.DeleteProfile(_game, "main");

        Assert.False(Directory.Exists(Path.Combine(Store, "main")));
        Assert.True(File.Exists(zip));
        Assert.Matches(@"^main-deleted-\d{8}-\d{6}\.zip$", Path.GetFileName(zip));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "a.sav");

        var backups = _svc.ListBackups(_game);
        Assert.Contains(backups, b => b.ProfileName == "main (deleted)" && b.ZipPath == zip);
    }

    [Fact]
    public void Delete_empty_parked_returns_empty_string()
    {
        Directory.CreateDirectory(Path.Combine(Store, "empty"));
        Assert.Equal("", _svc.DeleteProfile(_game, "empty"));
        Assert.False(Directory.Exists(Path.Combine(Store, "empty")));
    }

    [Fact]
    public void Delete_unknown_throws()
    {
        Assert.Throws<ProfileException>(() => _svc.DeleteProfile(_game, "ghost"));
    }

    // ---------- backups ----------

    [Fact]
    public void BackupNow_zips_active_and_returns_null_when_empty()
    {
        Assert.Null(_svc.BackupNow(_game));
        Directory.CreateDirectory(_savePath);
        Assert.Null(_svc.BackupNow(_game));

        File.WriteAllText(Path.Combine(_savePath, "a.sav"), "1");
        var zip = _svc.BackupNow(_game);
        Assert.NotNull(zip);
        Assert.True(File.Exists(zip));
        Assert.StartsWith("main-", Path.GetFileName(zip!));
        Assert.True(File.Exists(Path.Combine(_savePath, "a.sav")));
    }

    [Fact]
    public void Backups_in_same_second_get_distinct_names()
    {
        SeedSave(("a.sav", "1"));
        var z1 = _svc.BackupNow(_game);
        var z2 = _svc.BackupNow(_game);
        var z3 = _svc.BackupNow(_game);
        Assert.Equal(3, new[] { z1, z2, z3 }.Distinct().Count());
        Assert.Equal(3, _svc.ListBackups(_game).Count);
    }

    [Fact]
    public void Pruning_respects_MaxBackupsPerGame()
    {
        _config.MaxBackupsPerGame = 3;
        Directory.CreateDirectory(Backups);
        var stamps = new[] { "20260101-010101", "20260102-010101", "20260103-010101", "20260104-010101" };
        foreach (var s in stamps) File.WriteAllBytes(Path.Combine(Backups, $"main-{s}.zip"), new byte[] { 0x50, 0x4B });

        SeedSave(("a.sav", "1"));
        var zip = _svc.BackupNow(_game);

        var remaining = Directory.GetFiles(Backups, "*.zip").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(3, remaining.Count);
        Assert.Equal(Path.GetFileName(zip), remaining.Last());
        Assert.DoesNotContain("main-20260101-010101.zip", remaining);
        Assert.DoesNotContain("main-20260102-010101.zip", remaining);
        Assert.Contains("main-20260103-010101.zip", remaining);
        Assert.Contains("main-20260104-010101.zip", remaining);
    }

    [Fact]
    public void Pruning_disabled_when_zero()
    {
        _config.MaxBackupsPerGame = 0;
        Directory.CreateDirectory(Backups);
        for (var i = 1; i <= 5; i++)
            File.WriteAllBytes(Path.Combine(Backups, $"main-2026010{i}-010101.zip"), new byte[] { 1 });
        SeedSave(("a.sav", "1"));
        _svc.BackupNow(_game);
        Assert.Equal(6, Directory.GetFiles(Backups, "*.zip").Length);
    }

    [Fact]
    public void ListBackups_parses_names_newest_first()
    {
        Directory.CreateDirectory(Backups);
        File.WriteAllBytes(Path.Combine(Backups, "main-20260101-120000.zip"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(Backups, "kid 2-20260301-080000.zip"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(Backups, "old-deleted-20260201-000000.zip"), new byte[] { 1, 2 });
        var weird = Path.Combine(Backups, "weird.zip");
        File.WriteAllBytes(weird, new byte[] { 1 });
        File.SetCreationTimeUtc(weird, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // unparseable name falls back to file time

        var list = _svc.ListBackups(_game);

        Assert.Equal(4, list.Count);
        Assert.Equal("kid 2", list[0].ProfileName);
        Assert.Equal("old (deleted)", list[1].ProfileName);
        Assert.Equal("main", list[2].ProfileName);
        Assert.Equal(3, list[2].SizeBytes);
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(), list[2].CreatedUtc);
        Assert.Equal("weird", list[3].ProfileName);
        Assert.True(list.Zip(list.Skip(1)).All(p => p.First.CreatedUtc >= p.Second.CreatedUtc));
    }

    [Fact]
    public void ListBackups_empty_when_no_dir()
    {
        Assert.Empty(_svc.ListBackups(_game));
    }

    // ---------- list profiles ----------

    [Fact]
    public void ListProfiles_active_first_then_parked_sorted()
    {
        SeedSave(("a.sav", "12345"), (@"sub\b.sav", "67"));
        _svc.Switch(_game, "zeta");
        _svc.Switch(_game, "Alpha");
        _svc.Switch(_game, "mid");
        File.WriteAllText(Path.Combine(_savePath, "x.sav", ""), "");

        var list = _svc.ListProfiles(_game);

        Assert.Equal(new[] { "mid", "Alpha", "main", "zeta" }, list.Select(p => p.Name));
        Assert.True(list[0].IsActive);
        Assert.All(list.Skip(1), p => Assert.False(p.IsActive));

        var main = list.Single(p => p.Name == "main");
        Assert.Equal(2, main.FileCount);
        Assert.Equal(7, main.SizeBytes);
        Assert.NotNull(main.LastWriteUtc);

        var zeta = list.Single(p => p.Name == "zeta");
        Assert.Equal(0, zeta.FileCount);
        Assert.Null(zeta.LastWriteUtc);
    }

    [Fact]
    public void ListProfiles_when_save_folder_missing_returns_active_with_zeros()
    {
        var list = _svc.ListProfiles(_game);
        var only = Assert.Single(list);
        Assert.Equal("main", only.Name);
        Assert.True(only.IsActive);
        Assert.Equal(0, only.FileCount);
        Assert.Equal(0, only.SizeBytes);
        Assert.Null(only.LastWriteUtc);
    }
}
