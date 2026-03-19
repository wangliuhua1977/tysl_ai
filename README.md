# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面监控系统重构仓库，不是传统后台管理系统。当前已完成第 2 轮纠偏修正：地图点位不再由本地 SQLite 主档驱动，而是改为“平台设备权威源 + 本地补充信息”的结构预备版本。

## 当前阶段

- 平台设备点位由 `StubPlatformSiteProvider` 提供，作为第 3 轮接真实 ACIS / CTYun 前的权威源占位。
- 本地 SQLite 只保存 `site_local_profile` 补充信息，不再保存完整平台点位快照。
- 地图展示、右侧详情抽屉、底部异常缩略条都来自“平台快照 + 本地补充信息”的合并结果。
- 编辑弹窗只允许编辑本地补充信息，不允许新增平台设备，也不存在“新增点位”入口。
- 坐标规则为：平台坐标优先，本地手工坐标兜底，都没有则不显示地图点位。

## 项目定位

- 主界面长期保持“地图主视图 + 右侧详情抽屉 + 底部异常缩略条”。
- 静默巡检替代传统任务面板，复杂操作收入弹窗、抽屉或更多菜单。
- UI 不展示调试、诊断或平台接入细节。

## 分层结构

```text
/
  AGENTS.md
  README.md
  .codex/
    config.toml
  docs/
    architecture/
    ctyun-api/
  src/
    Tysl.Ai.App/
    Tysl.Ai.UI/
    Tysl.Ai.Services/
    Tysl.Ai.Infrastructure/
    Tysl.Ai.Core/
```

## 第 2 轮纠偏结果

- `Core` 定义了 `PlatformSiteSnapshot`、`SiteLocalProfile`、`SiteMergedView`、`SiteMapPoint` 以及对应接口。
- `Infrastructure/Integrations/Acis/StubPlatformSiteProvider` 提供 6 个平台设备演示快照，明确为 Stub / Placeholder。
- `Infrastructure/Persistence/Sqlite` 只管理本地补充信息表 `site_local_profile`，并兼容迁移旧的 `site_profile` 语义。
- `Services/Sites/SiteMapQueryService` 负责合并平台快照与本地补充信息，输出地图、详情与异常条 DTO。
- `UI` 删除“新增点位”入口，编辑弹窗仅维护别名、备注、监测开关、手工坐标和维护信息等本地字段。

## SQLite

- 数据库文件位置：`%LOCALAPPDATA%\\Tysl.Ai\\data\\site-profile.db`
- 当前表：`site_local_profile`
- 主键：`device_code`
- 保存范围：别名、备注、监测开关、手工坐标、地址、接入号、维护信息、创建时间、更新时间
- 不保存范围：平台设备名称、平台坐标、在线状态、演示状态、派单状态等平台快照字段

## 构建与运行

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```

## 当前边界

- 当前仍未接入真实 ACIS、CTYun、地图 SDK、WebView2、SQLite 之外的真实外设能力或 webhook。
- 第 3 轮再接真实 `Infrastructure/Integrations/Acis` 内核能力。
- 本轮只修正结构语义，不伪造真实平台接入实现。
