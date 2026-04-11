using System.Windows;
using Dnp3MasterTester.ViewModels;

namespace Dnp3MasterTester;

public partial class ConnectionSettingsWindow : Window
{
    public ConnectionSettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
