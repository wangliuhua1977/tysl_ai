namespace Tysl.Ai.UI.Models;

public sealed record MapHostCandidateCoordinateDto
{
    public required double Longitude { get; init; }

    public required double Latitude { get; init; }
}
