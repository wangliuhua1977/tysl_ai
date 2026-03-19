namespace Tysl.Ai.UI.Models;

public sealed record MapHostStateDto
{
    public required IReadOnlyList<MapHostPointDto> Points { get; init; }

    public string? SelectedDeviceCode { get; init; }

    public required bool CoordinatePickActive { get; init; }
}
