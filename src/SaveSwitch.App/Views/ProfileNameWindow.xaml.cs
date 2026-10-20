using System.Windows;
using System.Windows.Controls;

namespace SaveSwitch.App.Views;

public partial class ProfileNameWindow : Window
{
    public ProfileNameWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void NameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.SelectAll();
            box.Focus();
        }
    }
}
