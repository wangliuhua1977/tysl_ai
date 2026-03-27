using System.Windows;
using Tysl.Ai.UI.ViewModels;

namespace Tysl.Ai.UI.Views;

public partial class CloseWorkOrderDialog : Window
{
    private CloseWorkOrderDialogViewModel? viewModel;

    public CloseWorkOrderDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CloseWorkOrderDialogViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= HandleCloseRequested;
        }

        viewModel = e.NewValue as CloseWorkOrderDialogViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.CloseRequested += HandleCloseRequested;
    }

    private void HandleConfirmClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CloseWorkOrderDialogViewModel viewModel || viewModel.IsSubmitting)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "确认后将把当前恢复工单归档并结束本轮闭环，是否继续？",
            "二次确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            viewModel.ConfirmExecute();
        }
    }

    private void HandleCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
