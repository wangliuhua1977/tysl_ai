using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Dispatch;

public sealed class DispatchWorkflowService : IDispatchService
{
    private readonly IActiveWorkOrderStore activeWorkOrderStore;
    private readonly ILocalDiagnosticService diagnosticService;
    private readonly IDispatchPolicyProvider dispatchPolicyProvider;
    private readonly ISiteLocalProfileRepository localProfileRepository;
    private readonly IPlatformSiteProvider platformSiteProvider;
    private readonly ISiteRuntimeStateRepository runtimeStateRepository;
    private readonly IWebhookNotificationService webhookNotificationService;

    public DispatchWorkflowService(
        IDispatchPolicyProvider dispatchPolicyProvider,
        IActiveWorkOrderStore activeWorkOrderStore,
        ISiteLocalProfileRepository localProfileRepository,
        ISiteRuntimeStateRepository runtimeStateRepository,
        IPlatformSiteProvider platformSiteProvider,
        IWebhookNotificationService webhookNotificationService,
        ILocalDiagnosticService diagnosticService)
    {
        this.dispatchPolicyProvider = dispatchPolicyProvider;
        this.activeWorkOrderStore = activeWorkOrderStore;
        this.localProfileRepository = localProfileRepository;
        this.runtimeStateRepository = runtimeStateRepository;
        this.platformSiteProvider = platformSiteProvider;
        this.webhookNotificationService = webhookNotificationService;
        this.diagnosticService = diagnosticService;
    }

    public async Task ProcessInspectionResultAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken = default)
    {
        if (localProfile?.IsIgnored == true || localProfile?.IsMonitored == false)
        {
            return;
        }

        var currentFaultCode = MapFaultCode(currentState.LastFaultCode);
        var activeWorkOrder = await activeWorkOrderStore.GetLatestOpenByDeviceAsync(
            platformSite.DeviceCode,
            cancellationToken);

        if (currentFaultCode is null)
        {
            if (activeWorkOrder is not null)
            {
                var policy = await dispatchPolicyProvider.GetAsync(cancellationToken);
                await ProcessRecoveredWorkOrderAsync(
                    platformSite,
                    localProfile,
                    currentState,
                    activeWorkOrder,
                    policy,
                    cancellationToken);
            }

            return;
        }

        var dispatchPolicy = await dispatchPolicyProvider.GetAsync(cancellationToken);
        if (!dispatchPolicy.Enabled
            || localProfile?.IsAutoDispatchEnabled != true
            || !HasMinimumDispatchInfo(localProfile))
        {
            return;
        }

        var traceContext = CreateTraceContext("automatic-dispatch", platformSite.DeviceCode);
        var shouldAttemptDispatch = ShouldAttemptAutomaticDispatch(dispatchPolicy, activeWorkOrder);

        try
        {
            if (shouldAttemptDispatch)
            {
                await diagnosticService.WriteAsync(
                    "automatic-dispatch-execute-start",
                    $"deviceCode={platformSite.DeviceCode}, faultCode={currentFaultCode}",
                    cancellationToken);
            }

            var executionResult = await CreateOrUpdateFaultWorkOrderAsync(
                platformSite,
                localProfile,
                currentState,
                activeWorkOrder,
                DispatchSource.Automatic,
                shouldAttemptDispatch,
                traceContext,
                cancellationToken);

            if (shouldAttemptDispatch && executionResult.HasSuccessfulDelivery)
            {
                await diagnosticService.WriteAsync(
                    "automatic-dispatch-success",
                    $"deviceCode={platformSite.DeviceCode}, summary={executionResult.Summary}",
                    cancellationToken);
            }
            else if (shouldAttemptDispatch)
            {
                await diagnosticService.WriteAsync(
                    "automatic-dispatch-failed",
                    $"deviceCode={platformSite.DeviceCode}, stage=send, summary={executionResult.Summary}",
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await diagnosticService.WriteAsync(
                "automatic-dispatch-exception-caught",
                $"deviceCode={platformSite.DeviceCode}, type={ex.GetType().FullName}, message={ex.Message}",
                cancellationToken);
            await diagnosticService.WriteAsync(
                "automatic-dispatch-failed",
                $"deviceCode={platformSite.DeviceCode}, stage={ResolveFailureStage(ex)}, message={ex.Message}",
                cancellationToken);
        }
    }

    public async Task<ManualDispatchPreparation> PrepareManualDispatchAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadSiteContextAsync(deviceCode, cancellationToken);
        ValidateManualDispatchContext(context);

        var traceContext = CreateTraceContext("manual-dispatch", context.PlatformSite!.DeviceCode);
        var plan = await webhookNotificationService.BuildDispatchPlanAsync(
            NotificationTemplateKind.Dispatch,
            BuildDispatchTemplateContext(
                context.PlatformSite!,
                context.LocalProfile!,
                context.RuntimeState!,
                ResolveDispatchDisplayStatus(context.ActiveWorkOrder),
                context.RuntimeState!.LastInspectionAt ?? DateTimeOffset.UtcNow),
            traceContext,
            cancellationToken);

        return new ManualDispatchPreparation
        {
            DeviceCode = context.PlatformSite!.DeviceCode,
            SiteDisplayName = ResolveDisplayName(context.PlatformSite, context.LocalProfile),
            ProductAccessNumber = context.LocalProfile!.ProductAccessNumber,
            FaultReason = ResolveFaultReason(context.RuntimeState!, context.ActiveWorkOrder),
            MaintenanceUnit = context.LocalProfile.MaintenanceUnit,
            MaintainerName = context.LocalProfile.MaintainerName,
            MaintainerPhone = context.LocalProfile.MaintainerPhone,
            TemplateKind = NotificationTemplateKind.Dispatch,
            NotificationPool = plan.Pool,
            EnabledEndpointCount = plan.Endpoints.Count,
            TemplatePreview = CollapsePreview(plan.RenderedContent)
        };
    }

    public async Task ManualDispatchAsync(
        ManualDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await LoadSiteContextAsync(request.DeviceCode, cancellationToken);
        ValidateManualDispatchContext(context);

        var traceContext = CreateTraceContext("manual-dispatch", context.PlatformSite!.DeviceCode);
        var executionResult = await CreateOrUpdateFaultWorkOrderAsync(
            context.PlatformSite!,
            context.LocalProfile!,
            context.RuntimeState!,
            context.ActiveWorkOrder,
            DispatchSource.Manual,
            shouldAttemptDispatch: true,
            traceContext,
            cancellationToken);

        if (!executionResult.HasSuccessfulDelivery)
        {
            await diagnosticService.WriteAsync(
                "manual-dispatch-failed",
                $"deviceCode={context.PlatformSite.DeviceCode}, stage=send, summary={executionResult.Summary}",
                cancellationToken);
            throw CreateStagedException(
                "send",
                "手工派单未发送成功，请检查通知配置或稍后重试。");
        }
    }

    public async Task<CloseWorkOrderPreparation> PrepareCloseWorkOrderAsync(
        long workOrderId,
        CancellationToken cancellationToken = default)
    {
        var workOrder = await activeWorkOrderStore.GetByIdAsync(workOrderId, cancellationToken);
        if (workOrder is null || workOrder.Status != DispatchWorkOrderStatus.RecoveredPendingClose)
        {
            throw new InvalidOperationException("当前工单不处于待归档状态。");
        }

        return new CloseWorkOrderPreparation
        {
            WorkOrderId = workOrder.WorkOrderId,
            DeviceCode = workOrder.DeviceCode,
            SiteDisplayName = ResolveDisplayName(workOrder),
            ProductAccessNumber = workOrder.ProductAccessNumberSnapshot,
            CurrentFaultReason = workOrder.CurrentFaultReason,
            MaintenanceUnit = workOrder.MaintenanceUnitSnapshot,
            MaintainerName = workOrder.MaintainerNameSnapshot,
            MaintainerPhone = workOrder.MaintainerPhoneSnapshot,
            RecoveryStatusText = "待管理员确认归档",
            RecoveredAtText = workOrder.RecoveredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "刚刚恢复",
            LastNotificationSummary = workOrder.LastNotificationSummary
        };
    }

    public async Task CloseWorkOrderAsync(
        CloseWorkOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workOrder = await activeWorkOrderStore.GetByIdAsync(request.WorkOrderId, cancellationToken);
        if (workOrder is null || workOrder.Status == DispatchWorkOrderStatus.ClosedArchived)
        {
            return;
        }

        if (workOrder.Status != DispatchWorkOrderStatus.RecoveredPendingClose)
        {
            throw new InvalidOperationException("当前工单尚未进入待归档状态。");
        }

        var now = DateTimeOffset.UtcNow;
        var closingRemark = NormalizeText(request.ClosingRemark);
        var updated = workOrder with
        {
            Status = DispatchWorkOrderStatus.ClosedArchived,
            RecoveredAt = workOrder.RecoveredAt ?? now,
            RecoveryConfirmedAt = now,
            ClosedArchivedAt = now,
            RecoverySource = RecoverySource.ManualConfirmed,
            RecoverySummary = BuildManualRecoverySummary(closingRemark),
            ClosingRemark = closingRemark,
            UpdatedAt = now
        };

        await activeWorkOrderStore.UpdateAsync(updated, cancellationToken);
    }

    private async Task<DispatchExecutionResult> CreateOrUpdateFaultWorkOrderAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile localProfile,
        SiteRuntimeState runtimeState,
        ActiveWorkOrder? activeWorkOrder,
        DispatchSource dispatchSource,
        bool shouldAttemptDispatch,
        NotificationDispatchTraceContext? traceContext,
        CancellationToken cancellationToken)
    {
        var now = runtimeState.LastInspectionAt ?? DateTimeOffset.UtcNow;
        var faultCode = MapFaultCode(runtimeState.LastFaultCode) ?? nameof(RuntimeFaultCode.InspectionExecutionFailed);
        var faultReason = ResolveFaultReason(runtimeState, activeWorkOrder);

        var workOrder = activeWorkOrder is null
            ? new ActiveWorkOrder
            {
                DeviceCode = platformSite.DeviceCode,
                SiteNameSnapshot = platformSite.DeviceName,
                SiteAliasSnapshot = NormalizeText(localProfile.Alias),
                ProductAccessNumberSnapshot = NormalizeText(localProfile.ProductAccessNumber),
                CurrentFaultCode = faultCode,
                CurrentFaultReason = faultReason,
                DispatchSource = dispatchSource,
                Status = DispatchWorkOrderStatus.Ready,
                LatestExceptionAt = now,
                MaintenanceUnitSnapshot = NormalizeText(localProfile.MaintenanceUnit),
                MaintainerNameSnapshot = NormalizeText(localProfile.MaintainerName),
                MaintainerPhoneSnapshot = NormalizeText(localProfile.MaintainerPhone),
                DispatchRemarkSnapshot = NormalizeText(localProfile.DefaultDispatchRemark),
                RecoveryConfirmationModeSnapshot = localProfile.RecoveryConfirmationMode,
                AllowRecoveryAutoArchiveSnapshot = localProfile.AllowRecoveryAutoArchive,
                ProductStatusSnapshot = null,
                ArrearsAmountSnapshot = null,
                CreatedAt = now,
                UpdatedAt = now
            }
            : activeWorkOrder with
            {
                SiteNameSnapshot = platformSite.DeviceName,
                SiteAliasSnapshot = NormalizeText(localProfile.Alias),
                ProductAccessNumberSnapshot = NormalizeText(localProfile.ProductAccessNumber),
                CurrentFaultCode = faultCode,
                CurrentFaultReason = faultReason,
                DispatchSource = shouldAttemptDispatch ? dispatchSource : activeWorkOrder.DispatchSource,
                LatestExceptionAt = now,
                MaintenanceUnitSnapshot = NormalizeText(localProfile.MaintenanceUnit),
                MaintainerNameSnapshot = NormalizeText(localProfile.MaintainerName),
                MaintainerPhoneSnapshot = NormalizeText(localProfile.MaintainerPhone),
                DispatchRemarkSnapshot = NormalizeText(localProfile.DefaultDispatchRemark),
                RecoveryConfirmationModeSnapshot = localProfile.RecoveryConfirmationMode,
                AllowRecoveryAutoArchiveSnapshot = localProfile.AllowRecoveryAutoArchive,
                LastNotificationSummary = null,
                RecoverySource = null,
                RecoverySummary = null,
                ClosingRemark = null,
                RecoveredAt = null,
                RecoveryConfirmedAt = null,
                ClosedArchivedAt = null,
                UpdatedAt = now
            };

        if (activeWorkOrder is null)
        {
            try
            {
                var persistedId = await activeWorkOrderStore.AddAsync(workOrder, cancellationToken);
                workOrder = workOrder with { WorkOrderId = persistedId };
            }
            catch (Exception ex)
            {
                await LogDispatchPersistenceFailureAsync(traceContext, platformSite.DeviceCode, "work-order-add", ex, cancellationToken);
                throw CreateStagedException("work-order-add", "活动工单写入失败。", ex);
            }
        }

        if (!shouldAttemptDispatch)
        {
            try
            {
                await activeWorkOrderStore.UpdateAsync(
                    workOrder with
                    {
                        Status = DispatchWorkOrderStatus.Active,
                        UpdatedAt = now
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                await LogDispatchPersistenceFailureAsync(traceContext, platformSite.DeviceCode, "work-order-update", ex, cancellationToken);
                throw CreateStagedException("work-order-update", "活动工单更新失败。", ex);
            }

            return new DispatchExecutionResult(true, "工单状态已更新。");
        }

        var dispatchResult = await SendDispatchAsync(
            platformSite,
            localProfile,
            runtimeState,
            workOrder,
            now,
            traceContext,
            cancellationToken);
        workOrder = workOrder with
        {
            DispatchSource = dispatchSource,
            Status = dispatchResult.HasSuccessfulDelivery ? DispatchWorkOrderStatus.Active : DispatchWorkOrderStatus.Ready,
            FirstDispatchedAt = workOrder.FirstDispatchedAt ?? dispatchResult.AttemptedAt,
            LatestNotificationAt = dispatchResult.AttemptedAt,
            LastNotificationSummary = dispatchResult.Summary,
            UpdatedAt = now
        };

        try
        {
            await activeWorkOrderStore.UpdateAsync(workOrder, cancellationToken);
        }
        catch (Exception ex)
        {
            await LogDispatchPersistenceFailureAsync(traceContext, platformSite.DeviceCode, "work-order-update", ex, cancellationToken);
            throw CreateStagedException("work-order-update", "活动工单更新失败。", ex);
        }

        return new DispatchExecutionResult(dispatchResult.HasSuccessfulDelivery, dispatchResult.Summary);
    }

    private async Task ProcessRecoveredWorkOrderAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        ActiveWorkOrder workOrder,
        DispatchPolicy policy,
        CancellationToken cancellationToken)
    {
        if (workOrder.Status is DispatchWorkOrderStatus.RecoveredPendingClose or DispatchWorkOrderStatus.ClosedArchived)
        {
            return;
        }

        var recoveredAt = currentState.LastInspectionAt ?? DateTimeOffset.UtcNow;
        var shouldAutoArchive = ShouldAutoArchive(localProfile, workOrder);
        var recoverySummary = shouldAutoArchive
            ? "系统检测恢复，已自动归档。"
            : "系统检测恢复，等待管理员确认归档。";

        var updated = workOrder with
        {
            Status = shouldAutoArchive ? DispatchWorkOrderStatus.ClosedArchived : DispatchWorkOrderStatus.RecoveredPendingClose,
            RecoveredAt = workOrder.RecoveredAt ?? recoveredAt,
            RecoveryConfirmedAt = shouldAutoArchive ? recoveredAt : null,
            ClosedArchivedAt = shouldAutoArchive ? recoveredAt : null,
            RecoverySource = RecoverySource.SystemDetected,
            RecoverySummary = recoverySummary,
            UpdatedAt = recoveredAt
        };

        var notificationSummary = await SendRecoveryAsync(
            policy,
            platformSite,
            localProfile,
            currentState,
            updated,
            recoveredAt,
            cancellationToken);

        updated = updated with
        {
            LastNotificationSummary = notificationSummary,
            UpdatedAt = recoveredAt
        };

        await activeWorkOrderStore.UpdateAsync(updated, cancellationToken);
    }

    private async Task<DispatchAttemptResult> SendDispatchAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile localProfile,
        SiteRuntimeState runtimeState,
        ActiveWorkOrder workOrder,
        DateTimeOffset attemptedAt,
        NotificationDispatchTraceContext? traceContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var sendResults = await webhookNotificationService.SendAsync(
                NotificationTemplateKind.Dispatch,
                BuildDispatchTemplateContext(
                    platformSite,
                    localProfile,
                    runtimeState,
                    ResolveDispatchDisplayStatus(workOrder),
                    attemptedAt),
                traceContext,
                cancellationToken);

            return BuildNotificationAttemptResult("派单通知", attemptedAt, sendResults);
        }
        catch (Exception ex)
        {
            await diagnosticService.WriteAsync(
                "dispatch-notification-failed",
                $"deviceCode={platformSite.DeviceCode}, type={ex.GetType().FullName}, message={ex.Message}",
                cancellationToken);

            if (traceContext is not null)
            {
                await diagnosticService.WriteAsync(
                    $"{traceContext.EventPrefix}-failed",
                    $"deviceCode={platformSite.DeviceCode}, stage=send, message={ex.Message}",
                    cancellationToken);
            }

            return new DispatchAttemptResult(
                attemptedAt,
                false,
                $"派单通知发送失败：{ex.Message}");
        }
    }

    private async Task<string> SendRecoveryAsync(
        DispatchPolicy policy,
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState runtimeState,
        ActiveWorkOrder workOrder,
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken)
    {
        if (!policy.NotifyOnRecovery)
        {
            return "恢复通知已关闭。";
        }

        try
        {
            var sendResults = await webhookNotificationService.SendAsync(
                NotificationTemplateKind.Recovery,
                BuildRecoveryTemplateContext(
                    platformSite,
                    localProfile,
                    runtimeState,
                    workOrder,
                    recoveredAt,
                    RecoverySource.SystemDetected),
                cancellationToken: cancellationToken);

            return BuildNotificationAttemptResult("恢复通知", recoveredAt, sendResults).Summary;
        }
        catch (Exception ex)
        {
            await diagnosticService.WriteAsync(
                "recovery-notification-failed",
                $"deviceCode={platformSite.DeviceCode}, type={ex.GetType().FullName}, message={ex.Message}",
                cancellationToken);
            return $"恢复通知发送失败：{ex.Message}";
        }
    }

    private async Task<SiteContext> LoadSiteContextAsync(string deviceCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new InvalidOperationException("设备编码不能为空。");
        }

        var normalizedDeviceCode = deviceCode.Trim();
        var platformSites = await platformSiteProvider.ListAsync(cancellationToken);
        var platformSite = platformSites.FirstOrDefault(site => site.DeviceCode.Equals(normalizedDeviceCode, StringComparison.OrdinalIgnoreCase));
        var localProfile = await localProfileRepository.GetByDeviceCodeAsync(normalizedDeviceCode, cancellationToken);
        var runtimeState = await runtimeStateRepository.GetByDeviceCodeAsync(normalizedDeviceCode, cancellationToken);
        var activeWorkOrder = await activeWorkOrderStore.GetLatestOpenByDeviceAsync(normalizedDeviceCode, cancellationToken);
        return new SiteContext(platformSite, localProfile, runtimeState, activeWorkOrder);
    }

    private static void ValidateManualDispatchContext(SiteContext context)
    {
        if (context.PlatformSite is null)
        {
            throw new InvalidOperationException("当前点位不存在或平台未返回设备信息。");
        }

        if (context.LocalProfile is null || context.LocalProfile.IsMonitored == false)
        {
            throw new InvalidOperationException("当前点位未纳管，不能发起手工派单。");
        }

        if (context.LocalProfile.IsIgnored)
        {
            throw new InvalidOperationException("已忽略点位不能发起手工派单。");
        }

        if (!HasMinimumDispatchInfo(context.LocalProfile))
        {
            throw new InvalidOperationException("当前点位维护信息不完整，至少需要维护单位、维护人、联系电话和产品接入号。");
        }

        if (context.RuntimeState is null || MapFaultCode(context.RuntimeState.LastFaultCode) is null)
        {
            throw new InvalidOperationException("当前点位未处于异常状态，无法发起手工派单。");
        }

        if (context.ActiveWorkOrder is not null && context.ActiveWorkOrder.Status == DispatchWorkOrderStatus.Active)
        {
            throw new InvalidOperationException("当前点位已有活动工单，无需重复手工派单。");
        }
    }

    private static bool HasMinimumDispatchInfo(SiteLocalProfile? profile)
    {
        return !string.IsNullOrWhiteSpace(profile?.MaintenanceUnit)
            && !string.IsNullOrWhiteSpace(profile?.MaintainerName)
            && !string.IsNullOrWhiteSpace(profile?.MaintainerPhone)
            && !string.IsNullOrWhiteSpace(profile?.ProductAccessNumber);
    }

    private static bool ShouldAttemptAutomaticDispatch(
        DispatchPolicy policy,
        ActiveWorkOrder? activeWorkOrder)
    {
        if (activeWorkOrder is null)
        {
            return true;
        }

        if (activeWorkOrder.Status == DispatchWorkOrderStatus.Active)
        {
            return false;
        }

        if (activeWorkOrder.Status == DispatchWorkOrderStatus.RecoveredPendingClose)
        {
            return true;
        }

        if (!activeWorkOrder.LatestNotificationAt.HasValue)
        {
            return true;
        }

        var nextAllowedAttempt = activeWorkOrder.LatestNotificationAt.Value.AddMinutes(Math.Max(1, policy.CoolingMinutes));
        return nextAllowedAttempt <= DateTimeOffset.UtcNow;
    }

    private static bool ShouldAutoArchive(SiteLocalProfile? localProfile, ActiveWorkOrder workOrder)
    {
        var allowRecoveryAutoArchive = localProfile?.AllowRecoveryAutoArchive ?? workOrder.AllowRecoveryAutoArchiveSnapshot;
        var confirmationMode = localProfile?.RecoveryConfirmationMode ?? workOrder.RecoveryConfirmationModeSnapshot;
        return allowRecoveryAutoArchive && confirmationMode != RecoveryConfirmationMode.ManualOnly;
    }

    private static NotificationTemplateRenderContext BuildDispatchTemplateContext(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile localProfile,
        SiteRuntimeState runtimeState,
        string status,
        DateTimeOffset dispatchTime)
    {
        return new NotificationTemplateRenderContext
        {
            DeviceCode = platformSite.DeviceCode,
            DeviceName = platformSite.DeviceName,
            Alias = NormalizeText(localProfile.Alias) ?? platformSite.DeviceName,
            ProductAccessNumber = NormalizeText(localProfile.ProductAccessNumber),
            Status = status,
            FaultReason = ResolveFaultReason(runtimeState, null),
            DispatchTime = dispatchTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            MaintenanceUnit = NormalizeText(localProfile.MaintenanceUnit),
            MaintainerName = NormalizeText(localProfile.MaintainerName),
            MaintainerPhone = NormalizeText(localProfile.MaintainerPhone),
            Remark = NormalizeText(localProfile.DefaultDispatchRemark)
        };
    }

    private static NotificationTemplateRenderContext BuildRecoveryTemplateContext(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState runtimeState,
        ActiveWorkOrder workOrder,
        DateTimeOffset recoveredAt,
        RecoverySource recoverySource)
    {
        return new NotificationTemplateRenderContext
        {
            DeviceCode = platformSite.DeviceCode,
            DeviceName = platformSite.DeviceName,
            Alias = NormalizeText(localProfile?.Alias)
                ?? NormalizeText(workOrder.SiteAliasSnapshot)
                ?? platformSite.DeviceName,
            ProductAccessNumber = NormalizeText(localProfile?.ProductAccessNumber)
                ?? NormalizeText(workOrder.ProductAccessNumberSnapshot),
            Status = workOrder.Status == DispatchWorkOrderStatus.ClosedArchived ? "已归档" : "待管理员确认",
            FaultReason = NormalizeText(workOrder.CurrentFaultReason) ?? ResolveFaultReason(runtimeState, workOrder),
            RecoveryTime = recoveredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            RecoveryMethod = recoverySource == RecoverySource.SystemDetected ? "系统检测" : "管理员确认",
            MaintenanceUnit = NormalizeText(localProfile?.MaintenanceUnit)
                ?? NormalizeText(workOrder.MaintenanceUnitSnapshot),
            MaintainerName = NormalizeText(localProfile?.MaintainerName)
                ?? NormalizeText(workOrder.MaintainerNameSnapshot),
            MaintainerPhone = NormalizeText(localProfile?.MaintainerPhone)
                ?? NormalizeText(workOrder.MaintainerPhoneSnapshot),
            Remark = NormalizeText(localProfile?.DefaultDispatchRemark)
                ?? NormalizeText(workOrder.DispatchRemarkSnapshot),
            ClosingRemark = NormalizeText(workOrder.ClosingRemark)
                ?? NormalizeText(workOrder.RecoverySummary)
        };
    }

    private static DispatchAttemptResult BuildNotificationAttemptResult(
        string label,
        DateTimeOffset attemptedAt,
        IReadOnlyList<WebhookSendResult> sendResults)
    {
        if (sendResults.Count == 0)
        {
            return new DispatchAttemptResult(
                attemptedAt,
                false,
                $"{label}池无启用地址，状态已落地。");
        }

        var successCount = sendResults.Count(result => result.IsSuccess);
        var failureCount = sendResults.Count - successCount;
        if (successCount > 0)
        {
            return new DispatchAttemptResult(
                attemptedAt,
                true,
                $"{label}已发送：成功 {successCount}，失败 {failureCount}。");
        }

        var firstError = sendResults.FirstOrDefault(result => !result.IsSuccess)?.ErrorMessage;
        var errorSuffix = string.IsNullOrWhiteSpace(firstError) ? string.Empty : $" 首次失败：{firstError}";
        return new DispatchAttemptResult(
            attemptedAt,
            false,
            $"{label}发送失败：成功 0，失败 {failureCount}。{errorSuffix}".Trim());
    }

    private static string ResolveFaultReason(SiteRuntimeState runtimeState, ActiveWorkOrder? activeWorkOrder)
    {
        return NormalizeText(runtimeState.LastFaultSummary)
            ?? NormalizeText(activeWorkOrder?.CurrentFaultReason)
            ?? ResolveFaultLabel(MapFaultCode(runtimeState.LastFaultCode) ?? nameof(RuntimeFaultCode.InspectionExecutionFailed));
    }

    private static string ResolveDispatchDisplayStatus(ActiveWorkOrder? activeWorkOrder)
    {
        if (activeWorkOrder is null)
        {
            return "待派单";
        }

        return activeWorkOrder.Status switch
        {
            DispatchWorkOrderStatus.Ready => "待派单",
            DispatchWorkOrderStatus.RecoveredPendingClose => "待恢复确认",
            DispatchWorkOrderStatus.ClosedArchived => "已归档",
            _ => "已派单"
        };
    }

    private static string ResolveDisplayName(PlatformSiteSnapshot platformSite, SiteLocalProfile? localProfile)
    {
        return NormalizeText(localProfile?.Alias)
            ?? NormalizeText(platformSite.DeviceName)
            ?? platformSite.DeviceCode;
    }

    private static string ResolveDisplayName(ActiveWorkOrder workOrder)
    {
        return NormalizeText(workOrder.SiteAliasSnapshot)
            ?? NormalizeText(workOrder.SiteNameSnapshot)
            ?? workOrder.DeviceCode;
    }

    private static string BuildManualRecoverySummary(string? closingRemark)
    {
        return string.IsNullOrWhiteSpace(closingRemark)
            ? "管理员确认已处理并归档。"
            : $"管理员确认已处理并归档：{closingRemark}";
    }

    private static string CollapsePreview(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return normalized.Length <= 280 ? normalized : $"{normalized[..280]}...";
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

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task LogDispatchPersistenceFailureAsync(
        NotificationDispatchTraceContext? traceContext,
        string deviceCode,
        string stage,
        Exception ex,
        CancellationToken cancellationToken)
    {
        if (traceContext is null)
        {
            return;
        }

        await diagnosticService.WriteAsync(
            $"{traceContext.EventPrefix}-failed",
            $"deviceCode={deviceCode}, stage={stage}, type={ex.GetType().FullName}, message={ex.Message}",
            cancellationToken);
    }

    private static NotificationDispatchTraceContext CreateTraceContext(string eventPrefix, string deviceCode)
    {
        return new NotificationDispatchTraceContext
        {
            EventPrefix = eventPrefix,
            DeviceCode = deviceCode
        };
    }

    private static string ResolveFailureStage(Exception exception)
    {
        return exception.Data["dispatch-stage"] as string ?? "unknown";
    }

    private static Exception CreateStagedException(string stage, string message, Exception? innerException = null)
    {
        var exception = innerException is null
            ? new InvalidOperationException(message)
            : new InvalidOperationException(message, innerException);
        exception.Data["dispatch-stage"] = stage;
        return exception;
    }

    private sealed record SiteContext(
        PlatformSiteSnapshot? PlatformSite,
        SiteLocalProfile? LocalProfile,
        SiteRuntimeState? RuntimeState,
        ActiveWorkOrder? ActiveWorkOrder);

    private sealed record DispatchAttemptResult(
        DateTimeOffset AttemptedAt,
        bool HasSuccessfulDelivery,
        string Summary);

    private sealed record DispatchExecutionResult(
        bool HasSuccessfulDelivery,
        string Summary);
}
