using System.Windows;

namespace SaveSwitch.App.Views;

public partial class GameEditorWindow : Window
{
    public GameEditorWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void TokensHelp_Click(object sender, RoutedEventArgs e)
    {
        TokensPopup.IsOpen = !TokensPopup.IsOpen;
    }
}
