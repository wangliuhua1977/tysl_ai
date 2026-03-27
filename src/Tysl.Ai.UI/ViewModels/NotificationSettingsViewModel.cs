using System.Collections.ObjectModel;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class NotificationSettingsViewModel : ObservableObject
{
    private readonly ILocalDiagnosticService diagnosticService;
    private readonly IWebhookEndpointStore webhookEndpointStore;
    private string editingName = string.Empty;
    private string editingUsageRemark = string.Empty;
    private string editingWebhookUrl = string.Empty;
    private string statusText = string.Empty;
    private int editingSortOrder;
    private string? editingEndpointId;
    private WebhookEndpointPool editingPool = WebhookEndpointPool.Dispatch;
    private bool editingIsEnabled = true;

    public NotificationSettingsViewModel(
        IWebhookEndpointStore webhookEndpointStore,
        ILocalDiagnosticService diagnosticService)
    {
        this.webhookEndpointStore = webhookEndpointStore;
        this.diagnosticService = diagnosticService;

        DispatchEndpoints = [];
        RecoveryEndpoints = [];
        PoolOptions =
        [
            new WebhookEndpointPoolOption(WebhookEndpointPool.Dispatch, "派单通知池"),
            new WebhookEndpointPoolOption(WebhookEndpointPool.Recovery, "恢复通知池")
        ];

        AddDispatchEndpointCommand = new RelayCommand(() => BeginAdd(WebhookEndpointPool.Dispatch));
        AddRecoveryEndpointCommand = new RelayCommand(() => BeginAdd(WebhookEndpointPool.Recovery));
        EditEndpointCommand = new RelayCommand<WebhookEndpointItemViewModel>(BeginEdit);
        ToggleEndpointCommand = new AsyncRelayCommand<WebhookEndpointItemViewModel>(ToggleAsync);
        DeleteEndpointCommand = new AsyncRelayCommand<WebhookEndpointItemViewModel>(DeleteAsync);
        SaveEndpointCommand = new AsyncRelayCommand(SaveAsync);
        CancelEditCommand = new RelayCommand(ResetEditor);
    }

    public ObservableCollection<WebhookEndpointItemViewModel> DispatchEndpoints { get; }

    public ObservableCollection<WebhookEndpointItemViewModel> RecoveryEndpoints { get; }

    public IReadOnlyList<WebhookEndpointPoolOption> PoolOptions { get; }

    public RelayCommand AddDispatchEndpointCommand { get; }

    public RelayCommand AddRecoveryEndpointCommand { get; }

    public RelayCommand<WebhookEndpointItemViewModel> EditEndpointCommand { get; }

    public AsyncRelayCommand<WebhookEndpointItemViewModel> ToggleEndpointCommand { get; }

    public AsyncRelayCommand<WebhookEndpointItemViewModel> DeleteEndpointCommand { get; }

    public AsyncRelayCommand SaveEndpointCommand { get; }

    public RelayCommand CancelEditCommand { get; }

    public string Title => "通知设置";

    public string EditingName
    {
        get => editingName;
        set => SetProperty(ref editingName, value);
    }

    public string EditingWebhookUrl
    {
        get => editingWebhookUrl;
        set => SetProperty(ref editingWebhookUrl, value);
    }

    public string EditingUsageRemark
    {
        get => editingUsageRemark;
        set => SetProperty(ref editingUsageRemark, value);
    }

    public int EditingSortOrder
    {
        get => editingSortOrder;
        set => SetProperty(ref editingSortOrder, value);
    }

    public bool EditingIsEnabled
    {
        get => editingIsEnabled;
        set => SetProperty(ref editingIsEnabled, value);
    }

    public WebhookEndpointPool EditingPool
    {
        get => editingPool;
        set
        {
            if (SetProperty(ref editingPool, value))
            {
                OnPropertyChanged(nameof(EditSectionTitle));
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string EditSectionTitle => editingEndpointId is null
        ? $"新增{GetPoolLabel(EditingPool)}地址"
        : $"编辑{GetPoolLabel(EditingPool)}地址";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = await webhookEndpointStore.ListAsync(null, cancellationToken);
        ReplaceItems(DispatchEndpoints, endpoints.Where(item => item.Pool == WebhookEndpointPool.Dispatch));
        ReplaceItems(RecoveryEndpoints, endpoints.Where(item => item.Pool == WebhookEndpointPool.Recovery));
        ResetEditor();
        StatusText = "本地通知地址已加载。";
    }

    private void BeginAdd(WebhookEndpointPool pool)
    {
        editingEndpointId = null;
        EditingPool = pool;
        EditingName = string.Empty;
        EditingWebhookUrl = string.Empty;
        EditingUsageRemark = string.Empty;
        EditingSortOrder = ResolveDefaultSortOrder(pool);
        EditingIsEnabled = true;
        StatusText = $"正在新增{GetPoolLabel(pool)}地址。";
    }

    private void BeginEdit(WebhookEndpointItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        editingEndpointId = item.Id;
        EditingPool = item.Pool;
        EditingName = item.Name;
        EditingWebhookUrl = item.WebhookUrl;
        EditingUsageRemark = item.UsageRemark ?? string.Empty;
        EditingSortOrder = item.SortOrder;
        EditingIsEnabled = item.IsEnabled;
        StatusText = $"正在编辑“{item.Name}”。";
    }

    private async Task ToggleAsync(WebhookEndpointItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var existing = await webhookEndpointStore.GetByIdAsync(item.Id);
            if (existing is null)
            {
                return;
            }

            await webhookEndpointStore.UpsertAsync(existing with
            {
                IsEnabled = !existing.IsEnabled,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            await LoadAsync();
            StatusText = existing.IsEnabled ? "地址已停用。" : "地址已启用。";
        }
        catch (Exception ex)
        {
            await WriteExceptionAsync("toggle-webhook-endpoint", ex, item.Id);
            StatusText = "地址状态更新失败，请稍后重试。";
        }
    }

    private async Task DeleteAsync(WebhookEndpointItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await webhookEndpointStore.DeleteAsync(item.Id);
            await LoadAsync();
            StatusText = $"已删除“{item.Name}”。";
        }
        catch (Exception ex)
        {
            await WriteExceptionAsync("delete-webhook-endpoint", ex, item.Id);
            StatusText = "地址删除失败，请稍后重试。";
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var normalizedUrl = ValidateAndNormalizeUrl(EditingWebhookUrl);
            var now = DateTimeOffset.UtcNow;
            var id = editingEndpointId ?? Guid.NewGuid().ToString("N");
            var existing = editingEndpointId is null
                ? null
                : await webhookEndpointStore.GetByIdAsync(editingEndpointId);

            await webhookEndpointStore.UpsertAsync(new WebhookEndpoint
            {
                Id = id,
                Pool = EditingPool,
                Name = NormalizeRequired(EditingName, "名称不能为空。"),
                WebhookUrl = normalizedUrl,
                UsageRemark = NormalizeOptional(EditingUsageRemark),
                IsEnabled = EditingIsEnabled,
                SortOrder = Math.Max(0, EditingSortOrder),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            });

            await LoadAsync();
            StatusText = "通知地址已保存。";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            await WriteExceptionAsync("save-webhook-endpoint", ex, editingEndpointId);
            StatusText = "通知地址保存失败，请稍后重试。";
        }
    }

    private void ResetEditor()
    {
        editingEndpointId = null;
        EditingPool = WebhookEndpointPool.Dispatch;
        EditingName = string.Empty;
        EditingWebhookUrl = string.Empty;
        EditingUsageRemark = string.Empty;
        EditingSortOrder = ResolveDefaultSortOrder(WebhookEndpointPool.Dispatch);
        EditingIsEnabled = true;
    }

    private static void ReplaceItems(
        ObservableCollection<WebhookEndpointItemViewModel> target,
        IEnumerable<WebhookEndpoint> source)
    {
        target.Clear();
        foreach (var item in source
                     .OrderBy(endpoint => endpoint.SortOrder)
                     .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            target.Add(WebhookEndpointItemViewModel.FromModel(item));
        }
    }

    private int ResolveDefaultSortOrder(WebhookEndpointPool pool)
    {
        var currentItems = pool == WebhookEndpointPool.Dispatch ? DispatchEndpoints : RecoveryEndpoints;
        return currentItems.Count == 0 ? 10 : currentItems.Max(item => item.SortOrder) + 10;
    }

    private async Task WriteExceptionAsync(string source, Exception ex, string? id)
    {
        await diagnosticService.WriteAsync(
            "exception-caught",
            $"source={source}, id={id ?? "new"}, type={ex.GetType().FullName}, message={ex.Message}");
    }

    private static string NormalizeRequired(string value, string errorMessage)
    {
        var normalized = NormalizeOptional(value);
        return normalized ?? throw new InvalidOperationException(errorMessage);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ValidateAndNormalizeUrl(string value)
    {
        var normalized = NormalizeRequired(value, "Webhook 地址不能为空。");
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Webhook 地址必须为 http 或 https。");
        }

        return uri.ToString();
    }

    private static string GetPoolLabel(WebhookEndpointPool pool)
    {
        return pool == WebhookEndpointPool.Recovery ? "恢复通知池" : "派单通知池";
    }
}
