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
            oldViewModel.NotificationRequested -= HandleNotificationRequested;
        }

        shellViewModel = e.NewValue as ShellViewModel;
        if (shellViewModel is null)
        {
            return;
        }

        shellViewModel.EditorDialogRequested += HandleEditorDialogRequested;
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

    private void HandleNotificationRequested(object? sender, NotificationRequestedEventArgs e)
    {
        MessageBox.Show(this, e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public Task RequestCloseForAcceptanceAsync()
    {
        return Dispatcher.CheckAccess()
            ? RequestCloseForAcceptanceCoreAsync()
            : Dispatcher.InvokeAsync(RequestCloseForAcceptanceCoreAsync).Task.Unwrap();
    }

    private async Task RequestCloseForAcceptanceCoreAsync()
    {
        if (allowCloseAfterPreviewFlush)
        {
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
            shellViewModel?.BeginShutdownPreviewRelease();
            await PreviewHost.FlushActiveSessionAsync();
            shellViewModel?.CompleteShutdownAfterPreviewRelease();

            if (editorDialog is not null)
            {
                editorDialog.Closed -= HandleEditorDialogClosed;
                editorDialog.Close();
                editorDialog = null;
            }
        }
        catch (Exception ex)
        {
            _ = diagnosticService.WriteAsync(
                "exception-caught",
                $"source=shell-window-close, type={ex.GetType().FullName}, message={ex.Message}");
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

        if (shellViewModel is not null)
        {
            shellViewModel.EditorDialogRequested -= HandleEditorDialogRequested;
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
