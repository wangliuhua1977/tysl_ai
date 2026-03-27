namespace Tysl.Ai.UI.ViewModels;

public sealed class NotificationTemplateSettingsDialogRequestedEventArgs : EventArgs
{
    public NotificationTemplateSettingsDialogRequestedEventArgs(NotificationTemplateSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
    }

    public NotificationTemplateSettingsViewModel ViewModel { get; }
}
