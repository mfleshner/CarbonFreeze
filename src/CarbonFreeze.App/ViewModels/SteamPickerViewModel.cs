using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CarbonFreeze.Core.Models;
using CarbonFreeze.Core.Services;

namespace CarbonFreeze.App.ViewModels;

public sealed partial class SteamPickerViewModel : ObservableObject
{
    private readonly List<SteamGame> _all = new();

    public SteamPickerViewModel(ISteamLocator steam)
    {
        try
        {
            _all.AddRange(steam.ListInstalledGames().OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        }
        catch (Exception ex)
        {
            LoadError = $"Could not read the Steam library: {ex.Message}";
        }
        ApplyFilter();
    }

    public ObservableCollection<SteamGame> FilteredGames { get; } = new();

    public string? LoadError { get; }

    public string CountText => LoadError is not null
        ? ""
        : $"{FilteredGames.Count} of {_all.Count} installed games";

    [ObservableProperty]
    private string _filter = "";

    partial void OnFilterChanged(string value) => ApplyFilter();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SteamGame? _selectedGame;

    public bool HasSelection => SelectedGame is not null;

    private void ApplyFilter()
    {
        string f = Filter.Trim();
        FilteredGames.Clear();
        foreach (var g in _all)
        {
            if (f.Length == 0 ||
                g.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                g.AppId.Contains(f, StringComparison.OrdinalIgnoreCase))
            {
                FilteredGames.Add(g);
            }
        }
        if (SelectedGame is not null && !FilteredGames.Contains(SelectedGame))
            SelectedGame = null;
        OnPropertyChanged(nameof(CountText));
    }
}
