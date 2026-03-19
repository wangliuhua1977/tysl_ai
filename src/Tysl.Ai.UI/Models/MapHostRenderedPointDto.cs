namespace Tysl.Ai.UI.Models;

public sealed record MapHostRenderedPointDto
{
    public required string DeviceCode { get; init; }

    public required double Longitude { get; init; }

    public required double Latitude { get; init; }
}
