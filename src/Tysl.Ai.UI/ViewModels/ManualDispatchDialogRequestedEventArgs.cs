namespace Tysl.Ai.UI.ViewModels;

public sealed class ManualDispatchDialogRequestedEventArgs : EventArgs
{
    public ManualDispatchDialogRequestedEventArgs(ManualDispatchDialogViewModel viewModel)
    {
        ViewModel = viewModel;
    }

    public ManualDispatchDialogViewModel ViewModel { get; }
}
