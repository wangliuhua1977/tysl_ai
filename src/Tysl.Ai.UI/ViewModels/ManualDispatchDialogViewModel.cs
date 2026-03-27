using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class ManualDispatchDialogViewModel : ObservableObject
{
    private bool isSubmitting;

    public ManualDispatchDialogViewModel(ManualDispatchPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        DeviceCode = preparation.DeviceCode;
        SiteDisplayName = preparation.SiteDisplayName;
        ProductAccessNumber = string.IsNullOrWhiteSpace(preparation.ProductAccessNumber) ? "未补充" : preparation.ProductAccessNumber;
        FaultReason = preparation.FaultReason;
        MaintenanceUnit = string.IsNullOrWhiteSpace(preparation.MaintenanceUnit) ? "维护单位待补充" : preparation.MaintenanceUnit;
        MaintainerName = string.IsNullOrWhiteSpace(preparation.MaintainerName) ? "维护人待补充" : preparation.MaintainerName;
        MaintainerPhone = string.IsNullOrWhiteSpace(preparation.MaintainerPhone) ? "联系电话待补充" : preparation.MaintainerPhone;
        NotificationPoolText = preparation.NotificationPool == WebhookEndpointPool.Dispatch ? "派单通知池" : "恢复通知池";
        EnabledEndpointCountText = $"{preparation.EnabledEndpointCount} 个启用地址";
        TemplatePreview = preparation.TemplatePreview;

        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty), () => !IsSubmitting);
    }

    public event EventHandler? ExecuteConfirmed;

    public event EventHandler? CancelRequested;

    public event EventHandler? CloseRequested;

    public string DeviceCode { get; }

    public string SiteDisplayName { get; }

    public string ProductAccessNumber { get; }

    public string FaultReason { get; }

    public string MaintenanceUnit { get; }

    public string MaintainerName { get; }

    public string MaintainerPhone { get; }

    public string NotificationPoolText { get; }

    public string EnabledEndpointCountText { get; }

    public string TemplatePreview { get; }

    public string ConfirmButtonText => IsSubmitting ? "派单中..." : "确认派单";

    public bool CanConfirm => !IsSubmitting;

    public string Title => "手工派单确认";

    public RelayCommand CancelCommand { get; }

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
            OnPropertyChanged(nameof(ConfirmButtonText));
            OnPropertyChanged(nameof(CanConfirm));
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
