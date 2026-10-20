using System.Text.Json;
using SaveSwitch.Core.Models;

namespace SaveSwitch.Core.Services;

/// <summary>Built-in game presets, loaded from the embedded Presets\presets.json.</summary>
public sealed class PresetCatalog : IPresetCatalog
{
    internal const string ResourceName = "SaveSwitch.Core.Presets.presets.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public PresetCatalog()
    {
        All = Load();
    }

    public IReadOnlyList<GamePreset> All { get; }

    private static IReadOnlyList<GamePreset> Load()
    {
        using var stream = typeof(PresetCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        var presets = JsonSerializer.Deserialize<List<GamePreset>>(stream, Options) ?? new List<GamePreset>();
        return presets
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
