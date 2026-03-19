using System.Globalization;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteEditorViewModel : ObservableObject
{
    private string addressText;
    private string alias;
    private string coordinatePickSummary;
    private string deviceCode;
    private string deviceName;
    private bool isMonitored;
    private string latitudeText;
    private string longitudeText;
    private string maintainerName;
    private string maintainerPhone;
    private string maintenanceUnit;
    private string productAccessNumber;
    private string remark;
    private DispatchDemoStatus selectedDispatchStatus;
    private PointDemoStatus selectedDemoStatus;

    private SiteEditorViewModel(
        Guid? id,
        string deviceCode,
        string deviceName,
        string alias,
        string remark,
        bool isMonitored,
        double longitude,
        double latitude,
        string addressText,
        string productAccessNumber,
        string maintenanceUnit,
        string maintainerName,
        string maintainerPhone,
        PointDemoStatus demoStatus,
        DispatchDemoStatus dispatchStatus)
    {
        Id = id;
        IsEditMode = id.HasValue && id.Value != Guid.Empty;
        this.deviceCode = deviceCode;
        this.deviceName = deviceName;
        this.alias = alias;
        this.remark = remark;
        this.isMonitored = isMonitored;
        longitudeText = longitude.ToString("F6", CultureInfo.InvariantCulture);
        latitudeText = latitude.ToString("F6", CultureInfo.InvariantCulture);
        this.addressText = addressText;
        this.productAccessNumber = productAccessNumber;
        this.maintenanceUnit = maintenanceUnit;
        this.maintainerName = maintainerName;
        this.maintainerPhone = maintainerPhone;
        selectedDemoStatus = demoStatus;
        selectedDispatchStatus = dispatchStatus;
        coordinatePickSummary = "演示坐标拾取：点击主界面地图占位区域后回填经纬度。";

        SaveCommand = new RelayCommand(() => SaveRequested?.Invoke(this, EventArgs.Empty));
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        BeginCoordinatePickCommand = new RelayCommand(() => CoordinatePickRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? CoordinatePickRequested;

    public event EventHandler? CloseRequested;

    public Guid? Id { get; }

    public bool IsEditMode { get; }

    public string Title => IsEditMode ? "编辑点位" : "新增点位";

    public IReadOnlyList<PointDemoStatus> DemoStatusOptions { get; } = Enum.GetValues<PointDemoStatus>();

    public IReadOnlyList<DispatchDemoStatus> DispatchStatusOptions { get; } = Enum.GetValues<DispatchDemoStatus>();

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand BeginCoordinatePickCommand { get; }

    public string DeviceCode
    {
        get => deviceCode;
        set => SetProperty(ref deviceCode, value);
    }

    public string DeviceName
    {
        get => deviceName;
        set => SetProperty(ref deviceName, value);
    }

    public string Alias
    {
        get => alias;
        set => SetProperty(ref alias, value);
    }

    public string Remark
    {
        get => remark;
        set => SetProperty(ref remark, value);
    }

    public bool IsMonitored
    {
        get => isMonitored;
        set => SetProperty(ref isMonitored, value);
    }

    public string LongitudeText
    {
        get => longitudeText;
        set => SetProperty(ref longitudeText, value);
    }

    public string LatitudeText
    {
        get => latitudeText;
        set => SetProperty(ref latitudeText, value);
    }

    public string AddressText
    {
        get => addressText;
        set => SetProperty(ref addressText, value);
    }

    public string ProductAccessNumber
    {
        get => productAccessNumber;
        set => SetProperty(ref productAccessNumber, value);
    }

    public string MaintenanceUnit
    {
        get => maintenanceUnit;
        set => SetProperty(ref maintenanceUnit, value);
    }

    public string MaintainerName
    {
        get => maintainerName;
        set => SetProperty(ref maintainerName, value);
    }

    public string MaintainerPhone
    {
        get => maintainerPhone;
        set => SetProperty(ref maintainerPhone, value);
    }

    public PointDemoStatus SelectedDemoStatus
    {
        get => selectedDemoStatus;
        set => SetProperty(ref selectedDemoStatus, value);
    }

    public DispatchDemoStatus SelectedDispatchStatus
    {
        get => selectedDispatchStatus;
        set => SetProperty(ref selectedDispatchStatus, value);
    }

    public string CoordinatePickSummary
    {
        get => coordinatePickSummary;
        private set => SetProperty(ref coordinatePickSummary, value);
    }

    public static SiteEditorViewModel CreateForNew()
    {
        return new SiteEditorViewModel(
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            true,
            120.600000D,
            30.010000D,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            PointDemoStatus.Normal,
            DispatchDemoStatus.None);
    }

    public static SiteEditorViewModel CreateForEdit(SiteProfile siteProfile)
    {
        return new SiteEditorViewModel(
            siteProfile.Id,
            siteProfile.DeviceCode,
            siteProfile.DeviceName,
            siteProfile.Alias ?? string.Empty,
            siteProfile.Remark ?? string.Empty,
            siteProfile.IsMonitored,
            siteProfile.Longitude,
            siteProfile.Latitude,
            siteProfile.AddressText ?? string.Empty,
            siteProfile.ProductAccessNumber ?? string.Empty,
            siteProfile.MaintenanceUnit ?? string.Empty,
            siteProfile.MaintainerName ?? string.Empty,
            siteProfile.MaintainerPhone ?? string.Empty,
            siteProfile.DemoStatus,
            siteProfile.DemoDispatchStatus);
    }

    public void MarkCoordinatePickPending()
    {
        CoordinatePickSummary = "演示坐标拾取中：请点击主界面地图占位区域。";
    }

    public void ApplyPickedCoordinate(DemoCoordinate coordinate)
    {
        LongitudeText = coordinate.Longitude.ToString("F6", CultureInfo.InvariantCulture);
        LatitudeText = coordinate.Latitude.ToString("F6", CultureInfo.InvariantCulture);
        CoordinatePickSummary = $"已回填演示坐标：{LongitudeText}, {LatitudeText}";
    }

    public bool TryBuildInput(out SiteProfileInput? input, out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(DeviceCode))
        {
            errorMessage = "设备编码不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DeviceName))
        {
            errorMessage = "设备名称不能为空。";
            return false;
        }

        if (!TryParseCoordinate(LongitudeText, out var longitude))
        {
            errorMessage = "经度格式不正确。";
            return false;
        }

        if (!TryParseCoordinate(LatitudeText, out var latitude))
        {
            errorMessage = "纬度格式不正确。";
            return false;
        }

        input = new SiteProfileInput
        {
            Id = Id,
            DeviceCode = DeviceCode.Trim(),
            DeviceName = DeviceName.Trim(),
            Alias = Alias,
            Remark = Remark,
            IsMonitored = IsMonitored,
            Longitude = longitude,
            Latitude = latitude,
            AddressText = AddressText,
            ProductAccessNumber = ProductAccessNumber,
            MaintenanceUnit = MaintenanceUnit,
            MaintainerName = MaintainerName,
            MaintainerPhone = MaintainerPhone,
            DemoStatus = SelectedDemoStatus,
            DemoDispatchStatus = SelectedDispatchStatus
        };

        return true;
    }

    public void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryParseCoordinate(string value, out double coordinate)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out coordinate)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out coordinate);
    }
}
