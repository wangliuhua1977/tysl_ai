using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISnapshotStorage
{
    Task<SnapshotCaptureResult> CaptureAsync(
        SnapshotCaptureRequest request,
        CancellationToken cancellationToken = default);
}
