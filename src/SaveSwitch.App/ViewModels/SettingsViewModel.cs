using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveSwitch.App.Services;
using SaveSwitch.Core.Models;
using SaveSwitch.Core.Services;

namespace SaveSwitch.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public const string DefaultStoreRootHint = @"%LOCALAPPDATA%\SaveSwitch\Store";

    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(AppConfig config, IConfigStore configStore, IShellService shell, IDialogService dialogs)
    {
        _shell = shell;
        _dialogs = dialogs;
        _storeRoot = config.StoreRoot ?? "";
        _maxBackupsText = config.MaxBackupsPerGame.ToString();
        ConfigPath = configStore.ConfigPath;
    }

    public string ConfigPath { get; }

    [ObservableProperty]
    private string _storeRoot = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxBackupsError))]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private string _maxBackupsText = "";

    public string? MaxBackupsError =>
        int.TryParse(MaxBackupsText.Trim(), out int n) && n >= 0
            ? null
            : "Enter a whole number; 0 means unlimited.";

    public bool CanAccept => MaxBackupsError is null;

    public void ApplyTo(AppConfig config)
    {
        config.StoreRoot = string.IsNullOrWhiteSpace(StoreRoot) ? null : StoreRoot.Trim();
        if (int.TryParse(MaxBackupsText.Trim(), out int n) && n >= 0)
            config.MaxBackupsPerGame = n;
    }

    [RelayCommand]
    private void BrowseStoreRoot()
    {
        string? initial = Directory.Exists(StoreRoot) ? StoreRoot : null;
        string? picked = _dialogs.BrowseForFolder("Select the store folder", initial);
        if (picked is not null) StoreRoot = picked;
    }

    [RelayCommand]
    private void ClearStoreRoot() => StoreRoot = "";

    [RelayCommand]
    private void OpenConfigLocation()
    {
        if (_shell.RevealFile(ConfigPath)) return;

        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir is null || !_shell.OpenFolder(dir))
            _dialogs.ShowInfo("Config file", $"The config file has not been written yet.\n\n{ConfigPath}");
    }
}
