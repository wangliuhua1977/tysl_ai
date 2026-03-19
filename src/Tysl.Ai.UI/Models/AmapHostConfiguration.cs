namespace Tysl.Ai.UI.Models;

public sealed record AmapHostConfiguration
{
    public required bool IsConfigured { get; init; }

    public string? Key { get; init; }

    public string? SecurityJsCode { get; init; }

    public string? MapStyle { get; init; }

    public required int Zoom { get; init; }

    public required double[] Center { get; init; }
}
