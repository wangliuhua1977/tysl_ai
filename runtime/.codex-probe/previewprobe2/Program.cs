using TianyiVision.Acis.Reusable;

const string DeviceCode = "3TPBH7930658PHZ";

var options = AcisApiKernel.LoadOptions(@"C:\tysl_ai\configs\acis-kernel.json");
using var kernel = new AcisApiKernel(options);

async Task DumpAsync(string title, params string[] order)
{
    options.Preview.ClickProtocolOrder = order;

    try
    {
        var preview = await kernel.ResolvePreviewAsync(DeviceCode, AcisPreviewIntent.ClickPreview);
        Console.WriteLine(title);
        Console.WriteLine($"  success={preview.IsSuccess}");
        Console.WriteLine($"  selected={preview.SelectedProtocol}");
        Console.WriteLine($"  parsed={preview.ParsedProtocolType}");
        Console.WriteLine($"  attempted={string.Join('>', preview.AttemptedProtocols)}");
        Console.WriteLine($"  stream={preview.StreamAcquireResult}");
        Console.WriteLine($"  reason={preview.FailureReason}");
        Console.WriteLine($"  url={preview.PreviewUrl}");
    }
    catch (Exception ex)
    {
        Console.WriteLine(title);
        Console.WriteLine($"  exception={ex.GetType().Name}: {ex.Message}");
    }
}

await DumpAsync("webrtc only", "webrtc");
await DumpAsync("fallback chain", "webrtc", "flv", "hls");
