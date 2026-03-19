# 五层职责与依赖方向

## 固定分层
- `Tysl.Ai.App`：启动、组合根、数据库路径决定、依赖装配和启动初始化。
- `Tysl.Ai.UI`：WPF 视图、ViewModel、主题、控件、弹窗和交互状态。
- `Tysl.Ai.Services`：点位管理服务、地图查询服务、面向 UI 的应用编排。
- `Tysl.Ai.Infrastructure`：SQLite 连接工厂、数据库初始化器、仓储实现，以及后续外部系统接入占位。
- `Tysl.Ai.Core`：领域模型、枚举、稳定接口、查询 DTO 和无外部依赖抽象。

## 依赖方向
- `App -> UI / Services / Infrastructure / Core`
- `UI -> Services / Core`
- `Services -> Core`
- `Infrastructure -> Core`
- `Core -> none`

## 第 2 轮落点
- `Core/Models/SiteProfile`：点位主档实体。
- `Core/Interfaces/ISiteProfileRepository`：点位仓储接口。
- `Core/Interfaces/ISiteManagementService`：新增 / 编辑 / 查询主档接口。
- `Core/Interfaces/ISiteMapQueryService`：地图快照、详情抽屉和演示坐标换算接口。
- `Infrastructure/Persistence/Sqlite`：`SqliteConnectionFactory`、`SqliteDatabaseInitializer`、`SiteProfileRepository`。
- `Services/Sites`：`SiteManagementService`、`SiteMapQueryService`。
- `UI/Views/SiteEditorDialog`：点位编辑弹窗。

## 边界约束
- `UI` 不直接写 SQL、坐标换算或数据库初始化。
- `Services` 不承载 SQLite 连接细节，也不依赖 WPF。
- `Infrastructure` 只负责持久化和外部接入边界，不写界面交互。
- 后续 CTYun / ACIS 仍统一落在 `Infrastructure/Integrations/Acis`。
