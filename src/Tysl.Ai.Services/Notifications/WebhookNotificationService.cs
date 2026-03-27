using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Notifications;

public sealed class WebhookNotificationService : IWebhookNotificationService
{
    private readonly ILocalDiagnosticService diagnosticService;
    private readonly INotificationTemplateRenderService renderService;
    private readonly INotificationTemplateStore templateStore;
    private readonly IWebhookEndpointStore webhookEndpointStore;
    private readonly IWebhookSender webhookSender;

    public WebhookNotificationService(
        IWebhookEndpointStore webhookEndpointStore,
        INotificationTemplateStore templateStore,
        INotificationTemplateRenderService renderService,
        IWebhookSender webhookSender,
        ILocalDiagnosticService diagnosticService)
    {
        this.webhookEndpointStore = webhookEndpointStore;
        this.templateStore = templateStore;
        this.renderService = renderService;
        this.webhookSender = webhookSender;
        this.diagnosticService = diagnosticService;
    }

    public async Task<WebhookNotificationDispatchPlan> BuildDispatchPlanAsync(
        NotificationTemplateKind templateKind,
        NotificationTemplateRenderContext context,
        NotificationDispatchTraceContext? traceContext = null,
        CancellationToken cancellationToken = default)
    {
        await WriteTraceAsync(
            traceContext,
            "template-render-start",
            $"deviceCode={context.DeviceCode ?? traceContext?.DeviceCode ?? "unknown"}, kind={templateKind}",
            cancellationToken);

        var template = await templateStore.GetAsync(templateKind, cancellationToken);
        var endpoints = await webhookEndpointStore.ListAsync(MapPool(templateKind), cancellationToken);

        try
        {
            var plan = new WebhookNotificationDispatchPlan
            {
                TemplateKind = templateKind,
                Pool = MapPool(templateKind),
                RenderedContent = renderService.Render(template.Content, context),
                Endpoints = endpoints
                    .Where(endpoint => endpoint.IsEnabled)
                    .OrderBy(endpoint => endpoint.SortOrder)
                    .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
            };

            await WriteTraceAsync(
                traceContext,
                "template-render-end",
                $"deviceCode={context.DeviceCode ?? traceContext?.DeviceCode ?? "unknown"}, kind={templateKind}, enabledEndpointCount={plan.Endpoints.Count}",
                cancellationToken);

            return plan;
        }
        catch (Exception ex)
        {
            await diagnosticService.WriteAsync(
                "notification-template-render-failed",
                $"kind={templateKind}, type={ex.GetType().FullName}, message={ex.Message}",
                cancellationToken);
            throw new InvalidOperationException("通知模板渲染失败。", ex);
        }
    }

    public async Task<IReadOnlyList<WebhookSendResult>> SendAsync(
        NotificationTemplateKind templateKind,
        NotificationTemplateRenderContext context,
        NotificationDispatchTraceContext? traceContext = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildDispatchPlanAsync(templateKind, context, traceContext, cancellationToken);

        await WriteTraceAsync(
            traceContext,
            "send-start",
            $"deviceCode={context.DeviceCode ?? traceContext?.DeviceCode ?? "unknown"}, endpointCount={plan.Endpoints.Count}",
            cancellationToken);

        if (plan.Endpoints.Count == 0)
        {
            await WriteTraceAsync(
                traceContext,
                "send-end",
                $"deviceCode={context.DeviceCode ?? traceContext?.DeviceCode ?? "unknown"}, endpointCount=0, successCount=0, failureCount=0",
                cancellationToken);
            return Array.Empty<WebhookSendResult>();
        }

        var results = new List<WebhookSendResult>(plan.Endpoints.Count);
        foreach (var endpoint in plan.Endpoints)
        {
            var result = await webhookSender.SendAsync(
                endpoint.WebhookUrl,
                new WebhookMessage
                {
                    Content = plan.RenderedContent
                },
                cancellationToken);
            results.Add(result);
        }

        var successCount = results.Count(result => result.IsSuccess);
        await WriteTraceAsync(
            traceContext,
            "send-end",
            $"deviceCode={context.DeviceCode ?? traceContext?.DeviceCode ?? "unknown"}, endpointCount={results.Count}, successCount={successCount}, failureCount={results.Count - successCount}",
            cancellationToken);

        return results;
    }

    private static WebhookEndpointPool MapPool(NotificationTemplateKind templateKind)
    {
        return templateKind == NotificationTemplateKind.Recovery
            ? WebhookEndpointPool.Recovery
            : WebhookEndpointPool.Dispatch;
    }

    private Task WriteTraceAsync(
        NotificationDispatchTraceContext? traceContext,
        string suffix,
        string message,
        CancellationToken cancellationToken)
    {
        return traceContext is null
            ? Task.CompletedTask
            : diagnosticService.WriteAsync($"{traceContext.EventPrefix}-{suffix}", message, cancellationToken);
    }
}
