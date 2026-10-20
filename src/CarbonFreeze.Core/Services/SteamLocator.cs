using System.Diagnostics;
using Microsoft.Win32;
using CarbonFreeze.Core.Models;
using CarbonFreeze.Core.Steam;

namespace CarbonFreeze.Core.Services;

/// <summary>Finds the local Steam install, its libraries, installed games and logged-in users. Windows only.</summary>
public sealed class SteamLocator : ISteamLocator
{
    /// <summary>Offset between a SteamID3 account number and the corresponding SteamID64.</summary>
    public const long SteamId64Base = 76561197960265728L;

    private const string SteamworksRedistAppId = "228980";

    private readonly Func<string?> _steamPathResolver;

    public SteamLocator() : this(ResolveSteamPathFromSystem) { }

    /// <summary>Test hook: supply the Steam root directly instead of reading the registry.</summary>
    internal SteamLocator(Func<string?> steamPathResolver)
    {
        _steamPathResolver = steamPathResolver;
    }

    public SteamInstall? FindInstall()
    {
        string? root;
        try
        {
            root = _steamPathResolver();
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(root)) return null;
        root = Normalise(root);
        if (!Directory.Exists(root)) return null;

        var libraries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            string n;
            try { n = Normalise(p); } catch { return; }
            if (seen.Add(n)) libraries.Add(n);
        }

        Add(root);

        var vdf = VdfParser.TryParseFile(Path.Combine(root, "steamapps", "libraryfolders.vdf"));
        var folders = vdf?["libraryfolders"];
        if (folders is not null)
        {
            foreach (var entry in folders.Children)
            {
                if (entry.IsObject)
                {
                    Add(entry.GetString("path"));
                }
                else if (entry.Key.All(char.IsDigit))
                {
                    Add(entry.Value); // old format: "1" "D:\\SteamLibrary"
                }
            }
        }

        return new SteamInstall(root, libraries);
    }

    public IReadOnlyList<SteamGame> ListInstalledGames()
    {
        var install = FindInstall();
        if (install is null) return Array.Empty<SteamGame>();

        var games = new List<SteamGame>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in install.LibraryPaths)
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (var manifest in manifests)
            {
                var state = VdfParser.TryParseFile(manifest)?["AppState"];
                if (state is null) continue;

                var appId = state.GetString("appid")?.Trim();
                var name = state.GetString("name")?.Trim();
                var installDir = state.GetString("installdir")?.Trim() ?? "";
                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name)) continue;
                if (appId == SteamworksRedistAppId) continue;
                if (!seenIds.Add(appId)) continue;

                games.Add(new SteamGame(appId, name, installDir, library));
            }
        }

        games.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return games;
    }

    public IReadOnlyList<SteamUser> ListUsers()
    {
        var install = FindInstall();
        if (install is null) return Array.Empty<SteamUser>();

        var users = new List<SteamUser>();
        var vdf = VdfParser.TryParseFile(Path.Combine(install.SteamPath, "config", "loginusers.vdf"));
        var usersNode = vdf?["users"];
        if (usersNode is not null)
        {
            foreach (var u in usersNode.Children)
            {
                if (!u.IsObject || !long.TryParse(u.Key, out var id64)) continue;
                users.Add(new SteamUser(
                    SteamId64: u.Key,
                    SteamId3: ToSteamId3(id64),
                    AccountName: u.GetString("AccountName") ?? "",
                    PersonaName: u.GetString("PersonaName") ?? "",
                    MostRecent: u.GetString("MostRecent") == "1"));
            }
        }

        if (users.Count == 0)
        {
            var userdata = Path.Combine(install.SteamPath, "userdata");
            if (Directory.Exists(userdata))
            {
                foreach (var dir in Directory.EnumerateDirectories(userdata))
                {
                    var id3Text = Path.GetFileName(dir);
                    if (!long.TryParse(id3Text, out var id3) || id3 <= 0) continue;
                    users.Add(new SteamUser((id3 + SteamId64Base).ToString(), id3.ToString(), "", "", false));
                }
            }
        }

        return users;
    }

    public SteamUser? MostRecentUser()
    {
        var users = ListUsers();
        if (users.Count == 0) return null;

        var flagged = users.FirstOrDefault(u => u.MostRecent);
        if (flagged is not null) return flagged;

        var install = FindInstall();
        if (install is not null)
        {
            SteamUser? best = null;
            var bestTime = DateTime.MinValue;
            foreach (var u in users)
            {
                var dir = Path.Combine(install.SteamPath, "userdata", u.SteamId3);
                if (!Directory.Exists(dir)) continue;
                var t = Directory.GetLastWriteTimeUtc(dir);
                if (t > bestTime) { bestTime = t; best = u; }
            }
            if (best is not null) return best;
        }

        return users[0];
    }

    public bool AppUsesSteamCloud(string appId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(appId)) return false;
            var install = FindInstall();
            if (install is null) return false;
            var user = MostRecentUser();
            if (user is null) return false;

            var userdata = Path.Combine(install.SteamPath, "userdata", user.SteamId3);
            if (!File.Exists(Path.Combine(userdata, appId.Trim(), "remotecache.vdf"))) return false;

            // remotecache.vdf is left behind after the user turns Cloud off for a game, so it only
            // proves Cloud was on at some point. The per-game switch itself lives in sharedconfig.vdf.
            return IsCloudEnabledForApp(userdata, appId.Trim()) ?? true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the per-game "Keep games saves in the Steam Cloud" switch from
    /// userdata\&lt;id3&gt;\7\remote\sharedconfig.vdf. Returns null when the file or the entry is absent
    /// (Steam only writes the key once the user has toggled it).
    /// </summary>
    private static bool? IsCloudEnabledForApp(string userdata, string appId)
    {
        var shared = VdfParser.TryParseFile(Path.Combine(userdata, "7", "remote", "sharedconfig.vdf"));
        var value = shared?["UserRoamingConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"]?[appId]?.GetString("cloudenabled");
        return value switch
        {
            "0" => false,
            "1" => true,
            _ => null,
        };
    }

    public string? GuessSavePathTemplate(SteamGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        try
        {
            var install = FindInstall();
            if (install is null) return null;

            var user = MostRecentUser();
            if (user is not null)
            {
                var cache = VdfParser.TryParseFile(Path.Combine(install.SteamPath, "userdata", user.SteamId3, game.AppId, "remotecache.vdf"));
                if (cache is not null)
                {
                    var fromCache = SavePathGuesser.FromRemoteCache(cache, game.AppId, user, install.SteamPath, game);
                    if (fromCache is not null) return fromCache;
                }
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var bases = new List<SavePathGuesser.BaseFolder>();
            if (localAppData.Length > 0)
            {
                bases.Add(new("%LOCALAPPDATA%", localAppData));
                bases.Add(new("{LocalLow}", Path.GetFullPath(Path.Combine(localAppData, "..", "LocalLow")), ScanOneLevel: true));
            }
            if (roaming.Length > 0) bases.Add(new("%APPDATA%", roaming));
            if (documents.Length > 0)
            {
                bases.Add(new("{Documents}\\My Games", Path.Combine(documents, "My Games")));
                bases.Add(new("{Documents}", documents));
            }
            if (profile.Length > 0) bases.Add(new("{SavedGames}", Path.Combine(profile, "Saved Games")));

            return SavePathGuesser.FromCommonFolders(game.Name, game.InstallDir, bases, Directory.Exists, Directory.EnumerateDirectories);
        }
        catch
        {
            return null;
        }
    }

    public void Launch(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) throw new ArgumentException("Steam app id is required.", nameof(appId));
        Process.Start(new ProcessStartInfo($"steam://rungameid/{appId.Trim()}") { UseShellExecute = true });
    }

    public static string ToSteamId3(long steamId64) => (steamId64 - SteamId64Base).ToString();

    private static string Normalise(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? ResolveSteamPathFromSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            var fromHkcu = ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            if (!string.IsNullOrWhiteSpace(fromHkcu) && Directory.Exists(fromHkcu)) return fromHkcu;

            var fromHklm = ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            if (!string.IsNullOrWhiteSpace(fromHklm) && Directory.Exists(fromHklm)) return fromHklm;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) is { Length: > 0 } pf ? pf : @"C:\Program Files (x86)",
            "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static string? ReadRegistryString(RegistryKey hive, string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
