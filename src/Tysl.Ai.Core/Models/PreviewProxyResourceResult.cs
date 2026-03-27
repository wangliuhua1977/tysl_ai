namespace Tysl.Ai.Core.Models;

public sealed class PreviewProxyResourceResult
{
    public bool IsSuccess { get; set; }

    public string RequestUrl { get; set; } = string.Empty;

    public string ResponseUrl { get; set; } = string.Empty;

    public int StatusCode { get; set; } = 502;

    public string ReasonPhrase { get; set; } = "Bad Gateway";

    public string? ContentType { get; set; }

    public long? ContentLength { get; set; }

    public byte[] Content { get; set; } = [];

    public IReadOnlyDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool Redirected { get; set; }

    public int RedirectCount { get; set; }

    public string? FailureReason { get; set; }
}
