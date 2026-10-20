namespace CarbonFreeze.Core.Models;

/// <summary>A game registered in CarbonFreeze. Persisted in config.json.</summary>
public sealed class GameEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>
    /// Folder whose *contents* are one complete save. May contain environment variables
    /// (%LOCALAPPDATA%) and tokens ({SteamId64}, {SteamId3}, {Documents}, {SavedGames}).
    /// Resolved via IPathExpander at use time.
    /// </summary>
    public string SavePathTemplate { get; set; } = "";

    /// <summary>Steam app id, if known. Enables steam://rungameid launch and cloud warning.</summary>
    public string? SteamAppId { get; set; }

    /// <summary>Name of the profile currently sitting in the save folder.</summary>
    public string ActiveProfile { get; set; } = "main";

    /// <summary>Optional per-game override of where parked profiles and backups live.</summary>
    public string? StoreRootOverride { get; set; }

    public DateTime? LastSwitchedUtc { get; set; }
    public string? Notes { get; set; }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public List<GameEntry> Games { get; set; } = new();

    /// <summary>Default store root. Null means %LOCALAPPDATA%\CarbonFreeze\Store.</summary>
    public string? StoreRoot { get; set; }

    /// <summary>Backups kept per game before the oldest are pruned. 0 = unlimited.</summary>
    public int MaxBackupsPerGame { get; set; } = 20;

    /// <summary>Colour theme: "System" (follow Windows app mode), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
}

public sealed record ProfileInfo(
    string Name,
    bool IsActive,
    DateTime? LastWriteUtc,
    long SizeBytes,
    int FileCount);

public sealed record BackupInfo(
    string ProfileName,
    DateTime CreatedUtc,
    string ZipPath,
    long SizeBytes);

public sealed record SwitchResult(
    string PreviousProfile,
    string NewProfile,
    bool NewProfileWasCreated,
    string? BackupZipPath);

public sealed record GamePreset(
    string Name,
    string SavePathTemplate,
    string? SteamAppId,
    string? Notes);

public sealed record SteamInstall(string SteamPath, IReadOnlyList<string> LibraryPaths);

public sealed record SteamGame(string AppId, string Name, string InstallDir, string LibraryPath);

public sealed record SteamUser(string SteamId64, string SteamId3, string AccountName, string PersonaName, bool MostRecent);
