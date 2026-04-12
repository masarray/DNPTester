using System.Windows;
using Dnp3SlaveSimulator.ViewModels;

namespace Dnp3SlaveSimulator;

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

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.StopRuntimeCommand.Execute(null);
        base.OnClosed(e);
    }
}
