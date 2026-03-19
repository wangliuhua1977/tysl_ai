namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteEditorDialogRequestedEventArgs : EventArgs
{
    public SiteEditorDialogRequestedEventArgs(SiteEditorViewModel viewModel)
    {
        ViewModel = viewModel;
    }

    public SiteEditorViewModel ViewModel { get; }
}
