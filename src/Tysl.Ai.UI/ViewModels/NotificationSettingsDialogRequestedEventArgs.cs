namespace Tysl.Ai.UI.ViewModels;

public sealed class NotificationSettingsDialogRequestedEventArgs : EventArgs
{
    public NotificationSettingsDialogRequestedEventArgs(NotificationSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
    }

    public NotificationSettingsViewModel ViewModel { get; }
}
