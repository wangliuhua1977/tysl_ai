namespace Tysl.Ai.Core.Models;

public sealed record SnapshotCaptureResult
{
    public required bool IsSuccess { get; init; }

    public string? SnapshotPath { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }

    public required bool IsPlaceholder { get; init; }

    public string? ErrorMessage { get; init; }
}
