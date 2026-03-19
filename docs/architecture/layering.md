# 五层职责与依赖方向

## 固定分层

- `Tysl.Ai.App`
  - 负责启动、组合根、数据库路径决定和依赖装配。
- `Tysl.Ai.UI`
  - 负责 WPF 视图、ViewModel、主题、控件和交互状态。
- `Tysl.Ai.Services`
  - 负责面向 UI 的查询编排与本地补充信息写入服务。
- `Tysl.Ai.Infrastructure`
  - 负责 SQLite 持久化和 ACIS 平台接入占位。
- `Tysl.Ai.Core`
  - 负责领域模型、枚举、稳定接口和无外部依赖抽象。

## 依赖方向

- `App -> UI / Services / Infrastructure / Core`
- `UI -> Services / Core`
- `Services -> Core`
- `Infrastructure -> Core`
- `Core -> none`

## 第 2 轮纠偏落点

### Core

- `PlatformSiteSnapshot`
  - 平台设备快照模型
- `SiteLocalProfile`
  - 本地补充信息模型
- `SiteMergedView` / `SiteMapPoint`
  - 面向 UI 的合并 DTO
- `IPlatformSiteProvider`
  - 平台设备源接口
- `ISiteLocalProfileRepository`
  - 本地补充信息仓储接口
- `ISiteLocalProfileService`
  - 本地补充信息写入接口
- `ISiteMapQueryService`
  - 地图、详情、异常条查询接口

### Infrastructure

- `Integrations/Acis/StubPlatformSiteProvider`
  - 当前阶段的平台设备 Stub 权威源
- `Persistence/Sqlite/SiteLocalProfileRepository`
  - `site_local_profile` 仓储实现
- `Persistence/Sqlite/SqliteDatabaseInitializer`
  - 建表、旧表迁移与少量本地补充信息样例初始化

### Services

- `SiteLocalProfileService`
  - 只负责校验和保存本地补充信息
- `SiteMapQueryService`
  - 合并平台快照与本地补充信息

### UI

- `ShellViewModel`
  - 负责筛选、选中、详情抽屉、异常条和编辑弹窗协同
- `SiteEditorViewModel`
  - 只处理本地补充信息编辑

## 边界约束

- `UI` 不直接写 SQL，不直接访问平台设备源，不直接处理坐标合并规则。
- `Services` 不承载 SQLite 连接细节，也不承载真实 ACIS 签名、解密或坐标转换。
- `Infrastructure` 不写界面逻辑，不生成 UI 状态。
- 当前真实 CTYun / ACIS 内核尚未启用，第 3 轮再接入 `Infrastructure/Integrations/Acis`。
