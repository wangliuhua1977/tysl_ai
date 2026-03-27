using System.Windows;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.UI.ViewModels;

namespace Tysl.Ai.UI.Views;

public partial class ManualDispatchDialog : Window
{
    private ManualDispatchDialogViewModel? viewModel;

    public ILocalDiagnosticService? DiagnosticService { get; set; }

    public ManualDispatchDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ManualDispatchDialogViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= HandleCloseRequested;
        }

        viewModel = e.NewValue as ManualDispatchDialogViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.CloseRequested += HandleCloseRequested;
    }

    private void HandleConfirmClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualDispatchDialogViewModel viewModel || viewModel.IsSubmitting)
        {
            return;
        }

        _ = DiagnosticService?.WriteAsync(
            "manual-dispatch-confirm-start",
            $"deviceCode={viewModel.DeviceCode}");

        var result = MessageBox.Show(
            this,
            "确认后将立即发起手工派单并写入活动工单。是否继续？",
            "二次确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _ = DiagnosticService?.WriteAsync(
                "manual-dispatch-second-confirm-accepted",
                $"deviceCode={viewModel.DeviceCode}");
            viewModel.ConfirmExecute();
        }
    }

    private void HandleCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
