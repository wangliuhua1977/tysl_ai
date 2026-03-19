# Tysl.Ai

“天翼视联 AI 巡检系统”重构版桌面端仓库。当前阶段已完成第 2 轮开发：在既定 `App / UI / Services / Infrastructure / Core` 五层架构内，落地本地点位主档、SQLite 持久化、地图静态点位显示、右侧详情抽屉联动、点位编辑弹窗和演示坐标拾取。

## 项目定位
- 地图中心型桌面监控系统，不是传统后台管理系统。
- 主界面保持“地图主视图 + 右侧详情抽屉 + 底部异常缩略条”的值守工作方式。
- 本轮只处理本地数据和 UI 交互，不接真实 ACIS / CTYun / 地图 SDK / WebView2 / webhook。

## 当前阶段
- 已初始化 `Tysl.Ai.sln` 与五层项目结构。
- 已接入本地 SQLite，启动时自动建表并灌入演示点位。
- 已实现点位主档仓储、点位管理服务与地图查询服务。
- 已实现地图区域静态点位展示、筛选联动、详情抽屉刷新。
- 已实现新增 / 编辑点位弹窗、纳入监测开关和演示坐标拾取。

## 目录结构
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

## 构建与运行
```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```

## 第 2 轮已实现内容
- 本地 SQLite 数据库位于当前用户本地应用数据目录：`%LOCALAPPDATA%\Tysl.Ai\data\site-profile.db`
- 启动时自动初始化 `site_profile` 主表，并在空库时灌入 7 个覆盖正常、故障、预警、空闲、未监测、冷却中、已派单状态的演示点位。
- 主页面地图区域按数据库经纬度映射显示静态点位，名称采用“别名优先、设备名兜底”。
- 左侧筛选支持 `全部 / 异常 / 已监测 / 已派单`，并同步联动地图点位与底部异常缩略条。
- 右侧详情抽屉展示主档、维护信息、坐标、备注与演示状态。
- `SiteEditorDialog` 支持新增 / 编辑点位、维护监测状态、编辑本地坐标、执行演示坐标拾取并立即落库刷新。

## 当前边界
- 不引入真实 ACIS 内核文件。
- 不接真实 CTYun、地图 SDK、预览播放、WebView2、企业微信 webhook。
- 不在 UI 显示日志、异常堆栈或调试字符串。
- 不把业务逻辑和持久化逻辑写进 UI。
