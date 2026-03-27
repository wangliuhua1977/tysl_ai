namespace Tysl.Ai.UI.ViewModels;

public sealed class CloseWorkOrderDialogRequestedEventArgs : EventArgs
{
    public CloseWorkOrderDialogRequestedEventArgs(CloseWorkOrderDialogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public CloseWorkOrderDialogViewModel ViewModel { get; }
}
