using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SnapshotCaptureRequest
{
    public required string DeviceCode { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required PreviewResolveState PreviewResolveState { get; init; }

    public required RuntimeFaultCode FaultCode { get; init; }

    public string? Summary { get; init; }
}
