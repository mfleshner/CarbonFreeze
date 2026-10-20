using CarbonFreeze.Core.Models;
using CarbonFreeze.Core.Services;

namespace CarbonFreeze.Core.Tests;

/// <summary>A throw-away directory under %TEMP% that is removed on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CarbonFreezeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public string MkDir(params string[] parts)
    {
        var p = Sub(parts);
        Directory.CreateDirectory(p);
        return p;
    }

    public string WriteFile(string relativePath, string content)
    {
        var p = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}

public sealed class FakeSteamLocator : ISteamLocator
{
    public SteamUser? User { get; set; } = new("76561198012345678", "52079950", "tester", "Tester", true);
    public bool CloudApps { get; set; }
    public List<string> Launched { get; } = new();

    public SteamInstall? FindInstall() => null;
    public IReadOnlyList<SteamGame> ListInstalledGames() => Array.Empty<SteamGame>();
    public IReadOnlyList<SteamUser> ListUsers() => User is null ? Array.Empty<SteamUser>() : new[] { User };
    public SteamUser? MostRecentUser() => User;
    public bool AppUsesSteamCloud(string appId) => CloudApps;
    public string? GuessSavePathTemplate(SteamGame game) => null;
    public void Launch(string appId) => Launched.Add(appId);
}
