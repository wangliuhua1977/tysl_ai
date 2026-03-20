using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record DispatchPolicy
{
    public static DispatchPolicy Default { get; } = new()
    {
        Enabled = true,
        Mode = DispatchMode.Automatic,
        CoolingMinutes = 30,
        RecoveryMode = RecoveryMode.Manual,
        RepeatAfterRecovery = true,
        NotifyOnRecovery = true,
        WebhookUrl = null,
        MentionMobiles = Array.Empty<string>(),
        MentionAll = false,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public bool Enabled { get; init; }

    public DispatchMode Mode { get; init; }

    public int CoolingMinutes { get; init; }

    public RecoveryMode RecoveryMode { get; init; }

    public bool RepeatAfterRecovery { get; init; }

    public bool NotifyOnRecovery { get; init; }

    public string? WebhookUrl { get; init; }

    public IReadOnlyList<string> MentionMobiles { get; init; } = Array.Empty<string>();

    public bool MentionAll { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
