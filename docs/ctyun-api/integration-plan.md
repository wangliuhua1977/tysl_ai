# ACIS 内核接入计划

## 第 3 轮当前落点

1. 在 `Infrastructure/Integrations/Acis` 复用现有 `AcisApiKernel.cs`
2. 新增 `AcisKernelOptionsProvider`，读取 `configs/acis-kernel.json`
3. 新增 `AcisKernelPlatformSiteProvider`，作为平台设备权威源主路径
4. 通过 `GetDeviceCatalogPageAsync` 拉设备目录页，并限制总页数 / 总数量
5. 对部分设备按需调用 `GetDeviceDetailAsync` 补拉详情
6. 平台原始坐标直接保留到 `PlatformSiteSnapshot`
7. 无配置或配置无效时进入受控降级，应用不崩溃

## 当前主链路

```text
configs/acis-kernel.json
  -> AcisKernelOptionsProvider
  -> AcisKernelPlatformSiteProvider
  -> AcisApiKernel.GetDeviceCatalogPageAsync
  -> AcisApiKernel.GetDeviceDetailAsync
  -> SiteMapQueryService
  -> 地图 / 详情抽屉 / 异常缩略条
```

## 坐标纠偏说明

- 当前项目坐标转换走前端高德 JSAPI
- 后端 ACIS 主链不依赖高德 WebService 坐标转换
- `AcisKernelPlatformSiteProvider` 获取设备目录和详情时，不再调用 `ConvertCoordinatesAsync` 作为必要步骤
- 平台点位读取成功后，即使只有原始坐标，也要先进入地图 DTO
- `SiteMapQueryService` 只负责平台快照 + 本地补充信息 + 运行态合并，不在服务层做高德坐标转换
- 本地手工坐标仍作为平台无坐标时的兜底来源
- `amap.webServiceKey` 允许为空
- 若未来改回后端 Web 服务转换，再启用 `ConvertCoordinatesAsync`

## 本轮边界

- 不接真实播放 UI
- 不接静默巡检
- 不接企业微信派单
- 不接真实告警编排消费

## 后续轮次建议

1. 在真实平台环境下校准目录分页规模与详情补拉数量
2. 前端地图宿主接入高德 JSAPI，并按 `PlatformRawCoordinateType` 实施客户端转换
3. 补充平台告警接入，替换当前演示状态字段
4. 再接静默巡检与派单编排
