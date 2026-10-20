using CarbonFreeze.App.ViewModels;
using CarbonFreeze.Core.Models;

namespace CarbonFreeze.App.Services;

/// <summary>
/// Every UI interaction a view model needs that would otherwise require a MessageBox or a Window.
/// View models depend on this interface only, so they can be exercised without a WPF dispatcher.
/// </summary>
public interface IDialogService
{
    void ShowInfo(string title, string message);
    void ShowError(string title, string message);

    /// <summary>Yes/No style confirmation. Returns true when the user accepts.</summary>
    bool Confirm(string title, string message, bool destructive = false);

    /// <summary>
    /// Ask for a profile name. <paramref name="validate"/> is called live on every keystroke and
    /// returns null when the name is acceptable, else a reason. Returns null when cancelled.
    /// </summary>
    string? PromptProfileName(string title, string prompt, string? initialValue, Func<string, string?> validate);

    /// <summary>Modal add/edit game dialog. Returns true when OK was pressed.</summary>
    bool ShowGameEditor(GameEditorViewModel viewModel);

    /// <summary>Modal Steam game picker. Returns the chosen game, or null when cancelled.</summary>
    SteamGame? ShowSteamPicker(SteamPickerViewModel viewModel);

    /// <summary>Modal settings dialog. Returns true when OK was pressed.</summary>
    bool ShowSettings(SettingsViewModel viewModel);

    /// <summary>Folder picker. Returns null when cancelled.</summary>
    string? BrowseForFolder(string title, string? initialDirectory);
}
