# 五层职责与依赖方向

## 固定分层

- `Tysl.Ai.App`
  - 负责启动、组合根、数据库路径与平台设备源装配
- `Tysl.Ai.UI`
  - 负责 WPF 视图、ViewModel、主题和交互状态
- `Tysl.Ai.Services`
  - 负责面向 UI 的查询编排与本地补充信息写入
- `Tysl.Ai.Infrastructure`
  - 负责 SQLite 持久化和 ACIS / CTYun 外部接入
- `Tysl.Ai.Core`
  - 负责领域模型、枚举、稳定接口和无外部依赖抽象

## 依赖方向

- `App -> UI / Services / Infrastructure / Core`
- `UI -> Services / Core`
- `Services -> Core`
- `Infrastructure -> Core`
- `Core -> none`

## 当前落点

### Core

- `IPlatformSiteProvider`
  - 平台设备快照读取接口
- `IPlatformConnectionStateProvider`
  - 平台连接状态读取接口
- `PlatformSiteSnapshot`
  - 平台原始设备快照，保留原始经纬度与坐标类型
- `CoordinateSource`
  - 平台原始 / 本地手工 / 无坐标 的稳定枚举
- `PlatformConnectionState`
  - 平台已连接 / 未连接 / 数据暂不可用等稳定状态模型

### Infrastructure

- `Integrations/Acis/AcisApiKernel.cs`
  - 复用 ACIS 单文件内核
- `Integrations/Acis/AcisKernelOptionsProvider.cs`
  - 读取并校验 `configs/acis-kernel.json`
- `Integrations/Acis/AcisKernelPlatformSiteProvider.cs`
  - 真实平台设备主路径，负责目录、详情、原始坐标透传与受控降级
- `Integrations/Acis/StubPlatformSiteProvider.cs`
  - 可保留用于开发测试，但不再作为默认主路径

### Services

- `SiteMapQueryService`
  - 合并平台快照、本地补充信息和平台连接状态
  - 只决定显示坐标来源，不做高德坐标转换

### UI

- `ShellViewModel`
  - 只读取查询结果和本地补充信息服务，不触碰 ACIS 内核
- `SiteDetailViewModel`
  - 只展示坐标来源与状态，不做平台调用
- `SiteEditorViewModel`
  - 继续只编辑本地补充字段

## 严格边界

- `UI` 不直接 `new AcisApiKernel`
- `UI` 不直接写 `HttpClient`、签名、解密或坐标转换
- `Services` 不承载平台签名和 CTYun 细节
- `Infrastructure` 不写界面逻辑，不显示诊断信息
- `Core` 不依赖外部 SDK 或平台实现
- 后端 ACIS 主链不依赖 `ConvertCoordinatesAsync`
- 若未来改回后端 Web 服务转换，只能在 `Infrastructure/Integrations/Acis` 内重新启用
