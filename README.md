# Tysl.Ai

“天翼视联 AI 巡检系统”重构版桌面端仓库。当前阶段完成第 1 轮初始化：五层解决方案骨架、项目规则、Codex 配置、基线文档、首批技能和可运行的 WPF 主界面壳。

## 项目定位
- 地图中心型桌面监控系统，不是传统后台管理系统。
- 首版聚焦极简主壳：地图主视图、右侧详情抽屉、底部异常缩略条。
- 真实 CTYun、ACIS、地图 SDK、WebView2、SQLite、Webhook 暂不接入，只保留边界、占位与文档。

## 当前阶段
- 已初始化 `Tysl.Ai.sln` 与五层项目。
- 已建立项目级 `AGENTS.md`、`.codex/config.toml` 与 6 个仓库技能。
- 已落地 `docs/architecture` 与 `docs/ctyun-api` 基线文档。
- 已提供可运行的 WPF 壳与主题资源。

## 目录结构
```text
/
  AGENTS.md
  README.md
  .codex/
    config.toml
    skills/
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

## 后续开发轮次计划
1. 接入 ACIS 单文件复用内核边界，补全 `Infrastructure/Integrations/Acis`。
2. 建立静默巡检服务编排与时段控制模型，不引入 UI 任务面板。
3. 接入地图承载层与点位状态联动，保留极简壳布局。
4. 接入截图优先、单点播放的预览链路，避免直接上视频墙。
5. 引入 webhook 派单、去重、冷却时间和恢复判定。
