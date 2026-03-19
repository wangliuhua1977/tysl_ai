using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SiteManagementService : ISiteManagementService
{
    private readonly ISiteProfileRepository repository;

    public SiteManagementService(ISiteProfileRepository repository)
    {
        this.repository = repository;
    }

    public async Task<SiteProfile> CreateAsync(SiteProfileInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);

        var now = DateTimeOffset.UtcNow;
        var siteProfile = new SiteProfile
        {
            Id = input.Id ?? Guid.NewGuid(),
            DeviceCode = input.DeviceCode.Trim(),
            DeviceName = input.DeviceName.Trim(),
            Alias = NormalizeText(input.Alias),
            Remark = NormalizeText(input.Remark),
            IsMonitored = input.IsMonitored,
            Longitude = input.Longitude,
            Latitude = input.Latitude,
            AddressText = NormalizeText(input.AddressText),
            ProductAccessNumber = NormalizeText(input.ProductAccessNumber),
            MaintenanceUnit = NormalizeText(input.MaintenanceUnit),
            MaintainerName = NormalizeText(input.MaintainerName),
            MaintainerPhone = NormalizeText(input.MaintainerPhone),
            DemoStatus = input.DemoStatus,
            DemoDispatchStatus = input.DemoDispatchStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        await repository.CreateAsync(siteProfile, cancellationToken);
        return siteProfile;
    }

    public async Task<SiteProfile> UpdateAsync(SiteProfileInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);

        if (input.Id is null || input.Id == Guid.Empty)
        {
            throw new InvalidOperationException("更新点位时必须提供有效 Id。");
        }

        var existingSite = await repository.GetByIdAsync(input.Id.Value, cancellationToken);
        if (existingSite is null)
        {
            throw new InvalidOperationException("未找到需要更新的点位。");
        }

        existingSite.DeviceCode = input.DeviceCode.Trim();
        existingSite.DeviceName = input.DeviceName.Trim();
        existingSite.Alias = NormalizeText(input.Alias);
        existingSite.Remark = NormalizeText(input.Remark);
        existingSite.IsMonitored = input.IsMonitored;
        existingSite.Longitude = input.Longitude;
        existingSite.Latitude = input.Latitude;
        existingSite.AddressText = NormalizeText(input.AddressText);
        existingSite.ProductAccessNumber = NormalizeText(input.ProductAccessNumber);
        existingSite.MaintenanceUnit = NormalizeText(input.MaintenanceUnit);
        existingSite.MaintainerName = NormalizeText(input.MaintainerName);
        existingSite.MaintainerPhone = NormalizeText(input.MaintainerPhone);
        existingSite.DemoStatus = input.DemoStatus;
        existingSite.DemoDispatchStatus = input.DemoDispatchStatus;
        existingSite.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(existingSite, cancellationToken);
        return existingSite;
    }

    public Task<SiteProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return repository.GetByIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<SiteProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return repository.ListAsync(cancellationToken);
    }

    private static void Validate(SiteProfileInput input)
    {
        if (string.IsNullOrWhiteSpace(input.DeviceCode))
        {
            throw new InvalidOperationException("设备编码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(input.DeviceName))
        {
            throw new InvalidOperationException("设备名称不能为空。");
        }

        if (input.Longitude is < -180 or > 180)
        {
            throw new InvalidOperationException("经度超出有效范围。");
        }

        if (input.Latitude is < -90 or > 90)
        {
            throw new InvalidOperationException("纬度超出有效范围。");
        }
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
