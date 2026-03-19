# 五层职责与依赖方向

## 固定分层

- `Tysl.Ai.App`
  - 启动、组合根、配置读取和依赖装配
- `Tysl.Ai.UI`
  - WPF 视图、ViewModel、主题、WebView2 地图宿主和前端宿主页资源
- `Tysl.Ai.Services`
  - 面向 UI 的查询编排和本地补充信息写入
- `Tysl.Ai.Infrastructure`
  - SQLite 持久化、ACIS / CTYun 接入、地图配置读取
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
- `MapCoordinatePayload`
  - 地图宿主消费的坐标负载
- `CoordinateSource`
  - 平台原始 / 本地手工 / 无坐标

### Infrastructure

- `Integrations/Acis/AcisKernelPlatformSiteProvider`
  - 平台目录、详情、原始坐标透传和平台降级
- `Configuration/AmapJsOptionsProvider`
  - 读取 `configs/amap-js.json`
- 不在这里写 UI 逻辑或前端地图脚本

### Services

- `SiteMapQueryService`
  - 合并平台快照和本地补充信息
  - 输出地图点位 DTO 所需原始信息
  - 不做坐标转换

### UI

- `AmapHostControl`
  - 持有 `WebView2`
  - 负责 WPF 与 JS 消息桥
  - 不直接调用 ACIS
- `ShellViewModel`
  - 组装地图宿主状态
  - 接收 marker 点击、地图点击和显示坐标回流
- `Web/amap/*`
  - 初始化高德 JSAPI
  - 执行前端坐标转换
  - 渲染 marker 和摘要卡片

## 严格边界

- `UI` 不直接写 `HttpClient`、签名、解密或后端坐标转换
- `Services` 不承载平台接入细节
- `Infrastructure` 不显示 UI 诊断文本
- `Core` 不依赖 WebView2、高德 SDK 或 SQLite
- 后端主链不重新启用 `ConvertCoordinatesAsync`
