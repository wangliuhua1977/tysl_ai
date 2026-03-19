using System.Net.Http.Json;
using System.Text.Json;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Messaging;

public sealed class WeComWebhookSender : IWebhookSender, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    public WeComWebhookSender()
    {
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<WebhookSendResult> SendAsync(
        string webhookUrl,
        WebhookMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return new WebhookSendResult
            {
                IsSuccess = false,
                ErrorMessage = "Webhook URL is missing."
            };
        }

        try
        {
            var payload = new
            {
                msgtype = "text",
                text = new
                {
                    content = message.Content,
                    mentioned_mobile_list = BuildMentionList(message)
                }
            };

            using var response = await httpClient.PostAsJsonAsync(
                webhookUrl.Trim(),
                payload,
                JsonOptions,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new WebhookSendResult
                {
                    IsSuccess = false,
                    ErrorCode = (int)response.StatusCode,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}",
                    ResponseBody = responseBody
                };
            }

            var result = ParseResponse(responseBody);
            return result with { ResponseBody = responseBody };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WebhookSendResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private static IReadOnlyList<string> BuildMentionList(WebhookMessage message)
    {
        var mobiles = message.MentionMobiles
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (message.MentionAll && !mobiles.Contains("@all", StringComparer.OrdinalIgnoreCase))
        {
            mobiles.Add("@all");
        }

        return mobiles;
    }

    private static WebhookSendResult ParseResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new WebhookSendResult
            {
                IsSuccess = true
            };
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var errorCode = root.TryGetProperty("errcode", out var errCodeElement)
                && errCodeElement.TryGetInt32(out var errCode)
                ? errCode
                : 0;
            var errorMessage = root.TryGetProperty("errmsg", out var errMsgElement)
                ? errMsgElement.GetString()
                : null;

            return new WebhookSendResult
            {
                IsSuccess = errorCode == 0,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            };
        }
        catch
        {
            return new WebhookSendResult
            {
                IsSuccess = true
            };
        }
    }
}
