# ACIS 内核接入计划

## 当前已落地

1. 在 `Infrastructure/Integrations/Acis` 复用 `AcisApiKernel.cs`。
2. 通过 `AcisKernelOptionsProvider` 读取 `configs/acis-kernel.json`。
3. 通过 `AcisKernelPlatformSiteProvider` 作为平台设备权威源主路径。
4. 平台原始坐标、原始坐标类型直接进入 `PlatformSiteSnapshot`。
5. `SiteMapQueryService` 合并平台快照、本地补充信息、运行态和派单记录，不做服务层坐标转换。
6. 前端地图宿主已接入 `WebView2 + 高德 JSAPI 2.0`。
7. `SilentInspectionService` 复用平台目录和预览解析能力。
8. 运行态、截图记录、巡检设置、派单策略和派单记录已落到 SQLite。
9. 企业微信 webhook 派单、冷却与恢复逻辑首版已接入。

## 当前主链路

```text
configs/acis-kernel.json
  -> AcisKernelOptionsProvider
  -> AcisKernelPlatformSiteProvider
  -> AcisApiKernel.GetDeviceCatalogPageAsync / GetDeviceDetailAsync / ResolvePreviewAsync
  -> SilentInspectionService
  -> site_runtime_state / snapshot_record / runtime/snapshots
  -> DispatchService
  -> dispatch_policy / dispatch_record
  -> SiteMapQueryService
  -> ShellViewModel
  -> AmapHostControl
  -> UI/Web/amap/index.html + amap-host.js
```

## 本轮收口结果

- UI 收口到地图中心布局，不新增大模块。
- dispatch 默认策略不再硬编码真实 webhook。
- 新增 `configs/dispatch.template.json`，缺失配置时可启动。
- `runtime/.acis-kernel` 与 `runtime/snapshots` 改为说明 + 忽略策略。
- README 与架构文档同步到首版能力边界。

## 当前边界

- 不绕过 `AcisApiKernel` 重写 token、签名、解密或预览回退。
- 不扩展成复杂工单系统。
- 不接审批流。
- 不在 UI 暴露 JS 异常、堆栈或诊断信息。
- 缺少 ACIS / 地图 / 派单配置时必须受控降级，不允许程序崩溃。
