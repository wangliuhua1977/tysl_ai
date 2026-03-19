using System.Text;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Storage;

public sealed class SnapshotStorage : ISnapshotStorage
{
    private static readonly byte[] SuccessPlaceholder = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNg+A8AAucB9WHZQ3cAAAAASUVORK5CYII=");
    private static readonly byte[] WarningPlaceholder = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGP4zwAAAgEBB6FKT7YAAAAASUVORK5CYII=");
    private static readonly byte[] FaultPlaceholder = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=");

    private readonly string snapshotRootDirectory;

    public SnapshotStorage(string snapshotRootDirectory)
    {
        this.snapshotRootDirectory = snapshotRootDirectory;
        Directory.CreateDirectory(snapshotRootDirectory);
    }

    public async Task<SnapshotCaptureResult> CaptureAsync(
        SnapshotCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var capturedAt = request.CapturedAt;
            var dayFolder = Path.Combine(snapshotRootDirectory, capturedAt.ToLocalTime().ToString("yyyyMMdd"));
            Directory.CreateDirectory(dayFolder);

            var fileName =
                $"{SanitizeFileName(request.DeviceCode)}-{capturedAt.UtcDateTime:yyyyMMddHHmmss}-{ResolveSuffix(request)}.png";
            var path = Path.Combine(dayFolder, fileName);
            var bytes = ResolvePlaceholderBytes(request);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);

            var notePath = Path.ChangeExtension(path, ".txt");
            var note = BuildNote(request);
            await File.WriteAllTextAsync(notePath, note, new UTF8Encoding(false), cancellationToken);

            return new SnapshotCaptureResult
            {
                IsSuccess = true,
                SnapshotPath = path,
                CapturedAt = capturedAt,
                IsPlaceholder = true
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SnapshotCaptureResult
            {
                IsSuccess = false,
                IsPlaceholder = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string ResolveSuffix(SnapshotCaptureRequest request)
    {
        if (request.FaultCode == RuntimeFaultCode.None)
        {
            return request.PreviewResolveState == PreviewResolveState.Resolved ? "ok" : "skip";
        }

        return request.FaultCode.ToString().ToLowerInvariant();
    }

    private static byte[] ResolvePlaceholderBytes(SnapshotCaptureRequest request)
    {
        return request.FaultCode switch
        {
            RuntimeFaultCode.None => SuccessPlaceholder,
            RuntimeFaultCode.PreviewResolveFailed or RuntimeFaultCode.SnapshotFailed => WarningPlaceholder,
            _ => FaultPlaceholder
        };
    }

    private static string BuildNote(SnapshotCaptureRequest request)
    {
        var lines = new[]
        {
            $"deviceCode={request.DeviceCode}",
            $"displayName={request.DisplayName}",
            $"capturedAt={request.CapturedAt:O}",
            $"previewResolveState={request.PreviewResolveState}",
            $"faultCode={request.FaultCode}",
            $"summary={request.Summary ?? string.Empty}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
    }
}
