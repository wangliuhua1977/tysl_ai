using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Map;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SiteMapQueryService : ISiteMapQueryService
{
    private static readonly TimeSpan RecoveredHighlightWindow = TimeSpan.FromMinutes(45);

    private readonly IDispatchRecordRepository dispatchRecordRepository;
    private readonly ISiteLocalProfileRepository localProfileRepository;
    private readonly IPlatformConnectionStateProvider platformConnectionStateProvider;
    private readonly IPlatformSiteProvider platformSiteProvider;
    private readonly ISiteRuntimeStateRepository runtimeStateRepository;

    public SiteMapQueryService(
        IPlatformSiteProvider platformSiteProvider,
        IPlatformConnectionStateProvider platformConnectionStateProvider,
        ISiteLocalProfileRepository localProfileRepository,
        ISiteRuntimeStateRepository runtimeStateRepository,
        IDispatchRecordRepository dispatchRecordRepository)
    {
        this.platformSiteProvider = platformSiteProvider;
        this.platformConnectionStateProvider = platformConnectionStateProvider;
        this.localProfileRepository = localProfileRepository;
        this.runtimeStateRepository = runtimeStateRepository;
        this.dispatchRecordRepository = dispatchRecordRepository;
    }

    public async Task<SiteDashboardSnapshot> GetDashboardAsync(
        SiteDashboardFilter filter,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var mergeBundle = await BuildMergedSitesAsync(cancellationToken);
        var filteredSites = mergeBundle.Sites
            .Where(site => MatchesFilter(site, filter))
            .Where(site => MatchesSearch(site, searchText))
            .ToList();
        var visibleMapSites = filteredSites
            .Where(site => site.HasMapPoint)
            .ToList();

        return new SiteDashboardSnapshot
        {
            PlatformStatusText = mergeBundle.ConnectionState.SummaryText,
            PlatformStatusDetailText = mergeBundle.ConnectionState.DetailText,
            IsPlatformConnected = mergeBundle.ConnectionState.IsConnected,
            PointCount = mergeBundle.Sites.Count,
            MonitoredCount = mergeBundle.Sites.Count(site => site.IsMonitored),
            FaultCount = mergeBundle.Sites.Count(IsAttentionSite),
            DispatchedCount = mergeBundle.Sites.Count(HasActiveDispatch),
            CoverageSummary = new MapCoverageSummary
            {
                TotalPointCount = mergeBundle.Sites.Count,
                MappedPointCount = mergeBundle.Sites.Count(site => site.HasMapPoint),
                UnmappedPointCount = mergeBundle.Sites.Count(site => !site.HasMapPoint),
                FilteredPointCount = filteredSites.Count,
                CurrentVisiblePointCount = visibleMapSites.Count,
                FilteredUnmappedPointCount = filteredSites.Count(site => !site.HasMapPoint)
            },
            LastRefreshedAt = DateTimeOffset.Now,
            VisiblePoints = visibleMapSites
                .Select(ToMapPoint)
                .ToList(),
            VisibleAlerts = filteredSites
                .Where(IsAttentionSite)
                .OrderByDescending(GetAlertPriority)
                .ThenByDescending(GetAlertTimestamp)
                .Select(ToAlertDigest)
                .ToList(),
            UnmappedPoints = mergeBundle.Sites
                .Where(site => !site.HasMapPoint)
                .OrderBy(GetUnmappedPriority)
                .ThenBy(site => site.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(ToUnmappedDigest)
                .ToList()
        };
    }

    public async Task<SiteMergedView?> GetSiteDetailAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return null;
        }

        var normalizedDeviceCode = deviceCode.Trim();
        var mergeBundle = await BuildMergedSitesAsync(cancellationToken);

        return mergeBundle.Sites.FirstOrDefault(site => site.DeviceCode.Equals(
            normalizedDeviceCode,
            StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SiteMergeBundle> BuildMergedSitesAsync(CancellationToken cancellationToken)
    {
        var platformSites = await platformSiteProvider.ListAsync(cancellationToken);
        var platformConnectionState = platformConnectionStateProvider.GetCurrentState();
        var localProfiles = await localProfileRepository.ListAsync(cancellationToken);
        var runtimeStates = await runtimeStateRepository.ListAsync(cancellationToken);
        var dispatchRecords = await dispatchRecordRepository.ListLatestAsync(cancellationToken);

        var localProfileMap = localProfiles.ToDictionary(
            profile => profile.DeviceCode,
            profile => profile,
            StringComparer.OrdinalIgnoreCase);
        var runtimeStateMap = runtimeStates.ToDictionary(
            state => state.DeviceCode,
            state => state,
            StringComparer.OrdinalIgnoreCase);
        var dispatchRecordMap = dispatchRecords.ToDictionary(
            record => record.DeviceCode,
            record => record,
            StringComparer.OrdinalIgnoreCase);

        var mergedSites = platformSites
            .Select(platformSite =>
            {
                localProfileMap.TryGetValue(platformSite.DeviceCode, out var localProfile);
                runtimeStateMap.TryGetValue(platformSite.DeviceCode, out var runtimeState);
                dispatchRecordMap.TryGetValue(platformSite.DeviceCode, out var dispatchRecord);
                return Merge(platformSite, localProfile, runtimeState, dispatchRecord, platformConnectionState);
            })
            .ToList();

        return new SiteMergeBundle(mergedSites, platformConnectionState);
    }

    private static SiteMergedView Merge(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState? runtimeState,
        DispatchRecord? dispatchRecord,
        PlatformConnectionState platformConnectionState)
    {
        var coordinateGovernance = ResolveCoordinateGovernance(platformSite, localProfile);
        var isMonitored = localProfile?.IsMonitored ?? true;
        var dispatchPresentation = ResolveDispatchPresentation(dispatchRecord, runtimeState);

        return new SiteMergedView
        {
            DeviceCode = platformSite.DeviceCode,
            DeviceName = platformSite.DeviceName,
            DisplayName = ResolveDisplayName(platformSite, localProfile),
            Alias = localProfile?.Alias,
            Remark = localProfile?.Remark,
            IsMonitored = isMonitored,
            PlatformRawLongitude = platformSite.RawLongitude,
            PlatformRawLatitude = platformSite.RawLatitude,
            PlatformRawCoordinateType = platformSite.RawCoordinateType,
            IsPlatformCoordinateEnrichedFromDetail = platformSite.IsCoordinateEnrichedFromDetail,
            ManualLongitude = localProfile?.ManualLongitude,
            ManualLatitude = localProfile?.ManualLatitude,
            DisplayLongitude = coordinateGovernance.DisplayLongitude,
            DisplayLatitude = coordinateGovernance.DisplayLatitude,
            CoordinateDisplayStatus = coordinateGovernance.DisplayStatus,
            HasMapPoint = coordinateGovernance.HasMapPoint,
            HasDisplayCoordinate = coordinateGovernance.HasDisplayCoordinate,
            UnmappedReason = coordinateGovernance.UnmappedReason,
            UnmappedReasonText = ResolveUnmappedReasonText(coordinateGovernance.UnmappedReason),
            CoordinateDisplayStatusText = ResolveCoordinateDisplayStatusText(coordinateGovernance.DisplayStatus),
            CoordinateGovernanceHintText = ResolveCoordinateGovernanceHintText(coordinateGovernance.UnmappedReason),
            CoordinateSource = coordinateGovernance.CoordinateSource,
            CoordinateSourceText = ResolveCoordinateSourceText(coordinateGovernance.CoordinateSource, platformSite.IsCoordinateEnrichedFromDetail),
            PlatformStatusSummary = platformConnectionState.SummaryText,
            HasRuntimeState = runtimeState is not null,
            LastInspectionAt = runtimeState?.LastInspectionAt,
            LastProductState = runtimeState?.LastProductState,
            LastPreviewResolveState = runtimeState?.LastPreviewResolveState ?? PreviewResolveState.Unknown,
            LastSnapshotPath = runtimeState?.LastSnapshotPath,
            LastSnapshotAt = runtimeState?.LastSnapshotAt,
            RuntimeFaultCode = runtimeState?.LastFaultCode ?? RuntimeFaultCode.None,
            RuntimeSummary = runtimeState?.LastFaultSummary,
            ConsecutiveFailureCount = runtimeState?.ConsecutiveFailureCount ?? 0,
            LastInspectionRunState = runtimeState?.LastInspectionRunState ?? InspectionRunState.None,
            RuntimeUpdatedAt = runtimeState?.UpdatedAt,
            DispatchRecordId = dispatchRecord?.Id,
            HasDispatchRecord = dispatchRecord is not null,
            DispatchFaultCode = dispatchRecord?.FaultCode,
            DispatchFaultSummary = dispatchRecord?.FaultSummary,
            DispatchStatus = dispatchRecord?.DispatchStatus ?? DispatchStatus.None,
            DispatchMode = dispatchRecord?.DispatchMode ?? DispatchMode.Automatic,
            DispatchTriggeredAt = dispatchRecord?.TriggeredAt,
            DispatchSentAt = dispatchRecord?.SentAt,
            CoolingUntil = dispatchRecord?.CoolingUntil,
            RecoveryMode = dispatchRecord?.RecoveryMode ?? RecoveryMode.Automatic,
            RecoveryStatus = dispatchRecord?.RecoveryStatus ?? RecoveryStatus.None,
            RecoveredAt = dispatchRecord?.RecoveredAt,
            RecoverySummary = dispatchRecord?.RecoverySummary,
            DispatchMessageDigest = dispatchRecord?.MessageDigest,
            IsDispatchCooling = dispatchPresentation.IsCooling,
            CanConfirmRecovery = dispatchPresentation.CanConfirmRecovery,
            DispatchStatusText = dispatchPresentation.DispatchStatusText,
            RecoveryStatusText = dispatchPresentation.RecoveryStatusText,
            AddressText = localProfile?.AddressText,
            ProductAccessNumber = localProfile?.ProductAccessNumber,
            MaintenanceUnit = localProfile?.MaintenanceUnit,
            MaintainerName = localProfile?.MaintainerName,
            MaintainerPhone = localProfile?.MaintainerPhone,
            DemoOnlineState = runtimeState?.LastOnlineState ?? platformSite.DemoOnlineState,
            DemoStatus = platformSite.DemoStatus,
            DemoDispatchStatus = platformSite.DemoDispatchStatus,
            VisualState = ResolveVisualState(platformSite, runtimeState, dispatchPresentation, isMonitored),
            StatusText = ResolveStatusText(platformSite, runtimeState, dispatchPresentation, isMonitored),
            HasLocalProfile = localProfile is not null,
            CreatedAt = localProfile?.CreatedAt,
            UpdatedAt = localProfile?.UpdatedAt
        };
    }

    private static SiteMapPoint ToMapPoint(SiteMergedView site)
    {
        return new SiteMapPoint
        {
            DeviceCode = site.DeviceCode,
            DeviceName = site.DeviceName,
            DisplayName = site.DisplayName,
            Alias = site.Alias,
            AddressText = site.AddressText,
            MaintenanceUnit = site.MaintenanceUnit,
            MaintainerName = site.MaintainerName,
            MaintainerPhone = site.MaintainerPhone,
            IsMonitored = site.IsMonitored,
            CoordinatePayload = new MapCoordinatePayload
            {
                PlatformRawLongitude = site.PlatformRawLongitude,
                PlatformRawLatitude = site.PlatformRawLatitude,
                RawCoordinateType = site.PlatformRawCoordinateType,
                ManualLongitude = site.ManualLongitude,
                ManualLatitude = site.ManualLatitude,
                CoordinateDisplayStatus = site.CoordinateDisplayStatus,
                UnmappedReason = site.UnmappedReason,
                CoordinateSource = site.CoordinateSource,
                CoordinateSourceText = site.CoordinateSourceText
            },
            DisplayLongitude = site.DisplayLongitude,
            DisplayLatitude = site.DisplayLatitude,
            CoordinateDisplayStatus = site.CoordinateDisplayStatus,
            UnmappedReason = site.UnmappedReason,
            DemoOnlineState = site.DemoOnlineState,
            DemoStatus = site.DemoStatus,
            DemoDispatchStatus = site.DemoDispatchStatus,
            VisualState = site.VisualState,
            StatusText = site.StatusText,
            RuntimeSummaryText = ResolveRuntimeSummary(site),
            LastInspectionAt = site.LastInspectionAt,
            LastSnapshotPath = site.LastSnapshotPath,
            LastSnapshotAt = site.LastSnapshotAt,
            RuntimeFaultCode = site.RuntimeFaultCode,
            DispatchStatus = site.DispatchStatus,
            RecoveryStatus = site.RecoveryStatus,
            IsDispatchCooling = site.IsDispatchCooling,
            DispatchStateKey = ResolveDispatchStateKey(site),
            DispatchStateText = ResolveDispatchStateText(site)
        };
    }

    private static SiteAlertDigest ToAlertDigest(SiteMergedView site)
    {
        return new SiteAlertDigest
        {
            PointId = site.DeviceCode,
            PointDisplayName = site.DisplayName,
            IssueLabel = ResolveAlertLabel(site),
            OccurredAtText = (site.RecoveredAt ?? site.DispatchSentAt ?? site.DispatchTriggeredAt ?? site.LastInspectionAt ?? site.RuntimeUpdatedAt ?? site.UpdatedAt)
                ?.ToLocalTime()
                .ToString("HH:mm") ?? "--:--",
            RuntimeSummary = ResolveRuntimeSummary(site),
            SnapshotPath = site.LastSnapshotPath
        };
    }

    private static UnmappedPointDigest ToUnmappedDigest(SiteMergedView site)
    {
        return new UnmappedPointDigest
        {
            DeviceCode = site.DeviceCode,
            DisplayName = site.DisplayName,
            DeviceName = site.DeviceName,
            IsMonitored = site.IsMonitored,
            UnmappedReason = site.UnmappedReason,
            UnmappedReasonText = site.UnmappedReasonText,
            CoordinateSourceText = site.CoordinateSourceText,
            GovernanceHintText = site.CoordinateGovernanceHintText,
            PlatformCoordinateText = FormatCoordinate(site.PlatformRawLongitude, site.PlatformRawLatitude),
            PlatformCoordinateTypeText = CoordinateTypeCatalog.GetDisplayLabel(site.PlatformRawCoordinateType),
            ManualCoordinateText = FormatCoordinate(site.ManualLongitude, site.ManualLatitude)
        };
    }

    private static bool MatchesFilter(SiteMergedView site, SiteDashboardFilter filter)
    {
        return filter switch
        {
            SiteDashboardFilter.Fault => IsAttentionSite(site),
            SiteDashboardFilter.Monitored => site.IsMonitored,
            SiteDashboardFilter.Disposed => site.HasDispatchRecord,
            SiteDashboardFilter.Unmapped => !site.HasMapPoint,
            _ => true
        };
    }

    private static bool MatchesSearch(SiteMergedView site, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var term = searchText.Trim();

        return site.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.DeviceName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.DeviceCode.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.UnmappedReasonText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.CoordinateGovernanceHintText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (site.AddressText?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.MaintenanceUnit?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.MaintainerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.RuntimeSummary?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || site.DispatchStatusText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.RecoveryStatusText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (site.DispatchFaultSummary?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.RecoverySummary?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsAttentionSite(SiteMergedView site)
    {
        if (!site.IsMonitored)
        {
            return false;
        }

        if (site.CanConfirmRecovery || HasActiveDispatch(site))
        {
            return true;
        }

        if (IsRecentlyRecovered(site))
        {
            return true;
        }

        if (site.RuntimeFaultCode != RuntimeFaultCode.None)
        {
            return true;
        }

        if (site.LastInspectionRunState == InspectionRunState.Failed)
        {
            return true;
        }

        return site.DemoOnlineState == DemoOnlineState.Offline
            || site.DemoDispatchStatus != DispatchDemoStatus.None
            || site.DemoStatus != PointDemoStatus.Normal;
    }

    private static bool HasActiveDispatch(SiteMergedView site)
    {
        return site.HasDispatchRecord
            && !site.RecoveredAt.HasValue
            && site.DispatchStatus != DispatchStatus.None;
    }

    private static bool IsRecentlyRecovered(SiteMergedView site)
    {
        return site.RecoveredAt is DateTimeOffset recoveredAt
            && recoveredAt >= DateTimeOffset.UtcNow.Subtract(RecoveredHighlightWindow);
    }

    private static int GetAlertPriority(SiteMergedView site)
    {
        if (site.CanConfirmRecovery)
        {
            return 7;
        }

        if (HasActiveDispatch(site))
        {
            return site.RuntimeFaultCode switch
            {
                RuntimeFaultCode.Offline => 6,
                RuntimeFaultCode.InspectionExecutionFailed => 5,
                RuntimeFaultCode.PreviewResolveFailed => 4,
                RuntimeFaultCode.SnapshotFailed => 3,
                _ => 2
            };
        }

        if (IsRecentlyRecovered(site))
        {
            return 1;
        }

        return site.RuntimeFaultCode switch
        {
            RuntimeFaultCode.Offline => 5,
            RuntimeFaultCode.InspectionExecutionFailed => 4,
            RuntimeFaultCode.PreviewResolveFailed => 3,
            RuntimeFaultCode.SnapshotFailed => 2,
            _ => site.LastInspectionRunState == InspectionRunState.Failed ? 1 : 0
        };
    }

    private static DateTimeOffset GetAlertTimestamp(SiteMergedView site)
    {
        return site.RecoveredAt
            ?? site.DispatchSentAt
            ?? site.DispatchTriggeredAt
            ?? site.LastInspectionAt
            ?? site.RuntimeUpdatedAt
            ?? site.UpdatedAt
            ?? DateTimeOffset.MinValue;
    }

    private static int GetUnmappedPriority(SiteMergedView site)
    {
        return site.UnmappedReason switch
        {
            UnmappedReason.ManualCoordinateIncomplete => 1,
            UnmappedReason.PlatformCoordinateTypeUnrecognized => 2,
            UnmappedReason.PlatformCoordinateIncomplete => 3,
            UnmappedReason.MissingPlatformCoordinate => 4,
            _ => 9
        };
    }

    private static string ResolveDisplayName(PlatformSiteSnapshot platformSite, SiteLocalProfile? localProfile)
    {
        return string.IsNullOrWhiteSpace(localProfile?.Alias)
            ? platformSite.DeviceName
            : localProfile.Alias.Trim();
    }

    private static CoordinateGovernance ResolveCoordinateGovernance(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile)
    {
        var hasManualPair = CoordinateTypeCatalog.HasCompleteCoordinate(localProfile?.ManualLongitude, localProfile?.ManualLatitude);
        var hasManualPartial = CoordinateTypeCatalog.HasPartialCoordinate(localProfile?.ManualLongitude, localProfile?.ManualLatitude);
        var hasPlatformPair = CoordinateTypeCatalog.HasCompleteCoordinate(platformSite.RawLongitude, platformSite.RawLatitude);
        var hasPlatformPartial = CoordinateTypeCatalog.HasPartialCoordinate(platformSite.RawLongitude, platformSite.RawLatitude);
        var normalizedType = CoordinateTypeCatalog.Normalize(platformSite.RawCoordinateType);

        if (hasManualPair)
        {
            return new CoordinateGovernance(
                CoordinateSource.ManualOverride,
                CoordinateDisplayStatus.Ready,
                true,
                true,
                UnmappedReason.None,
                localProfile!.ManualLongitude,
                localProfile.ManualLatitude);
        }

        if (hasPlatformPair && CoordinateTypeCatalog.IsDirectDisplayType(normalizedType))
        {
            return new CoordinateGovernance(
                CoordinateSource.PlatformRaw,
                CoordinateDisplayStatus.Ready,
                true,
                true,
                UnmappedReason.None,
                platformSite.RawLongitude,
                platformSite.RawLatitude);
        }

        if (hasPlatformPair && CoordinateTypeCatalog.RequiresMapHostConversion(normalizedType))
        {
            return new CoordinateGovernance(
                CoordinateSource.PlatformRaw,
                CoordinateDisplayStatus.RequiresMapHostConversion,
                true,
                false,
                UnmappedReason.None,
                null,
                null);
        }

        if (hasManualPartial)
        {
            return new CoordinateGovernance(
                CoordinateSource.None,
                CoordinateDisplayStatus.Unmapped,
                false,
                false,
                UnmappedReason.ManualCoordinateIncomplete,
                null,
                null);
        }

        if (hasPlatformPair)
        {
            return new CoordinateGovernance(
                CoordinateSource.None,
                CoordinateDisplayStatus.Unmapped,
                false,
                false,
                UnmappedReason.PlatformCoordinateTypeUnrecognized,
                null,
                null);
        }

        if (hasPlatformPartial)
        {
            return new CoordinateGovernance(
                CoordinateSource.None,
                CoordinateDisplayStatus.Unmapped,
                false,
                false,
                UnmappedReason.PlatformCoordinateIncomplete,
                null,
                null);
        }

        return new CoordinateGovernance(
            CoordinateSource.None,
            CoordinateDisplayStatus.Unmapped,
            false,
            false,
            UnmappedReason.MissingPlatformCoordinate,
            null,
            null);
    }

    private static string ResolveCoordinateSourceText(CoordinateSource coordinateSource, bool isCoordinateEnrichedFromDetail)
    {
        return coordinateSource switch
        {
            CoordinateSource.PlatformRaw when isCoordinateEnrichedFromDetail => "平台原始坐标（详情补全）",
            CoordinateSource.PlatformRaw => "平台原始坐标",
            CoordinateSource.ManualOverride => "本地手工坐标",
            _ => "暂无可用显示坐标"
        };
    }

    private static string ResolveCoordinateDisplayStatusText(CoordinateDisplayStatus displayStatus)
    {
        return displayStatus switch
        {
            CoordinateDisplayStatus.Ready => "已具备显示坐标",
            CoordinateDisplayStatus.RequiresMapHostConversion => "地图宿主转换后可显示",
            _ => "未落图"
        };
    }

    private static string ResolveUnmappedReasonText(UnmappedReason unmappedReason)
    {
        return unmappedReason switch
        {
            UnmappedReason.MissingPlatformCoordinate => "平台未提供坐标，且尚未补录手工坐标",
            UnmappedReason.PlatformCoordinateIncomplete => "平台坐标不完整，且尚未补录手工坐标",
            UnmappedReason.PlatformCoordinateTypeUnrecognized => "平台原始坐标类型无法识别，且尚未补录手工坐标",
            UnmappedReason.ManualCoordinateIncomplete => "手工坐标未补全，暂时无法落图",
            _ => "当前已落图"
        };
    }

    private static string ResolveCoordinateGovernanceHintText(UnmappedReason unmappedReason)
    {
        return unmappedReason switch
        {
            UnmappedReason.PlatformCoordinateTypeUnrecognized => "请确认平台坐标类型，或直接通过“编辑补充信息”补录手工坐标。",
            UnmappedReason.ManualCoordinateIncomplete => "请补全手工经纬度后保存，点位才会重新落图。",
            UnmappedReason.None => "当前显示坐标有效，无需补录。",
            _ => "请通过“编辑补充信息”补录手工坐标后再落图。"
        };
    }

    private static SiteVisualState ResolveVisualState(
        PlatformSiteSnapshot platformSite,
        SiteRuntimeState? runtimeState,
        DispatchPresentation dispatchPresentation,
        bool isMonitored)
    {
        if (!isMonitored)
        {
            return SiteVisualState.Unmonitored;
        }

        if (dispatchPresentation.CanConfirmRecovery)
        {
            return SiteVisualState.PendingRecovery;
        }

        if (dispatchPresentation.IsRecovered)
        {
            return SiteVisualState.Recovered;
        }

        if (dispatchPresentation.HasActiveDispatch)
        {
            if (dispatchPresentation.IsCooling)
            {
                return SiteVisualState.Cooling;
            }

            return dispatchPresentation.DispatchStatus switch
            {
                DispatchStatus.PendingDispatch => SiteVisualState.PendingDispatch,
                DispatchStatus.SendFailed => SiteVisualState.PendingDispatch,
                DispatchStatus.WebhookNotConfigured => SiteVisualState.PendingDispatch,
                DispatchStatus.Dispatched => SiteVisualState.Dispatched,
                _ => SiteVisualState.Warning
            };
        }

        if (runtimeState is not null)
        {
            if (runtimeState.LastFaultCode == RuntimeFaultCode.Offline)
            {
                return SiteVisualState.Offline;
            }

            if (runtimeState.LastInspectionRunState == InspectionRunState.Failed
                || runtimeState.LastFaultCode == RuntimeFaultCode.InspectionExecutionFailed)
            {
                return SiteVisualState.Fault;
            }

            if (runtimeState.LastFaultCode is RuntimeFaultCode.PreviewResolveFailed or RuntimeFaultCode.SnapshotFailed)
            {
                return runtimeState.ConsecutiveFailureCount >= 3
                    ? SiteVisualState.Fault
                    : SiteVisualState.Warning;
            }
        }

        if (platformSite.DemoOnlineState == DemoOnlineState.Offline)
        {
            return SiteVisualState.Offline;
        }

        return platformSite.DemoDispatchStatus switch
        {
            DispatchDemoStatus.Dispatched => SiteVisualState.Dispatched,
            DispatchDemoStatus.Cooling => SiteVisualState.Cooling,
            _ => platformSite.DemoStatus switch
            {
                PointDemoStatus.Fault => SiteVisualState.Fault,
                PointDemoStatus.Warning => SiteVisualState.Warning,
                PointDemoStatus.Idle => SiteVisualState.Idle,
                _ => SiteVisualState.Normal
            }
        };
    }

    private static string ResolveStatusText(
        PlatformSiteSnapshot platformSite,
        SiteRuntimeState? runtimeState,
        DispatchPresentation dispatchPresentation,
        bool isMonitored)
    {
        if (!isMonitored)
        {
            return "未纳入监测";
        }

        if (dispatchPresentation.CanConfirmRecovery)
        {
            return "待恢复确认";
        }

        if (dispatchPresentation.IsRecovered)
        {
            return "已恢复";
        }

        if (dispatchPresentation.HasActiveDispatch)
        {
            return dispatchPresentation.DispatchStatusText;
        }

        if (runtimeState is not null)
        {
            return runtimeState.LastFaultCode switch
            {
                RuntimeFaultCode.Offline => "设备离线",
                RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
                RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
                RuntimeFaultCode.InspectionExecutionFailed => "巡检失败",
                _ => runtimeState.LastInspectionRunState switch
                {
                    InspectionRunState.Succeeded => "巡检正常",
                    InspectionRunState.SucceededWithFault => "巡检发现异常",
                    InspectionRunState.Failed => "巡检失败",
                    _ => "待巡检"
                }
            };
        }

        if (platformSite.DemoOnlineState == DemoOnlineState.Offline)
        {
            return "设备离线";
        }

        return platformSite.DemoDispatchStatus switch
        {
            DispatchDemoStatus.Dispatched => "已派单",
            DispatchDemoStatus.Cooling => "冷却中",
            _ => platformSite.DemoStatus switch
            {
                PointDemoStatus.Fault => "设备故障",
                PointDemoStatus.Warning => "预警关注",
                PointDemoStatus.Idle => "长时空闲",
                _ => "正常"
            }
        };
    }

    private static string ResolveAlertLabel(SiteMergedView site)
    {
        if (site.CanConfirmRecovery)
        {
            return "待恢复确认";
        }

        if (IsRecentlyRecovered(site))
        {
            return "已恢复";
        }

        if (HasActiveDispatch(site))
        {
            return ResolveDispatchStateText(site);
        }

        return site.RuntimeFaultCode switch
        {
            RuntimeFaultCode.Offline => "设备离线",
            RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
            RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
            RuntimeFaultCode.InspectionExecutionFailed => "巡检失败",
            _ => site.StatusText
        };
    }

    private static string ResolveRuntimeSummary(SiteMergedView site)
    {
        if (!site.IsMonitored)
        {
            return "当前点位未纳入静默巡检。";
        }

        if (!site.HasMapPoint)
        {
            return $"{site.UnmappedReasonText}。";
        }

        if (site.CanConfirmRecovery)
        {
            return "已派单，待恢复确认。";
        }

        if (IsRecentlyRecovered(site))
        {
            return "运行态已恢复。";
        }

        if (HasActiveDispatch(site))
        {
            return BuildDispatchSummary(site);
        }

        if (ResolveRuntimeIssueText(site) is string issueText)
        {
            return $"{issueText}。";
        }

        if (site.LastInspectionAt.HasValue)
        {
            return site.LastInspectionRunState switch
            {
                InspectionRunState.Succeeded => "巡检正常。",
                InspectionRunState.SucceededWithFault => "巡检发现异常。",
                InspectionRunState.Failed => "巡检失败。",
                InspectionRunState.Skipped => "当前巡检已跳过。",
                _ => "等待下一次巡检。"
            };
        }

        return "等待首次巡检。";
    }

    private static string ResolveDispatchStateKey(SiteMergedView site)
    {
        if (!site.HasDispatchRecord)
        {
            return "none";
        }

        if (site.CanConfirmRecovery)
        {
            return "recovery-pending";
        }

        if (site.RecoveredAt.HasValue)
        {
            return "recovered";
        }

        if (site.IsDispatchCooling)
        {
            return "cooling";
        }

        return site.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "pending",
            DispatchStatus.Dispatched => "dispatched",
            DispatchStatus.SendFailed => "send-failed",
            DispatchStatus.WebhookNotConfigured => "pending",
            _ => "none"
        };
    }

    private static string ResolveDispatchStateText(SiteMergedView site)
    {
        if (!site.HasDispatchRecord)
        {
            return "未处置";
        }

        if (site.CanConfirmRecovery)
        {
            return "待恢复确认";
        }

        if (site.RecoveredAt.HasValue)
        {
            return "已恢复";
        }

        if (site.IsDispatchCooling)
        {
            return "冷却中";
        }

        return site.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "待派单",
            DispatchStatus.Dispatched => "已派单",
            DispatchStatus.SendFailed => "发送失败",
            DispatchStatus.WebhookNotConfigured => "待发送",
            _ => "未处置"
        };
    }

    private static DispatchPresentation ResolveDispatchPresentation(
        DispatchRecord? dispatchRecord,
        SiteRuntimeState? runtimeState)
    {
        if (dispatchRecord is null)
        {
            return new DispatchPresentation(
                false,
                false,
                false,
                DispatchStatus.None,
                "未触发派单",
                "未恢复");
        }

        var isCooling =
            !dispatchRecord.IsRecovered
            && dispatchRecord.DispatchStatus == DispatchStatus.Dispatched
            && dispatchRecord.CoolingUntil is DateTimeOffset coolingUntil
            && coolingUntil > DateTimeOffset.UtcNow;
        var canConfirmRecovery = dispatchRecord.RecoveryStatus == RecoveryStatus.PendingConfirmation;

        return new DispatchPresentation(
            dispatchRecord.DispatchStatus != DispatchStatus.None && !dispatchRecord.IsRecovered,
            isCooling,
            canConfirmRecovery,
            dispatchRecord.DispatchStatus,
            ResolveDispatchStatusText(dispatchRecord, isCooling),
            ResolveRecoveryStatusText(dispatchRecord));
    }

    private static string ResolveDispatchStatusText(DispatchRecord record, bool isCooling)
    {
        if (isCooling)
        {
            return "冷却中";
        }

        return record.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "待派单",
            DispatchStatus.Dispatched => "已派单",
            DispatchStatus.SendFailed => "发送失败",
            DispatchStatus.WebhookNotConfigured => "待发送",
            _ => "未触发派单"
        };
    }

    private static string ResolveRecoveryStatusText(DispatchRecord record)
    {
        return record.RecoveryStatus switch
        {
            RecoveryStatus.PendingConfirmation => "待恢复确认",
            RecoveryStatus.Recovered => "已恢复",
            RecoveryStatus.NotificationFailed => "已恢复（通知未发送）",
            _ => record.RecoveredAt.HasValue ? "已恢复" : "未恢复"
        };
    }

    private static string BuildDispatchSummary(SiteMergedView site)
    {
        var issueText = ResolveRuntimeIssueText(site);

        return site.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => issueText is null ? "异常已识别，待派单。" : $"{issueText}，待派单。",
            DispatchStatus.Dispatched when site.IsDispatchCooling => issueText is null ? "已派单，处理中。" : $"已派单，{issueText}。",
            DispatchStatus.Dispatched => issueText is null ? "已派单，待现场处置。" : $"已派单，{issueText}。",
            DispatchStatus.SendFailed => issueText is null ? "异常已识别，待重试派单。" : $"{issueText}，待重试派单。",
            DispatchStatus.WebhookNotConfigured => issueText is null ? "异常已识别，待发送派单。" : $"{issueText}，待发送派单。",
            _ => issueText is null ? "异常已识别，处理中。" : $"{issueText}。"
        };
    }

    private static string? ResolveRuntimeIssueText(SiteMergedView site)
    {
        return site.RuntimeFaultCode switch
        {
            RuntimeFaultCode.Offline => "设备离线",
            RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
            RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
            RuntimeFaultCode.InspectionExecutionFailed => "巡检失败",
            _ => site.LastInspectionRunState switch
            {
                InspectionRunState.Failed => "巡检失败",
                InspectionRunState.SucceededWithFault => "巡检发现异常",
                _ => null
            }
        };
    }

    private static string? FormatCoordinate(double? longitude, double? latitude)
    {
        return CoordinateTypeCatalog.HasCompleteCoordinate(longitude, latitude)
            ? $"{longitude!.Value:F6}, {latitude!.Value:F6}"
            : null;
    }

    private sealed record SiteMergeBundle(
        IReadOnlyList<SiteMergedView> Sites,
        PlatformConnectionState ConnectionState);

    private sealed record DispatchPresentation(
        bool HasActiveDispatch,
        bool IsCooling,
        bool CanConfirmRecovery,
        DispatchStatus DispatchStatus,
        string DispatchStatusText,
        string RecoveryStatusText)
    {
        public bool IsRecovered =>
            !HasActiveDispatch
            && !CanConfirmRecovery
            && RecoveryStatusText.StartsWith("已恢复", StringComparison.Ordinal);
    }

    private sealed record CoordinateGovernance(
        CoordinateSource CoordinateSource,
        CoordinateDisplayStatus DisplayStatus,
        bool HasMapPoint,
        bool HasDisplayCoordinate,
        UnmappedReason UnmappedReason,
        double? DisplayLongitude,
        double? DisplayLatitude);
}
