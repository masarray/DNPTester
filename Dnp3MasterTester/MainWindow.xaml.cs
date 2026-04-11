using System.Windows;
using Dnp3MasterTester.ViewModels;

namespace Dnp3MasterTester;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void OpenConnectionSetup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionSettingsWindow(_viewModel)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }
}
