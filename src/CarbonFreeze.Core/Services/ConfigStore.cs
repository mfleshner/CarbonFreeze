using System.Text.Json;
using System.Text.Json.Serialization;
using CarbonFreeze.Core.Models;

namespace CarbonFreeze.Core.Services;

/// <summary>JSON-backed config store. Saves atomically (temp file + move).</summary>
public sealed class ConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ConfigStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CarbonFreeze", "config.json"))
    {
    }

    public ConfigStore(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentException("Config path is required.", nameof(configPath));
        ConfigPath = Path.GetFullPath(configPath);
    }

    public string ConfigPath { get; }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath)) return new AppConfig();
        var json = File.ReadAllText(ConfigPath);
        if (string.IsNullOrWhiteSpace(json)) return new AppConfig();
        var config = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        config.Games ??= new List<GameEntry>();
        return config;
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, Options);
        var temp = ConfigPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, ConfigPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }
}
