using System.Windows;
using Dnp3SlaveSimulator.ViewModels;

namespace Dnp3SlaveSimulator;

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
