# 五层职责与依赖方向

## 固定分层

- `Tysl.Ai.App`
  - 启动、组合根、配置读取和依赖装配
- `Tysl.Ai.UI`
  - WPF 视图、ViewModel、主题、WebView2 地图宿主和前端宿主页资源
- `Tysl.Ai.Services`
  - 面向 UI 的查询编排和本地补充信息写入
- `Tysl.Ai.Infrastructure`
  - SQLite 持久化、ACIS / CTYun 接入、截图存储、后台静默巡检
- `Tysl.Ai.Core`
  - 领域模型、枚举、稳定接口和无外部依赖抽象

## 依赖方向

- `App -> UI / Services / Infrastructure / Core`
- `UI -> Services / Core`
- `Services -> Core`
- `Infrastructure -> Core`
- `Core -> none`

## 当前落点

### Core

- `PlatformSiteSnapshot`
  - 平台原始坐标与原始坐标类型
- `SiteRuntimeState`
  - 最近巡检、最近截图、故障摘要、连续失败次数
- `InspectionSettings`
  - 监测时段、巡检间隔、截图保留和批量限制
- `ISiteRuntimeStateRepository / IInspectionSettingsProvider / ISnapshotStorage / ISilentInspectionService`
  - 运行态主线的稳定接口

### Infrastructure

- `Integrations/Acis/AcisKernelPlatformSiteProvider`
  - 平台目录、详情、原始坐标透传、预览解析和平台降级
- `Persistence/Sqlite/*`
  - 本地补充信息、运行态、截图记录、巡检设置
- `Storage/SnapshotStorage`
  - 最近截图留痕落盘
- `Background/SilentInspectionService`
  - 静默巡检首版编排
- `Background/SilentInspectionHostedService`
  - 应用启动后的后台巡检循环

### Services

- `SiteMapQueryService`
  - 合并平台快照、本地补充信息和运行态
  - 输出地图点位、详情抽屉和异常条所需视图模型源数据
  - 不做坐标转换

### UI

- `AmapHostControl`
  - 持有 `WebView2`
  - 负责 WPF 与 JS 消息桥
  - 不直接调用 ACIS
- `ShellViewModel`
  - 组装地图宿主状态
  - 定时刷新运行态合并结果
  - 接收 marker 点击、地图点击和显示坐标回流
- `Web/amap/*`
  - 初始化高德 JSAPI
  - 执行前端坐标转换
  - 渲染 marker 和摘要卡片

## 严格边界

- `UI` 不直接写 `HttpClient`、签名、解密或后端坐标转换
- `UI` 不显示本地日志、堆栈或协议细节
- `Services` 不承载平台接入细节
- `Infrastructure` 不回退到假数据主链
- `Core` 不依赖 WebView2、高德 SDK 或 SQLite
- 后端主链不重新启用 `ConvertCoordinatesAsync`
- 当前仍不接企业微信派单、工单系统和复杂报表
