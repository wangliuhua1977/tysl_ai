using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class CloseWorkOrderDialogViewModel : ObservableObject
{
    private bool isSubmitting;
    private string closingRemark = string.Empty;

    public CloseWorkOrderDialogViewModel(CloseWorkOrderPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        WorkOrderId = preparation.WorkOrderId;
        DeviceCode = preparation.DeviceCode;
        SiteDisplayName = preparation.SiteDisplayName;
        ProductAccessNumber = string.IsNullOrWhiteSpace(preparation.ProductAccessNumber) ? "未补充" : preparation.ProductAccessNumber;
        CurrentFaultReason = preparation.CurrentFaultReason;
        MaintenanceUnit = string.IsNullOrWhiteSpace(preparation.MaintenanceUnit) ? "维护单位待补充" : preparation.MaintenanceUnit;
        MaintainerName = string.IsNullOrWhiteSpace(preparation.MaintainerName) ? "维护人待补充" : preparation.MaintainerName;
        MaintainerPhone = string.IsNullOrWhiteSpace(preparation.MaintainerPhone) ? "联系电话待补充" : preparation.MaintainerPhone;
        RecoveryStatusText = preparation.RecoveryStatusText;
        RecoveredAtText = preparation.RecoveredAtText;
        LastNotificationSummaryText = string.IsNullOrWhiteSpace(preparation.LastNotificationSummary) ? "暂无通知摘要" : preparation.LastNotificationSummary;

        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty), () => !IsSubmitting);
    }

    public event EventHandler? ExecuteConfirmed;

    public event EventHandler? CancelRequested;

    public event EventHandler? CloseRequested;

    public long WorkOrderId { get; }

    public string DeviceCode { get; }

    public string SiteDisplayName { get; }

    public string ProductAccessNumber { get; }

    public string CurrentFaultReason { get; }

    public string MaintenanceUnit { get; }

    public string MaintainerName { get; }

    public string MaintainerPhone { get; }

    public string RecoveryStatusText { get; }

    public string RecoveredAtText { get; }

    public string LastNotificationSummaryText { get; }

    public string Title => "竣工归档确认";

    public RelayCommand CancelCommand { get; }

    public string ClosingRemark
    {
        get => closingRemark;
        set => SetProperty(ref closingRemark, value);
    }

    public bool IsSubmitting
    {
        get => isSubmitting;
        set
        {
            if (!SetProperty(ref isSubmitting, value))
            {
                return;
            }

            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public void ConfirmExecute()
    {
        if (IsSubmitting)
        {
            return;
        }

        ExecuteConfirmed?.Invoke(this, EventArgs.Empty);
    }

    public void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
