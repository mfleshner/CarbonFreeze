// TEMPORARY SKELETONS. The core agent replaces each class with a real implementation
// in its own file and deletes this file. Constructor signatures are the contract.
using SaveSwitch.Core.Models;

namespace SaveSwitch.Core.Services;

public sealed class ConfigStore : IConfigStore
{
    public ConfigStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveSwitch", "config.json")) { }
    public ConfigStore(string configPath) { ConfigPath = configPath; }
    public string ConfigPath { get; }
    public AppConfig Load() => throw new NotImplementedException();
    public void Save(AppConfig config) => throw new NotImplementedException();
}

public sealed class PathExpander : IPathExpander
{
    public PathExpander(ISteamLocator steam) { }
    public string Expand(string template) => throw new NotImplementedException();
    public IReadOnlyDictionary<string, string> DescribeTokens() => throw new NotImplementedException();
}

public sealed class ProfileService : IProfileService
{
    public ProfileService(IPathExpander expander, AppConfig config) { }
    public string GetSavePath(GameEntry game) => throw new NotImplementedException();
    public string GetStorePath(GameEntry game) => throw new NotImplementedException();
    public string GetBackupPath(GameEntry game) => throw new NotImplementedException();
    public IReadOnlyList<ProfileInfo> ListProfiles(GameEntry game) => throw new NotImplementedException();
    public SwitchResult Switch(GameEntry game, string targetProfile) => throw new NotImplementedException();
    public void Rename(GameEntry game, string oldName, string newName) => throw new NotImplementedException();
    public string DeleteProfile(GameEntry game, string name) => throw new NotImplementedException();
    public string? BackupNow(GameEntry game) => throw new NotImplementedException();
    public IReadOnlyList<BackupInfo> ListBackups(GameEntry game) => throw new NotImplementedException();
    public string? ValidateProfileName(string name) => throw new NotImplementedException();
}

public sealed class SteamLocator : ISteamLocator
{
    public SteamLocator() { }
    public SteamInstall? FindInstall() => throw new NotImplementedException();
    public IReadOnlyList<SteamGame> ListInstalledGames() => throw new NotImplementedException();
    public IReadOnlyList<SteamUser> ListUsers() => throw new NotImplementedException();
    public SteamUser? MostRecentUser() => throw new NotImplementedException();
    public bool AppUsesSteamCloud(string appId) => throw new NotImplementedException();
    public void Launch(string appId) => throw new NotImplementedException();
}

public sealed class PresetCatalog : IPresetCatalog
{
    public PresetCatalog() { }
    public IReadOnlyList<GamePreset> All => throw new NotImplementedException();
}
