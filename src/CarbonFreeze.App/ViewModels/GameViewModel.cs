using CommunityToolkit.Mvvm.ComponentModel;
using CarbonFreeze.Core.Models;

namespace CarbonFreeze.App.ViewModels;

/// <summary>Observable wrapper for a <see cref="GameEntry"/> row in the games list.</summary>
public sealed partial class GameViewModel : ObservableObject
{
    public GameViewModel(GameEntry entry)
    {
        Entry = entry;
    }

    public GameEntry Entry { get; }

    public string Name => Entry.Name;
    public string ActiveProfile => Entry.ActiveProfile;
    public string Subtitle => $"active: {Entry.ActiveProfile}";
    public bool HasSteamAppId => !string.IsNullOrWhiteSpace(Entry.SteamAppId);

    /// <summary>Call after the underlying entry has been mutated (switch, edit, rename).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ActiveProfile));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasSteamAppId));
    }
}
