using System.Windows.Input;

namespace Tysl.Ai.UI.ViewModels;

public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> executeAsync;
    private readonly Func<T?, bool>? canExecute;
    private bool isExecuting;

    public AsyncRelayCommand(Func<T?, Task> executeAsync, Func<T?, bool>? canExecute = null)
    {
        this.executeAsync = executeAsync;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !isExecuting && (canExecute?.Invoke((T?)parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        isExecuting = true;
        NotifyCanExecuteChanged();

        try
        {
            await executeAsync((T?)parameter);
        }
        finally
        {
            isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
