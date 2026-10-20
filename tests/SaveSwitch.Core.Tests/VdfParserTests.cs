using SaveSwitch.Core.Steam;

namespace SaveSwitch.Core.Tests;

public class VdfParserTests
{
    [Fact]
    public void Parses_nested_objects_and_values()
    {
        const string text = """
            "root"
            {
                "a"    "1"
                "b"
                {
                    "c"    "two"
                    "d"
                    {
                        "e"    "3"
                    }
                }
            }
            """;

        var root = VdfParser.Parse(text);
        var r = root["root"];
        Assert.NotNull(r);
        Assert.Equal("1", r!.GetString("a"));
        Assert.Equal("two", r["b"]!.GetString("c"));
        Assert.Equal("3", r["b"]!["d"]!.GetString("e"));
        Assert.True(r["b"]!.IsObject);
        Assert.False(r["a"]!.IsObject);
    }

    [Fact]
    public void Skips_comments_and_handles_bare_tokens()
    {
        const string text = """
            // leading comment
            "root"
            {
                // "commented"  "out"
                "key"   "value"   // trailing comment
                bare    token
                "url"   "http://example.com/x" // not a comment inside quotes
            }
            """;

        var r = VdfParser.Parse(text)["root"]!;
        Assert.Equal("value", r.GetString("key"));
        Assert.Equal("token", r.GetString("bare"));
        Assert.Equal("http://example.com/x", r.GetString("url"));
        Assert.Null(r["commented"]);
        Assert.Equal(3, r.Children.Count);
    }

    [Fact]
    public void Unescapes_backslashes_quotes_and_newlines()
    {
        const string text = """
            "root"
            {
                "path"   "C:\\Program Files (x86)\\Steam"
                "quote"  "say \"hi\""
                "nl"     "a\nb\tc"
            }
            """;

        var r = VdfParser.Parse(text)["root"]!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", r.GetString("path"));
        Assert.Equal("say \"hi\"", r.GetString("quote"));
        Assert.Equal("a\nb\tc", r.GetString("nl"));
    }

    [Fact]
    public void Key_lookup_is_case_insensitive()
    {
        var r = VdfParser.Parse("\"Root\" { \"KeY\" \"v\" }");
        Assert.Equal("v", r["root"]!.GetString("key"));
    }

    [Fact]
    public void Throws_on_missing_closing_brace()
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"root\" { \"a\" \"1\" "));
    }

    [Fact]
    public void Parses_libraryfolders_sample_new_format()
    {
        var root = VdfParser.Parse(Samples.LibraryFoldersNew);
        var folders = root["libraryfolders"]!;
        Assert.Equal(2, folders.Children.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders["0"]!.GetString("path"));
        Assert.Equal(@"D:\SteamLibrary", folders["1"]!.GetString("path"));
        Assert.Equal("1245620", folders["1"]!["apps"]!.Children[0].Key);
    }

    [Fact]
    public void Parses_libraryfolders_sample_old_format()
    {
        var folders = VdfParser.Parse(Samples.LibraryFoldersOld)["LibraryFolders"]!;
        Assert.Equal(@"D:\SteamLibrary", folders.GetString("1"));
        Assert.Equal("1567698419", folders.GetString("TimeNextStatsReport"));
    }

    [Fact]
    public void Parses_loginusers_sample()
    {
        var users = VdfParser.Parse(Samples.LoginUsers)["users"]!;
        Assert.Equal(2, users.Children.Count);
        var u = users["76561198012345678"]!;
        Assert.Equal("alice", u.GetString("AccountName"));
        Assert.Equal("Alice", u.GetString("PersonaName"));
        Assert.Equal("1", u.GetString("MostRecent"));
        Assert.Equal("0", users["76561198087654321"]!.GetString("MostRecent"));
    }

    [Fact]
    public void Parses_appmanifest_sample()
    {
        var state = VdfParser.Parse(Samples.AppManifest)["AppState"]!;
        Assert.Equal("1245620", state.GetString("appid"));
        Assert.Equal("ELDEN RING", state.GetString("name"));
        Assert.Equal("ELDEN RING", state.GetString("installdir"));
        Assert.Equal("1245621", state["InstalledDepots"]!.Children[0].Key);
    }

    [Fact]
    public void TryParseFile_returns_null_for_missing_or_bad_file()
    {
        using var tmp = new TempDir();
        Assert.Null(VdfParser.TryParseFile(tmp.Sub("nope.vdf")));
        var bad = tmp.WriteFile("bad.vdf", "\"x\" { \"a\" ");
        Assert.Null(VdfParser.TryParseFile(bad));
        var good = tmp.WriteFile("good.vdf", "\"x\" { \"a\" \"b\" }");
        Assert.Equal("b", VdfParser.TryParseFile(good)!["x"]!.GetString("a"));
    }
}

internal static class Samples
{
    public const string LibraryFoldersNew = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
                "contentid"		"1234567890123456789"
                "totalsize"		"0"
                "update_clean_bytes_tally"		"123"
                "time_last_update_verified"		"0"
                "apps"
                {
                    "228980"		"123456"
                    "4078430"		"5000000"
                }
            }
            "1"
            {
                "path"		"D:\\SteamLibrary"
                "label"		"Games"
                "contentid"		"987654321"
                "totalsize"		"2000398934016"
                "apps"
                {
                    "1245620"		"50000000000"
                }
            }
        }
        """;

    public const string LibraryFoldersOld = """
        "LibraryFolders"
        {
            "TimeNextStatsReport"		"1567698419"
            "ContentStatsID"		"-5345644263718619478"
            "1"		"D:\\SteamLibrary"
        }
        """;

    public const string LoginUsers = """
        "users"
        {
            "76561198012345678"
            {
                "AccountName"		"alice"
                "PersonaName"		"Alice"
                "RememberPassword"		"1"
                "WantsOfflineMode"		"0"
                "SkipOfflineModeWarning"		"0"
                "AllowAutoLogin"		"1"
                "MostRecent"		"1"
                "Timestamp"		"1700000000"
            }
            "76561198087654321"
            {
                "AccountName"		"bob"
                "PersonaName"		"Bob"
                "RememberPassword"		"1"
                "MostRecent"		"0"
                "Timestamp"		"1600000000"
            }
        }
        """;

    public const string AppManifest = """
        "AppState"
        {
            "appid"		"1245620"
            "Universe"		"1"
            "LauncherPath"		"C:\\Program Files (x86)\\Steam\\steam.exe"
            "name"		"ELDEN RING"
            "StateFlags"		"4"
            "installdir"		"ELDEN RING"
            "LastUpdated"		"1700000000"
            "SizeOnDisk"		"60000000000"
            "buildid"		"123456"
            "InstalledDepots"
            {
                "1245621"
                {
                    "manifest"		"111"
                    "size"		"222"
                }
            }
            "UserConfig"
            {
                "language"		"english"
            }
        }
        """;
}
