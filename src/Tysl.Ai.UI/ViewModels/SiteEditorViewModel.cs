using System.Globalization;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteEditorViewModel : ObservableObject
{
    private string addressText;
    private string alias;
    private string coordinatePickSummary;
    private bool isMonitored;
    private string latitudeText;
    private string maintainerName;
    private string maintainerPhone;
    private string maintenanceUnit;
    private string longitudeText;
    private string productAccessNumber;
    private string remark;

    private SiteEditorViewModel(
        string deviceCode,
        string deviceName,
        bool hasLocalProfile,
        string alias,
        string remark,
        bool isMonitored,
        double? manualLongitude,
        double? manualLatitude,
        string addressText,
        string productAccessNumber,
        string maintenanceUnit,
        string maintainerName,
        string maintainerPhone)
    {
        DeviceCode = deviceCode;
        DeviceName = deviceName;
        HasLocalProfile = hasLocalProfile;
        this.alias = alias;
        this.remark = remark;
        this.isMonitored = isMonitored;
        longitudeText = manualLongitude?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty;
        latitudeText = manualLatitude?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty;
        this.addressText = addressText;
        this.productAccessNumber = productAccessNumber;
        this.maintenanceUnit = maintenanceUnit;
        this.maintainerName = maintainerName;
        this.maintainerPhone = maintainerPhone;
        coordinatePickSummary = BuildDefaultCoordinatePickSummary();

        SaveCommand = new RelayCommand(() => SaveRequested?.Invoke(this, EventArgs.Empty));
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        BeginCoordinatePickCommand = new RelayCommand(() => CoordinatePickRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? CoordinatePickRequested;

    public event EventHandler? CloseRequested;

    public string DeviceCode { get; }

    public string DeviceName { get; }

    public bool HasLocalProfile { get; }

    public string Title => HasLocalProfile ? "编辑补充信息 / 补坐标" : "补录补充信息 / 补坐标";

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand BeginCoordinatePickCommand { get; }

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

    public string CoordinatePickSummary
    {
        get => coordinatePickSummary;
        private set => SetProperty(ref coordinatePickSummary, value);
    }

    public static SiteEditorViewModel CreateFromSite(SiteMergedView site)
    {
        return new SiteEditorViewModel(
            site.DeviceCode,
            site.DeviceName,
            site.HasLocalProfile,
            site.Alias ?? string.Empty,
            site.Remark ?? string.Empty,
            site.IsMonitored,
            site.ManualLongitude,
            site.ManualLatitude,
            site.AddressText ?? string.Empty,
            site.ProductAccessNumber ?? string.Empty,
            site.MaintenanceUnit ?? string.Empty,
            site.MaintainerName ?? string.Empty,
            site.MaintainerPhone ?? string.Empty);
    }

    public void MarkCoordinatePickPending(DemoCoordinate? coordinate = null)
    {
        CoordinatePickSummary = coordinate is null
            ? "请在地图上连续点击选择位置，确认后再保存。"
            : BuildPickedCoordinateSummary(coordinate.Longitude, coordinate.Latitude);
    }

    public void ApplyPickedCoordinate(DemoCoordinate coordinate)
    {
        LongitudeText = coordinate.Longitude.ToString("F6", CultureInfo.InvariantCulture);
        LatitudeText = coordinate.Latitude.ToString("F6", CultureInfo.InvariantCulture);
        CoordinatePickSummary = BuildPickedCoordinateSummary(coordinate.Longitude, coordinate.Latitude);
    }

    public bool TryBuildInput(out SiteLocalProfileInput? input, out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        if (!TryParseCoordinate(LongitudeText, out var longitude))
        {
            errorMessage = "手工经度格式不正确。";
            return false;
        }

        if (!TryParseCoordinate(LatitudeText, out var latitude))
        {
            errorMessage = "手工纬度格式不正确。";
            return false;
        }

        if (longitude.HasValue != latitude.HasValue)
        {
            errorMessage = "手工坐标必须同时填写经纬度，或同时留空。";
            return false;
        }

        input = new SiteLocalProfileInput
        {
            DeviceCode = DeviceCode,
            Alias = Alias,
            Remark = Remark,
            IsMonitored = IsMonitored,
            ManualLongitude = longitude,
            ManualLatitude = latitude,
            AddressText = AddressText,
            ProductAccessNumber = ProductAccessNumber,
            MaintenanceUnit = MaintenanceUnit,
            MaintainerName = MaintainerName,
            MaintainerPhone = MaintainerPhone
        };

        return true;
    }

    public void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryParseCoordinate(string value, out double? coordinate)
    {
        coordinate = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantValue)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out invariantValue))
        {
            coordinate = invariantValue;
            return true;
        }

        return false;
    }

    private static string BuildDefaultCoordinatePickSummary()
    {
        return "可补录手工坐标；保存后，未落图点位会重新参与落图。";
    }

    private static string BuildPickedCoordinateSummary(double longitude, double latitude)
    {
        return $"当前候选手工坐标：{longitude:F6}, {latitude:F6}。确认后点击“保存补充信息”。";
    }
}
