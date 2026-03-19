using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record PlatformConnectionState
{
    public required PlatformConnectionStatus Status { get; init; }

    public required string SummaryText { get; init; }

    public string? DetailText { get; init; }

    public required bool IsConfigured { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsConnected => Status == PlatformConnectionStatus.Connected;
}
