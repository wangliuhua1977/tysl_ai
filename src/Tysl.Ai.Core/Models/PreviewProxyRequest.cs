namespace Tysl.Ai.Core.Models;

public sealed class PreviewProxyRequest
{
    public string RequestUrl { get; set; } = string.Empty;

    public string Method { get; set; } = "GET";

    public IReadOnlyDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
