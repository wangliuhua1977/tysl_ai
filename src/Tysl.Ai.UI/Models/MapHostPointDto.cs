namespace Tysl.Ai.UI.Models;

public sealed record MapHostPointDto
{
    public required string DeviceCode { get; init; }

    public string? Alias { get; init; }

    public required string DisplayName { get; init; }

    public required string DeviceName { get; init; }

    public required string StatusText { get; init; }

    public required string VisualState { get; init; }

    public required string OnlineStateText { get; init; }

    public required string MonitoringText { get; init; }

    public required IReadOnlyList<string> StatusBadges { get; init; }

    public required string SummaryText { get; init; }

    public required string DispatchStateKey { get; init; }

    public required string DispatchStateText { get; init; }

    public double? PlatformRawLongitude { get; init; }

    public double? PlatformRawLatitude { get; init; }

    public required string RawCoordinateType { get; init; }

    public double? ManualLongitude { get; init; }

    public double? ManualLatitude { get; init; }
}
