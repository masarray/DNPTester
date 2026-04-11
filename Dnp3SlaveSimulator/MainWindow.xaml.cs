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

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.StopRuntimeCommand.Execute(null);
        base.OnClosed(e);
    }
}
