using System.Windows;
using Microsoft.Win32;

namespace CarbonFreeze.App.Services;

/// <summary>
/// Applies the colour theme. Two things change together: WPF's Fluent theme mode (control chrome,
/// title bars) through <see cref="Application.ThemeMode"/>, and the app's own Brush.* palette, which
/// is one of Resources\Themes\Light.xaml or Dark.xaml merged into the application resources.
/// </summary>
public static class ThemeManager
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static IReadOnlyList<string> Options { get; } = new[] { System, Light, Dark };

    private static ResourceDictionary? _palette;
    private static string _current = System;
    private static bool _hooked;

    /// <summary>Maps any stored value to one of <see cref="Options"/>; unknown or empty means System.</summary>
    public static string Normalise(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "light" => Light,
        "dark" => Dark,
        _ => System,
    };

    public static void Apply(string? theme)
    {
        var app = Application.Current;
        if (app is null) return;

        _current = Normalise(theme);

        app.ThemeMode = _current switch
        {
            Light => ThemeMode.Light,
            Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

        bool dark = _current == Dark || (_current == System && SystemPrefersDark());
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/CarbonFreeze;component/Resources/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };

        var merged = app.Resources.MergedDictionaries;
        if (_palette is not null) merged.Remove(_palette);
        merged.Insert(0, palette);
        _palette = palette;

        HookSystemChanges(app);
    }

    /// <summary>True when Windows "Choose your default app mode" is Dark.</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Re-applies the palette when the Windows app mode changes while the setting is System.</summary>
    private static void HookSystemChanges(Application app)
    {
        if (_hooked) return;
        try
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General || _current != System) return;
                app.Dispatcher.BeginInvoke(() => Apply(System));
            };
            _hooked = true;
        }
        catch
        {
            // Not fatal: the theme is still applied, it just will not follow live OS changes.
        }
    }
}
