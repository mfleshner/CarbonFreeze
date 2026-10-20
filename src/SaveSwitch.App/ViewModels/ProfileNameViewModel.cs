using CommunityToolkit.Mvvm.ComponentModel;

namespace SaveSwitch.App.ViewModels;

/// <summary>Backs the small "enter a profile name" prompt with live validation.</summary>
public sealed partial class ProfileNameViewModel : ObservableObject
{
    private readonly Func<string, string?> _validate;

    public ProfileNameViewModel(string title, string prompt, string? initialValue, Func<string, string?> validate)
    {
        Title = title;
        Prompt = prompt;
        _validate = validate;
        _name = initialValue ?? "";
        Revalidate();
    }

    public string Title { get; }
    public string Prompt { get; }

    [ObservableProperty]
    private string _name = "";

    partial void OnNameChanged(string value) => Revalidate();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _validationMessage;

    public bool IsValid => ValidationMessage is null && !string.IsNullOrWhiteSpace(Name);

    private void Revalidate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = null; // empty is simply not acceptable yet; no need to shout
            OnPropertyChanged(nameof(IsValid));
            return;
        }
        try
        {
            ValidationMessage = _validate(Name);
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
        }
    }
}
