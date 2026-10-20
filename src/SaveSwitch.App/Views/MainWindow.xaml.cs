using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaveSwitch.App.ViewModels;
using SaveSwitch.Core.Models;

namespace SaveSwitch.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// Enter = switch, Delete = delete. Handled in PreviewKeyDown because the DataGrid consumes
    /// both keys itself (row navigation / row deletion) before InputBindings get a look.
    /// </summary>
    private void ProfilesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        switch (e.Key)
        {
            case Key.Enter when ViewModel.SwitchToSelectedCommand.CanExecute(null):
                ViewModel.SwitchToSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true; // stop the grid from just moving to the next row
                break;
            case Key.Delete when ViewModel.DeleteProfileCommand.CanExecute(null):
                ViewModel.DeleteProfileCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                e.Handled = true;
                break;
        }
    }

    private void ProfilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.OriginalSource is DependencyObject source && FindRow(source) is { DataContext: ProfileInfo })
        {
            if (ViewModel.SwitchToSelectedCommand.CanExecute(null))
                ViewModel.SwitchToSelectedCommand.Execute(null);
        }
    }

    private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<ListBoxItem>(source) is not null)
        {
            if (ViewModel.EditGameCommand.CanExecute(null))
                ViewModel.EditGameCommand.Execute(null);
        }
    }

    private static DataGridRow? FindRow(DependencyObject source) => FindAncestor<DataGridRow>(source);

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
