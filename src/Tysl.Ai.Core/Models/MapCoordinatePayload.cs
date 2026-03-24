using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record MapCoordinatePayload
{
    public double? PlatformRawLongitude { get; init; }

    public double? PlatformRawLatitude { get; init; }

    public required string RawCoordinateType { get; init; }

    public double? ManualLongitude { get; init; }

    public double? ManualLatitude { get; init; }

    public required CoordinateDisplayStatus CoordinateDisplayStatus { get; init; }

    public required UnmappedReason UnmappedReason { get; init; }

    public required CoordinateSource CoordinateSource { get; init; }

    public required string CoordinateSourceText { get; init; }
}
