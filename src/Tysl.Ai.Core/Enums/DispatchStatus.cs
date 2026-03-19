namespace Tysl.Ai.Core.Enums;

public enum DispatchStatus
{
    None = 0,
    PendingDispatch = 1,
    Dispatched = 2,
    SendFailed = 3,
    WebhookNotConfigured = 4
}
