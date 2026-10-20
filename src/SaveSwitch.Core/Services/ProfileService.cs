using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using SaveSwitch.Core.Models;

namespace SaveSwitch.Core.Services;

/// <summary>
/// Parks and restores save-game profiles by renaming folders. The active profile lives in the
/// game's save folder; every other profile sits under &lt;store&gt;\&lt;slug&gt;\profiles\&lt;name&gt;.
/// A parked copy of the active profile must never exist.
/// </summary>
public sealed partial class ProfileService : IProfileService
{
    internal const string StampFormat = "yyyyMMdd-HHmmss";
    private const string DeletedSuffix = "-deleted";
    private const int MaxNameLength = 40;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
    };

    private readonly IPathExpander _expander;
    private readonly AppConfig _config;

    public ProfileService(IPathExpander expander, AppConfig config)
    {
        _expander = expander ?? throw new ArgumentNullException(nameof(expander));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    // ---------- paths ----------

    public string GetSavePath(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return _expander.Expand(game.SavePathTemplate);
    }

    public string GetStorePath(GameEntry game) => Path.Combine(GetGameRoot(game), "profiles");

    public string GetBackupPath(GameEntry game) => Path.Combine(GetGameRoot(game), "backups");

    private string GetGameRoot(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var root = FirstNonBlank(game.StoreRootOverride, _config.StoreRoot);
        root = root is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveSwitch", "Store")
            : _expander.Expand(root);
        return Path.Combine(root, GetSlug(game));
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Folder name for a game: lowercased name with unsafe characters replaced, plus a short id suffix.</summary>
    internal static string GetSlug(GameEntry game)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (game.Name ?? "").Trim().ToLowerInvariant()
            .Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)
            .ToArray();
        var slug = MultiDashRegex().Replace(new string(chars), "-").Trim('-', '.');
        if (slug.Length == 0) slug = "game";
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-', '.');
        return slug + "-" + game.Id.ToString("N")[..8];
    }

    // ---------- validation ----------

    public string? ValidateProfileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Profile name is empty.";
        if (name.Length > MaxNameLength) return $"Profile name is longer than {MaxNameLength} characters.";
        if (!NameRegex().IsMatch(name))
            return "Profile name may only contain letters, digits, spaces, '_', '.' and '-', and must start with a letter or digit.";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Profile name cannot end with a dot or a space.";

        var stem = name.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(stem)) return $"'{stem}' is a reserved Windows device name.";
        return null;
    }

    private void RequireValidName(string name)
    {
        var reason = ValidateProfileName(name);
        if (reason is not null) throw new ProfileException($"Invalid profile name '{name}': {reason}");
    }

    // ---------- listing ----------

    public IReadOnlyList<ProfileInfo> ListProfiles(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var list = new List<ProfileInfo> { Describe(game.ActiveProfile, true, GetSavePath(game)) };

        var store = GetStorePath(game);
        if (Directory.Exists(store))
        {
            foreach (var dir in Directory.GetDirectories(store).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                list.Add(Describe(Path.GetFileName(dir), false, dir));
        }

        return list;
    }

    private static ProfileInfo Describe(string name, bool isActive, string dir)
    {
        if (!Directory.Exists(dir)) return new ProfileInfo(name, isActive, null, 0, 0);

        long size = 0;
        var count = 0;
        DateTime? newest = null;
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", AllFiles))
        {
            count++;
            size += file.Length;
            var t = file.LastWriteTimeUtc;
            if (newest is null || t > newest) newest = t;
        }

        return new ProfileInfo(name, isActive, newest, size, count);
    }

    // ---------- switch ----------

    public SwitchResult Switch(GameEntry game, string targetProfile)
    {
        ArgumentNullException.ThrowIfNull(game);
        RequireValidName(targetProfile);

        var active = game.ActiveProfile;
        if (string.IsNullOrWhiteSpace(active)) active = "main";
        if (NameEquals(targetProfile, active))
            throw new ProfileException($"'{active}' is already the active profile.");

        var savePath = GetSavePath(game);
        var parent = Path.GetDirectoryName(savePath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            throw new ProfileException(
                $"The folder that should contain the save ({parent ?? savePath}) does not exist. Run the game once so it creates its save location, then try again.");

        var store = GetStorePath(game);
        var backups = GetBackupPath(game);
        GuardNoNesting(savePath, store);

        var activeStore = Path.Combine(store, active);
        var saveExists = Directory.Exists(savePath);
        var activeParked = Directory.Exists(activeStore);

        if (saveExists && activeParked)
            throw new ProfileException(
                $"Both the active save folder ({savePath}) and a parked copy of '{active}' ({activeStore}) exist. SaveSwitch cannot tell which one is current. Resolve this by hand (delete or rename one of them), then try again.");

        string? zipPath = null;
        if (saveExists)
        {
            if (HasAnyFile(savePath))
            {
                zipPath = CreateZip(backups, active, savePath);
                Prune(backups);
            }

            Directory.CreateDirectory(store);
            MoveDirectory(savePath, activeStore, $"park the active save '{active}'");
        }
        // else if activeParked: a previous switch parked the save but failed before bringing the
        // target in. The park is already done; just continue with the target.

        var existingTarget = FindProfileDir(store, targetProfile);
        var created = false;
        string finalName;
        if (existingTarget is not null)
        {
            finalName = Path.GetFileName(existingTarget);
            MoveDirectory(existingTarget, savePath, $"activate profile '{finalName}'");
        }
        else
        {
            finalName = targetProfile;
            try
            {
                Directory.CreateDirectory(savePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ProfileException($"Could not create an empty save folder at {savePath}: {ex.Message}", ex);
            }
            created = true;
        }

        game.ActiveProfile = finalName;
        game.LastSwitchedUtc = DateTime.UtcNow;
        return new SwitchResult(active, finalName, created, zipPath);
    }

    private static void GuardNoNesting(string savePath, string store)
    {
        if (IsSameOrUnder(store, savePath))
            throw new ProfileException($"The profile store ({store}) is inside the save folder ({savePath}). Choose a different store root.");
        if (IsSameOrUnder(savePath, store))
            throw new ProfileException($"The save folder ({savePath}) is inside the profile store ({store}). Choose a different store root.");
    }

    private static bool IsSameOrUnder(string candidate, string ancestor)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestor));
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (string.Equals(a, c, StringComparison.OrdinalIgnoreCase)) return true;
        return c.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- rename / delete / backup ----------

    public void Rename(GameEntry game, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(oldName)) throw new ProfileException("No profile selected to rename.");
        RequireValidName(newName);
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;

        var store = GetStorePath(game);
        var isActive = NameEquals(oldName, game.ActiveProfile);
        var onlyCaseDiffers = NameEquals(oldName, newName);

        if (!onlyCaseDiffers)
        {
            if (NameEquals(newName, game.ActiveProfile))
                throw new ProfileException($"'{newName}' is the active profile. Choose another name.");
            var clash = FindProfileDir(store, newName);
            if (clash is not null)
                throw new ProfileException($"A profile named '{Path.GetFileName(clash)}' already exists.");
        }

        if (isActive)
        {
            game.ActiveProfile = newName;
            return;
        }

        var src = FindProfileDir(store, oldName)
            ?? throw new ProfileException($"No profile named '{oldName}' was found.");
        var dst = Path.Combine(store, newName);
        try
        {
            Directory.Move(src, dst);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileException($"Could not rename profile '{oldName}' to '{newName}': {ex.Message} A file in the folder may be open in another program.", ex);
        }
    }

    public string DeleteProfile(GameEntry game, string name)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(name)) throw new ProfileException("No profile selected to delete.");
        if (NameEquals(name, game.ActiveProfile))
            throw new ProfileException($"'{name}' is the active profile and cannot be deleted. Switch to another profile first.");

        var store = GetStorePath(game);
        var dir = FindProfileDir(store, name)
            ?? throw new ProfileException($"No parked profile named '{name}' was found.");
        var diskName = Path.GetFileName(dir);

        var zip = "";
        if (HasAnyFile(dir))
            zip = CreateZip(GetBackupPath(game), diskName + DeletedSuffix, dir);

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileException($"Profile '{diskName}' was zipped to {zip} but could not be deleted: {ex.Message} A file in the folder may be open in another program.", ex);
        }

        return zip;
    }

    public string? BackupNow(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var savePath = GetSavePath(game);
        if (!Directory.Exists(savePath) || !HasAnyFile(savePath)) return null;

        var backups = GetBackupPath(game);
        var zip = CreateZip(backups, game.ActiveProfile, savePath);
        Prune(backups);
        return zip;
    }

    public IReadOnlyList<BackupInfo> ListBackups(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var backups = GetBackupPath(game);
        if (!Directory.Exists(backups)) return Array.Empty<BackupInfo>();

        var list = new List<(BackupInfo Info, DateTime Sort)>();
        foreach (var file in new DirectoryInfo(backups).EnumerateFiles("*.zip"))
        {
            var parsed = ParseBackupName(file.Name);
            string profile;
            DateTime createdUtc;
            if (parsed is { } p)
            {
                profile = p.Profile;
                createdUtc = p.CreatedUtc;
            }
            else
            {
                profile = Path.GetFileNameWithoutExtension(file.Name);
                createdUtc = file.CreationTimeUtc;
            }

            if (profile.EndsWith(DeletedSuffix, StringComparison.OrdinalIgnoreCase))
                profile = profile[..^DeletedSuffix.Length] + " (deleted)";

            list.Add((new BackupInfo(profile, createdUtc, file.FullName, file.Length), createdUtc));
        }

        return list
            .OrderByDescending(x => x.Sort)
            .ThenByDescending(x => x.Info.ZipPath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Info)
            .ToList();
    }

    // ---------- helpers ----------

    private static bool NameEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds a parked profile dir by name, case-insensitively, returning its on-disk path.</summary>
    private static string? FindProfileDir(string store, string name)
    {
        if (!Directory.Exists(store)) return null;
        return Directory.EnumerateDirectories(store)
            .FirstOrDefault(d => NameEquals(Path.GetFileName(d), name));
    }

    private static bool HasAnyFile(string dir) =>
        Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", AllFiles).Any();

    /// <summary>Zips <paramref name="sourceDir"/> into &lt;backups&gt;\&lt;prefix&gt;-yyyyMMdd-HHmmss.zip and returns the zip path.</summary>
    private static string CreateZip(string backups, string prefix, string sourceDir)
    {
        Directory.CreateDirectory(backups);

        var now = DateTime.Now;
        string zipPath;
        do
        {
            zipPath = Path.Combine(backups, $"{prefix}-{now.ToString(StampFormat, CultureInfo.InvariantCulture)}.zip");
            now = now.AddSeconds(1);
        } while (File.Exists(zipPath));

        try
        {
            ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* best effort */ }
            throw new ProfileException(
                $"Could not back up {sourceDir} to {zipPath}: {ex.Message} The game or its launcher may still be running, or a save file is locked by another program.", ex);
        }

        return zipPath;
    }

    /// <summary>Deletes the oldest backup zips beyond config.MaxBackupsPerGame (0 = unlimited).</summary>
    private void Prune(string backups)
    {
        var max = _config.MaxBackupsPerGame;
        if (max <= 0 || !Directory.Exists(backups)) return;

        var zips = new DirectoryInfo(backups).EnumerateFiles("*.zip")
            .Select(f => (File: f, Stamp: ParseBackupName(f.Name)?.CreatedUtc ?? f.CreationTimeUtc))
            .OrderBy(x => x.Stamp)
            .ThenBy(x => x.File.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var excess = zips.Count - max;
        for (var i = 0; i < excess; i++)
        {
            try { zips[i].File.Delete(); } catch { /* leave it; try again next time */ }
        }
    }

    /// <summary>Parses "&lt;profile&gt;-yyyyMMdd-HHmmss.zip". The stamp is local time; the result is UTC.</summary>
    internal static (string Profile, DateTime CreatedUtc)? ParseBackupName(string fileName)
    {
        var m = BackupNameRegex().Match(fileName);
        if (!m.Success) return null;
        if (!DateTime.TryParseExact(m.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var local))
            return null;
        return (m.Groups["name"].Value, local.ToUniversalTime());
    }

    /// <summary>Rename on the same volume; otherwise copy, verify, then delete the source.</summary>
    private static void MoveDirectory(string source, string destination, string what)
    {
        try
        {
            if (SameVolume(source, destination))
                Directory.Move(source, destination);
            else
                CopyVerifyDelete(source, destination);
        }
        catch (ProfileException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileException(
                $"Could not {what} (moving {source} to {destination}): {ex.Message} The game or its launcher may still be running, or a file in the folder is open in another program. Close them and try again.", ex);
        }
    }

    internal static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Cross-volume fallback: copy the tree, verify file count and total size, then delete the source.</summary>
    internal static void CopyVerifyDelete(string source, string destination)
    {
        if (Directory.Exists(destination))
            throw new ProfileException($"Destination {destination} already exists; refusing to overwrite it.");

        var (srcCount, srcSize) = Measure(source);

        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", AllFiles))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", AllFiles))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target, overwrite: false);
        }

        var (dstCount, dstSize) = Measure(destination);
        if (srcCount != dstCount || srcSize != dstSize)
            throw new ProfileException(
                $"Copy of {source} to {destination} did not verify ({srcCount} files/{srcSize} bytes vs {dstCount} files/{dstSize} bytes). Both copies were left in place; nothing was deleted.");

        Directory.Delete(source, recursive: true);
    }

    private static (int Count, long Size) Measure(string dir)
    {
        var count = 0;
        long size = 0;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", AllFiles))
        {
            count++;
            size += f.Length;
        }
        return (count, size);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _.-]*$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^(?<name>.+)-(?<stamp>\d{8}-\d{6})\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex BackupNameRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultiDashRegex();
}
