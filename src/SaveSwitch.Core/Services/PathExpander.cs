using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SaveSwitch.Core.Models;

namespace SaveSwitch.Core.Services;

/// <summary>
/// Expands %ENV% variables plus the SaveSwitch tokens ({SteamId64}, {SteamId3}, {UserProfile},
/// {Documents}, {SavedGames}, {LocalLow}). Tokens are case-insensitive.
/// </summary>
public sealed partial class PathExpander : IPathExpander
{
    private static readonly Guid FolderIdSavedGames = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");

    private static readonly IReadOnlyDictionary<string, string> Tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["{SteamId64}"] = "64-bit Steam ID of the most recently logged-in Steam user (e.g. 76561198012345678).",
        ["{SteamId3}"] = "Short Steam account ID of the most recent user, as used under Steam\\userdata (e.g. 52079950).",
        ["{UserProfile}"] = "Your Windows user folder, e.g. C:\\Users\\you.",
        ["{Documents}"] = "Your Documents folder (follows redirection, e.g. OneDrive).",
        ["{SavedGames}"] = "The Windows 'Saved Games' known folder, usually C:\\Users\\you\\Saved Games.",
        ["{LocalLow}"] = "%LOCALAPPDATA%\\..\\LocalLow, used by Unity games.",
    };

    private readonly ISteamLocator _steam;

    public PathExpander(ISteamLocator steam)
    {
        _steam = steam ?? throw new ArgumentNullException(nameof(steam));
    }

    public string Expand(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException("Save path template is empty.", nameof(template));

        var expanded = Environment.ExpandEnvironmentVariables(template.Trim());
        expanded = TokenRegex().Replace(expanded, m => ResolveToken(m.Groups[1].Value));

        var full = Path.GetFullPath(expanded);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0 || string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase))
            return full; // keep a bare root like "C:\" intact
        return trimmed;
    }

    public IReadOnlyDictionary<string, string> DescribeTokens() => Tokens;

    private string ResolveToken(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "steamid64":
                return RequireSteamUser(name).SteamId64;
            case "steamid3":
                return RequireSteamUser(name).SteamId3;
            case "userprofile":
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            case "documents":
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            case "savedgames":
                return GetSavedGamesFolder();
            case "locallow":
                return GetLocalLowFolder();
            default:
                throw new PathTokenException(
                    $"Unknown token {{{name}}} in save path. Known tokens: {string.Join(", ", Tokens.Keys)}.");
        }
    }

    private SteamUser RequireSteamUser(string token)
    {
        SteamUser? user;
        try
        {
            user = _steam.MostRecentUser();
        }
        catch (Exception ex) when (ex is not PathTokenException)
        {
            throw new PathTokenException($"Could not look up a Steam user for {{{token}}}: {ex.Message}");
        }

        return user ?? throw new PathTokenException(
            $"The save path uses {{{token}}} but no Steam user could be found. Install Steam and log in at least once, or replace the token with the ID folder name you see in the save location.");
    }

    internal static string GetLocalLowFolder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
            local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        return Path.GetFullPath(Path.Combine(local, "..", "LocalLow"));
    }

    internal static string GetSavedGamesFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            var ptr = IntPtr.Zero;
            try
            {
                var id = FolderIdSavedGames;
                var hr = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out ptr);
                if (hr == 0 && ptr != IntPtr.Zero)
                {
                    var path = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
            catch
            {
                // fall through to the default location
            }
            finally
            {
                if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
    private static partial Regex TokenRegex();
}
