using System.IO;
using System.Windows;
using Microsoft.Win32;
using SaveSwitch.App.ViewModels;
using SaveSwitch.App.Views;
using SaveSwitch.Core.Models;

namespace SaveSwitch.App.Services;

/// <summary>WPF implementation of <see cref="IDialogService"/>: message boxes and modal windows.</summary>
public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public void ShowInfo(string title, string message) =>
        Show(title, message, MessageBoxImage.Information);

    public void ShowError(string title, string message) =>
        Show(title, message, MessageBoxImage.Error);

    private static void Show(string title, string message, MessageBoxImage image)
    {
        var owner = Owner;
        if (owner is not null)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }

    public bool Confirm(string title, string message, bool destructive = false)
    {
        var image = destructive ? MessageBoxImage.Warning : MessageBoxImage.Question;
        var owner = Owner;
        var result = owner is not null
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, image, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, image, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    public string? PromptProfileName(string title, string prompt, string? initialValue, Func<string, string?> validate)
    {
        var vm = new ProfileNameViewModel(title, prompt, initialValue, validate);
        var window = new ProfileNameWindow { DataContext = vm, Owner = Owner };
        return window.ShowDialog() == true ? vm.Name.Trim() : null;
    }

    public bool ShowGameEditor(GameEditorViewModel viewModel)
    {
        var window = new GameEditorWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true;
    }

    public SteamGame? ShowSteamPicker(SteamPickerViewModel viewModel)
    {
        var window = new SteamPickerWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true ? viewModel.SelectedGame : null;
    }

    public bool ShowSettings(SettingsViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel, Owner = Owner };
        return window.ShowDialog() == true;
    }

    public string? BrowseForFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        var owner = Owner;
        bool? ok = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FolderName : null;
    }
}
