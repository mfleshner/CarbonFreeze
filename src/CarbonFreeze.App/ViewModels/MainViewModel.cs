using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CarbonFreeze.App.Services;
using CarbonFreeze.Core.Models;
using CarbonFreeze.Core.Services;

namespace CarbonFreeze.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly IConfigStore _configStore;
    private readonly IProfileService _profiles;
    private readonly IPathExpander _expander;
    private readonly ISteamLocator _steam;
    private readonly IPresetCatalog _presets;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;

    public MainViewModel(
        AppConfig config,
        IConfigStore configStore,
        IProfileService profiles,
        IPathExpander expander,
        ISteamLocator steam,
        IPresetCatalog presets,
        IDialogService dialogs,
        IShellService shell)
    {
        _config = config;
        _configStore = configStore;
        _profiles = profiles;
        _expander = expander;
        _steam = steam;
        _presets = presets;
        _dialogs = dialogs;
        _shell = shell;

        foreach (var game in _config.Games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            Games.Add(new GameViewModel(game));

        SelectedGame = Games.FirstOrDefault();
    }

    public const string AboutLine = "CarbonFreeze – swaps save folders by renaming them; nothing is deleted without a zip backup.";

    public const string SteamCloudWarning =
        "Steam Cloud appears to be on for this game. Disable it in Steam > Properties > General before switching, or Steam may overwrite a profile.";

    // ---------------------------------------------------------------- games list

    public ObservableCollection<GameViewModel> Games { get; } = new();

    public bool HasGames => Games.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    [NotifyPropertyChangedFor(nameof(SelectedGameName))]
    [NotifyCanExecuteChangedFor(nameof(EditGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackupNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenStoreFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupsFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSavePathCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwitchToSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private GameViewModel? _selectedGame;

    public bool HasSelectedGame => SelectedGame is not null;
    public string SelectedGameName => SelectedGame?.Name ?? "";

    partial void OnSelectedGameChanged(GameViewModel? value) => RefreshGameDetails();

    // ---------------------------------------------------------------- header details

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSavePathCommand))]
    private string? _resolvedSavePath;

    /// <summary>Message from PathTokenException (or any other expansion failure); null when resolved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavePathHasProblem))]
    private string? _savePathError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavePathHasProblem))]
    private bool _savePathExists;

    public bool SavePathHasProblem => SavePathError is not null || !SavePathExists;

    [ObservableProperty]
    private bool _showCloudWarning;

    [ObservableProperty]
    private string? _storePath;

    [ObservableProperty]
    private string? _backupPath;

    // ---------------------------------------------------------------- profiles / backups

    public ObservableCollection<ProfileInfo> Profiles { get; } = new();
    public ObservableCollection<BackupInfo> Backups { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchToSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private ProfileInfo? _selectedProfile;

    public bool HasBackups => Backups.Count > 0;

    // ---------------------------------------------------------------- status bar

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private bool _statusIsError;

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    // ================================================================ game commands

    [RelayCommand]
    private void AddGame()
    {
        var editor = new GameEditorViewModel(null, _expander, _steam, _presets, _profiles, _dialogs);
        if (!_dialogs.ShowGameEditor(editor))
            return;

        var entry = new GameEntry();
        editor.ApplyTo(entry);
        _config.Games.Add(entry);
        SaveConfig();

        var vm = new GameViewModel(entry);
        Games.Add(vm);
        OnPropertyChanged(nameof(HasGames));
        SelectedGame = vm;
        SetStatus($"Added {entry.Name}.");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGame))]
    private void EditGame()
    {
        if (SelectedGame is null) return;

        var editor = new GameEditorViewModel(SelectedGame.Entry, _expander, _steam, _presets, _profiles, _dialogs);
        if (!_dialogs.ShowGameEditor(editor))
            return;

        editor.ApplyTo(SelectedGame.Entry);
        SaveConfig();
        SelectedGame.Refresh();
        OnPropertyChanged(nameof(SelectedGameName));
        RefreshGameDetails();
        SetStatus($"Updated {SelectedGame.Entry.Name}.");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGame))]
    private void RemoveGame()
    {
        if (SelectedGame is null) return;
        var game = SelectedGame;

        bool ok = _dialogs.Confirm(
            "Remove game",
            $"Remove \"{game.Name}\" from CarbonFreeze?\n\n" +
            "This only forgets the game. Nothing is deleted: the save folder, the parked profiles " +
            "in the store and all backup zips stay exactly where they are.",
            destructive: false);
        if (!ok) return;

        _config.Games.Remove(game.Entry);
        SaveConfig();

        int index = Games.IndexOf(game);
        Games.Remove(game);
        OnPropertyChanged(nameof(HasGames));
        SelectedGame = Games.Count == 0 ? null : Games[Math.Min(index, Games.Count - 1)];
        SetStatus($"Removed {game.Name} from the list. Files were left untouched.");
    }

    private bool CanLaunch() => !string.IsNullOrWhiteSpace(SelectedGame?.Entry.SteamAppId);

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch()
    {
        if (SelectedGame?.Entry.SteamAppId is not { } appId) return;
        Run($"Launching {SelectedGame.Name} via Steam.", () =>
        {
            _steam.Launch(appId);
            return null;
        });
    }

    private bool CanOpenSavePath() => !string.IsNullOrWhiteSpace(ResolvedSavePath);

    [RelayCommand(CanExecute = nameof(CanOpenSavePath))]
    private void OpenSavePath()
    {
        if (ResolvedSavePath is null) return;
        if (!_shell.OpenFolder(ResolvedSavePath))
            SetStatus($"Folder not found: {ResolvedSavePath}", isError: true);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_config, _configStore, _shell, _dialogs);
        if (!_dialogs.ShowSettings(vm))
            return;

        vm.ApplyTo(_config);
        SaveConfig();
        ThemeManager.Apply(_config.Theme);
        RefreshGameDetails();
        SetStatus("Settings saved.");
    }

    [RelayCommand]
    private void Refresh()
    {
        SelectedGame?.Refresh();
        RefreshGameDetails();
        SetStatus("Refreshed.");
    }

    // ================================================================ profile commands

    private bool CanSwitchToSelected() =>
        SelectedGame is not null && SelectedProfile is { IsActive: false };

    [RelayCommand(CanExecute = nameof(CanSwitchToSelected))]
    private void SwitchToSelected()
    {
        if (SelectedProfile is null) return;
        SwitchTo(SelectedProfile.Name);
    }

    /// <summary>Shared by "Switch to selected", Enter on a row, double-click and "New profile".</summary>
    public void SwitchTo(string targetProfile)
    {
        if (SelectedGame is null) return;
        var game = SelectedGame;

        try
        {
            var result = _profiles.Switch(game.Entry, targetProfile);
            string verb = result.NewProfileWasCreated ? "Created and switched" : "Switched";
            string backup = result.BackupZipPath is null ? "no backup (save folder was empty)" : $"Backup: {result.BackupZipPath}";
            SetStatus($"{verb} {result.PreviousProfile} -> {result.NewProfile}. {backup}");
        }
        catch (ProfileException ex)
        {
            SetStatus(ex.Message, isError: true);
            _dialogs.ShowError("Switch failed", ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"Switch failed: {ex.Message}", isError: true);
            _dialogs.ShowError("Switch failed", ex.Message);
        }
        finally
        {
            // Switch mutates game.ActiveProfile before returning; persist whatever state we are in.
            SaveConfig();
            game.Refresh();
            RefreshGameDetails();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGame))]
    private void NewProfile()
    {
        if (SelectedGame is null) return;
        string active = SelectedGame.Entry.ActiveProfile;

        string? name = _dialogs.PromptProfileName(
            "New profile",
            $"Name for the new profile. The current save will be parked as \"{active}\" and an empty save folder becomes the new profile.",
            null,
            n => ValidateNewName(n, allowExisting: null));
        if (string.IsNullOrWhiteSpace(name)) return;

        SwitchTo(name.Trim());
    }

    private bool CanRenameProfile() => SelectedGame is not null && SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanRenameProfile))]
    private void RenameProfile()
    {
        if (SelectedGame is null || SelectedProfile is null) return;
        string oldName = SelectedProfile.Name;

        string? newName = _dialogs.PromptProfileName(
            "Rename profile",
            $"New name for \"{oldName}\":",
            oldName,
            n => ValidateNewName(n, allowExisting: oldName));
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == oldName) return;

        Run($"Renamed {oldName} -> {newName.Trim()}.", () =>
        {
            _profiles.Rename(SelectedGame.Entry, oldName, newName.Trim());
            return null;
        }, selectProfile: newName.Trim());
    }

    private bool CanDeleteProfile() =>
        SelectedGame is not null && SelectedProfile is { IsActive: false };

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        if (SelectedGame is null || SelectedProfile is null) return;
        string name = SelectedProfile.Name;

        bool ok = _dialogs.Confirm(
            "Delete profile",
            $"Delete the parked profile \"{name}\"?\n\n" +
            "It is zipped into the backups folder first and that zip is kept, so the saves can be recovered.",
            destructive: true);
        if (!ok) return;

        Run(null, () =>
        {
            string zip = _profiles.DeleteProfile(SelectedGame.Entry, name);
            return $"Deleted {name}. Backup: {zip}";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGame))]
    private void BackupNow()
    {
        if (SelectedGame is null) return;
        Run(null, () =>
        {
            string? zip = _profiles.BackupNow(SelectedGame.Entry);
            return zip is null
                ? "Nothing to back up: the save folder is empty."
                : $"Backup written: {zip}";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGame))]
    private void OpenStoreFolder() => OpenOrReport(StorePath, "Store folder");

    [RelayCommand(CanExecute = nameof(HasSelectedGame))]
    private void OpenBackupsFolder() => OpenOrReport(BackupPath, "Backups folder");

    [RelayCommand]
    private void OpenBackupLocation(BackupInfo? backup)
    {
        if (backup is null) return;
        if (!_shell.RevealFile(backup.ZipPath))
            SetStatus($"Backup not found: {backup.ZipPath}", isError: true);
    }

    // ================================================================ helpers

    private string? ValidateNewName(string name, string? allowExisting)
    {
        string trimmed = name.Trim();
        string? reason;
        try
        {
            reason = _profiles.ValidateProfileName(trimmed);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        if (reason is not null) return reason;

        bool clash = Profiles.Any(p =>
            p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase) &&
            !(allowExisting is not null && p.Name.Equals(allowExisting, StringComparison.OrdinalIgnoreCase)));
        return clash ? $"A profile named \"{trimmed}\" already exists." : null;
    }

    private void OpenOrReport(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus($"{what} is not available for this game.", isError: true);
            return;
        }
        if (!_shell.OpenFolder(path))
            SetStatus($"{what} does not exist yet: {path}", isError: true);
    }

    /// <summary>
    /// Runs a core operation with uniform error handling, then saves config and refreshes.
    /// <paramref name="operation"/> may return a status message that overrides <paramref name="successMessage"/>.
    /// </summary>
    private void Run(string? successMessage, Func<string?> operation, string? selectProfile = null)
    {
        try
        {
            string? msg = operation();
            SetStatus(msg ?? successMessage ?? "Done.");
        }
        catch (ProfileException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
            _dialogs.ShowError("Operation failed", ex.Message);
        }
        finally
        {
            SaveConfig();
            SelectedGame?.Refresh();
            RefreshGameDetails(selectProfile);
        }
    }

    private void SaveConfig()
    {
        try
        {
            _configStore.Save(_config);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save config: {ex.Message}", isError: true);
            _dialogs.ShowError("Could not save configuration", $"{ex.Message}\n\n{_configStore.ConfigPath}");
        }
    }

    private void RefreshGameDetails(string? selectProfile = null)
    {
        string? keepSelection = selectProfile ?? SelectedProfile?.Name;

        Profiles.Clear();
        Backups.Clear();
        SelectedProfile = null;
        ResolvedSavePath = null;
        SavePathError = null;
        SavePathExists = false;
        ShowCloudWarning = false;
        StorePath = null;
        BackupPath = null;
        OnPropertyChanged(nameof(HasBackups));

        if (SelectedGame is null)
            return;

        var game = SelectedGame.Entry;

        // Save path
        try
        {
            ResolvedSavePath = _profiles.GetSavePath(game);
            SavePathExists = Directory.Exists(ResolvedSavePath);
        }
        catch (PathTokenException ex)
        {
            SavePathError = ex.Message;
        }
        catch (Exception ex)
        {
            SavePathError = ex.Message;
        }

        // Store / backup folders
        try { StorePath = _profiles.GetStorePath(game); } catch { StorePath = null; }
        try { BackupPath = _profiles.GetBackupPath(game); } catch { BackupPath = null; }

        // Steam Cloud heuristic
        if (!string.IsNullOrWhiteSpace(game.SteamAppId))
        {
            try { ShowCloudWarning = _steam.AppUsesSteamCloud(game.SteamAppId); }
            catch { ShowCloudWarning = false; }
        }

        // Profiles
        try
        {
            foreach (var p in _profiles.ListProfiles(game))
                Profiles.Add(p);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not list profiles: {ex.Message}", isError: true);
        }

        // Backups
        try
        {
            foreach (var b in _profiles.ListBackups(game).OrderByDescending(b => b.CreatedUtc))
                Backups.Add(b);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not list backups: {ex.Message}", isError: true);
        }
        OnPropertyChanged(nameof(HasBackups));

        SelectedProfile = keepSelection is null
            ? Profiles.FirstOrDefault()
            : Profiles.FirstOrDefault(p => p.Name.Equals(keepSelection, StringComparison.OrdinalIgnoreCase))
              ?? Profiles.FirstOrDefault();

        LaunchCommand.NotifyCanExecuteChanged();
    }
}
