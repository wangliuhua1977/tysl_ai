namespace Tysl.Ai.Core.Models;

public sealed record MapCoverageSummary
{
    public required int TotalPointCount { get; init; }

    public required int MappedPointCount { get; init; }

    public required int UnmappedPointCount { get; init; }

    public required int FilteredPointCount { get; init; }

    public required int CurrentVisiblePointCount { get; init; }

    public required int FilteredUnmappedPointCount { get; init; }
}
