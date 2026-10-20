using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveSwitch.App.Services;
using SaveSwitch.Core.Models;
using SaveSwitch.Core.Services;

namespace SaveSwitch.App.ViewModels;

/// <summary>Backs the Add/Edit game dialog. Nothing is written to the GameEntry until <see cref="ApplyTo"/>.</summary>
public sealed partial class GameEditorViewModel : ObservableObject
{
    private readonly IPathExpander _expander;
    private readonly ISteamLocator _steam;
    private readonly IPresetCatalog _presets;
    private readonly IDialogService _dialogs;
    private readonly string _originalTemplate;

    public GameEditorViewModel(
        GameEntry? existing,
        IPathExpander expander,
        ISteamLocator steam,
        IPresetCatalog presets,
        IProfileService profiles,
        IDialogService dialogs)
    {
        _expander = expander;
        _steam = steam;
        _presets = presets;
        _dialogs = dialogs;

        IsEdit = existing is not null;
        Title = IsEdit ? "Edit game" : "Add game";

        try { Presets = new ObservableCollection<GamePreset>(_presets.All.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)); }
        catch { Presets = new ObservableCollection<GamePreset>(); }

        try
        {
            Tokens = _expander.DescribeTokens()
                .Select(kv => new TokenHelp(kv.Key, kv.Value))
                .ToList();
        }
        catch
        {
            Tokens = new List<TokenHelp>();
        }

        try { SteamAvailable = _steam.FindInstall() is not null; }
        catch { SteamAvailable = false; }

        if (existing is not null)
        {
            _name = existing.Name;
            _savePathTemplate = existing.SavePathTemplate;
            _steamAppId = existing.SteamAppId ?? "";
            _notes = existing.Notes ?? "";
            _storeRootOverride = existing.StoreRootOverride ?? "";

            try { HasParkedProfiles = profiles.ListProfiles(existing).Count > 1; }
            catch { HasParkedProfiles = false; }
        }

        _originalTemplate = _savePathTemplate;
        Reevaluate();
    }

    public string Title { get; }
    public bool IsEdit { get; }
    public bool HasParkedProfiles { get; }
    public bool SteamAvailable { get; }
    public string SteamPickerTooltip => SteamAvailable ? "Pick an installed Steam game" : "Steam not found";

    public ObservableCollection<GamePreset> Presets { get; }
    public IReadOnlyList<TokenHelp> Tokens { get; }

    // ---------------------------------------------------------------- fields

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    [NotifyPropertyChangedFor(nameof(ShowTemplateChangedWarning))]
    private string _savePathTemplate = "";

    partial void OnSavePathTemplateChanged(string value) => Reevaluate();

    [ObservableProperty]
    private string _steamAppId = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _storeRootOverride = "";

    [ObservableProperty]
    private GamePreset? _selectedPreset;

    partial void OnSelectedPresetChanged(GamePreset? value)
    {
        if (value is null) return;
        Name = value.Name;
        SavePathTemplate = value.SavePathTemplate;
        SteamAppId = value.SteamAppId ?? "";
        Notes = value.Notes ?? "";
    }

    // ---------------------------------------------------------------- live preview

    [ObservableProperty]
    private string? _resolvedPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private string? _resolveError;

    [ObservableProperty]
    private bool _resolvedExists;

    public string ResolveStatusText =>
        ResolveError is not null ? ResolveError
        : ResolvedPath is null ? ""
        : ResolvedExists ? "folder exists" : "folder not found (it will be created on first switch)";

    public bool CanAccept =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(SavePathTemplate) &&
        ResolveError is null;

    public bool ShowTemplateChangedWarning =>
        IsEdit && HasParkedProfiles &&
        !string.Equals(SavePathTemplate?.Trim(), _originalTemplate?.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Reevaluate()
    {
        string template = SavePathTemplate?.Trim() ?? "";
        if (template.Length == 0)
        {
            ResolvedPath = null;
            ResolveError = null;
            ResolvedExists = false;
        }
        else
        {
            try
            {
                ResolvedPath = _expander.Expand(template);
                ResolveError = null;
                ResolvedExists = Directory.Exists(ResolvedPath);
            }
            catch (PathTokenException ex)
            {
                ResolvedPath = null;
                ResolveError = ex.Message;
                ResolvedExists = false;
            }
            catch (Exception ex)
            {
                ResolvedPath = null;
                ResolveError = ex.Message;
                ResolvedExists = false;
            }
        }
        OnPropertyChanged(nameof(ResolveStatusText));
    }

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private void BrowseTemplate()
    {
        string? initial = ResolvedExists ? ResolvedPath : null;
        string? picked = _dialogs.BrowseForFolder("Select the save folder", initial);
        if (picked is not null)
            SavePathTemplate = picked;
    }

    [RelayCommand]
    private void BrowseStoreRoot()
    {
        string? initial = Directory.Exists(StoreRootOverride) ? StoreRootOverride : null;
        string? picked = _dialogs.BrowseForFolder("Select the store folder for this game", initial);
        if (picked is not null)
            StoreRootOverride = picked;
    }

    [RelayCommand(CanExecute = nameof(SteamAvailable))]
    private void PickFromSteam()
    {
        var picker = new SteamPickerViewModel(_steam);
        var game = _dialogs.ShowSteamPicker(picker);
        if (game is null) return;

        Name = game.Name;
        SteamAppId = game.AppId;

        var preset = FindPresetFor(game);
        if (preset is not null)
        {
            SavePathTemplate = preset.SavePathTemplate;
            if (string.IsNullOrWhiteSpace(Notes) && !string.IsNullOrWhiteSpace(preset.Notes))
                Notes = preset.Notes;
        }
    }

    private GamePreset? FindPresetFor(SteamGame game)
    {
        // Exact app id first, then name contains either way (case-insensitive).
        var byId = Presets.FirstOrDefault(p => p.SteamAppId is not null && p.SteamAppId == game.AppId);
        if (byId is not null) return byId;

        return Presets.FirstOrDefault(p =>
            p.Name.Contains(game.Name, StringComparison.OrdinalIgnoreCase) ||
            game.Name.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- result

    public void ApplyTo(GameEntry entry)
    {
        entry.Name = Name.Trim();
        entry.SavePathTemplate = SavePathTemplate.Trim();
        entry.SteamAppId = string.IsNullOrWhiteSpace(SteamAppId) ? null : SteamAppId.Trim();
        entry.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        entry.StoreRootOverride = string.IsNullOrWhiteSpace(StoreRootOverride) ? null : StoreRootOverride.Trim();
    }
}

public sealed record TokenHelp(string Token, string Description);
