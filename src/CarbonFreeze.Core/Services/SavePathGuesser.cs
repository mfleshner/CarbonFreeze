using CarbonFreeze.Core.Models;
using CarbonFreeze.Core.Steam;

namespace CarbonFreeze.Core.Services;

/// <summary>
/// Guesses a game's save-folder template. Primary source is Steam's own sync bookkeeping
/// (userdata\&lt;id3&gt;\&lt;appid&gt;\remotecache.vdf), which lists every synced file with a "root"
/// code naming the well-known folder it lives under. Fallback is a scan of the usual Windows save
/// locations for a folder named after the game.
/// </summary>
internal static class SavePathGuesser
{
    /// <summary>A base folder to scan, as the template the user will see and the resolved path on disk.</summary>
    public readonly record struct BaseFolder(string Template, string Path, bool ScanOneLevel = false);

    /// <summary>
    /// Maps Steam's ERemoteStorageFileRoot codes to a save-path template prefix. Returns null for roots
    /// that have no Windows equivalent.
    /// </summary>
    public static string? RootToTemplate(int root, string steamPath, string appId, SteamGame? game) => root switch
    {
        0 => Path.Combine(steamPath, "userdata", "{SteamId3}", appId, "remote"),      // Default: Steam Cloud API storage
        1 => game is { InstallDir.Length: > 0 }                                      // GameInstall
            ? Path.Combine(game.LibraryPath, "steamapps", "common", game.InstallDir)
            : null,
        2 => "{Documents}",                                                           // WinMyDocuments
        3 => "%LOCALAPPDATA%",                                                        // WinAppDataLocal
        4 => "%APPDATA%",                                                             // WinAppDataRoaming
        5 => Path.Combine(steamPath, "userdata", "{SteamId3}"),                       // SteamUserBaseStorage
        9 => "{SavedGames}",                                                          // WinSavedGames
        10 => "%PROGRAMDATA%",                                                        // WinProgramData
        12 => "{LocalLow}",                                                           // WinAppDataLocalLow
        _ => null,
    };

    /// <summary>
    /// Derives a save-folder template from a parsed remotecache.vdf. Picks the root most of the synced
    /// files live under, prefers files that look like saves (SaveGames, .sav, save, sav), takes the directory prefix they all
    /// share, and stops at the per-user folder when the path contains the user's Steam ID (which is
    /// replaced by the matching token). Returns null when nothing sensible can be derived.
    /// </summary>
    public static string? FromRemoteCache(VdfNode cache, string appId, SteamUser user, string steamPath, SteamGame? game)
    {
        var app = cache[appId] ?? cache.Children.FirstOrDefault(c => c.IsObject);
        if (app is null) return null;

        var entries = new List<(int Root, string Path)>();
        foreach (var child in app.Children)
        {
            if (!child.IsObject) continue;
            if (!int.TryParse(child.GetString("root"), out var root)) continue;
            var p = child.Key.Replace('\\', '/').Trim('/');
            if (p.Length == 0) continue;
            entries.Add((root, p));
        }
        if (entries.Count == 0) return null;

        var best = entries
            .GroupBy(e => e.Root)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First();

        var rootTemplate = RootToTemplate(best.Key, steamPath, appId, game);
        if (rootTemplate is null) return null;

        // Narrow to the files that are most clearly saves. Unreal games sync everything under
        // "Saved" (configs included), so "SaveGames" and ".sav" are tried before the broad match.
        var paths = best.Select(e => e.Path).ToList();
        foreach (var marker in new[] { "savegame", ".sav", "save", "sav" })
        {
            var subset = paths.Where(p => p.Contains(marker, StringComparison.OrdinalIgnoreCase)).ToList();
            if (subset.Count > 0) { paths = subset; break; }
        }

        var common = CommonDirectoryPrefix(paths);

        var idIndex = common.FindIndex(s => s == user.SteamId64 || s == user.SteamId3);
        if (idIndex >= 0) common = common.Take(idIndex + 1).ToList();

        if (common.Count == 0)
        {
            // Files sit directly in the root. Only Steam's own per-app folder is a safe swap target then.
            return best.Key == 0 ? rootTemplate : null;
        }

        var segments = common.Select(s =>
            s == user.SteamId64 ? "{SteamId64}" :
            s == user.SteamId3 ? "{SteamId3}" : s);

        return rootTemplate + "\\" + string.Join("\\", segments);
    }

    /// <summary>
    /// Looks for an existing folder named after the game under the usual save locations. Returns the
    /// template (base template plus folder name) of the first hit, or null.
    /// </summary>
    public static string? FromCommonFolders(
        string name,
        string installDir,
        IReadOnlyList<BaseFolder> bases,
        Func<string, bool> dirExists,
        Func<string, IEnumerable<string>> listDirs)
    {
        var candidates = NameCandidates(name, installDir);
        if (candidates.Count == 0) return null;

        foreach (var b in bases)
        {
            if (!dirExists(b.Path)) continue;

            foreach (var c in candidates)
            {
                if (dirExists(Path.Combine(b.Path, c)))
                    return b.Template + "\\" + OnDiskName(b.Path, c, listDirs);
            }

            if (!b.ScanOneLevel) continue;
            IEnumerable<string> subs;
            try { subs = listDirs(b.Path).ToList(); } catch { continue; }
            foreach (var sub in subs)
            {
                var subName = Path.GetFileName(sub);
                foreach (var c in candidates)
                {
                    if (dirExists(Path.Combine(sub, c)))
                        return b.Template + "\\" + subName + "\\" + OnDiskName(sub, c, listDirs);
                }
            }
        }
        return null;
    }

    /// <summary>Folder names match case-insensitively on Windows; return the casing actually on disk.</summary>
    private static string OnDiskName(string parent, string candidate, Func<string, IEnumerable<string>> listDirs)
    {
        try
        {
            foreach (var d in listDirs(parent))
            {
                var n = Path.GetFileName(d);
                if (string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase)) return n;
            }
        }
        catch { }
        return candidate;
    }

    internal static List<string> NameCandidates(string name, string installDir)
    {
        var raw = new[]
        {
            installDir,
            name,
            StripSymbols(name),
            new string(StripSymbols(name).Where(ch => !char.IsWhiteSpace(ch)).ToArray()),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var r in raw)
        {
            var t = r?.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (t.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            if (seen.Add(t)) list.Add(t);
        }
        return list;
    }

    private static string StripSymbols(string s)
    {
        var chars = s.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '\'').ToArray();
        var collapsed = new string(chars);
        while (collapsed.Contains("  ")) collapsed = collapsed.Replace("  ", " ");
        return collapsed.Trim();
    }

    private static List<string> CommonDirectoryPrefix(IEnumerable<string> paths)
    {
        List<string>? common = null;
        foreach (var p in paths)
        {
            var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dir = parts.Take(parts.Length - 1).ToList(); // drop the file name
            if (common is null) { common = dir; continue; }
            var n = 0;
            while (n < common.Count && n < dir.Count && string.Equals(common[n], dir[n], StringComparison.OrdinalIgnoreCase)) n++;
            common = common.Take(n).ToList();
            if (common.Count == 0) break;
        }
        return common ?? new List<string>();
    }
}
