namespace Tysl.Ai.UI.ViewModels;

public sealed class NotificationRequestedEventArgs : EventArgs
{
    public NotificationRequestedEventArgs(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }
}
