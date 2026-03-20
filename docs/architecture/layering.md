# 五层职责与依赖方向

## 固定分层

- `Tysl.Ai.App`
  - 启动、根组合、配置读取和依赖装配。
- `Tysl.Ai.UI`
  - WPF 视图、ViewModel、主题、控件、WebView2 地图宿主和前端资源。
- `Tysl.Ai.Services`
  - 面向 UI 的查询编排与本地补充信息写入服务。
- `Tysl.Ai.Infrastructure`
  - ACIS / CTYun 接入、SQLite 持久化、派单、截图、后台巡检。
- `Tysl.Ai.Core`
  - 领域模型、枚举、稳定接口和无外部依赖的抽象。

## 依赖方向

- `App -> UI / Services / Infrastructure / Core`
- `UI -> Services / Core`
- `Services -> Core`
- `Infrastructure -> Core`
- `Core -> none`

## 当前落点

### Core

- `DispatchPolicy / DispatchRecord`
- `InspectionSettings`
- `PlatformSiteSnapshot`
- `SiteRuntimeState`
- `SiteMergedView / SiteMapPoint / SiteAlertDigest`

### Infrastructure

- `Integrations/Acis/AcisKernelPlatformSiteProvider`
- `Persistence/Sqlite/*`
- `Dispatch/DispatchService`
- `Messaging/WeComWebhookSender`
- `Storage/SnapshotStorage`
- `Background/SilentInspectionService`

### Services

- `SiteMapQueryService`
  - 只做聚合与视图模型源数据组织，不做平台接入细节和坐标转换。

### UI

- `ShellViewModel`
- `AmapHostControl`
- `Web/amap/*`

## 严格边界

- `UI` 不直接写 `HttpClient`、签名、解密、webhook 调用或服务层坐标转换。
- `UI` 不显示日志、异常堆栈、请求细节或诊断信息。
- `Services` 不承载 ACIS 接入细节。
- `Infrastructure` 不回退成假实现主链。
- `Core` 不依赖 WebView2、高德 SDK、SQLite 或网络能力。
