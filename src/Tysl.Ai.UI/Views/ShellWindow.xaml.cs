using System.ComponentModel;
using System.Windows;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.UI.Models;
using Tysl.Ai.UI.ViewModels;
using Tysl.Ai.UI.Views.Controls;

namespace Tysl.Ai.UI.Views;

public partial class ShellWindow : Window
{
    private bool allowCloseAfterPreviewFlush;
    private readonly ILocalDiagnosticService diagnosticService;
    private TaskCompletionSource<bool>? closePreparationCompletionSource;
    private bool isClosingAsync;
    private CloseWorkOrderDialog? closeWorkOrderDialog;
    private ManualDispatchDialog? manualDispatchDialog;
    private NotificationSettingsDialog? notificationSettingsDialog;
    private NotificationTemplateSettingsDialog? notificationTemplateSettingsDialog;
    private readonly ISitePreviewService sitePreviewService;
    private SiteEditorDialog? editorDialog;
    private ShellViewModel? shellViewModel;

    public ShellWindow(
        AmapHostConfiguration mapHostConfiguration,
        ISitePreviewService sitePreviewService,
        ILocalDiagnosticService diagnosticService)
    {
        this.sitePreviewService = sitePreviewService ?? throw new ArgumentNullException(nameof(sitePreviewService));
        this.diagnosticService = diagnosticService ?? throw new ArgumentNullException(nameof(diagnosticService));
        InitializeComponent();

        AmapHost.Configuration = mapHostConfiguration;
        AmapHost.PointSelected += HandleMapPointSelected;
        AmapHost.MapClicked += HandleMapClicked;
        AmapHost.RenderedPointsUpdated += HandleRenderedPointsUpdated;
        PreviewHost.DiagnosticService = this.diagnosticService;
        PreviewHost.PreviewService = this.sitePreviewService;
        PreviewHost.HostInitialized += HandlePreviewHostInitialized;
        PreviewHost.PlaybackReady += HandlePreviewPlaybackReady;
        PreviewHost.PlaybackFailed += HandlePreviewPlaybackFailed;

        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel oldViewModel)
        {
            oldViewModel.EditorDialogRequested -= HandleEditorDialogRequested;
            oldViewModel.ManualDispatchDialogRequested -= HandleManualDispatchDialogRequested;
            oldViewModel.CloseWorkOrderDialogRequested -= HandleCloseWorkOrderDialogRequested;
            oldViewModel.NotificationSettingsDialogRequested -= HandleNotificationSettingsDialogRequested;
            oldViewModel.NotificationTemplateSettingsDialogRequested -= HandleNotificationTemplateSettingsDialogRequested;
            oldViewModel.NotificationRequested -= HandleNotificationRequested;
        }

        shellViewModel = e.NewValue as ShellViewModel;
        if (shellViewModel is null)
        {
            return;
        }

        shellViewModel.EditorDialogRequested += HandleEditorDialogRequested;
        shellViewModel.ManualDispatchDialogRequested += HandleManualDispatchDialogRequested;
        shellViewModel.CloseWorkOrderDialogRequested += HandleCloseWorkOrderDialogRequested;
        shellViewModel.NotificationSettingsDialogRequested += HandleNotificationSettingsDialogRequested;
        shellViewModel.NotificationTemplateSettingsDialogRequested += HandleNotificationTemplateSettingsDialogRequested;
        shellViewModel.NotificationRequested += HandleNotificationRequested;
    }

    private void HandleMapPointSelected(object? sender, MapPointSelectedEventArgs e)
    {
        shellViewModel?.HandleMapPointSelected(e.DeviceCode);
    }

    private void HandleMapClicked(object? sender, MapClickedEventArgs e)
    {
        shellViewModel?.HandleMapClicked(e.Longitude, e.Latitude);
    }

    private void HandleRenderedPointsUpdated(object? sender, MapRenderedPointsEventArgs e)
    {
        shellViewModel?.HandleMapPointsRendered(e.Points);
    }

    private void HandlePreviewPlaybackReady(object? sender, PreviewPlaybackReadyEventArgs e)
    {
        shellViewModel?.HandlePreviewPlaybackReady(e.DeviceCode, e.PlaybackSessionId, e.Protocol);
    }

    private void HandlePreviewHostInitialized(object? sender, PreviewHostInitializedEventArgs e)
    {
        shellViewModel?.HandlePreviewHostInitialized(e.DeviceCode, e.PlaybackSessionId, e.Protocol);
    }

    private void HandlePreviewPlaybackFailed(object? sender, PreviewPlaybackFailedEventArgs e)
    {
        shellViewModel?.HandlePreviewPlaybackFailed(
            e.DeviceCode,
            e.PlaybackSessionId,
            e.Protocol,
            e.Category,
            e.Reason);
    }

    private void HandleEditorDialogRequested(object? sender, SiteEditorDialogRequestedEventArgs e)
    {
        try
        {
            _ = diagnosticService.WriteAsync(
                "dialog-created",
                $"deviceCode={e.ViewModel.DeviceCode}, hasExistingDialog={editorDialog is not null}");

            if (editorDialog is not null)
            {
                editorDialog.Close();
            }

            editorDialog = new SiteEditorDialog
            {
                Owner = this,
                DataContext = e.ViewModel
            };

            _ = diagnosticService.WriteAsync(
                "dialog-datacontext-bound",
                $"deviceCode={e.ViewModel.DeviceCode}, viewModel={e.ViewModel.GetType().Name}");

            editorDialog.Closed += HandleEditorDialogClosed;
            _ = diagnosticService.WriteAsync(
                "showdialog-enter",
                $"deviceCode={e.ViewModel.DeviceCode}, mode=show");
            editorDialog.Show();
            editorDialog.Activate();
        }
        catch (Exception ex)
        {
            _ = diagnosticService.WriteAsync(
                "exception-caught",
                $"source=shell-window-open-dialog, type={ex.GetType().FullName}, message={ex.Message}");
            MessageBox.Show(this, "编辑补充信息窗口打开失败，请稍后重试。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleEditorDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not SiteEditorDialog dialog)
        {
            return;
        }

        var deviceCode = "unknown";

        dialog.Closed -= HandleEditorDialogClosed;

        if (dialog.DataContext is SiteEditorViewModel viewModel)
        {
            deviceCode = viewModel.DeviceCode;
            shellViewModel?.HandleEditorClosed(viewModel);
        }

        if (ReferenceEquals(editorDialog, dialog))
        {
            editorDialog = null;
        }

        _ = diagnosticService.WriteAsync(
            "showdialog-exit",
            $"deviceCode={deviceCode}");
    }

    private void HandleManualDispatchDialogRequested(object? sender, ManualDispatchDialogRequestedEventArgs e)
    {
        var stage = "new-dialog";
        var deviceCode = e.ViewModel.DeviceCode;

        try
        {
            _ = WriteManualDispatchDialogDiagnosticAsync(
                "manual-dispatch-open-stage",
                deviceCode,
                stage);

            if (manualDispatchDialog is not null)
            {
                manualDispatchDialog.Close();
            }

            var dialog = new ManualDispatchDialog(deferInitialize: true);

            stage = "initialize";
            _ = WriteManualDispatchDialogDiagnosticAsync(
                "manual-dispatch-open-stage",
                deviceCode,
                stage);
            dialog.InitializeDialogComponents();

            stage = "datacontext";
            _ = WriteManualDispatchDialogDiagnosticAsync(
                "manual-dispatch-open-stage",
                deviceCode,
                stage);
            dialog.Owner = this;
            dialog.DiagnosticService = diagnosticService;
            dialog.DataContext = e.ViewModel;

            manualDispatchDialog = dialog;
            manualDispatchDialog.Closed += HandleManualDispatchDialogClosed;

            stage = "show";
            _ = WriteManualDispatchDialogDiagnosticAsync(
                "manual-dispatch-open-stage",
                deviceCode,
                stage);
            manualDispatchDialog.Show();

            stage = "activate";
            _ = WriteManualDispatchDialogDiagnosticAsync(
                "manual-dispatch-open-stage",
                deviceCode,
                stage);
            manualDispatchDialog.Activate();
            _ = diagnosticService.WriteAsync(
                "manual-dispatch-open-end",
                $"deviceCode={deviceCode}");
        }
        catch (Exception ex)
        {
            if (manualDispatchDialog is not null && ReferenceEquals(manualDispatchDialog.DataContext, e.ViewModel))
            {
                manualDispatchDialog.Closed -= HandleManualDispatchDialogClosed;
                manualDispatchDialog = null;
            }

            _ = WriteManualDispatchDialogFailureAsync(
                "manual-dispatch-exception-caught",
                deviceCode,
                stage,
                ex);
            _ = WriteManualDispatchDialogFailureAsync(
                "manual-dispatch-failed",
                deviceCode,
                stage,
                ex);
            MessageBox.Show(this, "手工派单确认窗口打开失败，请稍后重试。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleCloseWorkOrderDialogRequested(object? sender, CloseWorkOrderDialogRequestedEventArgs e)
    {
        try
        {
            if (closeWorkOrderDialog is not null)
            {
                closeWorkOrderDialog.Close();
            }

            closeWorkOrderDialog = new CloseWorkOrderDialog
            {
                Owner = this,
                DataContext = e.ViewModel
            };
            closeWorkOrderDialog.Closed += HandleCloseWorkOrderDialogClosed;
            closeWorkOrderDialog.Show();
            closeWorkOrderDialog.Activate();
        }
        catch (Exception ex)
        {
            _ = diagnosticService.WriteAsync(
                "exception-caught",
                $"source=shell-window-open-close-work-order, type={ex.GetType().FullName}, message={ex.Message}");
            MessageBox.Show(this, "恢复归档确认窗口打开失败，请稍后重试。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleNotificationSettingsDialogRequested(object? sender, NotificationSettingsDialogRequestedEventArgs e)
    {
        try
        {
            if (notificationSettingsDialog is not null)
            {
                notificationSettingsDialog.Close();
            }

            notificationSettingsDialog = new NotificationSettingsDialog
            {
                Owner = this,
                DataContext = e.ViewModel
            };
            notificationSettingsDialog.Closed += HandleNotificationSettingsDialogClosed;
            notificationSettingsDialog.Show();
            notificationSettingsDialog.Activate();
        }
        catch (Exception ex)
        {
            _ = diagnosticService.WriteAsync(
                "exception-caught",
                $"source=shell-window-open-notification-settings, type={ex.GetType().FullName}, message={ex.Message}");
            MessageBox.Show(this, "通知设置窗口打开失败，请稍后重试。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleNotificationTemplateSettingsDialogRequested(object? sender, NotificationTemplateSettingsDialogRequestedEventArgs e)
    {
        try
        {
            if (notificationTemplateSettingsDialog is not null)
            {
                notificationTemplateSettingsDialog.Close();
            }

            notificationTemplateSettingsDialog = new NotificationTemplateSettingsDialog
            {
                Owner = this,
                DataContext = e.ViewModel
            };
            notificationTemplateSettingsDialog.Closed += HandleNotificationTemplateSettingsDialogClosed;
            notificationTemplateSettingsDialog.Show();
            notificationTemplateSettingsDialog.Activate();
        }
        catch (Exception ex)
        {
            _ = diagnosticService.WriteAsync(
                "exception-caught",
                $"source=shell-window-open-template-settings, type={ex.GetType().FullName}, message={ex.Message}");
            MessageBox.Show(this, "模板设置窗口打开失败，请稍后重试。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleNotificationSettingsDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not NotificationSettingsDialog dialog)
        {
            return;
        }

        dialog.Closed -= HandleNotificationSettingsDialogClosed;
        if (ReferenceEquals(notificationSettingsDialog, dialog))
        {
            notificationSettingsDialog = null;
        }
    }

    private void HandleNotificationTemplateSettingsDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not NotificationTemplateSettingsDialog dialog)
        {
            return;
        }

        dialog.Closed -= HandleNotificationTemplateSettingsDialogClosed;
        if (ReferenceEquals(notificationTemplateSettingsDialog, dialog))
        {
            notificationTemplateSettingsDialog = null;
        }
    }

    private void HandleManualDispatchDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not ManualDispatchDialog dialog)
        {
            return;
        }

        dialog.Closed -= HandleManualDispatchDialogClosed;
        if (dialog.DataContext is ManualDispatchDialogViewModel viewModel)
        {
            shellViewModel?.HandleManualDispatchDialogClosed(viewModel);
        }

        if (ReferenceEquals(manualDispatchDialog, dialog))
        {
            manualDispatchDialog = null;
        }
    }

    private void HandleCloseWorkOrderDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not CloseWorkOrderDialog dialog)
        {
            return;
        }

        dialog.Closed -= HandleCloseWorkOrderDialogClosed;
        if (dialog.DataContext is CloseWorkOrderDialogViewModel viewModel)
        {
            shellViewModel?.HandleCloseWorkOrderDialogClosed(viewModel);
        }

        if (ReferenceEquals(closeWorkOrderDialog, dialog))
        {
            closeWorkOrderDialog = null;
        }
    }

    private void HandleNotificationRequested(object? sender, NotificationRequestedEventArgs e)
    {
        MessageBox.Show(this, e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Task WriteManualDispatchDialogDiagnosticAsync(
        string eventName,
        string deviceCode,
        string stage)
    {
        return diagnosticService.WriteAsync(
            eventName,
            $"deviceCode={deviceCode}, stage={stage}, hasEditorDialog={editorDialog is not null}, editorDialogVisible={editorDialog?.IsVisible == true}, hasManualDispatchDialog={manualDispatchDialog is not null}, ownerWindowState={WindowState}");
    }

    private Task WriteManualDispatchDialogFailureAsync(
        string eventName,
        string deviceCode,
        string stage,
        Exception exception)
    {
        return diagnosticService.WriteAsync(
            eventName,
            $"deviceCode={deviceCode}, stage={stage}, exceptionType={exception.GetType().FullName}, message={exception.Message}, stackTrace={exception.StackTrace}, hasEditorDialog={editorDialog is not null}, editorDialogVisible={editorDialog?.IsVisible == true}, hasManualDispatchDialog={manualDispatchDialog is not null}, ownerWindowState={WindowState}");
    }

    private Task WritePreviewWindowLifecycleAsync(
        string eventName,
        string caller,
        string reason,
        Exception? exception = null)
    {
        var previewState = shellViewModel?.BuildPreviewDiagnosticState(caller, reason)
                           ?? $"caller={caller}, reason={reason}, hasShellViewModel=false";
        var exceptionSegment = exception is null
            ? string.Empty
            : $", exceptionType={exception.GetType().FullName}, message={exception.Message}, stackTrace={exception.StackTrace}";
        return diagnosticService.WriteAsync(
            eventName,
            $"{previewState}, allowCloseAfterPreviewFlush={allowCloseAfterPreviewFlush}, isClosingAsync={isClosingAsync}, hasClosePreparation={closePreparationCompletionSource is not null}, windowState={WindowState}, stackTrace={Environment.StackTrace.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}{exceptionSegment}");
    }

    public Task RequestCloseForAcceptanceAsync()
    {
        return Dispatcher.CheckAccess()
            ? RequestCloseForAcceptanceCoreAsync()
            : Dispatcher.InvokeAsync(RequestCloseForAcceptanceCoreAsync).Task.Unwrap();
    }

    private async Task RequestCloseForAcceptanceCoreAsync()
    {
        await WritePreviewWindowLifecycleAsync(
            "preview-window-close-requested",
            "RequestCloseForAcceptanceAsync",
            "acceptance_request");

        if (allowCloseAfterPreviewFlush)
        {
            await WritePreviewWindowLifecycleAsync(
                "preview-window-close-bypassed",
                "RequestCloseForAcceptanceAsync",
                "allow_close_after_flush");
            Close();
            return;
        }

        if (isClosingAsync && closePreparationCompletionSource is not null)
        {
            await closePreparationCompletionSource.Task;
            return;
        }

        closePreparationCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Close();
        await closePreparationCompletionSource.Task;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _ = WritePreviewWindowLifecycleAsync(
            "preview-window-closing",
            "ShellWindow.OnClosing",
            allowCloseAfterPreviewFlush ? "allow_close_after_flush" : "closing_event");

        if (allowCloseAfterPreviewFlush)
        {
            return;
        }

        e.Cancel = true;
        if (isClosingAsync)
        {
            return;
        }

        isClosingAsync = true;
        closePreparationCompletionSource ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            shellViewModel?.BeginShutdownPreviewRelease("ShellWindow.OnClosing");
            await WritePreviewWindowLifecycleAsync(
                "preview-window-flush-start",
                "ShellWindow.OnClosing",
                "before_flush_active_session");
            await PreviewHost.FlushActiveSessionAsync("window_closing");
            await WritePreviewWindowLifecycleAsync(
                "preview-window-flush-end",
                "ShellWindow.OnClosing",
                "after_flush_active_session");
            shellViewModel?.CompleteShutdownAfterPreviewRelease("ShellWindow.OnClosing");

            if (editorDialog is not null)
            {
                editorDialog.Closed -= HandleEditorDialogClosed;
                editorDialog.Close();
                editorDialog = null;
            }

            if (notificationSettingsDialog is not null)
            {
                notificationSettingsDialog.Closed -= HandleNotificationSettingsDialogClosed;
                notificationSettingsDialog.Close();
                notificationSettingsDialog = null;
            }

            if (notificationTemplateSettingsDialog is not null)
            {
                notificationTemplateSettingsDialog.Closed -= HandleNotificationTemplateSettingsDialogClosed;
                notificationTemplateSettingsDialog.Close();
                notificationTemplateSettingsDialog = null;
            }

            if (manualDispatchDialog is not null)
            {
                manualDispatchDialog.Closed -= HandleManualDispatchDialogClosed;
                manualDispatchDialog.Close();
                manualDispatchDialog = null;
            }

            if (closeWorkOrderDialog is not null)
            {
                closeWorkOrderDialog.Closed -= HandleCloseWorkOrderDialogClosed;
                closeWorkOrderDialog.Close();
                closeWorkOrderDialog = null;
            }
        }
        catch (Exception ex)
        {
            _ = diagnosticService.WriteAsync(
                "exception-caught",
                $"source=shell-window-close, type={ex.GetType().FullName}, message={ex.Message}");
            _ = WritePreviewWindowLifecycleAsync(
                "preview-window-close-failed",
                "ShellWindow.OnClosing",
                "exception",
                ex);
        }
        finally
        {
            isClosingAsync = false;
            allowCloseAfterPreviewFlush = true;
            closePreparationCompletionSource?.TrySetResult(true);
            closePreparationCompletionSource = null;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _ = WritePreviewWindowLifecycleAsync(
            "preview-window-closed",
            "ShellWindow.OnClosed",
            "window_closed");
        Closing -= OnClosing;
        Closed -= OnClosed;
        DataContextChanged -= OnDataContextChanged;
        AmapHost.PointSelected -= HandleMapPointSelected;
        AmapHost.MapClicked -= HandleMapClicked;
        AmapHost.RenderedPointsUpdated -= HandleRenderedPointsUpdated;
        PreviewHost.HostInitialized -= HandlePreviewHostInitialized;
        PreviewHost.PlaybackReady -= HandlePreviewPlaybackReady;
        PreviewHost.PlaybackFailed -= HandlePreviewPlaybackFailed;
        AmapHost.Dispose();
        PreviewHost.Dispose();

        if (editorDialog is not null)
        {
            editorDialog.Closed -= HandleEditorDialogClosed;
            editorDialog = null;
        }

        if (notificationSettingsDialog is not null)
        {
            notificationSettingsDialog.Closed -= HandleNotificationSettingsDialogClosed;
            notificationSettingsDialog = null;
        }

        if (notificationTemplateSettingsDialog is not null)
        {
            notificationTemplateSettingsDialog.Closed -= HandleNotificationTemplateSettingsDialogClosed;
            notificationTemplateSettingsDialog = null;
        }

        if (manualDispatchDialog is not null)
        {
            manualDispatchDialog.Closed -= HandleManualDispatchDialogClosed;
            manualDispatchDialog = null;
        }

        if (closeWorkOrderDialog is not null)
        {
            closeWorkOrderDialog.Closed -= HandleCloseWorkOrderDialogClosed;
            closeWorkOrderDialog = null;
        }

        if (shellViewModel is not null)
        {
            shellViewModel.EditorDialogRequested -= HandleEditorDialogRequested;
            shellViewModel.ManualDispatchDialogRequested -= HandleManualDispatchDialogRequested;
            shellViewModel.CloseWorkOrderDialogRequested -= HandleCloseWorkOrderDialogRequested;
            shellViewModel.NotificationSettingsDialogRequested -= HandleNotificationSettingsDialogRequested;
            shellViewModel.NotificationTemplateSettingsDialogRequested -= HandleNotificationTemplateSettingsDialogRequested;
            shellViewModel.NotificationRequested -= HandleNotificationRequested;
            shellViewModel.Dispose();
            shellViewModel = null;
        }

        if (Application.Current is { } app && ReferenceEquals(app.MainWindow, this))
        {
            app.Shutdown();
        }
    }
}
