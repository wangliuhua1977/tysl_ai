using System.Security.Cryptography;
using System.Text;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Dispatch;

public sealed class DispatchService
{
    private readonly IDispatchPolicyProvider dispatchPolicyProvider;
    private readonly IDispatchRecordRepository dispatchRecordRepository;
    private readonly ISiteLocalProfileRepository localProfileRepository;
    private readonly IPlatformSiteProvider platformSiteProvider;
    private readonly ISiteRuntimeStateRepository runtimeStateRepository;
    private readonly IWebhookSender webhookSender;

    public DispatchService(
        IDispatchPolicyProvider dispatchPolicyProvider,
        IDispatchRecordRepository dispatchRecordRepository,
        ISiteLocalProfileRepository localProfileRepository,
        ISiteRuntimeStateRepository runtimeStateRepository,
        IPlatformSiteProvider platformSiteProvider,
        IWebhookSender webhookSender)
    {
        this.dispatchPolicyProvider = dispatchPolicyProvider;
        this.dispatchRecordRepository = dispatchRecordRepository;
        this.localProfileRepository = localProfileRepository;
        this.runtimeStateRepository = runtimeStateRepository;
        this.platformSiteProvider = platformSiteProvider;
        this.webhookSender = webhookSender;
    }

    public async Task ProcessInspectionResultAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken = default)
    {
        if (localProfile?.IsIgnored == true)
        {
            return;
        }

        var policy = await dispatchPolicyProvider.GetAsync(cancellationToken);
        var activeRecord = await dispatchRecordRepository.GetLatestUnrecoveredByDeviceAsync(
            platformSite.DeviceCode,
            cancellationToken);
        var currentFaultCode = MapFaultCode(currentState.LastFaultCode);

        if (currentFaultCode is null)
        {
            if (activeRecord is not null)
            {
                await HandleRecoveryAsync(policy, activeRecord, platformSite, localProfile, currentState, cancellationToken);
            }

            return;
        }

        if (!policy.Enabled)
        {
            return;
        }

        if (activeRecord is not null)
        {
            await UpdateOngoingFaultAsync(policy, activeRecord, currentFaultCode, platformSite, localProfile, currentState, cancellationToken);
            return;
        }

        if (!policy.RepeatAfterRecovery)
        {
            var latestSameFault = await dispatchRecordRepository.GetLatestByDeviceAndFaultAsync(
                platformSite.DeviceCode,
                currentFaultCode,
                cancellationToken);

            if (latestSameFault?.RecoveredAt is DateTimeOffset recoveredAt
                && recoveredAt.AddMinutes(Math.Max(1, policy.CoolingMinutes)) > DateTimeOffset.UtcNow)
            {
                return;
            }
        }

        await CreateFaultRecordAsync(policy, currentFaultCode, platformSite, localProfile, currentState, cancellationToken);
    }

    public Task<ManualDispatchPreparation> PrepareManualDispatchAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Infrastructure.Dispatch.DispatchService 已退役，请改用 Services.Dispatch.DispatchWorkflowService。");
    }

    public async Task ConfirmRecoveryAsync(long dispatchRecordId, CancellationToken cancellationToken = default)
    {
        var record = await dispatchRecordRepository.GetByIdAsync(dispatchRecordId, cancellationToken);
        if (record is null || record.IsRecovered)
        {
            return;
        }

        var policy = await dispatchPolicyProvider.GetAsync(cancellationToken);
        var context = await LoadSiteContextAsync(record.DeviceCode, cancellationToken);
        if (context.LocalProfile?.IsIgnored == true)
        {
            return;
        }

        await FinalizeRecoveryAsync(
            policy,
            record,
            context.PlatformSite,
            context.LocalProfile,
            record.LastInspectionAt,
            "已人工确认恢复",
            cancellationToken);
    }

    public Task ManualDispatchAsync(
        ManualDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ManualDispatchInternalAsync(request.DeviceCode, cancellationToken);
    }

    public async Task ManualDispatchInternalAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new InvalidOperationException("deviceCode is required.");
        }

        var normalizedDeviceCode = deviceCode.Trim();
        var policy = await dispatchPolicyProvider.GetAsync(cancellationToken);
        var context = await LoadSiteContextAsync(normalizedDeviceCode, cancellationToken);
        if (context.PlatformSite is null)
        {
            throw new InvalidOperationException("当前点位不存在或平台未返回设备信息。");
        }

        if (context.LocalProfile?.IsIgnored == true)
        {
            throw new InvalidOperationException("已忽略点位不能发起派单。");
        }

        var activeRecord = await dispatchRecordRepository.GetLatestUnrecoveredByDeviceAsync(
            normalizedDeviceCode,
            cancellationToken);

        if (activeRecord is not null)
        {
            if (activeRecord.DispatchStatus == DispatchStatus.Dispatched && !activeRecord.IsRecovered)
            {
                return;
            }

            if (context.RuntimeState is null)
            {
                throw new InvalidOperationException("当前点位尚无运行态记录，无法发起派单。");
            }

            var faultCode = NormalizeFaultCode(activeRecord.FaultCode, context.RuntimeState.LastFaultCode);
            var message = BuildFaultMessage(
                policy,
                context.PlatformSite,
                context.LocalProfile,
                context.RuntimeState,
                faultCode,
                ResolveDispatchStatusText(DispatchStatus.Dispatched, isCooling: false));

            var recordToSend = activeRecord with
            {
                FaultCode = faultCode,
                FaultSummary = context.RuntimeState.LastFaultSummary ?? ResolveFaultLabel(faultCode),
                DispatchMode = DispatchMode.Manual,
                MessageDigest = ComputeDigest(message.Content),
                SnapshotPath = context.RuntimeState.LastSnapshotPath,
                LastInspectionAt = context.RuntimeState.LastInspectionAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            recordToSend = await SendDispatchAsync(policy, recordToSend, message, cancellationToken);
            await dispatchRecordRepository.UpdateAsync(recordToSend, cancellationToken);
            return;
        }

        if (context.RuntimeState is null)
        {
            throw new InvalidOperationException("当前点位尚无运行态记录，无法发起派单。");
        }

        var currentFaultCode = MapFaultCode(context.RuntimeState.LastFaultCode);
        if (currentFaultCode is null)
        {
            throw new InvalidOperationException("当前点位未处于异常状态，无法发起派单。");
        }

        await CreateManualDispatchRecordAsync(
            policy,
            currentFaultCode,
            context.PlatformSite,
            context.LocalProfile,
            context.RuntimeState,
            cancellationToken);
    }

    private async Task CreateFaultRecordAsync(
        DispatchPolicy policy,
        string faultCode,
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var initialStatus = policy.Mode == DispatchMode.Manual
            ? DispatchStatus.PendingDispatch
            : ResolveAutomaticDispatchStatus(policy);
        var message = BuildFaultMessage(
            policy,
            platformSite,
            localProfile,
            currentState,
            faultCode,
            ResolveDispatchStatusText(initialStatus, isCooling: false));

        var record = new DispatchRecord
        {
            DeviceCode = platformSite.DeviceCode,
            FaultCode = faultCode,
            FaultSummary = currentState.LastFaultSummary ?? ResolveFaultLabel(faultCode),
            DispatchStatus = initialStatus,
            DispatchMode = policy.Mode,
            TriggeredAt = now,
            RecoveryMode = policy.RecoveryMode,
            RecoveryStatus = RecoveryStatus.None,
            MessageDigest = ComputeDigest(message.Content),
            SnapshotPath = currentState.LastSnapshotPath,
            LastInspectionAt = currentState.LastInspectionAt,
            UpdatedAt = now
        };

        var persistedId = await dispatchRecordRepository.AddAsync(record, cancellationToken);
        record = record with { Id = persistedId };

        if (policy.Mode == DispatchMode.Automatic)
        {
            record = await SendDispatchAsync(policy, record, message, cancellationToken);
        }

        await dispatchRecordRepository.UpdateAsync(record, cancellationToken);
    }

    private async Task CreateManualDispatchRecordAsync(
        DispatchPolicy policy,
        string faultCode,
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var message = BuildFaultMessage(
            policy,
            platformSite,
            localProfile,
            currentState,
            faultCode,
            ResolveDispatchStatusText(DispatchStatus.Dispatched, isCooling: false));

        var record = new DispatchRecord
        {
            DeviceCode = platformSite.DeviceCode,
            FaultCode = faultCode,
            FaultSummary = currentState.LastFaultSummary ?? ResolveFaultLabel(faultCode),
            DispatchStatus = DispatchStatus.PendingDispatch,
            DispatchMode = DispatchMode.Manual,
            TriggeredAt = now,
            RecoveryMode = policy.RecoveryMode,
            RecoveryStatus = RecoveryStatus.None,
            MessageDigest = ComputeDigest(message.Content),
            SnapshotPath = currentState.LastSnapshotPath,
            LastInspectionAt = currentState.LastInspectionAt,
            UpdatedAt = now
        };

        var persistedId = await dispatchRecordRepository.AddAsync(record, cancellationToken);
        record = record with { Id = persistedId };
        record = await SendDispatchAsync(policy, record, message, cancellationToken);
        await dispatchRecordRepository.UpdateAsync(record, cancellationToken);
    }

    private async Task UpdateOngoingFaultAsync(
        DispatchPolicy policy,
        DispatchRecord activeRecord,
        string faultCode,
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken)
    {
        var normalizedRecord = activeRecord with
        {
            FaultCode = faultCode,
            FaultSummary = currentState.LastFaultSummary ?? ResolveFaultLabel(faultCode),
            SnapshotPath = currentState.LastSnapshotPath,
            LastInspectionAt = currentState.LastInspectionAt,
            RecoveryStatus = activeRecord.RecoveryStatus == RecoveryStatus.PendingConfirmation
                ? RecoveryStatus.None
                : activeRecord.RecoveryStatus,
            RecoverySummary = activeRecord.RecoveryStatus == RecoveryStatus.PendingConfirmation
                ? null
                : activeRecord.RecoverySummary,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var shouldSend =
            policy.Mode == DispatchMode.Automatic
            && normalizedRecord.DispatchStatus is DispatchStatus.PendingDispatch or DispatchStatus.SendFailed or DispatchStatus.WebhookNotConfigured;

        if (!shouldSend)
        {
            await dispatchRecordRepository.UpdateAsync(normalizedRecord, cancellationToken);
            return;
        }

        var message = BuildFaultMessage(
            policy,
            platformSite,
            localProfile,
            currentState,
            faultCode,
            ResolveDispatchStatusText(DispatchStatus.Dispatched, isCooling: false));

        normalizedRecord = normalizedRecord with
        {
            MessageDigest = ComputeDigest(message.Content)
        };

        normalizedRecord = await SendDispatchAsync(policy, normalizedRecord, message, cancellationToken);
        await dispatchRecordRepository.UpdateAsync(normalizedRecord, cancellationToken);
    }

    private async Task HandleRecoveryAsync(
        DispatchPolicy policy,
        DispatchRecord activeRecord,
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken)
    {
        if (activeRecord.IsRecovered)
        {
            return;
        }

        if (policy.RecoveryMode == RecoveryMode.Manual)
        {
            if (activeRecord.RecoveryStatus == RecoveryStatus.PendingConfirmation)
            {
                return;
            }

            var pendingRecord = activeRecord with
            {
                RecoveryMode = policy.RecoveryMode,
                RecoveryStatus = RecoveryStatus.PendingConfirmation,
                RecoverySummary = "运行态已恢复，待人工确认",
                LastInspectionAt = currentState.LastInspectionAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await dispatchRecordRepository.UpdateAsync(pendingRecord, cancellationToken);
            return;
        }

        await FinalizeRecoveryAsync(
            policy,
            activeRecord,
            platformSite,
            localProfile,
            currentState.LastInspectionAt,
            currentState.LastFaultSummary ?? "运行态恢复正常",
            cancellationToken);
    }

    private async Task<DispatchRecord> SendDispatchAsync(
        DispatchPolicy policy,
        DispatchRecord record,
        WebhookMessage message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var webhookUrl = NormalizeText(policy.WebhookUrl);
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return record with
            {
                DispatchStatus = DispatchStatus.WebhookNotConfigured,
                CoolingUntil = null,
                UpdatedAt = now
            };
        }

        var sendResult = await webhookSender.SendAsync(webhookUrl, message, cancellationToken);
        if (sendResult.IsSuccess)
        {
            return record with
            {
                DispatchStatus = DispatchStatus.Dispatched,
                SentAt = now,
                CoolingUntil = now.AddMinutes(Math.Max(1, policy.CoolingMinutes)),
                UpdatedAt = now
            };
        }

        return record with
        {
            DispatchStatus = DispatchStatus.SendFailed,
            CoolingUntil = now.AddMinutes(Math.Max(1, policy.CoolingMinutes)),
            UpdatedAt = now
        };
    }

    private async Task FinalizeRecoveryAsync(
        DispatchPolicy policy,
        DispatchRecord record,
        PlatformSiteSnapshot? platformSite,
        SiteLocalProfile? localProfile,
        DateTimeOffset? lastInspectionAt,
        string recoverySummary,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedSummary = NormalizeText(recoverySummary) ?? "运行态恢复正常";
        var shouldNotify = policy.NotifyOnRecovery && record.DispatchStatus == DispatchStatus.Dispatched;

        var recoveryStatus = RecoveryStatus.Recovered;
        if (shouldNotify)
        {
            var message = BuildRecoveryMessage(
                platformSite,
                localProfile,
                record.DeviceCode,
                policy,
                now,
                normalizedSummary);
            var webhookUrl = NormalizeText(policy.WebhookUrl);

            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                recoveryStatus = RecoveryStatus.NotificationFailed;
            }
            else
            {
                var sendResult = await webhookSender.SendAsync(webhookUrl, message, cancellationToken);
                if (!sendResult.IsSuccess)
                {
                    recoveryStatus = RecoveryStatus.NotificationFailed;
                }
            }
        }

        var finalizedRecord = record with
        {
            RecoveryMode = policy.RecoveryMode,
            RecoveryStatus = recoveryStatus,
            RecoverySummary = recoveryStatus == RecoveryStatus.NotificationFailed
                ? $"{normalizedSummary}；恢复通知未发送"
                : normalizedSummary,
            RecoveredAt = now,
            LastInspectionAt = lastInspectionAt ?? record.LastInspectionAt,
            UpdatedAt = now
        };

        await dispatchRecordRepository.UpdateAsync(finalizedRecord, cancellationToken);
    }

    private async Task<SiteContext> LoadSiteContextAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var platformSites = await platformSiteProvider.ListAsync(cancellationToken);
        var platformSite = platformSites.FirstOrDefault(site => site.DeviceCode.Equals(deviceCode, StringComparison.OrdinalIgnoreCase));
        var localProfile = await localProfileRepository.GetByDeviceCodeAsync(deviceCode, cancellationToken);
        var runtimeState = await runtimeStateRepository.GetByDeviceCodeAsync(deviceCode, cancellationToken);
        return new SiteContext(platformSite, localProfile, runtimeState);
    }

    private static string NormalizeFaultCode(string? persistedFaultCode, RuntimeFaultCode runtimeFaultCode)
    {
        if (!string.IsNullOrWhiteSpace(persistedFaultCode))
        {
            return persistedFaultCode.Trim();
        }

        return MapFaultCode(runtimeFaultCode) ?? nameof(RuntimeFaultCode.InspectionExecutionFailed);
    }

    private static DispatchStatus ResolveAutomaticDispatchStatus(DispatchPolicy policy)
    {
        return string.IsNullOrWhiteSpace(policy.WebhookUrl)
            ? DispatchStatus.WebhookNotConfigured
            : DispatchStatus.Dispatched;
    }

    private static string? MapFaultCode(RuntimeFaultCode faultCode)
    {
        return faultCode switch
        {
            RuntimeFaultCode.Offline => nameof(RuntimeFaultCode.Offline),
            RuntimeFaultCode.PreviewResolveFailed => nameof(RuntimeFaultCode.PreviewResolveFailed),
            RuntimeFaultCode.SnapshotFailed => nameof(RuntimeFaultCode.SnapshotFailed),
            RuntimeFaultCode.InspectionExecutionFailed => nameof(RuntimeFaultCode.InspectionExecutionFailed),
            _ => null
        };
    }

    private static string ResolveFaultLabel(string faultCode)
    {
        return faultCode switch
        {
            nameof(RuntimeFaultCode.Offline) => "设备离线",
            nameof(RuntimeFaultCode.PreviewResolveFailed) => "预览解析失败",
            nameof(RuntimeFaultCode.SnapshotFailed) => "截图留痕失败",
            nameof(RuntimeFaultCode.InspectionExecutionFailed) => "巡检失败",
            _ => "运行态异常"
        };
    }

    private static WebhookMessage BuildFaultMessage(
        DispatchPolicy policy,
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        string faultCode,
        string dispatchStatusText)
    {
        var lines = new List<string>
        {
            $"【故障派单】{ResolveDisplayName(platformSite, localProfile)}",
            $"设备编码：{platformSite.DeviceCode}",
            $"故障类型：{ResolveFaultLabel(faultCode)}",
            $"故障摘要：{NormalizeText(currentState.LastFaultSummary) ?? ResolveFaultLabel(faultCode)}",
            $"最近巡检：{FormatDateTime(currentState.LastInspectionAt)}",
            $"派单状态：{dispatchStatusText}",
            $"维护信息：{BuildMaintenanceText(localProfile)}",
            $"最近截图：{NormalizeText(currentState.LastSnapshotPath) ?? "暂无截图"}",
            $"位置摘要：{BuildLocationText(platformSite, localProfile)}"
        };

        return new WebhookMessage
        {
            Content = string.Join(Environment.NewLine, lines),
            MentionMobiles = policy.MentionMobiles,
            MentionAll = policy.MentionAll
        };
    }

    private static WebhookMessage BuildRecoveryMessage(
        PlatformSiteSnapshot? platformSite,
        SiteLocalProfile? localProfile,
        string deviceCode,
        DispatchPolicy policy,
        DateTimeOffset recoveredAt,
        string recoverySummary)
    {
        var displayName = platformSite is null
            ? NormalizeText(localProfile?.Alias) ?? deviceCode
            : ResolveDisplayName(platformSite, localProfile);

        return new WebhookMessage
        {
            Content = string.Join(
                Environment.NewLine,
                [
                    $"【恢复通知】{displayName}",
                    $"设备编码：{deviceCode}",
                    $"恢复时间：{FormatDateTime(recoveredAt)}",
                    $"恢复摘要：{recoverySummary}"
                ]),
            MentionMobiles = policy.MentionMobiles,
            MentionAll = policy.MentionAll
        };
    }

    private static string ResolveDisplayName(PlatformSiteSnapshot platformSite, SiteLocalProfile? localProfile)
    {
        return NormalizeText(localProfile?.Alias)
            ?? NormalizeText(platformSite.DeviceName)
            ?? platformSite.DeviceCode;
    }

    private static string ResolveDispatchStatusText(DispatchStatus status, bool isCooling)
    {
        if (isCooling && status == DispatchStatus.Dispatched)
        {
            return "冷却中";
        }

        return status switch
        {
            DispatchStatus.PendingDispatch => "待派单",
            DispatchStatus.Dispatched => "已派单",
            DispatchStatus.SendFailed => "发送失败",
            DispatchStatus.WebhookNotConfigured => "未配置 webhook",
            _ => "未派单"
        };
    }

    private static string BuildMaintenanceText(SiteLocalProfile? localProfile)
    {
        var parts = new[]
        {
            NormalizeText(localProfile?.MaintenanceUnit),
            NormalizeText(localProfile?.MaintainerName),
            NormalizeText(localProfile?.MaintainerPhone)
        }.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();

        return parts.Length == 0 ? "暂无维护信息" : string.Join(" / ", parts);
    }

    private static string BuildLocationText(PlatformSiteSnapshot platformSite, SiteLocalProfile? localProfile)
    {
        var address = NormalizeText(localProfile?.AddressText);
        if (!string.IsNullOrWhiteSpace(address))
        {
            return address;
        }

        if (platformSite.RawLongitude.HasValue && platformSite.RawLatitude.HasValue)
        {
            return $"{platformSite.RawLongitude.Value:F6}, {platformSite.RawLatitude.Value:F6}";
        }

        return "暂无坐标或地址";
    }

    private static string FormatDateTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无";
    }

    private static string ComputeDigest(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record SiteContext(
        PlatformSiteSnapshot? PlatformSite,
        SiteLocalProfile? LocalProfile,
        SiteRuntimeState? RuntimeState);
}
