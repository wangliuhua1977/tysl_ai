# 架构总览

## 产品目标

`Tysl.Ai` 面向地图中心型桌面值守场景，核心工作方式固定为：
- 地图主视图承载平台点位与状态
- 右侧详情抽屉承载单点详情与本地维护信息
- 底部异常缩略条承载关注点快速切换

本项目不回退成表格中心页面，也不在首页堆放配置、调试或诊断面板。

## 当前主链

- 真实 ACIS 内核已开始接入
- 平台设备权威源主路径已从 `StubPlatformSiteProvider` 切换到 `AcisKernelPlatformSiteProvider`
- `AcisKernelPlatformSiteProvider` 负责目录拉取、部分详情补拉、原始坐标透传与受控降级
- 本地 SQLite 继续只保存 `site_local_profile` 补充信息，不保存整份平台主档

## 当前数据流

1. `App` 启动时尝试读取 `configs/acis-kernel.json`
2. 配置有效时创建真实 ACIS 平台设备提供器
3. 提供器通过 `AcisApiKernel` 拉取设备目录页，并对部分点位补拉详情
4. 平台原始坐标直接写入 `PlatformSiteSnapshot`
5. `SiteMapQueryService` 将平台快照与 `site_local_profile` 合并成 `SiteMergedView / SiteMapPoint / SiteAlertDigest`
6. `UI` 只消费合并结果，不直接调用 ACIS 内核

## 坐标规则

- 后端主链只读取并透传平台原始坐标
- 当前项目坐标转换走前端高德 JSAPI，不依赖后端高德 WebService 坐标转换
- `SiteMapQueryService` 不做百度转高德，只负责决定显示坐标来源
- 平台原始坐标可通过 `PlatformRawCoordinateType` 标识 `bd09 / gcj02 / unknown`
- 平台无坐标时，允许使用本地手工坐标兜底
- 平台与本地都无坐标时，不显示地图点位
- 若未来改回后端 Web 服务转换，再启用 `ConvertCoordinatesAsync`

## 受控降级

- 未找到 `configs/acis-kernel.json` 时，应用进入“平台未连接”状态
- 配置不完整或平台访问失败时，应用进入“平台数据暂不可用”状态
- 即使 `amap.webServiceKey` 为空，平台点位主链仍可继续
- 降级状态下应用仍可运行，不向 UI 暴露异常堆栈、签名细节或诊断日志

## 当前非目标

- 真实播放 UI
- 静默巡检
- 企业微信派单
- Webhook / WebView2 / 地图 SDK 的完整整合
