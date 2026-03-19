# ACIS 内核接入计划

## 当前已落地

1. 在 `Infrastructure/Integrations/Acis` 复用 `AcisApiKernel.cs`
2. 通过 `AcisKernelOptionsProvider` 读取 `configs/acis-kernel.json`
3. 通过 `AcisKernelPlatformSiteProvider` 作为平台设备权威源主路径
4. 平台原始坐标、原始坐标类型直接进入 `PlatformSiteSnapshot`
5. `SiteMapQueryService` 合并平台快照、本地补充信息、运行态和派单记录，不做服务层坐标转换
6. 前端地图宿主已接入 `WebView2 + 高德 JSAPI 2.0`
7. `SilentInspectionService` 复用 `AcisKernelPlatformSiteProvider` 的平台目录与预览解析能力
8. 运行态、截图记录、巡检设置、派单策略和派单记录已落到 SQLite
9. 企业微信 webhook 派单、冷却与恢复逻辑首版已接入

## 当前主链路

```text
configs/acis-kernel.json
  -> AcisKernelOptionsProvider
  -> AcisKernelPlatformSiteProvider
  -> AcisApiKernel.GetDeviceCatalogPageAsync
  -> AcisApiKernel.GetDeviceDetailAsync
  -> AcisApiKernel.ResolvePreviewAsync(intent=Inspection)
  -> SilentInspectionService
  -> site_runtime_state / snapshot_record / runtime/snapshots
  -> DispatchService
  -> dispatch_policy / dispatch_record
  -> SiteMapQueryService
  -> ShellViewModel
  -> AmapHostControl
  -> UI/Web/amap/index.html + amap-host.js
```

## 当前编排边界

- ACIS / 后端继续负责：
  - 拉取并透传平台原始坐标
  - 透传 `RawCoordinateType`
  - 复用预览地址解析链路
  - 统一写本地诊断日志
- 前端地图宿主继续负责：
  - 根据 `RawCoordinateType` 决定是否 `convertFrom`
  - 手工坐标直接作为 `GCJ-02`
  - 渲染 marker
  - 将转换后的当前显示坐标回传给详情抽屉
- 静默巡检 + 处置首版当前只做：
  - 在线状态采集
  - 预览解析结果写入运行态
  - 最近截图留痕落盘
  - 企业微信 webhook 派单
  - 派单冷却控制
  - 自动恢复 / 手工确认恢复
  - UI 联动刷新

## 当前边界

- 不绕过 `AcisApiKernel` 重写 token、签名、解密或预览回退
- 当前只支持企业微信 webhook
- 不扩展成复杂工单系统
- 不接审批流
- 不在 UI 显示 JS 异常、堆栈或诊断信息
- 缺少 ACIS 或 webhook 配置时必须受控降级，不允许程序崩溃

## 最近截图留痕说明

- 当前为首版实现
- 优先保证：
  - 截图目录结构存在
  - 文件路径落库
  - 详情抽屉与异常条可展示
  - 派单记录可关联截图路径
  - 保留数量受 `inspection_settings.snapshot_retention_count` 控制
- 当前生成占位截图文件，不冒进实现重型真实抓帧

## 后续建议

1. 在真实环境联调 ACIS 预览解析稳定性与时段控制
2. 视需要把占位截图替换为真实抓帧实现
3. 后续再评估是否接工单编排，但不提前侵入当前主链
