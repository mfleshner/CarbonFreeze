using System.Windows;
using System.Windows.Input;
using CarbonFreeze.App.ViewModels;

namespace CarbonFreeze.App.Views;

public partial class SteamPickerWindow : Window
{
    public SteamPickerWindow()
    {
        InitializeComponent();
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void GamesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SteamPickerViewModel { HasSelection: true })
            DialogResult = true;
    }
}
