namespace CarbonFreeze.Core.Services;

/// <summary>
/// One-time move of data written by the app under its previous name, SaveSwitch. The config file
/// and the default store (parked profiles and backups) are moved, not copied, so an old copy of
/// the exe cannot keep switching against a store that has moved out from under it.
/// </summary>
public static class LegacyMigration
{
    public const string OldName = "SaveSwitch";
    public const string NewName = "CarbonFreeze";

    /// <summary>Migrates the real %APPDATA% and %LOCALAPPDATA% locations. Never throws.</summary>
    public static (bool Config, bool Store) Run()
    {
        try
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (roaming.Length == 0 || local.Length == 0) return (false, false);
            return Run(
                Path.Combine(roaming, OldName), Path.Combine(roaming, NewName),
                Path.Combine(local, OldName), Path.Combine(local, NewName));
        }
        catch
        {
            return (false, false);
        }
    }

    /// <summary>
    /// Moves <c>config.json</c> from <paramref name="oldConfigDir"/> and <c>Store</c> from
    /// <paramref name="oldLocalDir"/> when the old one exists and the new one does not. Returns what moved.
    /// </summary>
    public static (bool Config, bool Store) Run(string oldConfigDir, string newConfigDir, string oldLocalDir, string newLocalDir)
    {
        bool config = false, store = false;

        var oldConfig = Path.Combine(oldConfigDir, "config.json");
        var newConfig = Path.Combine(newConfigDir, "config.json");
        if (File.Exists(oldConfig) && !File.Exists(newConfig))
        {
            Directory.CreateDirectory(newConfigDir);
            File.Move(oldConfig, newConfig);
            config = true;
        }

        var oldStore = Path.Combine(oldLocalDir, "Store");
        var newStore = Path.Combine(newLocalDir, "Store");
        if (Directory.Exists(oldStore) && !Directory.Exists(newStore))
        {
            Directory.CreateDirectory(newLocalDir);
            Directory.Move(oldStore, newStore); // same volume: a rename, nothing is copied
            store = true;
        }

        return (config, store);
    }
}
