using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SiteLocalProfileService : ISiteLocalProfileService
{
    private readonly ISiteLocalProfileRepository repository;

    public SiteLocalProfileService(ISiteLocalProfileRepository repository)
    {
        this.repository = repository;
    }

    public Task<SiteLocalProfile?> GetByDeviceCodeAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return Task.FromResult<SiteLocalProfile?>(null);
        }

        return repository.GetByDeviceCodeAsync(deviceCode.Trim(), cancellationToken);
    }

    public async Task<SiteLocalProfile> IgnoreAsync(
        string deviceCode,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new InvalidOperationException("设备编码不能为空。");
        }

        var normalizedDeviceCode = deviceCode.Trim();
        var now = DateTimeOffset.UtcNow;
        var existingProfile = await repository.GetByDeviceCodeAsync(normalizedDeviceCode, cancellationToken);

        var profile = existingProfile ?? new SiteLocalProfile
        {
            DeviceCode = normalizedDeviceCode,
            CreatedAt = now,
            IsMonitored = true
        };

        profile.IsIgnored = true;
        profile.IgnoredAt = now;
        profile.IgnoredReason = NormalizeText(reason);
        profile.UpdatedAt = now;

        await repository.UpsertAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<SiteLocalProfile?> RestoreAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return null;
        }

        var profile = await repository.GetByDeviceCodeAsync(deviceCode.Trim(), cancellationToken);
        if (profile is null)
        {
            return null;
        }

        profile.IsIgnored = false;
        profile.IgnoredAt = null;
        profile.IgnoredReason = null;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpsertAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<SiteLocalProfile> UpsertAsync(
        SiteLocalProfileInput input,
        CancellationToken cancellationToken = default)
    {
        Validate(input);

        var deviceCode = input.DeviceCode.Trim();
        var now = DateTimeOffset.UtcNow;
        var existingProfile = await repository.GetByDeviceCodeAsync(deviceCode, cancellationToken);

        var profile = existingProfile ?? new SiteLocalProfile
        {
            DeviceCode = deviceCode,
            CreatedAt = now
        };

        profile.Alias = NormalizeText(input.Alias);
        profile.Remark = NormalizeText(input.Remark);
        profile.IsMonitored = input.IsMonitored;
        profile.ManualLongitude = input.ManualLongitude;
        profile.ManualLatitude = input.ManualLatitude;
        profile.AddressText = NormalizeText(input.AddressText);
        profile.ProductAccessNumber = NormalizeText(input.ProductAccessNumber);
        profile.MaintenanceUnit = NormalizeText(input.MaintenanceUnit);
        profile.MaintainerName = NormalizeText(input.MaintainerName);
        profile.MaintainerPhone = NormalizeText(input.MaintainerPhone);
        profile.UpdatedAt = now;

        await repository.UpsertAsync(profile, cancellationToken);
        return profile;
    }

    private static void Validate(SiteLocalProfileInput input)
    {
        if (string.IsNullOrWhiteSpace(input.DeviceCode))
        {
            throw new InvalidOperationException("设备编码不能为空。");
        }

        var hasManualLongitude = input.ManualLongitude.HasValue;
        var hasManualLatitude = input.ManualLatitude.HasValue;
        if (hasManualLongitude != hasManualLatitude)
        {
            throw new InvalidOperationException("手工坐标必须同时填写经度和纬度，或同时留空。");
        }

        if (input.ManualLongitude is < -180 or > 180)
        {
            throw new InvalidOperationException("手工经度超出有效范围。");
        }

        if (input.ManualLatitude is < -90 or > 90)
        {
            throw new InvalidOperationException("手工纬度超出有效范围。");
        }
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
