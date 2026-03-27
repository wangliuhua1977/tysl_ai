using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class WebhookEndpointItemViewModel
{
    private WebhookEndpointItemViewModel(WebhookEndpoint endpoint)
    {
        Id = endpoint.Id;
        Pool = endpoint.Pool;
        Name = endpoint.Name;
        WebhookUrl = endpoint.WebhookUrl;
        UsageRemark = endpoint.UsageRemark;
        IsEnabled = endpoint.IsEnabled;
        SortOrder = endpoint.SortOrder;
        UpdatedAtText = endpoint.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public string Id { get; }

    public WebhookEndpointPool Pool { get; }

    public string Name { get; }

    public string WebhookUrl { get; }

    public string? UsageRemark { get; }

    public bool IsEnabled { get; }

    public int SortOrder { get; }

    public string UpdatedAtText { get; }

    public string StatusText => IsEnabled ? "已启用" : "已停用";

    public string ToggleText => IsEnabled ? "停用" : "启用";

    public static WebhookEndpointItemViewModel FromModel(WebhookEndpoint endpoint)
    {
        return new WebhookEndpointItemViewModel(endpoint);
    }
}
