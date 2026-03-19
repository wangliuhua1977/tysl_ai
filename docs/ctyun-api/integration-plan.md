# ACIS 内核接入计划

## 当前已落地

1. 在 `Infrastructure/Integrations/Acis` 复用 `AcisApiKernel.cs`
2. 通过 `AcisKernelOptionsProvider` 读取 `configs/acis-kernel.json`
3. 通过 `AcisKernelPlatformSiteProvider` 作为平台设备权威源主路径
4. 平台原始坐标、原始坐标类型直接进入 `PlatformSiteSnapshot`
5. `SiteMapQueryService` 只合并平台快照与本地补充信息，不做服务层坐标转换
6. 前端地图宿主已接入 `WebView2 + 高德 JSAPI 2.0`
7. marker 点击、地图点击和前端显示坐标回流已接入 WPF

## 当前主链路

```text
configs/acis-kernel.json
  -> AcisKernelOptionsProvider
  -> AcisKernelPlatformSiteProvider
  -> AcisApiKernel.GetDeviceCatalogPageAsync
  -> AcisApiKernel.GetDeviceDetailAsync
  -> SiteMapQueryService
  -> ShellViewModel
  -> AmapHostControl
  -> UI/Web/amap/index.html + amap-host.js
```

## 坐标分工

- ACIS / 后端：
  - 拉取并透传平台原始坐标
  - 透传 `RawCoordinateType`
  - 保留本地手工坐标兜底信息
  - 不重新启用 `ConvertCoordinatesAsync`
- 前端地图宿主：
  - 根据 `RawCoordinateType` 决定是否 `convertFrom`
  - 手工坐标直接作为 `GCJ-02`
  - 渲染 marker
  - 将转换后的当前显示坐标回传给详情抽屉

## 当前边界

- 不接真实派单
- 不接复杂播放器
- 不在 UI 显示 JS 异常、堆栈或诊断信息
- 缺少地图配置时必须受控降级，不允许程序崩溃

## 后续建议

1. 用真实 `amap-js.json` 在目标环境完成联调
2. 继续补齐平台告警接入，替换当前演示状态字段
3. 再进入静默巡检和派单编排
