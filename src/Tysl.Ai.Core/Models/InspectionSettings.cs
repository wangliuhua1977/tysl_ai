namespace Tysl.Ai.Core.Models;

public sealed record InspectionSettings
{
    public required bool Enabled { get; init; }

    public required TimeOnly StartTime { get; init; }

    public required TimeOnly EndTime { get; init; }

    public required int IntervalMinutes { get; init; }

    public required int SnapshotRetentionCount { get; init; }

    public required bool PreviewResolveEnabled { get; init; }

    public required bool SnapshotEnabled { get; init; }

    public required int MaxPointsPerCycle { get; init; }

    public required int DetailBatchSize { get; init; }

    public static InspectionSettings Default { get; } = new()
    {
        Enabled = true,
        StartTime = new TimeOnly(7, 0),
        EndTime = new TimeOnly(22, 0),
        IntervalMinutes = 5,
        SnapshotRetentionCount = 20,
        PreviewResolveEnabled = true,
        SnapshotEnabled = true,
        MaxPointsPerCycle = 20,
        DetailBatchSize = 4
    };

    public bool IsWithinWindow(DateTimeOffset timestamp)
    {
        if (!Enabled)
        {
            return false;
        }

        var current = TimeOnly.FromDateTime(timestamp.LocalDateTime);
        if (StartTime <= EndTime)
        {
            return current >= StartTime && current <= EndTime;
        }

        return current >= StartTime || current <= EndTime;
    }
}
