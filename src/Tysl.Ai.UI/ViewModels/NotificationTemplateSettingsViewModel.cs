using System.Collections.ObjectModel;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class NotificationTemplateSettingsViewModel : ObservableObject
{
    private readonly ILocalDiagnosticService diagnosticService;
    private readonly INotificationTemplateRenderService renderService;
    private readonly INotificationTemplateStore templateStore;
    private string previewContent = string.Empty;
    private string statusText = string.Empty;
    private string templateContent = string.Empty;
    private NotificationTemplateKind selectedKind = NotificationTemplateKind.Dispatch;
    private string updatedAtText = "--";

    public NotificationTemplateSettingsViewModel(
        INotificationTemplateStore templateStore,
        INotificationTemplateRenderService renderService,
        ILocalDiagnosticService diagnosticService)
    {
        this.templateStore = templateStore;
        this.renderService = renderService;
        this.diagnosticService = diagnosticService;

        TemplateOptions =
        [
            new NotificationTemplateOption(NotificationTemplateKind.Dispatch, "派单通知模板"),
            new NotificationTemplateOption(NotificationTemplateKind.Recovery, "恢复通知模板")
        ];
        SupportedVariables = new ObservableCollection<TemplateVariableItemViewModel>(
            renderService.GetSupportedVariables()
                .Select(pair => new TemplateVariableItemViewModel(pair.Key, pair.Value)));

        SaveTemplateCommand = new AsyncRelayCommand(SaveAsync);
        ResetTemplateCommand = new AsyncRelayCommand(ResetAsync);
        RefreshPreviewCommand = new RelayCommand(RefreshPreview);
    }

    public IReadOnlyList<NotificationTemplateOption> TemplateOptions { get; }

    public ObservableCollection<TemplateVariableItemViewModel> SupportedVariables { get; }

    public AsyncRelayCommand SaveTemplateCommand { get; }

    public AsyncRelayCommand ResetTemplateCommand { get; }

    public RelayCommand RefreshPreviewCommand { get; }

    public string Title => "模板设置";

    public NotificationTemplateKind SelectedKind
    {
        get => selectedKind;
        set
        {
            if (SetProperty(ref selectedKind, value))
            {
                _ = LoadSelectedTemplateAsync();
            }
        }
    }

    public string TemplateContent
    {
        get => templateContent;
        set => SetProperty(ref templateContent, value);
    }

    public string PreviewContent
    {
        get => previewContent;
        private set => SetProperty(ref previewContent, value);
    }

    public string UpdatedAtText
    {
        get => updatedAtText;
        private set => SetProperty(ref updatedAtText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string PreviewHintText => "预览使用示例点位数据，不会触发真实派单。";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadSelectedTemplateAsync(cancellationToken);
    }

    private async Task LoadSelectedTemplateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await templateStore.GetAsync(SelectedKind, cancellationToken);
            TemplateContent = template.Content;
            UpdatedAtText = template.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            RefreshPreview();
            StatusText = "模板已加载。";
        }
        catch (Exception ex)
        {
            await WriteExceptionAsync("load-notification-template", ex, cancellationToken);
            StatusText = "模板加载失败，请稍后重试。";
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TemplateContent))
            {
                throw new InvalidOperationException("模板内容不能为空。");
            }

            var template = new NotificationTemplate
            {
                Kind = SelectedKind,
                Content = TemplateContent.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await templateStore.UpsertAsync(template);
            UpdatedAtText = template.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            RefreshPreview();
            StatusText = "模板已保存。";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            await WriteExceptionAsync("save-notification-template", ex);
            StatusText = "模板保存失败，请稍后重试。";
        }
    }

    private async Task ResetAsync()
    {
        try
        {
            var template = NotificationTemplate.CreateDefault(SelectedKind);
            await templateStore.UpsertAsync(template);
            TemplateContent = template.Content;
            UpdatedAtText = template.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            RefreshPreview();
            StatusText = "已恢复默认模板。";
        }
        catch (Exception ex)
        {
            await WriteExceptionAsync("reset-notification-template", ex);
            StatusText = "默认模板恢复失败，请稍后重试。";
        }
    }

    private void RefreshPreview()
    {
        try
        {
            PreviewContent = renderService.Render(TemplateContent, NotificationTemplateRenderContext.CreateSample());
        }
        catch (Exception ex)
        {
            _ = WriteExceptionAsync("preview-notification-template", ex);
            PreviewContent = "预览生成失败，请检查模板变量格式。";
        }
    }

    private Task WriteExceptionAsync(string source, Exception ex, CancellationToken cancellationToken = default)
    {
        return diagnosticService.WriteAsync(
            "exception-caught",
            $"source={source}, kind={SelectedKind}, type={ex.GetType().FullName}, message={ex.Message}",
            cancellationToken);
    }
}
