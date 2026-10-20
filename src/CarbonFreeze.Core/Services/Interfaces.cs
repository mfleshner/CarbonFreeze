using CarbonFreeze.Core.Models;

namespace CarbonFreeze.Core.Services;

public interface IConfigStore
{
    string ConfigPath { get; }
    AppConfig Load();
    void Save(AppConfig config);
}

public interface IPathExpander
{
    /// <summary>
    /// Expands %ENV% variables and the tokens {SteamId64}, {SteamId3}, {UserProfile},
    /// {Documents}, {SavedGames}, {LocalLow}. Returns a full, normalised path.
    /// Throws PathTokenException if a Steam token is used and no Steam user can be found.
    /// </summary>
    string Expand(string template);

    /// <summary>Tokens this expander knows, for UI help text.</summary>
    IReadOnlyDictionary<string, string> DescribeTokens();
}

public interface IProfileService
{
    /// <summary>Resolved folder that holds the active save for this game.</summary>
    string GetSavePath(GameEntry game);

    /// <summary>Folder where parked profiles live: &lt;store&gt;\&lt;game-slug&gt;.</summary>
    string GetStorePath(GameEntry game);

    string GetBackupPath(GameEntry game);

    /// <summary>Active profile first, then parked ones alphabetically.</summary>
    IReadOnlyList<ProfileInfo> ListProfiles(GameEntry game);

    /// <summary>
    /// Zip the active save, park it under its profile name, bring <paramref name="targetProfile"/>
    /// into the save folder (creating an empty one if it does not exist yet), update
    /// game.ActiveProfile and LastSwitchedUtc. Caller persists the config.
    /// Same-volume moves are directory renames; cross-volume falls back to copy, verify, delete.
    /// Throws ProfileException (with a user-readable message) on any refusal.
    /// </summary>
    SwitchResult Switch(GameEntry game, string targetProfile);

    /// <summary>Rename a parked profile, or the active one (updates game.ActiveProfile).</summary>
    void Rename(GameEntry game, string oldName, string newName);

    /// <summary>Delete a parked profile after zipping it. Refuses to delete the active profile.</summary>
    string DeleteProfile(GameEntry game, string name);

    /// <summary>Zip the active save now, without switching. Returns zip path or null if empty.</summary>
    string? BackupNow(GameEntry game);

    IReadOnlyList<BackupInfo> ListBackups(GameEntry game);

    /// <summary>Validate a profile name; returns null if OK, else a reason.</summary>
    string? ValidateProfileName(string name);
}

public interface ISteamLocator
{
    SteamInstall? FindInstall();
    IReadOnlyList<SteamGame> ListInstalledGames();
    IReadOnlyList<SteamUser> ListUsers();
    SteamUser? MostRecentUser();

    /// <summary>
    /// Heuristic: userdata\&lt;id3&gt;\&lt;appid&gt;\remotecache.vdf exists for the most recent user and the
    /// per-game Cloud switch in userdata\&lt;id3&gt;\7\remote\sharedconfig.vdf is not "0".
    /// </summary>
    bool AppUsesSteamCloud(string appId);

    /// <summary>
    /// Best-effort save-folder template for an installed game: derived from Steam's sync bookkeeping
    /// (remotecache.vdf) when the game has ever synced, else from a scan of the usual save locations
    /// for a folder named after the game. Null when nothing is found. Never throws.
    /// </summary>
    string? GuessSavePathTemplate(SteamGame game);

    void Launch(string appId);
}

public interface IPresetCatalog
{
    IReadOnlyList<GamePreset> All { get; }
}

public class ProfileException : Exception
{
    public ProfileException(string message) : base(message) { }
    public ProfileException(string message, Exception inner) : base(message, inner) { }
}

public class PathTokenException : Exception
{
    public PathTokenException(string message) : base(message) { }
}
