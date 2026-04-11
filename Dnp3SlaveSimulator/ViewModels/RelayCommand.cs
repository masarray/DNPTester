using System.Windows.Input;

namespace Dnp3SlaveSimulator.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?>? _execute;
    private readonly Func<object?, Task>? _executeAsync;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (_executeAsync is not null)
        {
            await _executeAsync(parameter);
            return;
        }

        _execute?.Invoke(parameter);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
